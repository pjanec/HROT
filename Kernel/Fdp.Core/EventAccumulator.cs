using System;
using System.Collections.Generic;
using System.Buffers;

namespace Fdp.Kernel
{
    /// <summary>
    /// Captures event history from live bus and flushes to replica buses.
    /// Enables slow modules to see accumulated events since last run.
    /// </summary>
    public sealed class EventAccumulator
    {
        private readonly Queue<FrameEventData> _history = new Queue<FrameEventData>();
        private readonly int _maxHistoryFrames;
        
        public EventAccumulator(int maxHistoryFrames = 10)
        {
            _maxHistoryFrames = maxHistoryFrames;
        }
        
        /// <summary>
        /// Captures events from live bus for a frame.
        /// Called on main thread after simulation phase.
        /// </summary>
        public void CaptureFrame(FdpEventBus liveBus, uint frameIndex)
        {
            // Extract event buffers (non-destructive)
            var frameData = liveBus.SnapshotCurrentBuffers();
            frameData.FrameIndex = frameIndex;
            
            _history.Enqueue(frameData);
            
            // Trim old history
            while (_history.Count > _maxHistoryFrames)
            {
                var old = _history.Dequeue();
                old.Dispose(); // Return buffers to pool
            }
        }
        
        /// <summary>
        /// Flushes accumulated history to replica bus.
        /// Only events AFTER lastSeenTick are flushed.
        /// Called on main thread during sync point.
        /// </summary>
        public void FlushToReplica(FdpEventBus replicaBus, uint lastSeenTick)
        {
            foreach (var frameData in _history)
            {
                if (frameData.FrameIndex <= lastSeenTick)
                    continue; // Already seen
                
                // Inject events into replica bus (append to existing)
                replicaBus.InjectEvents(frameData);
            }
        }
    }
    
    /// <summary>
    /// Captured event data for a single frame.
    /// Pooled to avoid allocations where possible.
    /// Uses ArrayPool for data buffers.
    /// </summary>
    public struct FrameEventData : IDisposable
    {
        public uint FrameIndex;
        // Tuple: TypeId, BufferFromPool, ActualLength, ElementSize
        public List<(int TypeId, byte[] Buffer, int Length, int ElementSize)> NativeEvents;
        // Tuple: TypeId, ObjectsFromPool, ActualCount, EventType
        public List<(int TypeId, object[] Objects, int Count, Type EventType)> ManagedEvents;
        
        public void Dispose()
        {
            if (NativeEvents != null)
            {
                foreach (var item in NativeEvents)
                {
                    if (item.Buffer != null)
                        ArrayPool<byte>.Shared.Return(item.Buffer);
                }
            }
            if (ManagedEvents != null)
            {
                foreach (var item in ManagedEvents)
                {
                   if (item.Objects != null)
                        ArrayPool<object>.Shared.Return(item.Objects);
                }
            }
        }
    }
}
