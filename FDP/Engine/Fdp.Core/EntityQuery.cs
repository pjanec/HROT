using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Fdp.Core.Internal;

namespace Fdp.Core
{
    /// <summary>
    /// Defines a query for entities with specific component requirements.
    /// Uses BitMask for O(1) filtering via SIMD (when AVX2 enabled).
    /// Immutable after construction for thread-safety.
    /// </summary>
    public sealed class EntityQuery
    {
        private readonly BitMask512 _includeMask;
        private readonly BitMask512 _excludeMask;
        private readonly BitMask512 _authorityIncludeMask;
        private readonly BitMask512 _authorityExcludeMask;
        private readonly EntityRepository _repository;
        private readonly bool _hasDisFilter;
        private readonly ulong _disFilterValue; // The target ID
        private readonly ulong _disFilterMask;  // Which bytes to check
        private readonly EntityLifecycle _lifecycleFilter;

        internal EntityQuery(EntityRepository repository, BitMask512 includeMask, BitMask512 excludeMask, BitMask512 authorityIncludeMask, BitMask512 authorityExcludeMask, bool hasDisFilter, ulong disFilterValue, ulong disFilterMask, EntityLifecycle lifecycleFilter)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _includeMask = includeMask;
            _excludeMask = excludeMask;
            _authorityIncludeMask = authorityIncludeMask;
            _authorityExcludeMask = authorityExcludeMask;
            _hasDisFilter = hasDisFilter;
            _disFilterValue = disFilterValue;
            _disFilterMask = disFilterMask;
            _lifecycleFilter = lifecycleFilter;
        }

        /// <summary>
        /// Iterates over all entities matching this query.
        /// Calls action for each matching entity.
        /// Performance: Skips chunks with no matching entities.
        /// </summary>
        [Obsolete("Use foreach loop for zero allocation. query.ForEach allocates closures.")]
        public void ForEach(Action<Entity> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            
            var entityIndex = _repository.GetEntityIndex();
            int maxIndex = entityIndex.MaxIssuedIndex;
            
            for (int i = 0; i <= maxIndex; i++)
            {
                // 1. Hot: component-mask checks (skip cold if fail)
                ref var compMask = ref entityIndex.GetComponentMaskUnsafe(i);
                if (!BitMask512.HasAll(compMask, _includeMask)) continue;
                if (BitMask512.HasAny(compMask, _excludeMask)) continue;

                // 2. Cold: liveness + full match
                ref readonly var meta = ref entityIndex.GetMetadataUnsafe(i);
                if (!meta.IsActive) continue;
                if (Matches(in compMask, in meta))
                {
                    action(new Entity(i, meta.Generation));
                }
            }
        }

        /// <summary>
        /// Gets an enumerator for zero-allocation iteration.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityEnumerator GetEnumerator() => new EntityEnumerator(this);

        /// <summary>
        /// Returns <c>true</c> if no entity currently matches this query.
        /// Intended for cheap early-exit guards in systems that are registered
        /// unconditionally but only do real work when matching entities exist.
        /// </summary>
        public bool IsEmpty
        {
            get
            {
                var en = GetEnumerator();
                return !en.MoveNext();
            }
        }

        /// <summary>
        /// Zero-allocation enumerator for EntityQuery.
        /// </summary>
        public ref struct EntityEnumerator
        {
            // Fields to cache for performance (avoid referencing EntityQuery object in loop)
            private readonly BitMask512 _includeMask;
            private readonly BitMask512 _excludeMask;
            private readonly BitMask512 _authorityIncludeMask;
            private readonly BitMask512 _authorityExcludeMask;
            private readonly bool _hasDisFilter;
            private readonly ulong _disFilterValue;
            private readonly ulong _disFilterMask;
            private readonly EntityLifecycle _lifecycleFilter;
            private readonly EntityIndex _entityIndex;
            
            private int _currentIndex;
            private readonly int _maxIndex;

            internal EntityEnumerator(EntityQuery query)
            {
                _includeMask = query._includeMask;
                _excludeMask = query._excludeMask;
                _authorityIncludeMask = query._authorityIncludeMask;
                _authorityExcludeMask = query._authorityExcludeMask;
                _hasDisFilter = query._hasDisFilter;
                _disFilterValue = query._disFilterValue;
                _disFilterMask = query._disFilterMask;
                _lifecycleFilter = query._lifecycleFilter;
                
                // Direct access to index for maximum speed
                _entityIndex = query._repository.GetEntityIndex();
                _maxIndex = _entityIndex.MaxIssuedIndex;
                _currentIndex = -1; 
            }

            public Entity Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => new Entity(_currentIndex, _entityIndex.GetMetadataUnsafe(_currentIndex).Generation);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                // Tight hot-first loop: hot-mask checks before any cold-data access.
                while (++_currentIndex <= _maxIndex)
                {
                    // 1. HOT: component-mask check (one cache line, no cold access)
                    ref var compMask = ref _entityIndex.GetComponentMaskUnsafe(_currentIndex);
                    if (!BitMask512.HasAll(compMask, _includeMask)) continue;
                    if (BitMask512.HasAny(compMask, _excludeMask))  continue;

                    // ---- Only entities passing hot filters reach cold memory ----

                    // 2. COLD: liveness
                    ref readonly var meta = ref _entityIndex.GetMetadataUnsafe(_currentIndex);
                    if (!meta.IsActive) continue;

                    // 3. COLD: lifecycle filter
                    if (_lifecycleFilter != EntityLifecycle.All)
                    {
                        if (meta.LifecycleState != _lifecycleFilter)
                            continue;
                    }

                    // 4. COLD: authority mask
                    if (!BitMask512.HasAll(meta.AuthorityMask, _authorityIncludeMask)) continue;
                    if (BitMask512.HasAny(meta.AuthorityMask, _authorityExcludeMask))  continue;

                    // 5. COLD: DIS filter
                    if (_hasDisFilter)
                    {
                        if ((meta.DisType.Value & _disFilterMask) != _disFilterValue)
                            continue;
                    }

                    return true;
                }

                return false;
            }
        }
        
        /// <summary>
        /// Counts entities matching this query.
        /// Optimized to avoid allocation.
        /// </summary>
        public int Count()
        {
            var entityIndex = _repository.GetEntityIndex();
            int maxIndex = entityIndex.MaxIssuedIndex;
            int count = 0;
            
            for (int i = 0; i <= maxIndex; i++)
            {
                // Hot first
                ref var compMask = ref entityIndex.GetComponentMaskUnsafe(i);
                if (!BitMask512.HasAll(compMask, _includeMask)) continue;
                if (BitMask512.HasAny(compMask, _excludeMask))  continue;

                ref readonly var meta = ref entityIndex.GetMetadataUnsafe(i);
                if (!meta.IsActive) continue;
                if (Matches(in compMask, in meta))
                    count++;
            }
            
            return count;
        }
        
        /// <summary>
        /// Checks if any entities match this query.
        /// Short-circuits on first match.
        /// </summary>
        public bool Any()
        {
            var entityIndex = _repository.GetEntityIndex();
            int maxIndex = entityIndex.MaxIssuedIndex;
            
            for (int i = 0; i <= maxIndex; i++)
            {
                // Hot first
                ref var compMask = ref entityIndex.GetComponentMaskUnsafe(i);
                if (!BitMask512.HasAll(compMask, _includeMask)) continue;
                if (BitMask512.HasAny(compMask, _excludeMask))  continue;

                ref readonly var meta = ref entityIndex.GetMetadataUnsafe(i);
                if (!meta.IsActive) continue;
                if (Matches(in compMask, in meta))
                    return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Gets the first entity matching this query.
        /// Returns Entity.Null if no matches.
        /// </summary>
        public Entity FirstOrNull()
        {
            var entityIndex = _repository.GetEntityIndex();
            int maxIndex = entityIndex.MaxIssuedIndex;
            
            for (int i = 0; i <= maxIndex; i++)
            {
                // Hot first
                ref var compMask = ref entityIndex.GetComponentMaskUnsafe(i);
                if (!BitMask512.HasAll(compMask, _includeMask)) continue;
                if (BitMask512.HasAny(compMask, _excludeMask))  continue;

                ref readonly var meta = ref entityIndex.GetMetadataUnsafe(i);
                if (!meta.IsActive) continue;
                if (Matches(in compMask, in meta))
                    return new Entity(i, meta.Generation);
            }
            
            return Entity.Null;
        }

        /// <summary>
        /// Checks if an entity's component mask and metadata match this query.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Matches(in BitMask512 componentMask, in EntityMetadataCold meta)
        {
            // Lifecycle Filter
            if (_lifecycleFilter != EntityLifecycle.All)
            {
                if (meta.LifecycleState != _lifecycleFilter)
                    return false;
            }

            // Component Mask
            if (!BitMask512.HasAll(componentMask, _includeMask)) return false;
            if (BitMask512.HasAny(componentMask, _excludeMask)) return false;

            // Authority Mask
            if (!BitMask512.HasAll(meta.AuthorityMask, _authorityIncludeMask)) return false;
            if (BitMask512.HasAny(meta.AuthorityMask, _authorityExcludeMask)) return false;
                
            // DIS Filter (Single instruction check)
            if (_hasDisFilter)
            {
                if ((meta.DisType.Value & _disFilterMask) != _disFilterValue)
                    return false;
            }
            
            return true;
        }
        
        // ================================================
        // CHUNK-AWARE ITERATION (Stage 8)
        // ================================================
        
        /// <summary>
        /// Iterates with chunk skipping optimization.
        /// Skips entire chunks if they have no active entities.
        /// Better cache locality than ForEach.
        /// </summary>
        public void ForEachChunked(Action<Entity> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            
            var entityIndex = _repository.GetEntityIndex();
            int totalChunks = entityIndex.GetTotalChunks();
            int chunkCapacity = entityIndex.GetChunkCapacity();
            
            for (int chunkIdx = 0; chunkIdx < totalChunks; chunkIdx++)
            {
                // Skip chunks with no entities
                int population = entityIndex.GetChunkPopulation(chunkIdx);
                if (population == 0)
                    continue;
                
                int startIndex = chunkIdx * chunkCapacity;
                int endIndex = Math.Min(startIndex + chunkCapacity, entityIndex.MaxIssuedIndex + 1);
                
                // Iterate through chunk
                for (int i = startIndex; i < endIndex; i++)
                {
                    // Hot first
                    ref var compMask = ref entityIndex.GetComponentMaskUnsafe(i);
                    if (!BitMask512.HasAll(compMask, _includeMask)) continue;
                    if (BitMask512.HasAny(compMask, _excludeMask))  continue;

                    ref readonly var meta = ref entityIndex.GetMetadataUnsafe(i);
                    if (!meta.IsActive) continue;
                    if (!Matches(in compMask, in meta)) continue;

                    var entity = new Entity(i, meta.Generation);
                    action(entity);
                }
            }
        }
        
        /// <summary>
        /// Parallel iteration over entities.
        /// Uses adaptive batching to balance overhead vs. granularity.
        /// Automatically handles chunk skipping and load balancing.
        /// Zero-allocation design via object pooling.
        /// Thread-safe as long as action doesn't modify shared state.
        /// </summary>
        /// <param name="action">Action to execute for each matching entity</param>
        /// <param name="hint">Workload hint for batch size optimization</param>
        public void ForEachParallel(Action<Entity> action, ParallelHint hint = ParallelHint.Light)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            
            var entityIndex = _repository.GetEntityIndex();
            int maxIndex = entityIndex.MaxIssuedIndex;
            int activeCount = entityIndex.ActiveCount;
            
            // 1. Fallback Threshold
            // Don't spin up threads for trivial counts where overhead exceeds benefit.
            // 1024 is the crossover point where parallelism beats single-threaded for light work.
            if (activeCount < 1024 && hint == ParallelHint.Light)
            {
                foreach (var entity in this)
                {
                    action(entity);
                }
                return;
            }
            
            // 2. Resolve Batch Size from Hint
            // Tune to balance task scheduler overhead vs. load balancing granularity.
            int batchSize = hint switch
            {
                ParallelHint.VeryHeavy => 16,
                ParallelHint.Heavy => 64,
                ParallelHint.Medium => 256,
                _ => 1024 // Light
            };
            
            // 3. Adaptive Tuning for Light Workloads
            // For simple operations, adjust batch size based on entity count
            // to avoid excessive synchronization overhead.
            if (hint == ParallelHint.Light)
            {
                int coreCount = FdpConfig.MaxDegreeOfParallelism > 0 
                    ? FdpConfig.MaxDegreeOfParallelism 
                    : Environment.ProcessorCount;
                
                // Target ~2x batches per core.
                // 4x adds too much synchronization overhead for light work.
                int targetBatches = coreCount * 2;
                int calculatedSize = activeCount / targetBatches;
                
                // Clamp: Never go below 512 for light work, never go above 8192 (cache locality limit)
                batchSize = Math.Clamp(calculatedSize, 512, 8192);
            }
            
            // 4. Profiling Start
#if FDP_PROFILING
            long startTicks = Stopwatch.GetTimestamp();
#endif
            
            // 5. Build Work List (Zero-Alloc via Pooling)
            var workBatches = BatchListPool.Get();
            
            try
            {
                GenerateBatches(entityIndex, maxIndex, batchSize, workBatches);
                
                // DEBUG ASSERTION: Validate batches don't overlap
#if DEBUG
                ValidateBatches(workBatches, maxIndex);
#endif
                
                // 6. Execute Parallel Loop
                Parallel.ForEach(workBatches, FdpConfig.ParallelOptions, range =>
                {
                    // Tight hot-first inner loop
                    for (int i = range.Item1; i < range.Item2; i++)
                    {
                        ref var compMask = ref entityIndex.GetComponentMaskUnsafe(i);
                        if (!BitMask512.HasAll(compMask, _includeMask)) continue;
                        if (BitMask512.HasAny(compMask, _excludeMask))  continue;

                        ref readonly var meta = ref entityIndex.GetMetadataUnsafe(i);
                        if (!meta.IsActive) continue;
                        if (Matches(in compMask, in meta))
                        {
                            action(new Entity(i, meta.Generation));
                        }
                    }
                });
            }
            finally
            {
                // 7. Return to pool to prevent GC pressure
                BatchListPool.Return(workBatches);
            }
            
            // 8. Telemetry Output
#if FDP_PROFILING
            long endTicks = Stopwatch.GetTimestamp();
            double ms = (endTicks - startTicks) * 1000.0 / Stopwatch.Frequency;
            // Only log if slow to avoid console spam
            if (ms > 1.0)
            {
                Console.WriteLine($"[FDP Query] Parallel: {ms:F2}ms, Batches: {workBatches.Count}, Hint: {hint}");
            }
#endif
        }
        
        /// <summary>
        /// Generates work batches by slicing populated chunks into cache-friendly ranges.
        /// Skips empty chunks entirely for efficiency.
        /// </summary>
        private void GenerateBatches(EntityIndex index, int maxIndex, int batchSize, 
            System.Collections.Generic.List<(int Start, int End)> batches)
        {
            int totalChunks = index.GetTotalChunks();
            int chunkCapacity = index.GetChunkCapacity();
            
            for (int c = 0; c < totalChunks; c++)
            {
                // Chunk Skipping Optimization: Skip entire 64KB chunks with no entities
                if (index.GetChunkPopulation(c) == 0)
                    continue;
                
                int chunkStart = c * chunkCapacity;
                if (chunkStart > maxIndex)
                    break;
                
                int chunkEnd = Math.Min(chunkStart + chunkCapacity, maxIndex + 1);
                
                // Flattened Slicing: Break populated chunks into smaller batches
                // for load balancing across cores
                for (int b = chunkStart; b < chunkEnd; b += batchSize)
                {
                    int batchEnd = Math.Min(b + batchSize, chunkEnd);
                    batches.Add((b, batchEnd));
                }
            }
        }
        
        /// <summary>
        /// Debug validation to ensure batches are well-formed and don't overlap.
        /// Only active in DEBUG builds.
        /// </summary>
        [Conditional("DEBUG")]
        private void ValidateBatches(System.Collections.Generic.List<(int Start, int End)> batches, int maxIndex)
        {
            for (int i = 0; i < batches.Count; i++)
            {
                var b = batches[i];
                Debug.Assert(b.Start >= 0, "Batch start negative");
                Debug.Assert(b.End > b.Start, "Batch size zero or negative");
                Debug.Assert(b.End <= maxIndex + 1, "Batch out of bounds");
                
                // Ensure batches are ordered and non-overlapping
                if (i > 0)
                {
                    Debug.Assert(b.Start >= batches[i - 1].End, 
                        "Batches overlap or unordered - potential data race!");
                }
            }
        }
        
        /// <summary>
        /// Gets the include mask (for advanced usage).
        /// </summary>
        public BitMask512 IncludeMask => _includeMask;
        
        /// <summary>
        /// Gets the exclude mask (for advanced usage).
        /// </summary>
        public BitMask512 ExcludeMask => _excludeMask;
    }
}
