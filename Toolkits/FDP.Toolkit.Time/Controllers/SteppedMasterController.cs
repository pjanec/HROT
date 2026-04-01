using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using ModuleHost.Core.Time;
using FDP.Toolkit.Time.Messages;

namespace FDP.Toolkit.Time.Controllers
{
    /// <summary>
    /// Master controller for Deterministic (Lockstep) mode.
    /// Advances time manually via Step() and coordinates Slaves via FrameOrder/Ack.
    /// </summary>
    public class SteppedMasterController : ISteppableTimeController
    {
        private readonly FdpEventBus _eventBus;
        private readonly HashSet<int> _slaveNodeIds;
        private readonly TimeConfig _config; // Using TimeConfig for simplicity (TimeControllerConfig passes this)
        
        // Time state
        private double _totalTime;
        private long _frameNumber = 0;
        private float _timeScale = 1.0f;
        private double _unscaledTotalTime;
        /// <summary>
        /// Wall-clock tick accumulator seeded from <see cref="SeedState"/> so that
        /// <see cref="GlobalTime.TotalWallTicks"/> is continuous after a
        /// <see cref="SwitchableTimeController.SwitchTo"/> from a
        /// <see cref="MasterTimeController"/>. Each <see cref="Step"/> advances this by
        /// <c>(long)(fixedDeltaTime * Stopwatch.Frequency)</c> — the deterministic
        /// mapping from fixed delta seconds to Stopwatch ticks.
        /// </summary>
        private long _totalWallTicks;
        
        // Lockstep state
        private bool _waitingForAcks;
        private HashSet<int> _pendingAcks;
        private long _lastFrameSequence;
        
        public SteppedMasterController(FdpEventBus eventBus, HashSet<int> nodeIds, TimeConfig config) // Changed signature to match usage
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            if (nodeIds == null) throw new ArgumentNullException(nameof(nodeIds));
            _slaveNodeIds = new HashSet<int>(nodeIds); // Defensive copy — prevents external mutation leaking into controller state
            _config = config ?? TimeConfig.Default;
            _pendingAcks = new HashSet<int>(_slaveNodeIds);
            

            // Must NOT wait initially, otherwise we never start the first step
            _waitingForAcks = false;

            // Register messaging
            _eventBus.Register<FrameOrderDescriptor>();
            _eventBus.Register<TimePulseDescriptor>();
        }
        
        // Ctor overload to match Task 6 signature: (bus, ids, TimeControllerConfig) 
        // Note: TimeControllerConfig contains inner SyncConfig
        public SteppedMasterController(FdpEventBus eventBus, HashSet<int> nodeIds, TimeControllerConfig configWrapper)
             : this(eventBus, nodeIds, configWrapper?.SyncConfig ?? TimeConfig.Default)
        {
        }

        public GlobalTime Update()
        {
            // Process any incoming ACKs but do NOT auto-step.
            // Stepping only happens when Step() is called explicitly (e.g. via OrchestratorSubsystem
            // on receipt of a ClusterOpType.StepTime command from the UI).
            var acks = _eventBus.Consume<FrameAckDescriptor>();
            foreach(var ack in acks) OnAckReceived(ack);

            return GetCurrentTime();
        }
        
        /// <summary>
        /// Manually advance one frame by <paramref name="fixedDeltaTime"/> seconds.
        /// Returns the current (un-advanced) time and is a no-op if the previous frame's
        /// ACKs have not yet all arrived — this protects lockstep integrity when the user
        /// presses the Step button faster than the network round-trip.
        /// </summary>
        public GlobalTime Step(float fixedDeltaTime)
        {
            if (_waitingForAcks)
            {
                // Previous frame not yet acknowledged by all slaves — ignore this step request.
                return GetCurrentTime();
            }

            float scaledDelta = fixedDeltaTime * _timeScale;

            // Update time
            _frameNumber++;
            _totalTime += scaledDelta;
            _unscaledTotalTime += fixedDeltaTime;
            _totalWallTicks += (long)(fixedDeltaTime * Stopwatch.Frequency);

            // Publish a TimePulse so the UI cache (ClusterUiCache) and slave PLL nodes see
            // the new sim time immediately.  MasterTimeController emits this at 1 Hz during
            // continuous mode; SteppedMasterController must emit one on every explicit Step so
            // the Orchestrator panel's sim-time display updates rather than freezing at the
            // value it held when Pause was pressed.
            _eventBus.Publish(new TimePulseDescriptor
            {
                MasterWallTicks = _totalWallTicks,
                SimTimeSnapshot = _totalTime,
                TimeScale       = _timeScale,
                SequenceId      = _frameNumber,
            });

            // Send Order for current frame (carries TimeScale so all slaves apply the same scale)
            var order = new FrameOrderDescriptor
            {
               FrameID         = _frameNumber,
               FixedDelta      = fixedDeltaTime,
               SequenceID      = _frameNumber,
               TimeScale       = _timeScale,
               // Carry the master's post-step TotalTime so every slave can snap to the
               // exact same sim-time rather than computing seed + delta independently
               // (which diverges due to each slave's local seed differing from the master's).
               TargetSimTime   = _totalTime,
            };
            _eventBus.Publish(order);

            var slaveList = string.Join(",", _slaveNodeIds);
            var simSpan   = System.TimeSpan.FromSeconds(_totalTime);
            string simStr = string.Format("{0:D2}:{1:D2}:{2:D2}.{3:D3}",
                (int)simSpan.TotalHours, simSpan.Minutes, simSpan.Seconds, simSpan.Milliseconds);
            FdpLog<SteppedMasterController>.Info(
                "[Master] Step frame {0}: delta={1:F4}s  simTime={2}  waiting for: [{3}]",
                _frameNumber, fixedDeltaTime, simStr, slaveList);
            
            _lastFrameSequence = _frameNumber;
            _pendingAcks.UnionWith(_slaveNodeIds);
            
            if (_slaveNodeIds.Count > 0)
                _waitingForAcks = true;
                
            return GetCurrentTime(fixedDeltaTime, scaledDelta);
        }
        
        private void OnAckReceived(FrameAckDescriptor ack)
        {
            if (ack.FrameID == _lastFrameSequence)
            {
                if (_pendingAcks.Remove(ack.NodeID))
                {
                    if (_pendingAcks.Count == 0)
                    {
                        FdpLog<SteppedMasterController>.Info(
                            "[Master] Frame {0} CONFIRMED by all slaves. Step complete.",
                            _lastFrameSequence);
                        _waitingForAcks = false;
                    }
                    else
                    {
                        FdpLog<SteppedMasterController>.Info(
                            "[Master] Ack frame {0} from node {1}. Still waiting for: [{2}]",
                            ack.FrameID,
                            ack.NodeID,
                            string.Join(",", _pendingAcks));
                    }
                }
            }
        }
        
        private GlobalTime GetCurrentTime(float unscaledDelta = 0f, float scaledDelta = 0f)
        {
            return new GlobalTime
            {
                FrameNumber = _frameNumber,
                DeltaTime = scaledDelta,
                TotalTime = _totalTime,
                TimeScale = _timeScale,
                UnscaledDeltaTime = unscaledDelta,
                UnscaledTotalTime = _unscaledTotalTime,
                StartWallTicks = 0,
                TotalWallTicks = _totalWallTicks
            };
        }

        public GlobalTime GetCurrentState() => GetCurrentTime();

        public void SeedState(GlobalTime state)
        {
            _frameNumber = state.FrameNumber;
            _totalTime = state.TotalTime;
            _unscaledTotalTime = state.UnscaledTotalTime;
            _timeScale = state.TimeScale;
            // Preserves wall-clock continuity across SwitchableTimeController.SwitchTo:
            // barrier math in DistributedTimeCoordinator/SlaveTimeModeListener depends on
            // TotalWallTicks being the same PLL-synchronized virtual clock, not a
            // re-derived approximation from _unscaledTotalTime * Stopwatch.Frequency.
            _totalWallTicks = state.TotalWallTicks;
            
            _pendingAcks.Clear();
            _waitingForAcks = false;
        }

        public void SetTimeScale(float scale)
        {
            _timeScale = scale;
        }

        public float GetTimeScale() => _timeScale;
        public TimeMode GetMode() => TimeMode.Deterministic;

        public void Dispose()
        {
            // clean up subscriptions? EventBus might hold weak refs or we should unsubscribe?
            // FdpEventBus typical pattern doesn't mandate unsubscribe if transient, but good practice.
        }
    }
}
