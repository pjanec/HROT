using System;
using System.Runtime.CompilerServices;

namespace Fdp.Core
{
    /// <summary>
    /// Stores components of type T using NativeChunkTable.
    /// Provides O(1) access to components by entity index.
    /// </summary>
    public sealed class ComponentTable<T> : IComponentTable, FlightRecorder.IUnmanagedComponentTable where T : unmanaged
    {
        private readonly NativeChunkTable<T> _data;
        private readonly int _componentTypeId;
        
        public ComponentTable()
        {
            _data = new NativeChunkTable<T>();
            _componentTypeId = ComponentType<T>.ID;
        }
        
        public int ComponentTypeId => _componentTypeId;
        public Type ComponentType => typeof(T);
        public int ComponentSize => ComponentType<T>.Size;

        // NEW: Type-erased setter
        public void SetRawObject(int index, object value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            
            // Fast cast - type safety guaranteed by caller logic or via InvalidCastException
            _data[index] = (T)value;
        }
        
        // NEW: Type-erased getter
        public object GetRawObject(int index)
        {
            // Box the struct
            return _data[index];
        }

        public void ClearRaw(int index)
        {
            // For unmanaged components, clearing data is optional as mask handles validity.
            // But for consistency we could zero it. 
            // However, FDP design says "Component data is not explicitly cleared".
            // So we can leave it empty or zero it.
            // Let's match current behavior: do nothing. Mask is what matters.
        }

        public void Clear()
        {
            // Unmanaged data doesn't trigger GC issues and is guarded by ComponentMask.
            // Clearing it is expensive and unnecessary.
        }
        
        /// <summary>
        /// Efficiently checks if this table has been modified since the specified version.
        /// </summary>
        public bool HasChanges(uint sinceVersion)
        {
            return _data.HasChanges(sinceVersion);
        }

        public uint GetVersionForEntity(int entityId)
        {
            int chunkIndex = entityId / _data.ChunkCapacity;
            return _data.GetChunkVersion(chunkIndex);
        }
        
        /// <summary>
        /// Gets reference to component for entity at given index.
        /// Does NOT validate if component exists - caller must check ComponentMask.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int entityIndex)
        {
            return ref _data[entityIndex];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void* GetRawPointer(int entityIndex)
        {
            return Unsafe.AsPointer(ref _data.GetRefRW(entityIndex, 0));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetRW(int entityIndex, uint version)
        {
            return ref _data.GetRefRW(entityIndex, version);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref readonly T GetRO(int entityIndex)
        {
            return ref _data.GetRefRO(entityIndex);
        }
        
        /// <summary>
        /// Sets component value for entity at given index.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int entityIndex, in T component, uint version)
        {
            _data.GetRefRW(entityIndex, version) = component;
        }
        
        /// <summary>
        /// Legacy/Test helper. Sets with version 0 (no change tracking).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int entityIndex, in T component)
        {
            Set(entityIndex, component, 0);
        }
        
        /// <summary>
        /// Gets a Span over the specified chunk's data.
        /// Advanced API for heavy optimization.
        /// </summary>
        public Span<T> GetSpan(int chunkIndex) => _data.GetChunkSpan(chunkIndex);

        /// <summary>
        /// Gets the underlying chunk table (for advanced usage).
        /// </summary>
        public NativeChunkTable<T> GetChunkTable() => _data;
        
        /// <summary>
        /// Sets component from raw bytes (used by EntityCommandBuffer).
        /// </summary>
        public unsafe void SetRaw(int entityIndex, IntPtr dataPtr, int size, uint version)
        {
            if (size != Unsafe.SizeOf<T>())
                throw new ArgumentException($"Size mismatch: expected {Unsafe.SizeOf<T>()} but got {size}");
            
            ref T dest = ref _data.GetRefRW(entityIndex, version);
            void* destPtr = Unsafe.AsPointer(ref dest);
            Buffer.MemoryCopy((void*)dataPtr, destPtr, size, size);
        }
        
        // ================================================
        // FLIGHT RECORDER SUPPORT
        // ================================================
        
        /// <summary>
        /// Sanitizes a chunk by zeroing dead entity slots.
        /// Required by IUnmanagedComponentTable for Flight Recorder.
        /// </summary>
        public void SanitizeChunk(int chunkIndex, ReadOnlySpan<bool> livenessMap)
        {
            _data.SanitizeChunk(chunkIndex, livenessMap);
        }
        
        /// <summary>
        /// Copies raw chunk data to a buffer.
        /// Required by IUnmanagedComponentTable for Flight Recorder.
        /// </summary>
        public int CopyChunkToBuffer(int chunkIndex, Span<byte> destination)
        {
            return _data.CopyChunkToBuffer(chunkIndex, destination);
        }
        
        /// <summary>
        /// Restores chunk data from a buffer.
        /// Required by IUnmanagedComponentTable for Flight Recorder playback.
        /// </summary>
        public void RestoreChunkFromBuffer(int chunkIndex, ReadOnlySpan<byte> source)
        {
            _data.RestoreChunkFromBuffer(chunkIndex, source);
        }
        
        public void Dispose()
        {
            _data?.Dispose();
        }

        public void SyncFrom(IComponentTable source)
        {
            if (source is ComponentTable<T> typedSource)
            {
                _data.SyncDirtyChunks(typedSource._data);
            }
            #if FDP_PARANOID_MODE
            else
            {
                throw new ArgumentException($"Source table type mismatch. Expected {typeof(ComponentTable<T>).Name}, got {source.GetType().Name}");
            }
            #endif
        }
    }
}
