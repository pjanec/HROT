using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Fdp.Core;

namespace Fdp.Benchmarks
{
    /// <summary>
    /// Measures the performance difference between a naive per-frame scan of all
    /// entities versus the QueryDelta approach that first skips unchanged chunks
    /// and then uses a per-entity dirty-flag check (simulated with a Dictionary
    /// to mirror SmartEgressUtil.ShouldPublish overhead).
    ///
    /// Key hypothesis: when only a small fraction of entities change each frame,
    /// QueryDelta reduces dictionary lookups from O(N) to O(changed_chunk_entities).
    ///
    /// EntityHeader chunk capacity = 65536 / 96 = 682 entities per chunk.
    /// Position  chunk capacity   = 65536 /  8 = 8192 entities per chunk.
    ///
    /// Run with: dotnet run -c Release
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(RuntimeMoniker.Net80)]
    public class QueryDeltaBenchmarks
    {
        // Number of entities in the world.
        [Params(1_000, 5_000, 10_000)]
        public int EntityCount;

        // How many entities actually change their Position component each frame.
        // 0  = nothing changed (hot path: zero-work case).
        // 10 = typical sparse mutation (10 AI units updated their intent).
        // 100 = moderate burst.
        [Params(0, 10, 100)]
        public int ChangedCount;

        private EntityRepository _repo;
        private EntityQuery _query;
        private Entity[] _entities;
        private uint _lastScanTick;

        // Simulates per-entity publication state, mirroring the algorithmic cost
        // of SmartEgressUtil.ShouldPublish (one Dictionary<int,uint> lookup per
        // entity that passes the coarse filter).
        private Dictionary<int, uint> _pubState;

        [GlobalSetup]
        public void Setup()
        {
            ComponentTypeRegistry.Clear();

            _repo = new EntityRepository();
            _repo.RegisterComponent<BenchPosition>();

            _entities = new Entity[EntityCount];
            for (int i = 0; i < EntityCount; i++)
            {
                _entities[i] = _repo.CreateEntity();
                _repo.AddComponent(_entities[i], new BenchPosition { X = i, Y = i });
            }

            _query = _repo.Query().With<BenchPosition>().Build();
            _pubState = new Dictionary<int, uint>(EntityCount);

            // Perform an initial tick so GlobalVersion > the creation version.
            _repo.Tick();
            _lastScanTick = _repo.GlobalVersion;
        }

        /// <summary>
        /// Called before each benchmark iteration to simulate one game frame:
        /// advance the global tick and dirty a controlled number of entities.
        /// </summary>
        [IterationSetup]
        public void IterationSetup()
        {
            // Snapshot the current version as the "previous frame" baseline for
            // QueryDelta, then advance the clock.
            _lastScanTick = _repo.GlobalVersion;
            _repo.Tick();

            // Spread the changed entities evenly across the entity ID space so
            // they hit multiple EntityHeader chunks, giving QueryDelta a realistic
            // chance to skip unchanged regions.
            if (ChangedCount > 0)
            {
                int step = EntityCount / ChangedCount;
                for (int i = 0; i < ChangedCount; i++)
                {
                    int idx = (i * step) % EntityCount;
                    ref var pos = ref _repo.GetComponentRW<BenchPosition>(_entities[idx]);
                    pos.X += 1;
                }
            }
        }

        /// <summary>
        /// Baseline: iterates ALL entities every frame (the existing broken pattern).
        /// Calls ShouldPublish-equivalent for each entity regardless of whether it
        /// changed. Represents the pre-QueryDelta state.
        /// </summary>
        [Benchmark(Baseline = true)]
        public int NaiveScan()
        {
            int published = 0;
            foreach (var entity in _query)
            {
                // Simulate SmartEgressUtil.ShouldPublish: one dictionary lookup.
                if (!_pubState.TryGetValue(entity.Index, out uint lastTick)
                    || lastTick < _lastScanTick)
                {
                    _pubState[entity.Index] = _repo.GlobalVersion;
                    published++;
                }
            }
            return published;
        }

        /// <summary>
        /// Optimised: uses QueryDelta to skip unchanged chunks entirely, then
        /// performs the dictionary lookup only for entities that passed the
        /// unmanaged coarse filter.
        /// </summary>
        [Benchmark]
        public int QueryDeltaScan()
        {
            int published = 0;
            foreach (var entity in _repo.QueryDelta(_query, _lastScanTick))
            {
                // Fine-grained managed filter (mirrors SmartEgressUtil.ShouldPublish).
                if (!_pubState.TryGetValue(entity.Index, out uint lastTick)
                    || lastTick < _lastScanTick)
                {
                    _pubState[entity.Index] = _repo.GlobalVersion;
                    published++;
                }
            }
            return published;
        }
    }

    [ComponentId(240)]
    struct BenchPosition
    {
        public float X, Y;
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkRunner.Run<CommandBufferPlaybackBenchmarks>();
            BenchmarkRunner.Run<QueryDeltaBenchmarks>();
        }
    }
}
