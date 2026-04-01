using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using ModuleHost.Core.Time;
using FDP.Toolkit.Time.Messages;

namespace FDP.Toolkit.Time.Controllers
{
    /// <summary>
    /// Slave controller for Deterministic (Lockstep) mode.
    /// Advances time only when FrameOrder is received from Master.
    /// </summary>
    public class SteppedSlaveController : ITimeController
    {
        private readonly FdpEventBus _eventBus;
        private readonly int _localNodeId;
        private readonly float _configuredDelta;
        
        // Time state
        private double _totalTime;
        private long _frameNumber;
        private float _timeScale = 1.0f;
        private double _unscaledTotalTime;
        /// <summary>
        /// Wall-clock tick accumulator — seeded from <see cref="SeedState"/> and advanced on
        /// each processed <see cref="FrameOrderDescriptor"/> by the deterministic tick-equivalent
        /// of the fixed delta.  Ensures <see cref="GlobalTime.TotalWallTicks"/> is continuous
        /// when the kernel transitions back to <see cref="SlaveTimeController"/> or
        /// <see cref="MasterTimeController"/> on Resume.
        /// </summary>
        private long _totalWallTicks;
        
        // Tracks the last FrameID received from the master via a FrameOrderDescriptor.
        // Initialized to -1 meaning "no order received yet". This is intentionally separate
        // from _frameNumber (which is seeded from the slave's own local frame counter via
        // SeedState). The master's frame counter and each slave's local counter naturally
        // diverge during continuous mode, so using _frameNumber+1 to validate the first
        // FrameOrder from the master produces a spurious "out of order" warning.
        private long _lastReceivedOrderFrameId = -1;

        // Frame Queue
        private readonly Queue<FrameOrderDescriptor> _pendingOrders = new();
        
        public SteppedSlaveController(FdpEventBus eventBus, int localNodeId, float fixedDeltaSeconds)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _localNodeId = localNodeId;
            _configuredDelta = fixedDeltaSeconds;
            
            _eventBus.Register<FrameOrderDescriptor>();
        }
        
        public GlobalTime Update()
        {
            // 1. Refill buffer from network.
            // Accept any FrameID that is strictly greater than the last one we processed
            // from the master.  Before the first order arrives (_lastReceivedOrderFrameId=-1)
            // we fall back to comparing against _frameNumber (the slave's seeded local counter)
            // so that stale re-deliveries from before the mode switch are discarded.
            long acceptAfter = _lastReceivedOrderFrameId >= 0 ? _lastReceivedOrderFrameId : _frameNumber;
            var orders = _eventBus.Consume<FrameOrderDescriptor>();
            foreach (var order in orders)
            {
                if (order.FrameID > acceptAfter)
                    _pendingOrders.Enqueue(order);
            }
            
            // 2. Process one frame if available
            if (_pendingOrders.Count > 0)
            {
                // Peek first? Or Dequeue?
                // We must execute ordered.
                // Assuming Queue preserves order (it does).
                // What if order is mixed? (UDP Reordering).
                // In local transport/TCP, ordered. FDP event bus usually ordered locally.
                // But distributed might be unordered.
                // For now assume Ordered or "Next Frame is Filtered".
                
                // Sort?
                // _pendingOrders is a Queue, can't sort.
                // If we care about UDP, we should use a PriorityQueue or List.
                // But simple Queue is efficient.
                
                var order = _pendingOrders.Dequeue();
                
                // Validate sequence against the MASTER's last sent frame ID.
                // Only warn after we've received at least one order — the first order
                // from the master may have a higher FrameID than _lastReceivedOrderFrameId+1
                // because the master's frame counter advanced more frames during the
                // barrier-wait than the slave's local counter did.
                if (_lastReceivedOrderFrameId >= 0 && order.FrameID != _lastReceivedOrderFrameId + 1)
                {
                    FdpLog<SteppedSlaveController>.Warn(
                        "[SteppedSlave] Warning: Out of order frame. Expected {0}, got {1}",
                        _lastReceivedOrderFrameId + 1,
                        order.FrameID);
                }

                // Execute Step — apply master's time scale if provided (≠0), else keep local
                float dt = order.FixedDelta;
                if (dt <= 0) dt = _configuredDelta;
                if (order.TimeScale > 0f) _timeScale = order.TimeScale;

                _lastReceivedOrderFrameId = order.FrameID;
                _frameNumber = order.FrameID;
                _unscaledTotalTime += dt;   // always accumulate unscaled

                // Use master's authoritative post-step TotalTime when provided (non-zero).
                // This is the real fix for the "stale slave time" bug: each slave was seeded
                // from its own local pause moment (arrived ~200 ms before the barrier), so
                // computing seed + delta independently gave a different TotalTime than the
                // master.  With TargetSimTime the slave snaps to the master's exact value.
                if (order.TargetSimTime > 0.0)
                    _totalTime = order.TargetSimTime;
                else
                    _totalTime += dt * _timeScale;
                _totalWallTicks += (long)(dt * System.Diagnostics.Stopwatch.Frequency);
                
                // Send Ack
                SendAck(order.FrameID);
                
                return GetCurrentTime(dt, dt * _timeScale);
            }
            
            // Frozen
            return GetCurrentTime(0f, 0f);
        }
        
        private void SendAck(long frameId)
        {
            _eventBus.Publish(new FrameAckDescriptor
            {
                FrameID = frameId,
                NodeID = _localNodeId,
                Checksum = 0 // Implement hash if needed
            });
        }
        
         private GlobalTime GetCurrentTime(float unscaledDelta, float scaledDelta)
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

        public GlobalTime GetCurrentState() => GetCurrentTime(0, 0);

        public void SeedState(GlobalTime state)
        {
            _frameNumber = state.FrameNumber;
            _totalTime = state.TotalTime;
            _unscaledTotalTime = state.UnscaledTotalTime;
            _timeScale = state.TimeScale;
            _totalWallTicks = state.TotalWallTicks;
            // Reset master-frame tracking so the first FrameOrder from the master is
            // accepted unconditionally regardless of the master's frame counter vs our
            // seeded local frame number (the two can diverge during continuous mode).
            _lastReceivedOrderFrameId = -1;
            _pendingOrders.Clear();
        }

        public void SetTimeScale(float scale)
        {
            _timeScale = scale;
        }

        public float GetTimeScale() => _timeScale;
        public TimeMode GetMode() => TimeMode.Deterministic;

        public void Dispose()
        {
        }
    }
}
