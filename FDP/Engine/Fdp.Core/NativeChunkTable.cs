using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices; // For specialized copy ops if needed

namespace Fdp.Core
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

        // ⭐ CE-2394 — the highest chunk that is committed or carries a version (only grows; Clear() resets it). The loops that walked
        //   every chunk the table COULD have (MAX_ENTITIES / capacity: 250 000 for a 16 KB component) stop here. 📐 The SoD copy for the
        //   background modules (navigation, EQS, traffic) spent most of its time walking empty chunks of the big blackboard tables.
        private int _highWater = -1;

        /// <summary>⭐ CE-2394 — one past the highest chunk that is committed or carries a version: every chunk at or above it is empty
        /// and version 0.</summary>
        public int UsedChunkBound => System.Threading.Volatile.Read(ref _highWater) + 1;

        private void Touch(int chunkIndex)
        {
            int hw;
            while (chunkIndex > (hw = System.Threading.Volatile.Read(ref _highWater))
                   && System.Threading.Interlocked.CompareExchange(ref _highWater, chunkIndex, hw) != hw) { }
        }
        
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
            Touch(chunkIndex);   // ⭐ CE-2396 — a counted chunk stays inside the bound Clear() walks
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
            int end = UsedChunkBound;   // ⭐ CE-2394 — every chunk above it is version 0
            for (int i = 0; i < end; i++)
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

#if DEBUG
            if (currentVersion != 0 && Jobs.ParallelGuard.ReadOnly)   // ⭐ POC 2 Q-4 — a read-only parallel body took a writable reference (version 0 = a read)
                throw new InvalidOperationException($"a writable {typeof(T).Name} was taken inside a READ-ONLY parallel body (ParallelGuard)");
#endif
            
            EnsureChunkAllocated(chunkIndex);
            
            // OPTIMIZATION: Check-Before-Write to prevent cache line thrashing.
            // When multiple threads process entities in the same chunk,
            // they all write the same version value. Without this check,
            // each write invalidates other cores' cache lines (false sharing),
            // causing severe performance degradation.
            // Also ignoring version 0 to prevent read-via-indexer from resetting versions.
            if (currentVersion != 0 && StampTrace.On) StampTrace.Note(typeof(T));   // ⭐ CE-4445 M-4 — who stamps (opt-in)
            if (currentVersion != 0 && _chunkVersions[chunkIndex].Value != currentVersion)
            {
                if (_preStamp != null) _preStamp[chunkIndex] = _chunkVersions[chunkIndex].Value;   // ⭐ D-5 — the version before this frame's stamp
                _chunkVersions[chunkIndex].Value = currentVersion;
            }
            if (Diagnostics.TableWriteStats.On && currentVersion != 0)
            {
                long r = System.Threading.Interlocked.Increment(ref RwRefs);   // ⭐ M-1 (off: removed by the JIT)
                if (s_sampleStacks && (r & 31) == 0) Diagnostics.TableWriteStats.SampleCaller(typeof(T).Name);   // ⭐ POC 2 — who takes it writable
            }
            
            return ref _chunks[chunkIndex][localIndex];
        }

        // ⭐ D-5 (the parallel experiment, E-4) — COMMIT BY CONTENT: at the frame's end a chunk stamped this frame whose bytes equal its shadow
        //   (the bytes it held when its PREVIOUS version was committed) gets that previous version back — a write that changed nothing is not a
        //   change, for every later SyncFrom / HasChanges / QueryDelta. ⚠ Sound only for consumers that read at frame boundaries: a mid-frame
        //   reader that saw an intermediate value (A → B → A) keeps B. Opt-in: HROT_CONTENT_VERSION=1.
        private readonly uint[]? _preStamp = Diagnostics.ContentVersioning.On ? new uint[FdpConfig.GetRequiredChunks<T>()] : null;
        private byte[]?[]? _cshadow;
        private uint[]? _cshadowVer;

        /// <summary>⭐ D-5 — gives every chunk stamped <paramref name="frameVersion"/> whose bytes did not change its previous version back;
        /// keeps the shadows current. Returns the chunks reverted. Allocates only a shadow on a chunk's first sight.</summary>
        internal int CommitByContent(uint frameVersion)
        {
            if (_preStamp == null) return 0;
            _cshadow ??= new byte[]?[_totalChunks];
            _cshadowVer ??= new uint[_totalChunks];
            int end = UsedChunkBound, reverted = 0;
            for (int i = 0; i < end; i++)
            {
                if (!IsChunkCommitted(i)) continue;
                uint v = _chunkVersions[i].Value;
                var bytes = new ReadOnlySpan<byte>((byte*)_basePtr + (long)i * FdpConfig.CHUNK_SIZE_BYTES, FdpConfig.CHUNK_SIZE_BYTES);
                var sh = _cshadow[i];
                if (v == frameVersion)
                {
                    // ⭐ the guard: revert only when nothing else moved the chunk since its shadow (its pre-stamp version IS the shadow's)
                    if (sh != null && _preStamp[i] == _cshadowVer[i] && bytes.SequenceEqual(sh))
                    {
                        _chunkVersions[i].Value = _preStamp[i];
                        reverted++;
                        continue;
                    }
                }
                else if (sh != null && _cshadowVer[i] == v) continue;   // untouched since its shadow
                if (sh == null) _cshadow[i] = sh = new byte[FdpConfig.CHUNK_SIZE_BYTES];
                bytes.CopyTo(sh);
                _cshadowVer[i] = v;
            }
            return reverted;
        }

        // ⭐ M-1 (DESIGN_Parallel_Simulation §8.3) — the write instrument's per-table state; touched only when TableWriteStats.On
        internal long RwRefs;
        private static readonly bool s_sampleStacks = Diagnostics.TableWriteStats.On
            && Environment.GetEnvironmentVariable("HROT_WRITE_STATS_STACKS") == typeof(T).Name;
        private byte[]?[]? _shadow;

        /// <summary>⭐ M-1 — this frame's writable references, the chunks stamped with <paramref name="frameVersion"/>, and how many of those hold the
        /// same bytes as at the last sample; then remembers every committed chunk's bytes for the next sample.</summary>
        internal Diagnostics.TableWriteStats.Sample SampleWrites(uint frameVersion)
        {
            long refs = System.Threading.Interlocked.Exchange(ref RwRefs, 0);
            int end = UsedChunkBound, dirty = 0, same = 0, committed = 0;
            _shadow ??= new byte[]?[_totalChunks];
            for (int i = 0; i < end; i++)
            {
                if (!IsChunkCommitted(i)) continue;
                committed++;
                var bytes = new ReadOnlySpan<byte>((byte*)_basePtr + (long)i * FdpConfig.CHUNK_SIZE_BYTES, FdpConfig.CHUNK_SIZE_BYTES);
                var prev = _shadow[i];
                if (_chunkVersions[i].Value == frameVersion)
                {
                    dirty++;
                    if (prev != null && bytes.SequenceEqual(prev)) same++;
                }
                if (prev == null) _shadow[i] = prev = new byte[FdpConfig.CHUNK_SIZE_BYTES];
                bytes.CopyTo(prev);
            }
            return new Diagnostics.TableWriteStats.Sample(refs, dirty, same, committed);
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
                Touch(chunkIndex);   // ⭐ CE-2394
                
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
                // ⭐ CE-2396 — only up to the used bound: above it every chunk is uncommitted, uncounted and version 0 (CE-2394). It walked
                //   every POSSIBLE chunk (250 000 for a 16 KB component) — per table, on every keyframe apply and every staging reuse.
                int end = UsedChunkBound;
                for (int i = 0; i < end; i++)
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
                System.Threading.Volatile.Write(ref _highWater, -1);   // ⭐ CE-2394 — every version is 0 again
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
        public int CopyChunkToBuffer(int chunkIndex, Span<byte> destination) => CopyChunkToBuffer(chunkIndex, destination, FdpConfig.CHUNK_SIZE_BYTES);

        /// <summary>
        /// ⭐ CE-2392 — the first <paramref name="maxBytes"/> bytes of a chunk only (the recorder's delta: the slots up to the highest issued
        /// entity, not the whole 64 KB chunk). Returns the bytes written; 0 for an uncommitted chunk.
        /// </summary>
        public int CopyChunkToBuffer(int chunkIndex, Span<byte> destination, int maxBytes)
        {
            if (!IsChunkCommitted(chunkIndex)) return 0;

            byte* sourcePtr = (byte*)_basePtr + (chunkIndex * (long)FdpConfig.CHUNK_SIZE_BYTES);
            int copySize = Math.Clamp(maxBytes, 0, FdpConfig.CHUNK_SIZE_BYTES);

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

            IncrementChunkVersion(chunkIndex);
        }

        /// <summary>⭐ CE-1478 P6 — restores one block run: <paramref name="source"/> to <paramref name="byteOffset"/> bytes into the chunk; the chunk is stamped changed.</summary>
        public void RestoreChunkRange(int chunkIndex, int byteOffset, ReadOnlySpan<byte> source)
        {
            EnsureChunkAllocated(chunkIndex);
            int copySize = Math.Min(source.Length, FdpConfig.CHUNK_SIZE_BYTES - byteOffset);
            if (copySize > 0)
            {
                byte* destPtr = (byte*)_basePtr + (chunkIndex * (long)FdpConfig.CHUNK_SIZE_BYTES) + byteOffset;
                fixed (byte* srcPtr = source) Unsafe.CopyBlock(destPtr, srcPtr, (uint)copySize);
            }
            IncrementChunkVersion(chunkIndex);
        }

        /// <summary>
        /// Synchronizes dirty chunks from a source table to this table.
        /// Uses version tracking to optimize transfer (only copies modified chunks).
        /// </summary>
        /// <param name="force">
        /// ⭐ <c>CE-3067</c> — copy every chunk regardless of version. For a table whose writers do NOT stamp the
        /// chunk version (the entity index's component masks and metadata), version equality is not proof the
        /// contents match, and a skipped chunk silently keeps the destination's bytes.
        /// </param>
        /// <summary>⭐ EXPERIMENT (threaded kernel X-1) — how many chunks <see cref="SyncDirtyChunks"/> would copy from <paramref name="source"/> now.</summary>
        internal int CountDirtyChunks(NativeChunkTable<T> source)
        {
            int n = 0, end = Math.Max(source.UsedChunkBound, UsedChunkBound);
            for (int i = 0; i < end; i++)
                if (_chunkVersions[i].Value != source.GetChunkVersion(i)) n++;
            return n;
        }

        public void SyncDirtyChunks(NativeChunkTable<T> source, bool force = false)
        {
            #if FDP_PARANOID_MODE
            if (source.TotalChunks != _totalChunks)
                throw new ArgumentException("Source table has different topology");
            #endif

            // Get pointer to access raw data for memcpy
            byte* thisBase = (byte*)_basePtr;
            byte* sourceBase = (byte*)source._basePtr;

            // ⭐ CE-2394 — only up to the higher of the two used bounds: above both, each side is uncommitted and version 0, so the
            //   version check would skip the chunk anyway (and force would copy "nothing" onto "nothing"). It walked _totalChunks.
            int end = Math.Max(source.UsedChunkBound, UsedChunkBound);
            for (int i = 0; i < end; i++)
            {
                // Optimization: Version Check
                uint srcVer = source.GetChunkVersion(i);
                if (!force && _chunkVersions[i].Value == srcVer)
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
                    if (srcVer != 0) Touch(i);   // ⭐ CE-2394 — a versioned, uncommitted chunk stays inside the bound
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
             if (count != 0) Touch(chunkIndex);   // ⭐ CE-2396
        }

        /// <summary>⭐ M-4 (CE-4483) — the version a sync never matches (no world reaches it).</summary>
        public const uint NeverSyncedVersion = uint.MaxValue;

        /// <summary>⭐ M-4 — every chunk stamped at or after <paramref name="version"/> gets <see cref="NeverSyncedVersion"/>: the next
        /// <see cref="SyncDirtyChunks"/> copies it again (or decommits it, if the source has none).</summary>
        public void InvalidateChunksStampedSince(uint version)
        {
            int end = UsedChunkBound;
            for (int i = 0; i < end; i++)
                if (_chunkVersions[i].Value >= version) _chunkVersions[i].Value = NeverSyncedVersion;
        }

        public void IncrementChunkVersion(int chunkIndex)
        {
            _chunkVersions[chunkIndex].Value++;
            Touch(chunkIndex);   // ⭐ CE-2394
        }
    }
}
