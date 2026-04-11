using System;
using System.Runtime.CompilerServices;

namespace Fdp.Kernel
{
    /// <summary>
    /// Wrapper around a single 64KB chunk of unmanaged memory.
    /// Provides typed, bounds-checked access to chunk elements.
    /// </summary>
    /// <typeparam name="T">Unmanaged element type stored in this chunk</typeparam>
    public readonly unsafe struct NativeChunk<T> where T : unmanaged
    {
        private readonly T* _data;
        private readonly int _capacity;
        
        public NativeChunk(void* basePtr, int capacity)
        {
            _data = (T*)basePtr;
            _capacity = capacity;
        }
        
        public int Capacity => _capacity;
        
        public bool IsNull => _data == null;
        
        /// <summary>
        /// Gets pointer to start of chunk data.
        /// </summary>
        public T* DataPtr => _data;
        
        /// <summary>
        /// Accesses element at local index within this chunk.
        /// </summary>
        public ref T this[int localIndex]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                #if FDP_PARANOID_MODE
                if (localIndex < 0 || localIndex >= _capacity)
                {
                    throw new IndexOutOfRangeException(
                        $"Local index {localIndex} out of range [0, {_capacity})");
                }
                if (_data == null)
                {
                    throw new NullReferenceException("Chunk is not allocated");
                }
                #endif
                
                return ref _data[localIndex];
            }
        }
        
        /// <summary>
        /// Clears all elements in the chunk to default values.
        /// </summary>
        public void Clear()
        {
            if (_data == null) return;
            
            // Use platform memset for performance
            uint sizeInBytes = (uint)(_capacity * sizeof(T));
            Unsafe.InitBlockUnaligned(_data, 0, sizeInBytes);
        }
        
        /// <summary>
        /// Gets a span over the entire chunk.
        /// Useful for SIMD operations and bulk processing.
        /// </summary>
        public Span<T> AsSpan()
        {
            if (_data == null)
                return Span<T>.Empty;
            
            return new Span<T>(_data, _capacity);
        }
        
        public override string ToString()
        {
            if (_data == null)
                return $"NativeChunk<{typeof(T).Name}> [NULL]";
            
            return $"NativeChunk<{typeof(T).Name}> [Capacity: {_capacity}, Ptr: {(long)_data:X}]";
        }
    }
}
