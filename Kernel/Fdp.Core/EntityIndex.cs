using System;
using System.Runtime.CompilerServices;

namespace Fdp.Kernel
{
    /// <summary>
    /// Manages entity lifecycle using free-list allocation.
    /// Thread-safe creation/destruction via lock.
    /// Tracks active entities and generation numbers.
    /// </summary>
    public sealed class EntityIndex : IDisposable
    {
        private readonly NativeChunkTable<EntityHeader> _headers;
        private readonly object _createLock = new object();
        
        // Free-list for recycled entity IDs
        private readonly int[] _freeList;
        private int _freeCount;
        
        // Highest entity index ever issued
        private int _maxIssuedIndex = -1;
        
        // Total active entities
        private int _activeCount;
        
        private bool _disposed;
        
        public EntityIndex()
        {
            _headers = new NativeChunkTable<EntityHeader>();
            _freeList = new int[FdpConfig.MAX_ENTITIES];
            _freeCount = 0;
            _activeCount = 0;
        }
        
        /// <summary>
        /// Maximum entity index that has ever been issued.
        /// Used for iteration bounds.
        /// </summary>
        public int MaxIssuedIndex => _maxIssuedIndex;
        
        /// <summary>
        /// Total number of active entities.
        /// </summary>
        public int ActiveCount => _activeCount;
        
        /// <summary>
        /// Creates a new entity.
        /// Thread-safe via lock.
        /// </summary>
        public Entity CreateEntity()
        {
            lock (_createLock)
            {
                int index;
                
                // Try to reuse from free-list first
                if (_freeCount > 0)
                {
                    index = _freeList[--_freeCount];
                }
                else
                {
                    // Allocate new index
                    index = ++_maxIssuedIndex;
                    
                    #if FDP_PARANOID_MODE
                    if (index >= FdpConfig.MAX_ENTITIES)
                    {
                        throw new InvalidOperationException(
                            $"Maximum entity count ({FdpConfig.MAX_ENTITIES}) exceeded");
                    }
                    #endif
                }
                
                // Get header (will allocate chunk if needed)
                ref var header = ref _headers[index];
                
                // CRITICAL SAFETY FIX:
                // If generation is 0 (fresh/zeroed memory), bump to 1.
                // This ensures default(Entity) {Index:0, Generation:0} never matches a valid entity.
                // The microscopic performance cost is worth preventing dangerous collisions with
                // uninitialized Entity arrays, fields, or default parameter values.
                if (header.Generation == 0)
                {
                    header.Generation = 1;
                }
                
                // Clear component masks but preserve generation
                header.ComponentMask.Clear();
                header.AuthorityMask.Clear();
                header.SetActive(true);
                
                // Increment chunk population
                int chunkIndex = index / _headers.ChunkCapacity;
                _headers.IncrementPopulation(chunkIndex);
                _headers.IncrementChunkVersion(chunkIndex);
                
                _activeCount++;
                
                return new Entity(index, header.Generation);
            }
        }

        /// <summary>
        /// Reserves a range of entity IDs at the start of the index.
        /// Useful for ID partitioning strategies.
        /// </summary>
        public void ReserveIdRange(int maxId)
        {
            lock (_createLock)
            {
                if (maxId > _maxIssuedIndex)
                {
                    _maxIssuedIndex = maxId;
                }
            }
        }
        
        /// <summary>
        /// Destroys an entity and recycles its index.
        /// Thread-safe via lock.
        /// </summary>
        public void DestroyEntity(Entity entity)
        {
            #if FDP_PARANOID_MODE
            if (entity.IsNull)
                throw new ArgumentException("Cannot destroy null entity", nameof(entity));
            if (entity.Index < 0 || entity.Index > _maxIssuedIndex)
                throw new ArgumentException($"Entity index {entity.Index} out of range", nameof(entity));
            #endif
            
            lock (_createLock)
            {
                ref var header = ref _headers[entity.Index];
                
                // Validate generation
                #if FDP_PARANOID_MODE
                if (header.Generation != entity.Generation)
                {
                    throw new InvalidOperationException(
                        $"Entity {entity} is stale (current generation: {header.Generation})");
                }
                if (!header.IsActive)
                {
                    throw new InvalidOperationException($"Entity {entity} is already destroyed");
                }
                #endif
                
                // Mark as inactive
                header.SetActive(false);
                
                // Increment generation (with wraparound, skipping 0)
                header.Generation = (ushort)((header.Generation + 1) % ushort.MaxValue);
                if (header.Generation == 0)
                    header.Generation = 1;
                
                // Clear component masks
                header.ComponentMask.Clear();
                header.AuthorityMask.Clear();
                
                // Decrement chunk population
                int chunkIndex = entity.Index / _headers.ChunkCapacity;
                _headers.DecrementPopulation(chunkIndex);
                _headers.IncrementChunkVersion(chunkIndex);
                
                // Add to free-list
                #if FDP_PARANOID_MODE
                if (_freeCount >= FdpConfig.MAX_ENTITIES)
                    throw new InvalidOperationException("Free-list overflow");
                #endif
                
                _freeList[_freeCount++] = entity.Index;
                
                _activeCount--;
            }
        }
        
        /// <summary>
        /// Checks if an entity is currently alive.
        /// Validates both active flag and generation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsAlive(Entity entity)
        {
            // Bounds check covers negative indices and those beyond allocated range
            if (entity.Index < 0 || entity.Index > _maxIssuedIndex)
                return false;
            
            ref var header = ref _headers[entity.Index];
            
            // Validate generation match and active state
            // Note: Since header.Generation is never 0, default(Entity) {0,0} fails generation check here implicitly
            return header.IsActive && header.Generation == entity.Generation;
        }
        
        /// <summary>
        /// Gets direct reference to entity header.
        /// WARNING: Does not validate generation - use with care!
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref EntityHeader GetHeader(int entityIndex)
        {
            #if FDP_PARANOID_MODE
            if (entityIndex < 0 || entityIndex > _maxIssuedIndex)
            {
                throw new IndexOutOfRangeException(
                    $"Entity index {entityIndex} out of range [0, {_maxIssuedIndex}]");
            }
            #endif
            
            return ref _headers[entityIndex];
        }
        
        /// <summary>
        /// Gets direct reference to entity header WITHOUT bounds checking.
        /// ONLY use this when the index is guaranteed valid by the caller.
        /// Used internally by optimized parallel iterators.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref EntityHeader GetHeaderUnsafe(int entityIndex)
        {
            #if DEBUG
            System.Diagnostics.Debug.Assert(entityIndex >= 0 && entityIndex <= _maxIssuedIndex,
                $"GetHeaderUnsafe: Index {entityIndex} out of range [0, {_maxIssuedIndex}]");
            #endif
            
            return ref _headers[entityIndex];
        }
        
        /// <summary>
        /// Gets population count for a chunk (for iterator optimization).
        /// </summary>
        public int GetChunkPopulation(int chunkIndex)
        {
            return _headers.GetPopulationCount(chunkIndex);
        }
        
        /// <summary>
        /// Gets total number of chunks in the index.
        /// </summary>
        public int GetTotalChunks()
        {
            return _headers.TotalChunks;
        }
        
        /// <summary>
        /// Gets chunk capacity.
        /// </summary>
        public int GetChunkCapacity()
        {
            return _headers.ChunkCapacity;
        }

        /// <summary>
        /// Fills a span with the liveness state of entities in a specific chunk.
        /// True = Alive, False = Dead/Free.
        /// </summary>
        public void GetChunkLiveness(int chunkIndex, Span<bool> output)
        {
            int capacity = _headers.ChunkCapacity;
            #if FDP_PARANOID_MODE
            if (output.Length < capacity)
                throw new ArgumentException("Output span too small");
            #endif

            int startId = chunkIndex * capacity;
            
            // Iterate the chunk's potential entity IDs
            for (int i = 0; i < capacity; i++)
            {
                int entityId = startId + i;
                
                // Check bounds (global)
                if (entityId > _maxIssuedIndex)
                {
                    output[i] = false;
                    continue;
                }

                // Check liveness: Must be Active AND (Not in free list? No, IsActive is the truth)
                // Note: IsActive is strictly controlled by Create/Destroy.
                // We don't check Generation here because we just want to know if the slot is "Constructed".
                // HOWEVER, SanitizeChunk needs to know if it's "Garbage".
                // Garbage = Not Active.
                
                ref var header = ref _headers[entityId];
                output[i] = header.IsActive; 
            }
        }

        // ===================================
        // SYNCHRONIZATION SUPPORT
        // ===================================

        /// <summary>
        /// Synchronizes this index with a source index.
        /// Copies entity headers (generations, masks, flags) and global counters.
        /// </summary>
        public void SyncFrom(EntityIndex source)
        {
            // Sync the underlying headers table using extremely fast chunk-based memcpy
            _headers.SyncDirtyChunks(source._headers);
            
            // Sync global counters
            _activeCount = source._activeCount;
            _maxIssuedIndex = source._maxIssuedIndex;
            
            // Note: We do NOT sync the free list. 
            // The replica is expected to be Read-Only or synced fully from source.
            // Rebuilding the free list (if ever needed) is expensive and unnecessary for pure replication.
        }

        public void ApplyComponentFilter(BitMask256 mask)
        {
            int totalChunks = _headers.TotalChunks;
            int chunkCapacity = _headers.ChunkCapacity;

            for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
            {
                // Skip if destination has no data for this chunk
                if (!_headers.IsChunkCommitted(chunkIndex))
                    continue;

                // We can iterate the chunk.
                // Note: using GetRefRW(..., 0) now SAFE (ignores version update).
                // We are choosing NOT to dirty the chunk version because we are applying a deterministic filter
                // that is consistent with the source + filter state. 
                // Any mismatch will be fixed by next SyncFrom which overwrites (if source changed).
                
                int startId = chunkIndex * chunkCapacity;
                int endId = Math.Min(startId + chunkCapacity, _maxIssuedIndex + 1);

                for (int i = startId; i < endId; i++)
                {
                   ref var header = ref _headers.GetRefRW(i, 0); 
                   if (header.IsActive)
                   {
                       header.ComponentMask.BitwiseAnd(mask);
                   }
                }
                
                // Invalidate version so next sync re-fetches full headers (in case mask changes/widens)
                // This ensures that if we filter out components now, we can get them back later if the mask changes.
                // This forces a header copy every frame for filtered repositories, which is the correct trade-off for correctness.
                _headers.IncrementChunkVersion(chunkIndex);
            }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            _headers?.Dispose();
            _disposed = true;
        }

        // ===================================
        // SERIALIZATION SUPPORT (One-way)
        // ===================================
        
        /// <summary>
        /// Restores an entity header at a specific index. 
        /// Used by serialization to reconstruct state.
        /// WARNING: This bypasses all safety checks and modifies internal state.
        /// </summary>
        internal void ForceRestoreEntity(int index, bool isActive, int generation, BitMask256 componentMask, DISEntityType disType = default)
        {
            if (index > _maxIssuedIndex)
            {
                _maxIssuedIndex = index;
            }
            
            ref var header = ref _headers[index];
            bool wasActive = header.IsActive; // Capture previous state
            
            header.SetActive(isActive);
            header.Generation = (ushort)generation;
            header.ComponentMask = componentMask;
            // Note: Authority mask is lost/cleared on default unless serialized
            header.AuthorityMask.Clear(); 
            header.DisType = disType;
            
            int chunkIndex = index / _headers.ChunkCapacity;
            _headers.IncrementChunkVersion(chunkIndex);

            // Fix Count Logic: Only increment active count if we are transitioning from Dead -> Alive
            if (isActive && !wasActive)
            {
                 _activeCount++;
                 
                 // Update Chunk Population
                 _headers.IncrementPopulation(chunkIndex); 
            }
            // If we are transitioning Alive -> Dead (unlikely in Restore, but possible if overwriting)
            else if (!isActive && wasActive)
            {
                _activeCount--;
                _headers.DecrementPopulation(chunkIndex);
            }
        }

        /// <summary>
        /// Clears the index to an initial state.
        /// </summary>
        internal void Clear()
        {
            lock (_createLock)
            {
                _maxIssuedIndex = -1;
                _activeCount = 0;
                _freeCount = 0;
                // Decommit all chunks to ensure a clean slate
                _headers.Clear();
            }
        }
        
        /// <summary>
        /// Rebuilds the free list based on gaps in active entities.
        /// </summary>
        internal void RebuildFreeList()
        {
            lock (_createLock)
            {
                _freeCount = 0;
                // Active count should have been accumulated during Restore
                // Iterate to find inactive ones
                for (int i = 0; i <= _maxIssuedIndex; i++)
                {
                    if (!_headers[i].IsActive)
                    {
                        if (_freeCount < FdpConfig.MAX_ENTITIES)
                            _freeList[_freeCount++] = i;
                    }
                }
            }
        }

        // ===================================
        // FLIGHT RECORDER SUPPORT
        // ===================================
        
        public int CopyChunkToBuffer(int chunkIndex, Span<byte> destination)
        {
            return _headers.CopyChunkToBuffer(chunkIndex, destination);
        }
        
        public void RestoreChunkFromBuffer(int chunkIndex, byte[] data)
        {
            _headers.RestoreChunkFromBuffer(chunkIndex, data);
        }
        
        public void SanitizeChunk(int chunkIndex, ReadOnlySpan<bool> liveness)
        {
            _headers.SanitizeChunk(chunkIndex, liveness);
        }
        
        /// <summary>
        /// Scan all headers to rebuild metadata (_activeCount, freeList, populationCounts).
        /// Call this after blindly restoring headers from disk.
        /// </summary>
        public void RebuildMetadata()
        {
            lock (_createLock)
            {
                _activeCount = 0;
                _freeCount = 0;
                
                // Reset Max Issued Index scan
                _maxIssuedIndex = -1;

                int chunkCapacity = _headers.ChunkCapacity;
                int totalChunks = _headers.TotalChunks;
                
                for (int c = 0; c < totalChunks; c++)
                {
                    if (!_headers.IsChunkCommitted(c))
                    {
                        // Console.WriteLine($"DEBUG: Chunk {c} NOT Committed");
                        _headers.SetPopulation(c, 0);
                        continue;
                    }
                    
                    int chunkPop = 0;
                    int startId = c * chunkCapacity;
                    
                    for (int i = 0; i < chunkCapacity; i++) 
                    {
                        int entityId = startId + i;
                        // Since we restore raw memory, we rely on checking raw headers.
                        // We assume memory outside allocated range is zero/null, but IsChunkCommitted checks that.
                        
                        ref readonly var header = ref _headers.GetRefRO(entityId); // Safe access within committed chunk
                        
                        if (header.IsActive)
                        {
                            chunkPop++;
                            _activeCount++;
                            if (entityId > _maxIssuedIndex) _maxIssuedIndex = entityId;
                        }
                    }
                    
                    _headers.SetPopulation(c, chunkPop);
                }
                
                // Rebuild free list fully
                RebuildFreeList();
            }
        }
    }
}
