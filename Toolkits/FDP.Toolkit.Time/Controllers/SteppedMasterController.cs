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
        }
        
        // Ctor overload to match Task 6 signature: (bus, ids, TimeControllerConfig) 
        // Note: TimeControllerConfig contains inner SyncConfig
        public SteppedMasterController(FdpEventBus eventBus, HashSet<int> nodeIds, TimeControllerConfig configWrapper)
             : this(eventBus, nodeIds, configWrapper?.SyncConfig ?? TimeConfig.Default)
        {
        }

        public GlobalTime Update()
        {
            // Debug file output removed

            // Process any incoming ACKs
            var acks = _eventBus.Consume<FrameAckDescriptor>();
            foreach(var ack in acks) OnAckReceived(ack);
            
            // In lockstep master, we automatically advance if all ACKs are received
            if (!_waitingForAcks)
            {
                var t = Step(_config.FixedDeltaSeconds);
                // Console.Out.Flush(); // Ensure logs are flushed
                return t;
            }
            // else Console.WriteLine($"[SteppedMaster] Waiting... Need: {string.Join(",", _pendingAcks)} for Frame {_lastFrameSequence}");
            
            return GetCurrentTime();
        }
        
        /// <summary>
        /// Manually advance one frame.
        /// </summary>
        public GlobalTime Step(float fixedDeltaTime)
        {
            float scaledDelta = fixedDeltaTime * _timeScale;

            // Update time
            _frameNumber++;
            _totalTime += scaledDelta;
            _unscaledTotalTime += fixedDeltaTime;
            
            // Send Order for current frame
            var order = new FrameOrderDescriptor 
            { 
               FrameID = _frameNumber,
               FixedDelta = fixedDeltaTime,
               SequenceID = _frameNumber 
            };
            _eventBus.Publish(order);
            
            var slaveList = string.Join(",", _slaveNodeIds);
            // File output removed
            FdpLog<SteppedMasterController>.Info(
                "[DEBUG-MASTER] Frame {0}. Sent Order. Waiting for: {1}",
                _frameNumber,
                slaveList);
            
            _lastFrameSequence = _frameNumber;
            _pendingAcks.UnionWith(_slaveNodeIds);
            
            if (_slaveNodeIds.Count > 0)
                _waitingForAcks = true;
                
            return GetCurrentTime(fixedDeltaTime, scaledDelta);
        }
        
        private void OnAckReceived(FrameAckDescriptor ack)
        {
            var pendingList = string.Join(",", _pendingAcks);
            // File output removed
            FdpLog<SteppedMasterController>.Info(
                "[DEBUG-MASTER] Ack {0} from {1}. Need {2}. Pending: {3}",
                ack.FrameID,
                ack.NodeID,
                _lastFrameSequence,
                pendingList);
            
            if (ack.FrameID == _lastFrameSequence)
            {
                if (_pendingAcks.Remove(ack.NodeID))
                {
                    if (_pendingAcks.Count == 0)
                    {
                        FdpLog<SteppedMasterController>.Info(
                            "[DEBUG-MASTER] Frame {0} CONFIRMED. Advancing.",
                            _lastFrameSequence);
                        _waitingForAcks = false;
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
                TotalWallTicks = (long)(_unscaledTotalTime * Stopwatch.Frequency)
            };
        }

        public GlobalTime GetCurrentState() => GetCurrentTime();

        public void SeedState(GlobalTime state)
        {
            _frameNumber = state.FrameNumber;
            _totalTime = state.TotalTime;
            _unscaledTotalTime = state.UnscaledTotalTime;
            _timeScale = state.TimeScale;
            
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
