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

        // Barrier state — wall-tick-based (not frame-based).
        // -1 means no pending barrier.
        private long _pendingBarrierWallTicks = -1;
        private HashSet<int>? _pendingSlaveIds;

        public DistributedTimeCoordinator(FdpEventBus eventBus, ModuleHostKernel kernel,
                                          TimeControllerConfig config, HashSet<int> slaveNodeIds)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _slaveNodeIds = slaveNodeIds;

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

            // Publish so every slave receives BarrierWallTicks and can mirror the swap.
            _eventBus.Publish(new SwitchTimeModeEvent
            {
                TargetMode = TimeMode.Deterministic,
                BarrierWallTicks = barrierWallTicks,
                FixedDelta = _config.SyncConfig.FixedDeltaSeconds
            });

            FdpLog<DistributedTimeCoordinator>.Info(
                "[Master] Scheduled barrier at TotalWallTicks={0} (current={1}, lookahead={2} ticks)",
                barrierWallTicks,
                currentState.TotalWallTicks,
                lookahead);
        }

        /// <summary>
        /// Initiates an immediate switch back to Continuous (Real-time) mode.
        /// Cancels any pending barrier and broadcasts a continuous-mode event.
        /// </summary>
        public void SwitchToContinuous()
        {
            // Cancel pending barrier
            _pendingBarrierWallTicks = -1;

            // Publish so slaves also switch immediately (BarrierWallTicks = 0 → no wait).
            _eventBus.Publish(new SwitchTimeModeEvent
            {
                TargetMode = TimeMode.Continuous,
                BarrierWallTicks = 0
            });

            // Swap master immediately
            var master = new MasterTimeController(_eventBus, _config.SyncConfig);
            _kernel.SwapTimeController(master);

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
            FdpLog<DistributedTimeCoordinator>.Info(
                "[Master] Barrier reached (TotalWallTicks={0}). Swapping to SteppedMasterController.",
                _kernel.CurrentTime.TotalWallTicks);

            var steppedMaster = new SteppedMasterController(_eventBus, _pendingSlaveIds!, _config.SyncConfig);
            _kernel.SwapTimeController(steppedMaster);
        }
    }
}
