using System;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using ModuleHost.Core.Network;
using ModuleHost.Core.Time;
using ModuleHost.Core;
using FDP.Toolkit.Time.Messages;

namespace FDP.Toolkit.Time.Controllers
{
    /// <summary>
    /// Listens for <see cref="SwitchTimeModeEvent"/> on Slave nodes and performs the
    /// controller swap once the local <see cref="GlobalTime.TotalWallTicks"/> reaches the
    /// Future Barrier specified in the event — ensuring the swap fires within one ECS tick
    /// of the master regardless of per-node frame rates or ECS frame counters.
    /// </summary>
    public class SlaveTimeModeListener
    {
        private readonly FdpEventBus _eventBus;
        private readonly ModuleHostKernel _kernel;
        private readonly TimeControllerConfig _config;
        private readonly string _instanceName;

        /// <summary>
        /// Optional factory that produces the <see cref="ITimeController"/> to install when
        /// the cluster resumes (switches back to <see cref="TimeMode.Continuous"/>).
        /// <para>
        /// When <see langword="null"/> (default, used by IG / CGF) a fresh
        /// <see cref="SlaveTimeController"/> is created.  Nodes that own the authoritative
        /// simulation clock — specifically SimHost — must pass a factory that returns a new
        /// <see cref="MasterTimeController"/> so that <c>TimePulseDescriptor</c> publication
        /// resumes after every Pause/Resume cycle.
        /// </para>
        /// </summary>
        private readonly Func<ITimeController>? _continuousControllerFactory;

        /// <param name="continuousControllerFactory">
        /// Optional factory for the controller installed on Resume.
        /// Pass <see langword="null"/> to use the default <see cref="SlaveTimeController"/>.
        /// </param>
        /// <param name="instanceName">
        /// Human-readable subsystem name for log messages (e.g. "SimHost", "IG-300", "CGF-400").
        /// Defaults to <c>"Slave:{nodeId}"</c> when not provided.
        /// </param>
        public SlaveTimeModeListener(FdpEventBus eventBus, ModuleHostKernel kernel,
            TimeControllerConfig config,
            Func<ITimeController>? continuousControllerFactory = null,
            string? instanceName = null)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _continuousControllerFactory = continuousControllerFactory;
            _instanceName = instanceName ?? $"Slave:{config.LocalNodeId}";

            _eventBus.Register<SwitchTimeModeEvent>();
        }

        /// <summary>
        /// Polls for incoming <see cref="SwitchTimeModeEvent"/> events and immediately
        /// performs the controller swap.  Must be called every frame.
        ///
        /// <para>The barrier-wait logic that existed in earlier revisions compared the
        /// slave's <see cref="GlobalTime.TotalWallTicks"/> against a barrier computed on
        /// the master node.  Because each node accumulates wall-ticks from its own
        /// <see cref="System.Diagnostics.Stopwatch"/> (started at different absolute times),
        /// the two counters are not comparable across DDS nodes — the slave's counter is
        /// typically seconds behind the master's, so the barrier was never crossed.  The
        /// DDS delivery round-trip itself provides sufficient temporal spacing; an
        /// idempotent guard prevents double-swaps on duplicate or replayed messages.</para>
        /// </summary>
        public void Update()
        {
            foreach (var evt in _eventBus.Consume<SwitchTimeModeEvent>())
            {
                OnModeSwitchRequested(evt);
            }
        }

        private void OnModeSwitchRequested(SwitchTimeModeEvent evt)
        {
            if (evt.TargetMode == TimeMode.Deterministic)
            {
                // Idempotent: skip if already in Deterministic mode to avoid re-seeding
                // SteppedSlaveController on duplicate or replayed DDS messages.
                if (_kernel.GetTimeController().GetMode() == TimeMode.Deterministic)
                    return;

                var pauseSpan = System.TimeSpan.FromSeconds(_kernel.CurrentTime.TotalTime);
                string simStr = string.Format("{0:D2}:{1:D2}:{2:D2}.{3:D3}",
                    (int)pauseSpan.TotalHours, pauseSpan.Minutes, pauseSpan.Seconds, pauseSpan.Milliseconds);
                FdpLog<SlaveTimeModeListener>.Info(
                    "[{0}] Pausing → SteppedSlaveController. SimTime={1}",
                    _instanceName, simStr);
                ExecuteSwapToDeterministic(evt);
            }
            else if (evt.TargetMode == TimeMode.Continuous)
            {
                // Idempotent: if already in Continuous mode do not re-swap.
                // Without this guard every DDS loopback echo of SwitchTimeModeEvent(Continuous)
                // would trigger another SwapTimeController call, which (a) needlessly re-creates
                // the controller, (b) calls SetTimeScale(1) on the new SlaveTimeController via
                // ModuleHostKernel.SwapTimeController, generating the "[SlaveTimeController]
                // Warning: SetTimeScale called locally" spam seen in production logs.
                if (_kernel.GetTimeController().GetMode() == TimeMode.Continuous)
                    return;

                // Unpause is always immediate — no barrier needed.
                ExecuteSwapToContinuous(evt);
            }
        }

        private void ExecuteSwapToDeterministic(SwitchTimeModeEvent evt)
        {
            float fixedDelta = evt.FixedDelta > 0 ? evt.FixedDelta : _config.SyncConfig.FixedDeltaSeconds;

            var steppedSlave = new SteppedSlaveController(_eventBus, _config.LocalNodeId, fixedDelta);

            // Seed from local state to ensure no rewind — jitter-free continuity.
            var localState = _kernel.GetTimeController().GetCurrentState();
            steppedSlave.SeedState(localState);

            // Apply the time scale that was active on the master at the moment of Pause.
            // Without this, SteppedSlaveController inherits the slave's local scale
            // (which could differ if SetTimeScale was applied to master but not yet
            // propagated to this slave via TimePulse).
            if (evt.TimeScale > 0f)
                steppedSlave.SetTimeScale(evt.TimeScale);

            _kernel.SwapTimeController(steppedSlave);
        }

        private void ExecuteSwapToContinuous(SwitchTimeModeEvent evt)
        {
            var resumeSpan = System.TimeSpan.FromSeconds(_kernel.CurrentTime.TotalTime);
            string simStr = string.Format("{0:D2}:{1:D2}:{2:D2}.{3:D3}",
                (int)resumeSpan.TotalHours, resumeSpan.Minutes, resumeSpan.Seconds, resumeSpan.Milliseconds);
            FdpLog<SlaveTimeModeListener>.Info(
                "[{0}] Resuming → Continuous. SimTime={1}",
                _instanceName, simStr);

            // Use the caller-supplied factory when available (e.g. SimHost supplies a
            // MasterTimeController factory so TimePulseDescriptor publication resumes after
            // every Pause/Resume cycle).  Pure slave nodes (IG, CGF) fall back to a fresh
            // SlaveTimeController driven by the master's TimePulse.
            ITimeController continuous = _continuousControllerFactory != null
                ? _continuousControllerFactory()
                : new SlaveTimeController(_eventBus, _config.SyncConfig);

            var localState = _kernel.GetTimeController().GetCurrentState();

            // Resume event carries the master's authoritative sim-time (populated by
            // DistributedTimeCoordinator.SwitchToContinuous).  Seed the new controller
            // from that value so all nodes resume at the master's post-step time, not
            // each slave's own locally-accumulated time (which differs by the pause-barrier
            // delay and causes the UI to jump backwards after Pause → Step → Resume).
            if (evt.SimTimeSnapshot > 0.0)
            {
                localState = new GlobalTime
                {
                    FrameNumber       = localState.FrameNumber,
                    DeltaTime         = localState.DeltaTime,
                    TotalTime         = evt.SimTimeSnapshot,
                    TimeScale         = localState.TimeScale,
                    UnscaledDeltaTime = localState.UnscaledDeltaTime,
                    UnscaledTotalTime = evt.SimTimeSnapshot,
                    StartWallTicks    = localState.StartWallTicks,
                    TotalWallTicks    = localState.TotalWallTicks,
                };
            }

            continuous.SeedState(localState);

            // Apply the time scale ordered by the master on Resume.
            // Without this, resuming after a SetTimeScale change (while paused) ignores
            // the new speed — the controller is seeded at the old scale from localState.
            if (evt.TimeScale > 0f)
                continuous.SetTimeScale(evt.TimeScale);

            _kernel.SwapTimeController(continuous);
        }
    }
}
