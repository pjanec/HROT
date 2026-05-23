using System;
using System.Runtime.CompilerServices;

namespace Fdp.Core
{
    /// <summary>
    /// Manages entity lifecycle using free-list allocation.
    /// Thread-safe creation/destruction via lock.
    /// Tracks active entities and generation numbers.
    ///
    /// Internal layout: two parallel NativeChunkTable arrays.
    ///   _hotMasks  -- BitMask512 per entity (component-presence mask); 64 bytes / 1 cache line.
    ///   _coldMeta  -- EntityMetadataCold per entity (authority, generation, flags, etc.); 128 bytes.
    /// Query traversal reads the hot mask first; cold data is only fetched on a mask hit.
    /// </summary>
    public sealed class EntityIndex : IDisposable
    {
        private readonly NativeChunkTable<BitMask512>         _hotMasks;
        private readonly NativeChunkTable<EntityMetadataCold> _coldMeta;
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
            _hotMasks = new NativeChunkTable<BitMask512>();
            _coldMeta = new NativeChunkTable<EntityMetadataCold>();
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

                // Get references to both tables (allocates chunks on demand)
                ref var compMask = ref _hotMasks[index];
                ref var meta     = ref _coldMeta[index];

                // CRITICAL SAFETY FIX:
                // If generation is 0 (fresh/zeroed memory), bump to 1.
                // This ensures default(Entity) {Index:0, Generation:0} never matches a valid entity.
                if (meta.Generation == 0)
                {
                    meta.Generation = 1;
                }

                // Clear component-presence mask and authority mask, set active flag
                compMask.Clear();
                meta.AuthorityMask.Clear();
                meta.SetActive(true);

                // Increment chunk population on both tables
                int hotChunk  = index / _hotMasks.ChunkCapacity;
                int coldChunk = index / _coldMeta.ChunkCapacity;
                _hotMasks.IncrementPopulation(hotChunk);
                _hotMasks.IncrementChunkVersion(hotChunk);
                _coldMeta.IncrementPopulation(coldChunk);
                _coldMeta.IncrementChunkVersion(coldChunk);

                _activeCount++;

                return new Entity(index, meta.Generation);
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
                ref var compMask = ref _hotMasks[entity.Index];
                ref var meta     = ref _coldMeta[entity.Index];

                // Validate generation
                #if FDP_PARANOID_MODE
                if (meta.Generation != entity.Generation)
                {
                    throw new InvalidOperationException(
                        $"Entity {entity} is stale (current generation: {meta.Generation})");
                }
                if (!meta.IsActive)
                {
                    throw new InvalidOperationException($"Entity {entity} is already destroyed");
                }
                #endif

                // Clear component-presence mask (hot)
                compMask.Clear();

                // Increment generation in cold (with wraparound, skipping 0)
                meta.Generation = (ushort)((meta.Generation + 1) % ushort.MaxValue);
                if (meta.Generation == 0)
                    meta.Generation = 1;

                // Mark inactive and clear authority in cold
                meta.SetActive(false);
                meta.AuthorityMask.Clear();

                // Decrement chunk population on both tables
                int hotChunk  = entity.Index / _hotMasks.ChunkCapacity;
                int coldChunk = entity.Index / _coldMeta.ChunkCapacity;
                _hotMasks.DecrementPopulation(hotChunk);
                _hotMasks.IncrementChunkVersion(hotChunk);
                _coldMeta.DecrementPopulation(coldChunk);
                _coldMeta.IncrementChunkVersion(coldChunk);

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

            ref readonly var meta = ref _coldMeta.GetRefRO(entity.Index);

            // Validate generation match and active state
            return meta.IsActive && meta.Generation == entity.Generation;
        }

        // ===================================
        // HOT TABLE ACCESSORS
        // ===================================

        /// <summary>
        /// Gets a direct reference to the component-presence mask for an entity.
        /// Bounds-checked in PARANOID mode.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref BitMask512 GetComponentMask(int entityIndex)
        {
            #if FDP_PARANOID_MODE
            if (entityIndex < 0 || entityIndex > _maxIssuedIndex)
            {
                throw new IndexOutOfRangeException(
                    $"Entity index {entityIndex} out of range [0, {_maxIssuedIndex}]");
            }
            #endif

            return ref _hotMasks[entityIndex];
        }

        /// <summary>
        /// Gets a direct reference to the component-presence mask WITHOUT bounds checking.
        /// ONLY use this when the index is guaranteed valid by the caller.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref BitMask512 GetComponentMaskUnsafe(int entityIndex)
        {
            #if DEBUG
            System.Diagnostics.Debug.Assert(entityIndex >= 0 && entityIndex <= _maxIssuedIndex,
                $"GetComponentMaskUnsafe: Index {entityIndex} out of range [0, {_maxIssuedIndex}]");
            #endif

            return ref _hotMasks[entityIndex];
        }

        // ===================================
        // COLD TABLE ACCESSORS
        // ===================================

        /// <summary>
        /// Gets a direct reference to the cold metadata for an entity.
        /// Bounds-checked in PARANOID mode.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref EntityMetadataCold GetMetadata(int entityIndex)
        {
            #if FDP_PARANOID_MODE
            if (entityIndex < 0 || entityIndex > _maxIssuedIndex)
            {
                throw new IndexOutOfRangeException(
                    $"Entity index {entityIndex} out of range [0, {_maxIssuedIndex}]");
            }
            #endif

            return ref _coldMeta[entityIndex];
        }

        /// <summary>
        /// Gets a direct reference to the cold metadata WITHOUT bounds checking.
        /// ONLY use this when the index is guaranteed valid by the caller.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref EntityMetadataCold GetMetadataUnsafe(int entityIndex)
        {
            #if DEBUG
            System.Diagnostics.Debug.Assert(entityIndex >= 0 && entityIndex <= _maxIssuedIndex,
                $"GetMetadataUnsafe: Index {entityIndex} out of range [0, {_maxIssuedIndex}]");
            #endif

            return ref _coldMeta[entityIndex];
        }

        // ===================================
        // CHUNK ITERATION SUPPORT (hot table drives iteration)
        // ===================================

        /// <summary>
        /// Gets population count for a hot chunk (for iterator optimization).
        /// </summary>
        public int GetChunkPopulation(int chunkIndex)
        {
            return _hotMasks.GetPopulationCount(chunkIndex);
        }

        /// <summary>
        /// Gets total number of hot chunks in the index.
        /// </summary>
        public int GetTotalChunks()
        {
            return _hotMasks.TotalChunks;
        }

        /// <summary>
        /// Gets hot chunk capacity (entities per hot chunk).
        /// </summary>
        public int GetChunkCapacity()
        {
            return _hotMasks.ChunkCapacity;
        }

        /// <summary>
        /// Gets total number of cold chunks in the index.
        /// </summary>
        public int GetColdTotalChunks()
        {
            return _coldMeta.TotalChunks;
        }

        /// <summary>
        /// Gets cold chunk capacity (entities per cold chunk).
        /// </summary>
        public int GetColdChunkCapacity()
        {
            return _coldMeta.ChunkCapacity;
        }

        /// <summary>
        /// Fills a span with the liveness state of entities in a specific hot chunk.
        /// True = Alive, False = Dead/Free.
        /// Uses cold metadata (IsActive flag) as the source of truth.
        /// The output span must be at least GetChunkCapacity() in length.
        /// </summary>
        public void GetChunkLiveness(int chunkIndex, Span<bool> output)
        {
            int capacity = _hotMasks.ChunkCapacity;
            #if FDP_PARANOID_MODE
            if (output.Length < capacity)
                throw new ArgumentException("Output span too small");
            #endif

            int startId = chunkIndex * capacity;

            for (int i = 0; i < capacity; i++)
            {
                int entityId = startId + i;

                if (entityId > _maxIssuedIndex)
                {
                    output[i] = false;
                    continue;
                }

                ref readonly var meta = ref _coldMeta.GetRefRO(entityId);
                output[i] = meta.IsActive;
            }
        }

        // ===================================
        // SYNCHRONIZATION SUPPORT
        // ===================================

        /// <summary>
        /// Synchronizes this index with a source index.
        /// Copies component masks and cold metadata, and global counters.
        /// </summary>
        public void SyncFrom(EntityIndex source)
        {
            // Sync both underlying tables using fast chunk-based memcpy
            _hotMasks.SyncDirtyChunks(source._hotMasks);
            _coldMeta.SyncDirtyChunks(source._coldMeta);

            // Sync global counters
            _activeCount    = source._activeCount;
            _maxIssuedIndex = source._maxIssuedIndex;

            // Clear local free-list to prevent stale recycled indices from surviving a rewind/sync.
            _freeCount = 0;
        }

        /// <summary>
        /// Clears non-snapshotable component bits from every active entity's component mask.
        /// </summary>
        public void ApplyComponentFilter(in BitMask512 mask)
        {
            int totalChunks   = _hotMasks.TotalChunks;
            int chunkCapacity = _hotMasks.ChunkCapacity;

            for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
            {
                if (!_hotMasks.IsChunkCommitted(chunkIndex))
                    continue;

                int startId = chunkIndex * chunkCapacity;
                int endId   = Math.Min(startId + chunkCapacity, _maxIssuedIndex + 1);

                for (int i = startId; i < endId; i++)
                {
                    ref var compMask         = ref _hotMasks.GetRefRW(i, 0);
                    ref readonly var meta    = ref _coldMeta.GetRefRO(i);
                    if (meta.IsActive)
                    {
                        compMask.BitwiseAnd(mask);
                    }
                }

                // Invalidate version so next sync re-fetches full hot masks
                _hotMasks.IncrementChunkVersion(chunkIndex);
            }
        }

        /// <summary>
        /// Overload that accepts a BitMask256 filter, zero-extending to BitMask512.
        /// Supports callers that still use BitMask256 for component masks (IDs 0-255).
        /// </summary>
        public void ApplyComponentFilter(in BitMask256 mask)
        {
            unsafe
            {
                BitMask512 mask512 = default;
                fixed (BitMask256* src = &mask)
                {
                    System.Buffer.MemoryCopy(src, &mask512, 64, 32);
                }
                ApplyComponentFilter(in mask512);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _hotMasks?.Dispose();
            _coldMeta?.Dispose();
            _disposed = true;
        }

        // ===================================
        // SERIALIZATION SUPPORT (One-way)
        // ===================================

        /// <summary>
        /// Restores entity state at a specific index.
        /// Used by serialization/playback to reconstruct state.
        /// WARNING: Bypasses all safety checks and modifies internal state directly.
        /// </summary>
        internal void ForceRestoreEntity(int index, bool isActive, int generation, BitMask512 componentMask, DISEntityType disType = default)
        {
            if (index > _maxIssuedIndex)
            {
                _maxIssuedIndex = index;
            }

            ref var compMask = ref _hotMasks[index];
            ref var meta     = ref _coldMeta[index];

            bool wasActive = meta.IsActive; // Capture previous state

            // Restore hot mask
            compMask = componentMask;

            // Restore cold metadata
            meta.SetActive(isActive);
            meta.Generation = (ushort)generation;
            meta.AuthorityMask.Clear();
            meta.DisType = disType;

            int hotChunk  = index / _hotMasks.ChunkCapacity;
            int coldChunk = index / _coldMeta.ChunkCapacity;
            _hotMasks.IncrementChunkVersion(hotChunk);
            _coldMeta.IncrementChunkVersion(coldChunk);

            // Fix population counters: only update on state transition
            if (isActive && !wasActive)
            {
                _activeCount++;
                _hotMasks.IncrementPopulation(hotChunk);
                _coldMeta.IncrementPopulation(coldChunk);
            }
            else if (!isActive && wasActive)
            {
                _activeCount--;
                _hotMasks.DecrementPopulation(hotChunk);
                _coldMeta.DecrementPopulation(coldChunk);
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
                _activeCount    = 0;
                _freeCount      = 0;
                _hotMasks.Clear();
                _coldMeta.Clear();
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
                for (int i = 0; i <= _maxIssuedIndex; i++)
                {
                    ref readonly var meta = ref _coldMeta.GetRefRO(i);
                    if (!meta.IsActive)
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

        /// <summary>
        /// Copies a hot (component-mask) chunk to a byte buffer.
        /// Returns the number of bytes written.
        /// </summary>
        public int CopyHotChunkToBuffer(int chunkIndex, Span<byte> destination)
        {
            return _hotMasks.CopyChunkToBuffer(chunkIndex, destination);
        }

        /// <summary>
        /// Copies a cold (metadata) chunk to a byte buffer.
        /// Returns the number of bytes written.
        /// </summary>
        public int CopyColdChunkToBuffer(int chunkIndex, Span<byte> destination)
        {
            return _coldMeta.CopyChunkToBuffer(chunkIndex, destination);
        }

        /// <summary>
        /// Restores a hot (component-mask) chunk from a byte buffer.
        /// </summary>
        public void RestoreHotChunkFromBuffer(int chunkIndex, byte[] data)
        {
            _hotMasks.RestoreChunkFromBuffer(chunkIndex, data);
        }

        /// <summary>
        /// Restores a cold (metadata) chunk from a byte buffer.
        /// </summary>
        public void RestoreColdChunkFromBuffer(int chunkIndex, byte[] data)
        {
            _coldMeta.RestoreChunkFromBuffer(chunkIndex, data);
        }

        /// <summary>
        /// Zeros hot (component-mask) slots for dead entities in a chunk.
        /// </summary>
        public void SanitizeHotChunk(int chunkIndex, ReadOnlySpan<bool> liveness)
        {
            _hotMasks.SanitizeChunk(chunkIndex, liveness);
        }

        /// <summary>
        /// Zeros cold (metadata) slots for dead entities in a chunk.
        /// </summary>
        public void SanitizeColdChunk(int chunkIndex, ReadOnlySpan<bool> liveness)
        {
            _coldMeta.SanitizeChunk(chunkIndex, liveness);
        }

        /// <summary>
        /// Scans all cold metadata to rebuild _activeCount, free list, and population counts.
        /// Call this after blindly restoring chunks from disk.
        /// </summary>
        public void RebuildMetadata()
        {
            lock (_createLock)
            {
                _activeCount    = 0;
                _freeCount      = 0;
                _maxIssuedIndex = -1;

                int coldCapacity    = _coldMeta.ChunkCapacity;
                int coldTotalChunks = _coldMeta.TotalChunks;
                int hotCapacity     = _hotMasks.ChunkCapacity;

                // Reset hot population counts before recount
                for (int c = 0; c < _hotMasks.TotalChunks; c++)
                {
                    _hotMasks.SetPopulation(c, 0);
                }

                for (int c = 0; c < coldTotalChunks; c++)
                {
                    if (!_coldMeta.IsChunkCommitted(c))
                    {
                        _coldMeta.SetPopulation(c, 0);
                        continue;
                    }

                    int chunkPop = 0;
                    int startId  = c * coldCapacity;

                    for (int i = 0; i < coldCapacity; i++)
                    {
                        int entityId = startId + i;

                        ref readonly var meta = ref _coldMeta.GetRefRO(entityId);

                        if (meta.IsActive)
                        {
                            chunkPop++;
                            _activeCount++;
                            if (entityId > _maxIssuedIndex) _maxIssuedIndex = entityId;

                            // Keep hot chunk population in sync
                            int hotChunk = entityId / hotCapacity;
                            _hotMasks.IncrementPopulation(hotChunk);
                        }
                    }

                    _coldMeta.SetPopulation(c, chunkPop);
                }

                // Rebuild free list fully
                RebuildFreeList();
            }
        }
    }
}
