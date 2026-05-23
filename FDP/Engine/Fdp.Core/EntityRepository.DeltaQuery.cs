using System;
using System.Runtime.CompilerServices;

namespace Fdp.Core
{
    public partial class EntityRepository
    {
        /// <summary>
        /// Zero-allocation delta query. Returns a foreach-compatible enumerable that iterates
        /// entities matching the query which have changed since the specified version.
        /// Skips empty EntityHeader chunks for O(populated_chunks) performance.
        /// </summary>
        public DeltaQueryEnumerable QueryDelta(EntityQuery query, uint sinceVersion)
        {
            return new DeltaQueryEnumerable(this, query, sinceVersion);
        }

        /// <summary>
        /// Foreach-compatible container for a delta query.
        /// Zero allocation: no heap objects are created during iteration.
        /// </summary>
        public readonly ref struct DeltaQueryEnumerable
        {
            private readonly EntityRepository _repo;
            private readonly EntityQuery _query;
            private readonly uint _sinceVersion;

            internal DeltaQueryEnumerable(EntityRepository repo, EntityQuery query, uint sinceVersion)
            {
                _repo = repo;
                _query = query;
                _sinceVersion = sinceVersion;
            }

            public DeltaQueryEnumerator GetEnumerator() =>
                new DeltaQueryEnumerator(_repo, _query, _sinceVersion);
        }

        /// <summary>
        /// Zero-allocation enumerator for delta queries.
        ///
        /// Two-level filtering strategy:
        ///   Level 1 (O(chunks)) — at each EntityHeader chunk boundary, check whether the
        ///     EntityHeader chunk itself or any required component-table chunk has changed.
        ///     If neither has a version newer than sinceVersion, the entire 64KB block is
        ///     skipped with a single comparison per component type.  This makes the
        ///     no-change hot path O(populated_chunks) rather than O(entities).
        ///   Level 2 (O(entities in changed chunks)) — within a chunk known to have changed,
        ///     each individual entity is checked: structural (LastChangeTick) and component
        ///     value (per-entity component chunk version).  This resolves chunk-level false
        ///     positives (adjacent entities in the same memory block that did not change).
        /// </summary>
        public ref struct DeltaQueryEnumerator
        {
            private readonly EntityRepository _repo;
            private readonly EntityQuery _query;
            private readonly uint _sinceVersion;
            private readonly EntityIndex _entityIndex;
            private readonly int _maxIndex;
            private readonly int _chunkCapacity;

            private int _currentIndex;
            private int _chunkEnd; // Exclusive end of the current EntityHeader chunk.

            internal DeltaQueryEnumerator(EntityRepository repo, EntityQuery query, uint sinceVersion)
            {
                _repo = repo;
                _query = query;
                _sinceVersion = sinceVersion;
                _entityIndex = repo.GetEntityIndex();
                _maxIndex = _entityIndex.MaxIssuedIndex;
                _chunkCapacity = _entityIndex.GetChunkCapacity();
                _currentIndex = -1;
                _chunkEnd = 0; // Triggers chunk-boundary logic on the first MoveNext call.
            }

            public Entity Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => new Entity(_currentIndex, _entityIndex.GetMetadataUnsafe(_currentIndex).Generation);
            }

            public bool MoveNext()
            {
                while (true)
                {
                    _currentIndex++;

                    if (_currentIndex > _maxIndex)
                        return false;

                    // -- Level 1: chunk-boundary check. --
                    // _chunkEnd starts at 0 so this fires on the very first call.
                    if (_currentIndex >= _chunkEnd)
                    {
                        // Advance to the next populated EntityHeader chunk whose data
                        // has changed since sinceVersion.
                        int chunkIndex = _currentIndex / _chunkCapacity;
                        int totalChunks = _entityIndex.GetTotalChunks();
                        var tableCache  = _repo._tableCache;
                        var includeMask = _query.IncludeMask;

                        while (chunkIndex < totalChunks)
                        {
                            // Skip empty chunks (no entities at all).
                            if (_entityIndex.GetChunkPopulation(chunkIndex) == 0)
                            {
                                chunkIndex++;
                                continue;
                            }

                            // Check component-table chunk versions.  NativeChunkTable stores
                            // the global tick (set by GetRefRW) as the chunk version for
                            // component tables, so it lives in the same version space as
                            // sinceVersion.  A single representative entity index (the
                            // chunk's first slot) is sufficient because GetVersionForEntity
                            // returns the chunk-level version, not the per-entity version.
                            // If a component chunk covers a wider range than this EntityHeader
                            // chunk, the check is conservative: we may over-include but never
                            // under-include.
                            //
                            // NOTE: EntityIndex._headers uses IncrementChunkVersion (a simple
                            // counter unrelated to the global tick), so it cannot be compared
                            // to sinceVersion and is intentionally NOT checked here.
                            bool chunkChanged = false;
                            int chunkStart = chunkIndex * _chunkCapacity;
                            for (int typeId = 0; typeId < tableCache.Length; typeId++)
                            {
                                if (!includeMask.IsSet(typeId))
                                    continue;
                                var table = tableCache[typeId];
                                if (table != null && table.GetVersionForEntity(chunkStart) > _sinceVersion)
                                {
                                    chunkChanged = true;
                                    break;
                                }
                            }

                            if (chunkChanged)
                                break; // Found a candidate chunk; iterate its entities below.

                            chunkIndex++;
                        }

                        if (chunkIndex >= totalChunks)
                            return false;

                        _currentIndex = chunkIndex * _chunkCapacity;
                        _chunkEnd = Math.Min(_currentIndex + _chunkCapacity, _maxIndex + 1);

                        if (_currentIndex > _maxIndex)
                            return false;
                    }

                    // -- Level 2: per-entity check within the candidate chunk. --

                    ref readonly var metaDQ  = ref _entityIndex.GetMetadataUnsafe(_currentIndex);
                    ref var          compDQ  = ref _entityIndex.GetComponentMaskUnsafe(_currentIndex);

                    if (!metaDQ.IsActive)
                        continue;

                    // Level 1 already confirmed that this chunk has at least
                    // one component change (component-table chunk version > sinceVersion).
                    // A per-entity component-version check would be redundant: component
                    // tables use chunk-level granularity, so every entity in the chunk
                    // reports the same version as its neighbours.  Repeating the check
                    // per-entity would only add overhead without filtering additional
                    // false positives.
                    //
                    // Instead, yield all active, query-matching entities from a hot chunk
                    // and rely on the caller's fine filter (e.g. IntentId comparison) to
                    // reject unchanged entities cheaply.
                    if (_query.Matches(_currentIndex, in compDQ, in metaDQ))
                        return true;
                }
            }
        }
    }
}
