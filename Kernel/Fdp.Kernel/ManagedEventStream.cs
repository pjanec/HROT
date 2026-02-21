using System;
using System.Collections.Generic;
using System.Linq;

namespace Fdp.Kernel
{
    /// <summary>
    /// Type-agnostic interface for managed event stream operations.
    /// Used by FdpEventBus to avoid dynamic dispatch.
    /// </summary>
    public interface IManagedEventStream
    {
        void WriteRaw(object evt);
        void Swap();
        void ClearCurrent();
    }

    /// <summary>
    /// Double-buffered event stream for managed (reference type) events.
    /// Uses locking for thread safety since List<T> is not thread-safe.
    /// Suitable for low-volume events (< 100/frame).
    /// </summary>
    /// <typeparam name="T">Managed event type — class or struct-containing-references (managed struct)</typeparam>
    public class ManagedEventStream<T> : IManagedEventStreamInfo, IEventStreamInspector, IManagedEventStream
    {
        // Double buffers: front for reading, back for writing
        private List<T> _front = new List<T>();
        private List<T> _back = new List<T>();
        
        private readonly object _lock = new object();

        // IManagedEventStreamInfo implementation
        public int TypeId => typeof(T).FullName!.GetHashCode() & 0x7FFFFFFF;
        public Type EventType => typeof(T);
        
        // IEventStreamInspector implementation
        public int EventTypeId => TypeId;
        
        // Note: The public Count property returns the BACK (Write) buffer count for historical reasons.
        // The interface requires the READ buffer count.
        int IEventStreamInspector.Count => _front.Count;

        public IEnumerable<object> InspectReadBuffer()
        {
            foreach (var item in _front)
            {
                yield return item;
            }
        }

        public IEnumerable<object> InspectWriteBuffer()
        {
            lock (_lock)
            {
                // Return a copy to avoid concurrency issues during enumeration.
                // Cast<object>() is used instead of new List<object>(_back) because
                // List<T>.IEnumerable<T> is not covariant to IEnumerable<object> for value-type T.
                return _back.Cast<object>().ToList();
            }
        }

        // Zero-Alloc access to pending events.
        // WARNING: Not thread-safe if concurrent writes occur. Assumes recording happens in safe phase.
        public System.Collections.IList PendingEvents => _back;

        /// <summary>
        /// Writes an event to the stream.
        /// Thread-safe via locking.
        /// </summary>
        public void Write(T evt)
        {
            // For reference types, guard against null. For value types this check is always false.
            if (evt is null)
                throw new ArgumentNullException(nameof(evt));

            lock (_lock)
            {
                _back.Add(evt);
            }
        }

        public void WriteRaw(object evt)
        {
            Write((T)evt);
        }

        /// <summary>
        /// Returns read-only list of events from previous frame.
        /// Safe to call during read phase (after swap).
        /// </summary>
        public IReadOnlyList<T> Read() => _front;

        /// <summary>
        /// Swaps read/write buffers.
        /// Called at end of frame in PostSimulation phase.
        /// </summary>
        public void Swap()
        {
            lock (_lock)
            {
                // Swap buffer references
                var temp = _front;
                _front = _back;
                _back = temp;

                // Clear the new write buffer (old read buffer)
                _back.Clear();
            }
        }
        
        /// <summary>
        /// Injects events directly into the current (read) buffer.
        /// Used by Flight Recorder during replay.
        /// </summary>
        public void InjectIntoCurrent(IEnumerable<T> events)
        {
            _front.AddRange(events);
        }

        /// <summary>
        /// Clears the current (read) buffer.
        /// Used by Flight Recorder during replay to clear state before injection.
        /// </summary>
        public void ClearCurrent()
        {
            _front.Clear();
        }

        /// <summary>
        /// Gets the pending (write) buffer for recording.
        /// Used by Flight Recorder to capture events before swap.
        /// </summary>
        public IReadOnlyList<T> GetPendingList()
        {
            lock (_lock)
            {
                // Return copy to avoid race conditions
                return new List<T>(_back);
            }
        }

        /// <summary>
        /// Gets current event count (for debugging).
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _back.Count;
                }
            }
        }
    }
}
