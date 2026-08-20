using System;
using Fdp.Core.FlightRecorder;

namespace Fdp.Core
{
    /// <summary>
    /// Stores managed components (classes, strings, etc.) in GC-managed arrays.
    /// Tier 2 storage for "cold" data that doesn't need the performance of native memory.
    /// Uses standard .NET arrays, so GC manages all memory.
    /// </summary>
    public sealed class ManagedComponentTable<T> : IComponentTable where T : class
    {
        private T?[][] _chunks;
        private readonly int _chunkSize;
        private readonly int _componentTypeId;
        private uint[] _chunkVersions;
        
        public ManagedComponentTable(int chunkSize = 16384)
        {
            _chunkSize = chunkSize;
            _componentTypeId = ManagedComponentType<T>.ID;
            
            // Pre-allocate chunk array (but not the actual data arrays yet - lazy)
            int maxChunks = (FdpConfig.MAX_ENTITIES / chunkSize) + 1;
            _chunks = new T[maxChunks][];
            _chunkVersions = new uint[maxChunks];
        }
        
        public int ComponentTypeId => _componentTypeId;
        public Type ComponentType => typeof(T);
        public int ComponentSize => IntPtr.Size; // Reference size
        public int ChunkCapacity => _chunkSize;
        
        // NEW: Type-erased setter
        public void SetRawObject(int index, object value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            int chunkIndex = index / _chunkSize;
            int localIndex = index % _chunkSize;

            if (_chunks[chunkIndex] == null)
                _chunks[chunkIndex] = new T[_chunkSize];

            _chunks[chunkIndex][localIndex] = (T)value;

            // Bump the chunk version so SyncDirtyChunks detects the write on the next
            // SyncFrom call.  Without this, the version stays at 0 and the version-
            // equality check (_chunkVersions[i] == srcVer) silently skips the chunk,
            // leaving managed components absent from SoD snapshots.
            _chunkVersions[chunkIndex] = _chunkVersions[chunkIndex] == 0
                ? 1u
                : _chunkVersions[chunkIndex] + 1u;
        }
        
        // NEW: Type-erased getter
        public object GetRawObject(int index)
        {
            var val = this[index];
            if (val == null) return null!;
            return val;
        }

        public void ClearRaw(int index)
        {
            this[index] = null;
        }

        public void Clear()
        {
            // System.Console.WriteLine($"[DEBUG] Clearing Managed Table for {typeof(T).Name}");
            Array.Clear(_chunks, 0, _chunks.Length);
            Array.Clear(_chunkVersions, 0, _chunkVersions.Length);
        }
        
        /// <summary>
        /// Efficiently checks if this table has been modified since the specified version.
        /// Uses lazy scan of chunk versions.
        /// </summary>
        public bool HasChanges(uint sinceVersion)
        {
            for (int i = 0; i < _chunkVersions.Length; i++)
            {
                if (_chunkVersions[i] > sinceVersion)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Gets or sets component for an entity. Lazy-allocates chunks.
        /// </summary>
        public T? this[int entityIndex]
        {
            get
            {
                int chunkIndex = entityIndex / _chunkSize;
                int localIndex = entityIndex % _chunkSize;
                
                var chunk = _chunks[chunkIndex];
                if (chunk == null)
                    return null; // Not allocated yet
                
                return chunk[localIndex];
            }
            set
            {
                int chunkIndex = entityIndex / _chunkSize;
                int localIndex = entityIndex % _chunkSize;
                
                // Lazy allocate chunk if needed
                if (_chunks[chunkIndex] == null)
                {
                    _chunks[chunkIndex] = new T[_chunkSize];
                }
                
                _chunks[chunkIndex][localIndex] = value;
            }
        }
        
        /// <summary>
        /// Gets reference for read/write access. Allocates chunk if needed.
        /// </summary>
        public ref T? GetRW(int entityIndex, uint version)
        {
            int chunkIndex = entityIndex / _chunkSize;
            int localIndex = entityIndex % _chunkSize;
            
            // Lazy allocate chunk if needed
            if (_chunks[chunkIndex] == null)
            {
                _chunks[chunkIndex] = new T[_chunkSize];
            }
            
            // Update version
            _chunkVersions[chunkIndex] = version;
            
            return ref _chunks[chunkIndex][localIndex];
        }
        
        /// <summary>
        /// Gets value for read-only access. Does NOT allocate.
        /// Returns null if chunk not allocated.
        /// </summary>
        public T? GetRO(int entityIndex)
        {
            int chunkIndex = entityIndex / _chunkSize;
            int localIndex = entityIndex % _chunkSize;
            
            var chunk = _chunks[chunkIndex];
            if (chunk == null)
                return null;
            
            return chunk[localIndex];
        }
        
        /// <summary>
        /// Sets component value with version tracking.
        /// </summary>
        public void Set(int entityIndex, T? value, uint version)
        {
            int chunkIndex = entityIndex / _chunkSize;
            int localIndex = entityIndex % _chunkSize;
            
            // Lazy allocate chunk if needed
            if (_chunks[chunkIndex] == null)
            {
                _chunks[chunkIndex] = new T[_chunkSize];
            }
            
            _chunks[chunkIndex][localIndex] = value;
            if (version != 0)
            {
                _chunkVersions[chunkIndex] = version;
            }
        }

        /// <summary>
        /// Sets a full chunk of data. Used by PlaybackSystem.
        /// </summary>
        public void SetChunk(int chunkIndex, T?[] data, uint version)
        {
             // Ensure capacity
             if (chunkIndex >= _chunks.Length)
             {
                 // Resize logic? Or assume pre-allocated?
                 // Constructor allocates fixed size.
                 // We should probably check bounds.
                 if (chunkIndex >= _chunks.Length) throw new IndexOutOfRangeException("Chunk index out of bounds");
             }
             
             _chunks[chunkIndex] = data;
             _chunkVersions[chunkIndex] = version;
        }
        
        /// <summary>
        /// Gets the version of the chunk containing the entity.
        /// </summary>
        public uint GetVersionForEntity(int entityId)
        {
            int chunkIndex = entityId / _chunkSize;
            return _chunkVersions[chunkIndex];
        }
        
        /// <summary>
        /// Gets chunk version by index (for query system).
        /// </summary>
        public uint GetChunkVersion(int chunkIndex)
        {
            if (chunkIndex >= _chunkVersions.Length)
                return 0;
            return _chunkVersions[chunkIndex];
        }
        
        /// <summary>
        /// Sets component from raw bytes. For managed types, this deserializes the object.
        /// Used by EntityCommandBuffer for type-erased operations.
        /// </summary>
        public unsafe void SetRaw(int entityIndex, IntPtr dataPtr, int size, uint version)
        {
            // For managed types, we can't use raw memory copy
            // The caller needs to have already deserialized the object
            // This is a limitation of the raw API for managed types
            throw new NotSupportedException(
                "SetRaw is not supported for managed components. " +
                "Use Set() with the deserialized object instead.");
        }

        /// <summary>
        /// Ruling 14 — ⛔ not applicable to a managed component: it has no byte layout to patch, so
        /// there is no "the other fields" to preserve. Callers route a managed edit through
        /// <c>SetManagedComponentRaw</c>, which replaces the reference wholesale.
        /// </summary>
        public unsafe void SetRawAt(int entityIndex, int byteOffset, IntPtr src, int size, uint version)
        {
            throw new NotSupportedException(
                "SetRawAt is not supported for managed components — a reference-typed component has "
                + "no byte layout. Replace the object via the managed path instead.");
        }
        
        /// <summary>
        /// Checks if chunk is allocated.
        /// </summary>
        public bool IsChunkAllocated(int chunkIndex)
        {
            return chunkIndex < _chunks.Length && _chunks[chunkIndex] != null;
        }
        
        /// <summary>
        /// Clears a component value (sets to null).
        /// </summary>
        public void Clear(int entityIndex)
        {
            int chunkIndex = entityIndex / _chunkSize;
            int localIndex = entityIndex % _chunkSize;
            
            if (_chunks[chunkIndex] != null)
            {
                _chunks[chunkIndex][localIndex] = null;
            }
        }

        public int TotalChunks => _chunks.Length;

        /// <summary>
        /// Synchronizes dirty chunks from a source table.
        /// Uses shallow copy (Array.Copy) to copy references.
        /// Version tracking avoids unnecessary copies.
        /// </summary>
        /// <summary>
        /// Synchronizes dirty chunks from a source table.
        /// Uses shallow copy (Array.Copy) for normal components.
        /// Uses deep clone for components marked with DataPolicy.SnapshotViaClone.
        /// </summary>
        public void SyncDirtyChunks(ManagedComponentTable<T> source)
        {
            // Check if this type requires cloning
            int typeId = ComponentTypeRegistry.GetId(typeof(T));
            bool needsClone = ComponentTypeRegistry.NeedsClone(typeId);
            
            // Assuming matching configurations by architecture design
            int loopMax = Math.Min(_chunks.Length, source._chunks.Length);
            
            for (int i = 0; i < loopMax; i++)
            {
                // Optimization: Version Check
                uint srcVer = source.GetChunkVersion(i);
                if (_chunkVersions[i] == srcVer)
                    continue;

                // Liveness check: Does source have data?
                // Access source._chunks directly as we are same type
                var srcChunk = source._chunks[i]; 
                
                if (srcChunk == null)
                {
                    // Source is empty. If we have data, clear it.
                    if (_chunks[i] != null)
                    {
                        // Release the array for GC
                        _chunks[i] = null!; 
                    }
                    
                    _chunkVersions[i] = srcVer;
                    continue;
                }

                // Source has data. Ensure we have storage.
                if (_chunks[i] == null)
                {
                    _chunks[i] = new T[_chunkSize];
                }
                
                if (needsClone)
                {
                    // ━━━ SLOW PATH: Deep Clone ━━━
                    for (int k = 0; k < _chunkSize; k++)
                    {
                        var srcVal = srcChunk[k];
                        if (srcVal != null)
                        {
                            _chunks[i][k] = FdpAutoSerializer.DeepClone(srcVal);
                        }
                        else
                        {
                            _chunks[i][k] = null!;
                        }
                    }
                }
                else
                {
                    // ━━━ FAST PATH: Shallow Copy ━━━
                    // This relies on components being immutable records or safe by design.
                    Array.Copy(srcChunk, _chunks[i], _chunkSize);
                }

                // Update version
                _chunkVersions[i] = srcVer;
            }
        }
        
        public void Dispose()
        {
            // Let GC handle cleanup
            // We could null out arrays to help GC, but it's not strictly necessary
            for (int i = 0; i < _chunks.Length; i++)
            {
                _chunks[i] = null!;
            }
        }
        public unsafe void* GetRawPointer(int entityIndex)
        {
            throw new NotSupportedException("Cannot get raw pointer for managed component");
        }

        public void SyncFrom(IComponentTable source)
        {
             if (source is ManagedComponentTable<T> typedSource)
             {
                 this.SyncDirtyChunks(typedSource);
             }
             #if FDP_PARANOID_MODE
             else
             {
                 throw new ArgumentException($"Source table type mismatch: {source.GetType().Name}");
             }
             #endif
        }
    }
}
