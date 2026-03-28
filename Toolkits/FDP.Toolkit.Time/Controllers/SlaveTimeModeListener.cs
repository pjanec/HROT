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

        // Barrier state — wall-tick-based (not frame-based).
        // -1 means no pending barrier.
        private long _pendingBarrierWallTicks = -1;
        private SwitchTimeModeEvent? _pendingEvent;

        public SlaveTimeModeListener(FdpEventBus eventBus, ModuleHostKernel kernel, TimeControllerConfig config)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _config = config ?? throw new ArgumentNullException(nameof(config));

            _eventBus.Register<SwitchTimeModeEvent>();
        }

        /// <summary>
        /// Polls for incoming <see cref="SwitchTimeModeEvent"/> events and fires the
        /// controller swap when <see cref="GlobalTime.TotalWallTicks"/> crosses the barrier.
        /// Must be called every frame.
        /// </summary>
        public void Update()
        {
            // Poll for events
            foreach (var evt in _eventBus.Consume<SwitchTimeModeEvent>())
            {
                OnModeSwitchRequested(evt);
            }

            if (_pendingBarrierWallTicks >= 0
                && _kernel.CurrentTime.TotalWallTicks >= _pendingBarrierWallTicks)
            {
                if (_pendingEvent.HasValue)
                {
                    ExecuteSwapToDeterministic(_pendingEvent.Value);
                }
                _pendingBarrierWallTicks = -1;
                _pendingEvent = null;
            }
        }

        private void OnModeSwitchRequested(SwitchTimeModeEvent evt)
        {
            if (evt.TargetMode == TimeMode.Deterministic)
            {
                _pendingBarrierWallTicks = evt.BarrierWallTicks;
                _pendingEvent = evt;

                FdpLog<SlaveTimeModeListener>.Info(
                    "[Slave] Received Deterministic barrier BarrierWallTicks={0} (current TotalWallTicks={1})",
                    evt.BarrierWallTicks,
                    _kernel.CurrentTime.TotalWallTicks);

                // Safety: if we are already past the barrier (very late DDS delivery), swap immediately.
                if (_kernel.CurrentTime.TotalWallTicks >= evt.BarrierWallTicks)
                {
                    FdpLog<SlaveTimeModeListener>.Warn(
                        "[Slave] Already past barrier (TotalWallTicks={0} >= {1}). Swapping immediately.",
                        _kernel.CurrentTime.TotalWallTicks,
                        evt.BarrierWallTicks);
                    ExecuteSwapToDeterministic(evt);
                    _pendingBarrierWallTicks = -1;
                    _pendingEvent = null;
                }
            }
            else if (evt.TargetMode == TimeMode.Continuous)
            {
                // Unpause is always immediate — no barrier needed.
                ExecuteSwapToContinuous(evt);
            }
        }

        private void ExecuteSwapToDeterministic(SwitchTimeModeEvent evt)
        {
            FdpLog<SlaveTimeModeListener>.Info(
                "[Slave] Barrier reached (TotalWallTicks={0}). Swapping to SteppedSlaveController.",
                _kernel.CurrentTime.TotalWallTicks);

            float fixedDelta = evt.FixedDelta > 0 ? evt.FixedDelta : _config.SyncConfig.FixedDeltaSeconds;

            var steppedSlave = new SteppedSlaveController(_eventBus, _config.LocalNodeId, fixedDelta);

            // Seed from local state to ensure no rewind — jitter-free continuity.
            var localState = _kernel.GetTimeController().GetCurrentState();
            steppedSlave.SeedState(localState);

            _kernel.SwapTimeController(steppedSlave);
        }

        private void ExecuteSwapToContinuous(SwitchTimeModeEvent evt)
        {
            FdpLog<SlaveTimeModeListener>.Info("[Slave] Switching to Continuous Mode.");

            var slave = new SlaveTimeController(_eventBus, _config.SyncConfig);
            var localState = _kernel.GetTimeController().GetCurrentState();
            slave.SeedState(localState);

            _kernel.SwapTimeController(slave);
        }
    }
}
