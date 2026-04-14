using System;
using System.Collections.Generic;
using System.Threading;

namespace Fdp.Kernel
{
    /// <summary>
    /// Lock-free, auto-expanding event stream for unmanaged events.
    /// Uses TRUE double-buffering: write to _writeBuffer, read from _readBuffer.
    /// Thread-safe for concurrent writes via atomic reservation.
    /// </summary>
    /// <typeparam name="T">Unmanaged event type with [EventId] attribute</typeparam>
    public unsafe class NativeEventStream<T> : INativeEventStream, IEventStreamInspector, IDisposable where T : unmanaged
    {
        // Double buffers
        private Buffer _readBuffer;
        private Buffer _writeBuffer;
        
        // Lock used ONLY during swap and resize
        private readonly object _lock = new object();
        
        // IEventStreamInspector Implementation
        public Type EventType => typeof(T);
        public int Count => _readBuffer.Count;

        public IEnumerable<object> InspectReadBuffer()
        {
            // Read buffer is stable (only modified during Swap which is single-threaded usually)
            var list = new List<object>(_readBuffer.Count);
            for (int i = 0; i < _readBuffer.Count; i++)
            {
                list.Add(_readBuffer.Data[i]);
            }
            return list;
        }

        public IEnumerable<object> InspectWriteBuffer()
        {
            // Write buffer might be changing if simulation is running
            // Use snapshot of current count limited by capacity
            int count = Math.Min(_writeBuffer.Count, _writeBuffer.Capacity);
            var list = new List<object>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(_writeBuffer.Data[i]);
            }
            return list;
        }

        private bool _disposed;

        /// <summary>
        /// Event type ID from [EventId] attribute.
        /// </summary>
        public int EventTypeId => EventType<T>.Id;

        /// <summary>
        /// Size of a single event in bytes.
        /// </summary>
        public int ElementSize => sizeof(T);

        /// <summary>
        /// Creates a new native event stream with specified initial capacity.
        /// </summary>
        /// <param name="initialCapacity">Initial buffer size (default: 1024 events)</param>
        public NativeEventStream(int initialCapacity = 1024)
        {
            if (initialCapacity <= 0)
                throw new ArgumentException("Initial capacity must be positive", nameof(initialCapacity));

            _readBuffer = new Buffer(initialCapacity);
            _writeBuffer = new Buffer(initialCapacity);
        }

        /// <summary>
        /// Writes an event from raw memory.
        /// </summary>
        public unsafe void WriteRaw(void* data)
        {
             Write(System.Runtime.CompilerServices.Unsafe.AsRef<T>(data));
        }

        /// <summary>
        /// Writes an event to the write buffer.
        /// Thread-safe: multiple threads can write concurrently.
        /// Auto-expands buffer if capacity is exceeded.
        /// </summary>
        public void Write(in T evt)
        {
            // 1. Optimistic Reservation (lock-free)
            int index = Interlocked.Increment(ref _writeBuffer.Count) - 1;

            // 2. Fast Path (99.9% of cases)
            if (index < _writeBuffer.Capacity)
            {
                _writeBuffer.Data[index] = evt;
                return;
            }

            // 3. Slow Path (buffer full, need to resize)
            ResizeAndWrite(index, evt);
        }

        /// <summary>
        /// Resizes the write buffer and writes the event.
        /// </summary>
        private void ResizeAndWrite(int intendedIndex, in T evt)
        {
            lock (_lock)
            {
                // Double-check: another thread might have resized already
                if (intendedIndex < _writeBuffer.Capacity)
                {
                    _writeBuffer.Data[intendedIndex] = evt;
                    return;
                }

                // Expand buffer (2x or enough to fit intendedIndex)
                int newCapacity = Math.Max(_writeBuffer.Capacity * 2, intendedIndex + 1);
                _writeBuffer.Resize(newCapacity);

                // Write the event
                _writeBuffer.Data[intendedIndex] = evt;
            }
        }

        /// <summary>
        /// Returns read-only span of all events in the read buffer.
        /// Safe to call during read phase (after swap).
        /// </summary>
        public ReadOnlySpan<T> Read()
        {
            return new ReadOnlySpan<T>(_readBuffer.Data, _readBuffer.Count);
        }

        /// <summary>
        /// Gets raw byte representation of all events in read buffer.
        /// Used by recorder for serialization.
        /// </summary>
        public ReadOnlySpan<byte> GetRawBytes()
        {
            return new ReadOnlySpan<byte>(_readBuffer.Data, _readBuffer.Count * sizeof(T));
        }

        /// <summary>
        /// Swaps read/write buffers.
        /// Called at end of frame in PostSimulation phase.
        /// </summary>
        public void Swap()
        {
            lock (_lock)
            {
                // Swap buffer references
                var temp = _readBuffer;
                _readBuffer = _writeBuffer;
                _writeBuffer = temp;

                // Clear the new write buffer (old read buffer)
                _writeBuffer.Clear();
            }
        }

        /// <summary>
        /// Clears both buffers. Used during disposal.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _readBuffer.Clear();
                _writeBuffer.Clear();
            }
        }

        // ========== FLIGHT RECORDER INTEGRATION ==========

        /// <summary>
        /// Gets raw bytes from the WRITE (Pending) buffer.
        /// Used by Flight Recorder during PostSimulation to capture events that just happened.
        /// </summary>
        public ReadOnlySpan<byte> GetPendingBytes()
        {
            // No lock needed - we only read Count (atomic) and Data (stable during simulation)
            return new ReadOnlySpan<byte>(_writeBuffer.Data, _writeBuffer.Count * sizeof(T));
        }

        /// <summary>
        /// Injects raw bytes directly into the READ (Current) buffer.
        /// Used by Flight Recorder during replay to make events immediately consumable.
        /// BYPASSES normal Publish/Swap flow.
        /// </summary>
        public void InjectIntoCurrent(ReadOnlySpan<byte> data)
        {
            lock (_lock)
            {
                int eventCount = data.Length / sizeof(T);
                int newCount = _readBuffer.Count + eventCount;
                
                // Ensure capacity
                if (newCount > _readBuffer.Capacity)
                {
                    _readBuffer.Resize(newCount);
                }
                
                // Copy data directly (Append)
                long offsetBytes = (long)_readBuffer.Count * sizeof(T);
                fixed (byte* src = data)
                {
                    System.Buffer.MemoryCopy(src, (byte*)_readBuffer.Data + offsetBytes, _readBuffer.Size - offsetBytes, data.Length);
                }
                
                _readBuffer.Count = newCount;
            }
        }

        /// <summary>
        /// Clears the READ (Current) buffer.
        /// Used during replay to prevent mixing old events with injected ones.
        /// </summary>
        public void ClearCurrent()
        {
            lock (_lock)
            {
                _readBuffer.Clear();
            }
        }


        /// <summary>
        /// Disposes the stream and frees all memory.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _readBuffer.Dispose();
            _writeBuffer.Dispose();

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Finalizer as safety net for cleanup.
        /// </summary>
        ~NativeEventStream()
        {
            Dispose();
        }

        /// <summary>
        /// Internal buffer structure.
        /// </summary>
        private class Buffer : IDisposable
        {
            public T* Data;
            public long Size; // Size in bytes
            public int Capacity; // Capacity in elements
            public int Count; // Current count (atomic)

            // Graveyard for old buffers during resize
            private readonly List<(IntPtr ptr, long size)> _graveyard = new();

            public Buffer(int capacity)
            {
                Capacity = capacity;
                Size = (long)sizeof(T) * capacity;
                Data = (T*)NativeMemoryAllocator.Reserve(Size);
                NativeMemoryAllocator.Commit(Data, Size);
                Count = 0;
            }

            public void Resize(int newCapacity)
            {
                long newSize = (long)sizeof(T) * newCapacity;

                // Allocate new buffer
                T* newData = (T*)NativeMemoryAllocator.Reserve(newSize);
                NativeMemoryAllocator.Commit(newData, newSize);

                // Copy existing VALID data (up to old capacity, not count!)
                // Count may be > Capacity during concurrent writes
                int validCount = Math.Min(Count, Capacity);
                if (validCount > 0)
                {
                    long copySize = (long)sizeof(T) * validCount;
                    System.Buffer.MemoryCopy(Data, newData, newSize, copySize);
                }

                // Retire old buffer to graveyard
                _graveyard.Add(((IntPtr)Data, Size));

                // Update references
                Data = newData;
                Size = newSize;
                Capacity = newCapacity;
            }

            public void Clear()
            {
                Count = 0;

                // Free graveyard
                foreach (var (ptr, size) in _graveyard)
                {
                    NativeMemoryAllocator.Free((void*)ptr, size);
                }
                _graveyard.Clear();
            }

            public void Dispose()
            {
                Clear();
                if (Data != null)
                {
                    NativeMemoryAllocator.Free(Data, Size);
                    Data = null;
                }
            }
        }
    }
}
