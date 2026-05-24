using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Spatial.Eqs;
using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>
    /// Unit tests for <see cref="NavmeshSamplesGenerator"/>, <see cref="NavmeshReachableTest"/>,
    /// and <see cref="PathCostScoreTest"/> (TASK-EQS-017).
    /// </summary>
    public class NavmeshTests : IDisposable
    {
        private readonly EntityRepository _repo;

        // Mock navmesh where IsReachable always returns false.
        private sealed class AlwaysUnreachableNavmesh : INavmeshProvider
        {
            public bool IsReachable(Vector2 from, Vector2 to) => false;
            public bool TryGetPathDistance(Vector2 from, Vector2 to, out float pathDist) { pathDist = 0f; return false; }
            public int GetRandomPointsInRadius(Vector2 center, float radius, Span<Vector2> results) => 0;
        }

        // Mock navmesh where IsReachable always returns true, but no path distance.
        private sealed class AlwaysReachableNavmesh : INavmeshProvider
        {
            public bool IsReachable(Vector2 from, Vector2 to) => true;
            public bool TryGetPathDistance(Vector2 from, Vector2 to, out float pathDist) { pathDist = 0f; return false; }
            public int GetRandomPointsInRadius(Vector2 center, float radius, Span<Vector2> results) => 0;
        }

        // Mock navmesh where TryGetPathDistance always returns false (no path).
        private sealed class NoPathNavmesh : INavmeshProvider
        {
            public bool IsReachable(Vector2 from, Vector2 to) => true;
            public bool TryGetPathDistance(Vector2 from, Vector2 to, out float pathDist) { pathDist = 0f; return false; }
            public int GetRandomPointsInRadius(Vector2 center, float radius, Span<Vector2> results) => 0;
        }

        public NavmeshTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<SimTransform>();
        }

        public void Dispose() => _repo.Dispose();

        // T-NS1: NavmeshSamplesGenerator produces positional candidates (EntityId=0).
        [Fact]
        public void NavmeshSamplesGenerator_ProducesPositionalCandidates()
        {
            _repo.SetSingletonManaged<INavmeshProvider>(new StubNavmeshProvider());

            var observer = _repo.CreateEntity();
            _repo.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });

            var sensor = new EqsSensor { SearchRadius = 10f };
            var candidates = new EqsResult[16];
            var gen = new NavmeshSamplesGenerator();

            int count = gen.Generate(observer, ref sensor, _repo, candidates.AsSpan());

            Assert.True(count > 0, "Expected at least one navmesh sample point");
            for (int i = 0; i < count; i++)
                Assert.Equal(0L, candidates[i].EntityId);
        }

        // T-NR1: NavmeshReachableTest: unreachable candidates get EntityId=-1L.
        [Fact]
        public void NavmeshReachableTest_UnreachableCandidates_GetRejected()
        {
            _repo.SetSingletonManaged<INavmeshProvider>(new AlwaysUnreachableNavmesh());

            var observer = _repo.CreateEntity();
            _repo.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });

            var candidates = new EqsResult[]
            {
                new EqsResult { EntityId = 0L, PositionX = 5f,  PositionY = 0f },
                new EqsResult { EntityId = 0L, PositionX = 10f, PositionY = 0f },
            };

            var sensor = new EqsSensor();
            var test = new NavmeshReachableTest();
            test.ExecuteBatch(observer, ref sensor, _repo, candidates.AsSpan());

            Assert.Equal(-1L, candidates[0].EntityId);
            Assert.Equal(-1L, candidates[1].EntityId);
        }

        // T-NR2: NavmeshReachableTest: reachable candidates get flag bit 3.
        [Fact]
        public void NavmeshReachableTest_ReachableCandidates_GetFlagBit3()
        {
            _repo.SetSingletonManaged<INavmeshProvider>(new AlwaysReachableNavmesh());

            var observer = _repo.CreateEntity();
            _repo.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });

            var candidates = new EqsResult[]
            {
                new EqsResult { EntityId = 0L, PositionX = 5f,  PositionY = 0f },
                new EqsResult { EntityId = 0L, PositionX = 10f, PositionY = 0f },
            };

            var sensor = new EqsSensor();
            var test = new NavmeshReachableTest();
            test.ExecuteBatch(observer, ref sensor, _repo, candidates.AsSpan());

            Assert.Equal(0L, candidates[0].EntityId); // not rejected
            Assert.Equal(0L, candidates[1].EntityId); // not rejected
            Assert.NotEqual(0, candidates[0].Flags & (1 << 3));
            Assert.NotEqual(0, candidates[1].Flags & (1 << 3));
        }

        // T-NR3: NavmeshReachableTest skips already-rejected candidates.
        [Fact]
        public void NavmeshReachableTest_SkipsAlreadyRejected()
        {
            _repo.SetSingletonManaged<INavmeshProvider>(new AlwaysReachableNavmesh());

            var observer = _repo.CreateEntity();
            _repo.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });

            var candidates = new EqsResult[]
            {
                new EqsResult { EntityId = -1L, PositionX = 5f, PositionY = 0f, Flags = 0 },
            };

            var sensor = new EqsSensor();
            var test = new NavmeshReachableTest();
            test.ExecuteBatch(observer, ref sensor, _repo, candidates.AsSpan());

            Assert.Equal(-1L, candidates[0].EntityId); // still rejected
            Assert.Equal(0, candidates[0].Flags);      // flag not set
        }

        // T-PC1: PathCostScoreTest rejects candidates with no path.
        [Fact]
        public void PathCostScoreTest_NoPath_RejectsCandidate()
        {
            _repo.SetSingletonManaged<INavmeshProvider>(new NoPathNavmesh());

            var observer = _repo.CreateEntity();
            _repo.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });

            var candidates = new EqsResult[]
            {
                new EqsResult { EntityId = 0L, PositionX = 5f, PositionY = 0f },
            };

            var sensor = new EqsSensor { SearchRadius = 60f };
            var test = new PathCostScoreTest();
            test.ExecuteBatch(observer, ref sensor, _repo, candidates.AsSpan());

            Assert.Equal(-1L, candidates[0].EntityId);
        }

        // T-PC2: PathCostScoreTest scores shorter path higher.
        [Fact]
        public void PathCostScoreTest_ShorterPathScoresHigher()
        {
            _repo.SetSingletonManaged<INavmeshProvider>(new StubNavmeshProvider());

            var observer = _repo.CreateEntity();
            _repo.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });

            // Candidate at (5,0): Euclidean dist = 5.
            // Candidate at (20,0): Euclidean dist = 20.
            var candidates = new EqsResult[]
            {
                new EqsResult { EntityId = 0L, PositionX = 5f,  PositionY = 0f },
                new EqsResult { EntityId = 0L, PositionX = 20f, PositionY = 0f },
            };

            var sensor = new EqsSensor { SearchRadius = 60f };
            var test = new PathCostScoreTest();
            test.ExecuteBatch(observer, ref sensor, _repo, candidates.AsSpan());

            // Shorter path (5) should score higher than longer path (20).
            Assert.True(candidates[0].Score > candidates[1].Score,
                "Candidate at (5,0) should score higher than candidate at (20,0)");
        }
    }
}
