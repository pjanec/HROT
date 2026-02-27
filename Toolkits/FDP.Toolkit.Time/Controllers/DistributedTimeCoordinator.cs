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
    /// Handles the "Future Barrier" synchronization to ensure jitter-free transitions.
    /// </summary>
    public class DistributedTimeCoordinator
    {
        private readonly FdpEventBus _eventBus;
        private readonly ModuleHostKernel _kernel;
        private readonly TimeControllerConfig _config;
        private readonly HashSet<int> _slaveNodeIds;
        
        // Barrier State
        private long _pendingBarrierFrame = -1;
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
            var currentName = _kernel.GetTimeController().GetType().Name;
            bool isDet = currentName.Contains("Stepped");

            if (ev.TargetMode == TimeMode.Deterministic && !isDet)
            {
                // Accept request if BarrierFrame is 0 (immediate request from Input or unscheduled source)
                if (ev.BarrierFrame == 0)
                {
                    SwitchToDeterministic(_slaveNodeIds);
                }
            }
            else if (ev.TargetMode == TimeMode.Continuous && isDet)
            {
                // Immediate switch
                SwitchToContinuous();
            }
        }
        
        /// <summary>
        /// Initiates a switch to Deterministic (Paused/Stepped) mode.
        /// Broadcasts a future barrier frame and waits for it.
        /// </summary>
        public void SwitchToDeterministic(HashSet<int> slaveNodeIds)
        {
            var currentState = _kernel.GetTimeController().GetCurrentState();
            
            // Plan barrier using configured lookahead
            // Use SyncConfig properties
            int lookahead = _config.SyncConfig.PauseBarrierFrames;
            long barrierFrame = currentState.FrameNumber + lookahead;
            
            _pendingBarrierFrame = barrierFrame;
            _pendingSlaveIds = slaveNodeIds;
            
            // Publish mode switch event
            _eventBus.Publish(new SwitchTimeModeEvent
            {
                TargetMode = TimeMode.Deterministic,
                FrameNumber = currentState.FrameNumber, // Ref time
                TotalTime = currentState.TotalTime,     // Ref time
                BarrierFrame = barrierFrame,
                FixedDeltaSeconds = _config.SyncConfig.FixedDeltaSeconds
            });
            
            FdpLog<DistributedTimeCoordinator>.Info(
                "[Master] Scheduled Pause at Frame {0} (Current: {1}, Lookahead: {2})",
                barrierFrame,
                currentState.FrameNumber,
                lookahead);
        }
        
        /// <summary>
        /// Initiates a switch to Continuous (Real-time) mode.
        /// Broadcasts switch event and immediately swaps (Atomic change usually safe for unpause).
        /// </summary>
        public void SwitchToContinuous()
        {
            // Cancel pending barrier
            _pendingBarrierFrame = -1;
            
            var currentState = _kernel.GetTimeController().GetCurrentState();
            
            // Publish switch event
            _eventBus.Publish(new SwitchTimeModeEvent
            {
                TargetMode = TimeMode.Continuous,
                FrameNumber = currentState.FrameNumber,
                TotalTime = currentState.TotalTime,
                BarrierFrame = 0 // Immediate
                // No IDs needed for continuous
            });
            
            // Swap immediately
            var master = new MasterTimeController(_eventBus, _config.SyncConfig);
            _kernel.SwapTimeController(master);
            
             FdpLog<DistributedTimeCoordinator>.Info(
                 "[Master] Switched to Continuous Mode at Frame {0}",
                 currentState.FrameNumber);
        }
        
        /// <summary>
        /// Checks if barrier frame is reached to execute the pending swap.
        /// Must be called every frame (e.g. from DemoSimulation or Kernel tick).
        /// </summary>
        public void Update()
        {
            foreach(var ev in _eventBus.Consume<SwitchTimeModeEvent>())
            {
                HandleModeSwitch(ev);
            }

            if (_pendingBarrierFrame != -1 && _kernel.CurrentTime.FrameNumber >= _pendingBarrierFrame)
            {
                ExecuteSwapToDeterministic();
                _pendingBarrierFrame = -1;
            }
        }
        
        private void ExecuteSwapToDeterministic()
        {
            FdpLog<DistributedTimeCoordinator>.Info("[Master] Barrier Reached. Swapping to SteppedMasterController.");
            
            // Create new controller
            // Note: We use the *current* state of kernel (which should be at BarrierFrame) via Swap logic
            var steppedMaster = new SteppedMasterController(_eventBus, _pendingSlaveIds!, _config.SyncConfig);
            
            // Swap
            _kernel.SwapTimeController(steppedMaster);
        }
    }
}
