using System;
using System.Collections.Generic;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using ModuleHost.Core.Network;
using ModuleHost.Core.Time;
using ModuleHost.Core;
using FDP.Toolkit.Time.Messages;

namespace FDP.Toolkit.Time.Controllers
{
    /// <summary>
    /// Coordinates time mode switching on the Master node.
    /// Implements the Future Barrier protocol: computes a future
    /// <see cref="Fdp.Kernel.GlobalTime.TotalWallTicks"/>-based barrier, publishes
    /// <see cref="SwitchTimeModeEvent"/>, and performs the local controller swap
    /// when the master's own virtual wall clock crosses that barrier.
    /// </summary>
    public class DistributedTimeCoordinator
    {
        private readonly FdpEventBus _eventBus;
        private readonly ModuleHostKernel _kernel;
        private readonly TimeControllerConfig _config;
        private readonly HashSet<int> _slaveNodeIds;
        private readonly Func<ITimeController>? _continuousControllerFactory;

        // Barrier state — wall-tick-based (not frame-based).
        // -1 means no pending barrier.
        private long _pendingBarrierWallTicks = -1;
        private HashSet<int>? _pendingSlaveIds;

        public DistributedTimeCoordinator(FdpEventBus eventBus, ModuleHostKernel kernel,
                                          TimeControllerConfig config, HashSet<int> slaveNodeIds,
                                          Func<ITimeController>? continuousControllerFactory = null)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _slaveNodeIds = slaveNodeIds;
            _continuousControllerFactory = continuousControllerFactory;

            _eventBus.Register<SwitchTimeModeEvent>();
        }

        private void HandleModeSwitch(SwitchTimeModeEvent ev)
        {
            // When an externally published SwitchTimeModeEvent arrives:
            //   • BarrierWallTicks == 0 → treat as a "please initiate a switch" request:
            //     delegate to SwitchToDeterministic which computes a proper future barrier
            //     and broadcasts it to slaves.  This mirrors the previous BarrierFrame == 0
            //     semantics (request → coordinator decides the barrier).
            //   • BarrierWallTicks > 0 → an already-computed barrier arrived (e.g. relayed
            //     between coordinator instances).  Set the pending barrier idempotently so
            //     this node performs the local swap at the right wall-tick.
            if (ev.TargetMode == TimeMode.Deterministic)
            {
                // Idempotent guard: if already in Deterministic mode, ignore duplicate or
                // loopback events (DDS loopback can re-deliver the original SwitchTimeModeEvent
                // and would otherwise reset _frameNumber in SteppedMasterController to 0).
                if (_kernel.GetTimeController().GetMode() == TimeMode.Deterministic)
                    return;

                if (ev.BarrierWallTicks == 0)
                {
                    // External "initiate" request: compute and broadcast the barrier.
                    SwitchToDeterministic(_slaveNodeIds);
                }
                else if (_pendingBarrierWallTicks < 0 || ev.BarrierWallTicks != _pendingBarrierWallTicks)
                {
                    // A relayed event with a definite barrier: accept idempotently.
                    _pendingBarrierWallTicks = ev.BarrierWallTicks;
                    _pendingSlaveIds ??= _slaveNodeIds;
                }
            }
            else if (ev.TargetMode == TimeMode.Continuous)
            {
                SwitchToContinuous();
            }
        }

        /// <summary>
        /// Initiates a switch to Deterministic (Paused/Stepped) mode using the Future Barrier
        /// protocol.  Computes <c>BarrierWallTicks = currentTotalWallTicks +
        /// LookaheadWallTicks</c> so all PLL-synchronized slaves see the same barrier.
        /// </summary>
        public void SwitchToDeterministic(HashSet<int> slaveNodeIds)
        {
            var currentState = _kernel.GetTimeController().GetCurrentState();

            long lookahead = _config.SyncConfig.LookaheadWallTicks;
            long barrierWallTicks = currentState.TotalWallTicks + lookahead;

            _pendingBarrierWallTicks = barrierWallTicks;
            _pendingSlaveIds = slaveNodeIds;

            // Embed current TimeScale so slaves install SteppedSlaveController with the
            // same scale that was active during continuous mode.
            float timeScale = _kernel.GetTimeController().GetTimeScale();

            // Publish so every slave receives BarrierWallTicks and can mirror the swap.
            _eventBus.Publish(new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Deterministic,
                BarrierWallTicks = barrierWallTicks,
                FixedDelta       = _config.SyncConfig.FixedDeltaSeconds,
                TimeScale        = timeScale,
            });

            var pauseSpan = System.TimeSpan.FromSeconds(currentState.TotalTime);
            string simStr = string.Format("{0:D2}:{1:D2}:{2:D2}.{3:D3}",
                (int)pauseSpan.TotalHours, pauseSpan.Minutes, pauseSpan.Seconds, pauseSpan.Milliseconds);
            double lookaheadSecs = (double)lookahead / System.Diagnostics.Stopwatch.Frequency;
            FdpLog<DistributedTimeCoordinator>.Info(
                "[Master] Pause requested. SimTime={0}  barrier in {1:F3}s",
                simStr, lookaheadSecs);
        }

        /// <summary>
        /// Initiates an immediate switch back to Continuous (Real-time) mode.
        /// Cancels any pending barrier and broadcasts a continuous-mode event.
        /// </summary>
        /// <param name="resumeTimeScale">
        /// Time scale to broadcast to slaves on resume.  When 0 (default) the current
        /// master kernel's time scale is used, which preserves the scale active at pause.
        /// Pass a positive value to change speed atomically with the resume.
        /// </param>
        public void SwitchToContinuous(float resumeTimeScale = 0f)
        {
            // Idempotent: if already in Continuous mode with no pending barrier, there is
            // nothing to do.  Skipping avoids re-publishing SwitchTimeModeEvent(Continuous) on
            // every frame when the DDS loopback delivers the event back to this coordinator,
            // which would otherwise create a sustained bus event chain that falsely triggers
            // the echo-suppression logic in SwitchTimeModeDescriptorTranslator and prevents
            // Resume events from reaching slave nodes in subsequent Pause/Resume cycles.
            //
            // NOTE: if there IS a pending barrier (_pendingBarrierWallTicks >= 0) we must NOT
            // return early — the caller is cancelling a pending Pause (Rapid Pause/Unpause
            // scenario) and the barrier must be cleared.
            if (_kernel.GetTimeController().GetMode() == TimeMode.Continuous
                && _pendingBarrierWallTicks < 0)
                return;

            // Cancel pending barrier
            _pendingBarrierWallTicks = -1;

            // Embed the master's authoritative sim-time so every slave can seed its
            // resumed controller from this value rather than from its own locally-accumulated
            // SteppedSlaveController time.  Without this the slave (e.g. SimHost) resumes
            // at its own pause-seeded time (~3.827s) while the master was at ~4.000s,
            // causing the orchestrator UI to visibly jump backwards on Resume.
            double masterSimTime = _kernel.CurrentTime.TotalTime;

            // Determine effective time scale: caller override takes priority, else keep current.
            float effectiveScale = resumeTimeScale > 0f
                ? resumeTimeScale
                : _kernel.GetTimeController().GetTimeScale();

            // Swap master immediately — install before publishing so the local kernel is in
            // Continuous mode before any DDS loopback echo arrives back on this node.
            ITimeController continuous = _continuousControllerFactory != null
                ? _continuousControllerFactory()
                : new MasterTimeController(_eventBus, _config.SyncConfig);
            continuous.SetTimeScale(effectiveScale);
            _kernel.SwapTimeController(continuous);

            // Publish so slaves also switch immediately (BarrierWallTicks = 0 → no wait).
            // TimeScale is carried so all slaves apply the same speed on their resumed controller.
            _eventBus.Publish(new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Continuous,
                BarrierWallTicks = 0,
                SimTimeSnapshot  = masterSimTime,
                TimeScale        = effectiveScale,
            });

            FdpLog<DistributedTimeCoordinator>.Info("[Master] Switched to Continuous Mode.");
        }

        /// <summary>
        /// Polls for incoming <see cref="SwitchTimeModeEvent"/> events and checks if the
        /// Future Barrier wall-tick target has been reached.
        /// Must be called every frame (e.g. from the main simulation loop).
        /// </summary>
        public void Update()
        {
            foreach (var ev in _eventBus.Consume<SwitchTimeModeEvent>())
            {
                HandleModeSwitch(ev);
            }

            if (_pendingBarrierWallTicks >= 0
                && _kernel.CurrentTime.TotalWallTicks >= _pendingBarrierWallTicks)
            {
                ExecuteSwapToDeterministic();
                _pendingBarrierWallTicks = -1;
            }
        }

        private void ExecuteSwapToDeterministic()
        {
            var t    = _kernel.CurrentTime.TotalTime;
            var span = System.TimeSpan.FromSeconds(t);
            string simStr = string.Format("{0:D2}:{1:D2}:{2:D2}.{3:D3}",
                (int)span.TotalHours, span.Minutes, span.Seconds, span.Milliseconds);
            FdpLog<DistributedTimeCoordinator>.Info(
                "[Master] Barrier reached. Swapping to SteppedMasterController. SimTime={0}  Frame={1}",
                simStr, _kernel.CurrentTime.FrameNumber);

            var steppedMaster = new SteppedMasterController(_eventBus, _pendingSlaveIds!, _config.SyncConfig);
            _kernel.SwapTimeController(steppedMaster);
        }
    }
}
