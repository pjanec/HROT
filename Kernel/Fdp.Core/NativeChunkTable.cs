using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices; // For specialized copy ops if needed

namespace Fdp.Kernel
{
    /// <summary>
    /// Page table for lazily-allocated 64KB chunks.
    /// Provides O(1) entity ID → chunk → local offset mapping.
    /// Thread-safe for chunk allocation.
    /// </summary>
    /// <typeparam name="T">Unmanaged element type</typeparam>
    public sealed unsafe class NativeChunkTable<T> : IDisposable where T : unmanaged
    {
        private readonly void* _basePtr;
        private readonly long _totalReservedBytes;
        private readonly int _chunkCapacity;
        private readonly int _totalChunks;
        private readonly NativeChunk<T>[] _chunks;
        
        // Track which chunks are committed (1 bit per chunk)
        private readonly ulong[] _committedMask;
        
        // Track population count per chunk for iterator optimization
        private readonly int[] _populationCounts;
        
        // Track modification version per chunk for Delta Iteration
        // Using PaddedVersion to prevent false sharing when multiple threads
        // update versions for entities in the same chunk simultaneously
        private readonly Internal.PaddedVersion[] _chunkVersions;
        
        // Lock for thread-safe chunk allocation
        private readonly object _allocationLock = new object();
        
        private bool _disposed;
        
        public NativeChunkTable()
        {
            _chunkCapacity = FdpConfig.GetChunkCapacity<T>();
            _totalChunks = FdpConfig.GetRequiredChunks<T>();
            _totalReservedBytes = (long)_totalChunks * FdpConfig.CHUNK_SIZE_BYTES;
            
            // Reserve entire address space upfront
            _basePtr = NativeMemoryAllocator.Reserve(_totalReservedBytes);
            
            // Allocate tracking arrays
            _chunks = new NativeChunk<T>[_totalChunks];
            _populationCounts = new int[_totalChunks];
            _chunkVersions = new Internal.PaddedVersion[_totalChunks];
            
            // Committed mask: 1 bit per chunk
            int maskSize = (_totalChunks + 63) / 64;
            _committedMask = new ulong[maskSize];
        }
        
        /// <summary>
        /// Chunk capacity (number of elements per chunk).
        /// This is a compile-time constant per T.
        /// </summary>
        public int ChunkCapacity => _chunkCapacity;
        
        /// <summary>
        /// Total number of chunks in the table.
        /// </summary>
        public int TotalChunks => _totalChunks;
        
        /// <summary>
        /// Gets the last modification version of a chunk.
        /// </summary>
        public uint GetChunkVersion(int chunkIndex) => _chunkVersions[chunkIndex].Value;
        
        /// <summary>
        /// Gets population count for a specific chunk.
        /// Used for chunk-skipping optimization during iteration.
        /// </summary>
        public int GetPopulationCount(int chunkIndex)
        {
            return _populationCounts[chunkIndex];
        }
        
        /// <summary>
        /// Increments population count for a chunk.
        /// Called when an element is added.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IncrementPopulation(int chunkIndex)
        {
            System.Threading.Interlocked.Increment(ref _populationCounts[chunkIndex]);
        }
        
        /// <summary>
        /// Decrements population count for a chunk.
        /// Called when an element is removed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DecrementPopulation(int chunkIndex)
        {
            System.Threading.Interlocked.Decrement(ref _populationCounts[chunkIndex]);
        }
        
        /// <summary>
        /// Accesses element at global entity ID.
        /// Lazily allocates chunks as needed (thread-safe).
        /// </summary>
        public ref T this[int entityId]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref GetRefRW(entityId, 0); // Legacy behavior: assumes no version update? Or 0?
            // Actually, legacy [] usage implies implicit read/write?
            // If we want to strictly support versioning, we should likely force users to use explicit methods.
            // But to maintain compat, we'll leave it as non-updating RW or just delegate.
            // For now, let's leave it as "Un-verified RW" (no version bump).
        }
        
        /// <summary>
        /// Efficiently checks if this table has been modified since the specified version.
        /// Uses lazy scan of chunk versions (O(chunks), typically ~100 chunks for 100k entities).
        /// PERFORMANCE: 10-50ns scan time, L1-cache friendly, no write contention.
        /// </summary>
        public bool HasChanges(uint sinceVersion)
        {
            // Fast L1 cache scan of chunk versions array
            // For 100k entities (~100 chunks):
            // - Sequential int reads:  ~10-50 nanoseconds total
            // - L1 cache friendly: one array, sequential access, CPU prefetching
            // - No writes: zero contention
            for (int i = 0; i < _totalChunks; i++)
            {
                // Each chunk already tracks its version
                if (_chunkVersions[i].Value > sinceVersion)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Gets write access to a component and updates the chunk version.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetRefRW(int entityId, uint currentVersion)
        {
            #if FDP_PARANOID_MODE
            if (entityId < 0 || entityId >= FdpConfig.MAX_ENTITIES)
                throw new IndexOutOfRangeException($"Entity ID {entityId} out of range");
            #endif
            
            int chunkIndex = entityId / _chunkCapacity;
            int localIndex = entityId % _chunkCapacity;
            
            EnsureChunkAllocated(chunkIndex);
            
            // OPTIMIZATION: Check-Before-Write to prevent cache line thrashing.
            // When multiple threads process entities in the same chunk,
            // they all write the same version value. Without this check,
            // each write invalidates other cores' cache lines (false sharing),
            // causing severe performance degradation.
            // Also ignoring version 0 to prevent read-via-indexer from resetting versions.
            if (currentVersion != 0 && _chunkVersions[chunkIndex].Value != currentVersion)
            {
                _chunkVersions[chunkIndex].Value = currentVersion;
            }
            
            return ref _chunks[chunkIndex][localIndex];
        }
        
        /// <summary>
        /// Gets read-only access to a component. Does not update version.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref readonly T GetRefRO(int entityId)
        {
            #if FDP_PARANOID_MODE
            if (entityId < 0 || entityId >= FdpConfig.MAX_ENTITIES)
                throw new IndexOutOfRangeException($"Entity ID {entityId} out of range");
            #endif
            
            int chunkIndex = entityId / _chunkCapacity;
            int localIndex = entityId % _chunkCapacity;
            
            EnsureChunkAllocated(chunkIndex);
            
            return ref _chunks[chunkIndex][localIndex];
        }
        
        /// <summary>
        /// Gets direct access to a chunk.
        /// Returns null chunk if not yet allocated.
        /// </summary>
        public NativeChunk<T> GetChunk(int chunkIndex)
        {
            #if FDP_PARANOID_MODE
            if (chunkIndex < 0 || chunkIndex >= _totalChunks)
            {
                throw new IndexOutOfRangeException(
                    $"Chunk index {chunkIndex} out of range [0, {_totalChunks})");
            }
            #endif
            
            return _chunks[chunkIndex];
        }

        /// <summary>
        /// Gets a Span over the specified chunk's data.
        /// Returns empty span if chunk is not committed.
        /// </summary>
        public Span<T> GetChunkSpan(int chunkIndex)
        {
            if (!IsChunkCommitted(chunkIndex))
                return Span<T>.Empty;

            return _chunks[chunkIndex].AsSpan();
        }
        
        /// <summary>
        /// Checks if a chunk has been committed.
        /// </summary>
        public bool IsChunkCommitted(int chunkIndex)
        {
            int maskIndex = chunkIndex / 64;
            int bitIndex = chunkIndex % 64;
            return (_committedMask[maskIndex] & (1UL << bitIndex)) != 0;
        }
        
        /// <summary>
        /// Ensures a chunk is allocated and committed.
        /// Thread-safe via lock.
        /// </summary>
        private void EnsureChunkAllocated(int chunkIndex)
        {
            // Fast path: already committed
            if (IsChunkCommitted(chunkIndex))
                return;
            
            // Slow path: need to commit (thread-safe)
            lock (_allocationLock)
            {
                // Double-check after acquiring lock
                if (IsChunkCommitted(chunkIndex))
                    return;
                
                // Calculate chunk address
                byte* chunkPtr = (byte*)_basePtr + (chunkIndex * (long)FdpConfig.CHUNK_SIZE_BYTES);
                
                // Commit physical memory
                NativeMemoryAllocator.Commit(chunkPtr, FdpConfig.CHUNK_SIZE_BYTES);
                
                // Create chunk wrapper
                _chunks[chunkIndex] = new NativeChunk<T>(chunkPtr, _chunkCapacity);
                
                // Clear chunk to zeros
                _chunks[chunkIndex].Clear();
                
                // Mark as committed
                int maskIndex = chunkIndex / 64;
                int bitIndex = chunkIndex % 64;
                _committedMask[maskIndex] |= (1UL << bitIndex);
            }
        }
        
        /// <summary>
        /// Clears all chunks (decommits memory).
        /// </summary>
        public void Clear()
        {
            lock (_allocationLock)
            {
                for (int i = 0; i < _totalChunks; i++)
                {
                    if (IsChunkCommitted(i))
                    {
                        byte* chunkPtr = (byte*)_basePtr + (i * (long)FdpConfig.CHUNK_SIZE_BYTES);
                        NativeMemoryAllocator.Decommit(chunkPtr, FdpConfig.CHUNK_SIZE_BYTES);
                        _chunks[i] = default;
                    }
                    _populationCounts[i] = 0;
                    _chunkVersions[i].Value = 0;
                }
                Array.Clear(_committedMask, 0, _committedMask.Length);
            }
        }

        /// <summary>
        /// Attempts to decommit a chunk if it's empty.
        /// Used for memory reclamation without full compaction.
        /// </summary>
        public bool TryDecommitChunk(int chunkIndex)
        {
            #if FDP_PARANOID_MODE
            if (chunkIndex < 0 || chunkIndex >= _totalChunks)
            {
                throw new IndexOutOfRangeException(
                    $"Chunk index {chunkIndex} out of range [0, {_totalChunks})");
            }
            #endif
            
            lock (_allocationLock)
            {
                // Only decommit if empty
                if (_populationCounts[chunkIndex] != 0)
                    return false;
                
                // Not committed? Nothing to do
                if (!IsChunkCommitted(chunkIndex))
                    return false;
                
                // Calculate chunk address
                byte* chunkPtr = (byte*)_basePtr + (chunkIndex * (long)FdpConfig.CHUNK_SIZE_BYTES);
                
                // Decommit physical memory
                NativeMemoryAllocator.Decommit(chunkPtr, FdpConfig.CHUNK_SIZE_BYTES);
                
                // Mark as not committed
                int maskIndex = chunkIndex / 64;
                int bitIndex = chunkIndex % 64;
                _committedMask[maskIndex] &= ~(1UL << bitIndex);
                
                // Nullify chunk
                _chunks[chunkIndex] = default;
                
                return true;
            }
        }
        
        /// <summary>
        /// Gets statistics about memory usage.
        /// </summary>
        public (int TotalChunks, int CommittedChunks, long CommittedBytes) GetMemoryStats()
        {
            lock (_allocationLock)
            {
                int committedCount = 0;
                for (int i = 0; i < _totalChunks; i++)
                {
                    if (IsChunkCommitted(i))
                        committedCount++;
                }
                
                long committedBytes = committedCount * (long)FdpConfig.CHUNK_SIZE_BYTES;
                
                return (_totalChunks, committedCount, committedBytes);
            }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            // Free entire reserved region
            if (_basePtr != null)
            {
                NativeMemoryAllocator.Free(_basePtr, _totalReservedBytes);
            }
            
            _disposed = true;
            GC.SuppressFinalize(this);
        }
        
        ~NativeChunkTable()
        {
            Dispose();
        }
        /// <summary>
        /// Zeros out memory for dead entities in the specified chunk.
        /// CRITICAL: This modifies live memory. Must be thread-safe relative to Simulation.
        /// The livenessMap is indexed by entity ID, not component slot.
        /// </summary>
        public void SanitizeChunk(int chunkIndex, ReadOnlySpan<bool> livenessMap)
        {
            if (!IsChunkCommitted(chunkIndex)) return;

            // Pointer math to get chunk start
            byte* chunkBase = (byte*)_basePtr + (chunkIndex * (long)FdpConfig.CHUNK_SIZE_BYTES);
            int stride = Unsafe.SizeOf<T>();
            
            // The livenessMap is indexed by entity ID (e.g., 0-682 for chunk 0 of EntityIndex)
            // We need to iterate through those entity IDs and zero their component slots
            for (int i = 0; i < livenessMap.Length; i++)
            {
                if (!livenessMap[i]) // Entity is DEAD
                {
                    // Calculate address of the component slot for this entity ID
                    // Component data is stored at [entityId] position
                    // BUT WAIT: livenessMap[i] corresponds to the i-th entity IN THIS CHUNK?
                    // Yes, standard usage assumes livenessMap length == chunkCapacity.
                    // entityId = chunkIndex * capacity + i.
                    // But here we are dealing with a physical chunk.
                    // The elements in this chunk are indexed 0..capacity-1.
                    
                    void* slotPtr = chunkBase + (i * stride);
                    
                    // Nuke it. 
                    Unsafe.InitBlock(slotPtr, 0, (uint)stride);
                }
            }
        }

        /// <summary>
        /// Copies the raw byte content of a chunk to a destination buffer.
        /// Used for snapshots.
        /// Returns number of bytes written.
        /// </summary>
        public int CopyChunkToBuffer(int chunkIndex, Span<byte> destination)
        {
            if (!IsChunkCommitted(chunkIndex)) return 0;

            byte* sourcePtr = (byte*)_basePtr + (chunkIndex * (long)FdpConfig.CHUNK_SIZE_BYTES);
            int copySize = FdpConfig.CHUNK_SIZE_BYTES;

            #if FDP_PARANOID_MODE
            if (destination.Length < copySize)
                throw new ArgumentException($"Destination buffer too small. Required: {copySize}, Got: {destination.Length}");
            #endif

            fixed (byte* destPtr = destination)
            {
                Unsafe.CopyBlock(destPtr, sourcePtr, (uint)copySize);
            }

            return copySize;
        }

        /// <summary>
        /// Restores chunk data from a buffer.
        /// Used for playback/replay.
        /// </summary>
        public void RestoreChunkFromBuffer(int chunkIndex, ReadOnlySpan<byte> source)
        {
            // Ensure chunk is allocated
            EnsureChunkAllocated(chunkIndex);

            byte* destPtr = (byte*)_basePtr + (chunkIndex * (long)FdpConfig.CHUNK_SIZE_BYTES);
            int copySize = Math.Min(source.Length, FdpConfig.CHUNK_SIZE_BYTES);

            fixed (byte* srcPtr = source)
            {
                Unsafe.CopyBlock(destPtr, srcPtr, (uint)copySize);
            }
        }

        /// <summary>
        /// Synchronizes dirty chunks from a source table to this table.
        /// Uses version tracking to optimize transfer (only copies modified chunks).
        /// </summary>
        public void SyncDirtyChunks(NativeChunkTable<T> source)
        {
            #if FDP_PARANOID_MODE
            if (source.TotalChunks != _totalChunks)
                throw new ArgumentException("Source table has different topology");
            #endif

            // Get pointer to access raw data for memcpy
            byte* thisBase = (byte*)_basePtr;
            byte* sourceBase = (byte*)source._basePtr;

            for (int i = 0; i < _totalChunks; i++)
            {
                // Optimization: Version Check
                uint srcVer = source.GetChunkVersion(i);
                if (_chunkVersions[i].Value == srcVer)
                    continue;

                // Source likely has changes (or we are stale).
                
                // Liveness check: Is source committed?
                if (!source.IsChunkCommitted(i))
                {
                    // Source is empty/null. If we are committed, we should clear/decommit.
                    if (IsChunkCommitted(i))
                    {
                        SetPopulation(i, 0); // Mark empty so Decommit succeeds
                        TryDecommitChunk(i);
                    }
                    
                    // Sync version so we don't check again until it changes
                    _chunkVersions[i].Value = srcVer;
                    continue;
                }

                // Source has data. Copy it.
                EnsureChunkAllocated(i);
                
                long offset = i * (long)FdpConfig.CHUNK_SIZE_BYTES;
                byte* destChunk = thisBase + offset;
                byte* sourceChunk = sourceBase + offset;

                // Tier 1 Copy: Memcpy (64KB)
                Unsafe.CopyBlock(destChunk, sourceChunk, (uint)FdpConfig.CHUNK_SIZE_BYTES);

                // Update metadata
                _chunkVersions[i].Value = srcVer;
                SetPopulation(i, source.GetPopulationCount(i));
            }
        }

        /// <summary>
        /// Explicitly sets the population count for a chunk.
        /// Internal use only for metadata rebuilding.
        /// </summary>
        public void SetPopulation(int chunkIndex, int count)
        {
             System.Threading.Interlocked.Exchange(ref _populationCounts[chunkIndex], count);
        }

        public void IncrementChunkVersion(int chunkIndex)
        {
            _chunkVersions[chunkIndex].Value++;
        }
    }
}
