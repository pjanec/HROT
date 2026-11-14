using System;
using System.Numerics;
using CarKinem.Trajectory;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.EngineBacked;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Tests for <see cref="EngineBackedPathRegistry"/> (NAV-P6-T4).
    /// </summary>
    public sealed class EngineBackedPathRegistryTests : IDisposable
    {
        private readonly TrajectoryPoolManager _pool;
        private readonly EngineBackedPathRegistry _registry;

        public EngineBackedPathRegistryTests()
        {
            _pool     = new TrajectoryPoolManager();
            _registry = new EngineBackedPathRegistry(_pool);
        }

        public void Dispose()
        {
            _pool.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private void RegisterPath(int handle, Vector2[] positions,
                                  byte replanCount = 0, float totalDist = 0f, byte backend = 0)
        {
            _pool.RegisterTrajectoryWithKey(positions, handle);
            _registry.Register(handle, replanCount, totalDist, backend);
        }

        // ── Tests ─────────────────────────────────────────────────────────────────

        [Fact]
        public void Register_ThenIsCached_ReturnsTrue()
        {
            RegisterPath(42, new[] { new Vector2(0f, 0f), new Vector2(10f, 10f) });

            Assert.True(_registry.IsCached(42));
        }

        [Fact]
        public void Register_ThenTryGetSummary_ReturnsSummaryWithCorrectWaypointCount()
        {
            var positions = new[]
            {
                new Vector2(0f,  0f),
                new Vector2(5f,  5f),
                new Vector2(10f, 0f),
            };
            RegisterPath(10, positions, totalDist: 14.14f);

            bool ok = _registry.TryGetSummary(10, out var summary);

            Assert.True(ok);
            Assert.Equal(3, summary.WaypointCount);
            Assert.Equal(10, summary.RouteHandle);
        }

        [Fact]
        public void TryGetWaypoints_PositionsMatchTrajectory()
        {
            var positions = new[]
            {
                new Vector2(1f, 2f),
                new Vector2(3f, 4f),
            };
            RegisterPath(7, positions);

            var buf = new NavWaypoint[4];
            bool ok = _registry.TryGetWaypoints(7, buf.AsSpan(), out int count);

            Assert.True(ok);
            Assert.Equal(2, count);
            // Z-up (R-182 / W7): the 2-D trajectory point (x, y) is NavWaypoint (x, y, altitude 0) — no swizzle.
            Assert.Equal(1f, buf[0].Position.X, precision: 4);
            Assert.Equal(2f, buf[0].Position.Y, precision: 4);
            Assert.Equal(0f, buf[0].Position.Z, precision: 4);
            Assert.Equal(3f, buf[1].Position.X, precision: 4);
            Assert.Equal(4f, buf[1].Position.Y, precision: 4);
        }

        [Fact]
        public void TryGetWaypoints_TraversalKindIsWalk()
        {
            var positions = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(2f, 2f),
            };
            RegisterPath(11, positions);

            var buf = new NavWaypoint[4];
            _registry.TryGetWaypoints(11, buf.AsSpan(), out int count);

            for (int i = 0; i < count; i++)
                Assert.Equal(TraversalKind.Walk, buf[i].Traversal);
        }

        [Fact]
        public void TryGetWaypointsSlice_ReturnsCorrectSubset()
        {
            var positions = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(2f, 0f),
                new Vector2(3f, 0f),
            };
            RegisterPath(20, positions);

            var buf = new NavWaypoint[4];
            bool ok = _registry.TryGetWaypointsSlice(20, startSegment: 1, maxCount: 2, buf.AsSpan(), out int actual);

            Assert.True(ok);
            Assert.Equal(2, actual);
            Assert.Equal(1f, buf[0].Position.X, precision: 4);
            Assert.Equal(2f, buf[1].Position.X, precision: 4);
        }

        [Fact]
        public void Free_RemovesEntry_IsCachedReturnsFalse()
        {
            RegisterPath(30, new[] { new Vector2(0f, 0f), new Vector2(1f, 1f) });
            Assert.True(_registry.IsCached(30));

            _registry.Free(30);

            Assert.False(_registry.IsCached(30));
        }

        [Fact]
        public void Free_RemovesFromPool()
        {
            RegisterPath(31, new[] { new Vector2(0f, 0f), new Vector2(1f, 1f) });

            _registry.Free(31);

            Assert.False(_pool.TryGetTrajectory(31, out _));
        }

        [Fact]
        public void TryGetWaypoints_StaleReplanCount_ReturnsFalse()
        {
            RegisterPath(50, new[] { new Vector2(0f, 0f), new Vector2(5f, 5f) }, replanCount: 0);

            var buf = new NavWaypoint[4];
            bool ok = _registry.TryGetWaypoints(50, expectedReplanCount: 1, buf.AsSpan(), out int count);

            Assert.False(ok);
            Assert.Equal(0, count);
        }

        [Fact]
        public void TryGetWaypoints_MatchingReplanCount_ReturnsTrue()
        {
            RegisterPath(51, new[] { new Vector2(0f, 0f), new Vector2(5f, 5f) }, replanCount: 2);

            var buf = new NavWaypoint[4];
            bool ok = _registry.TryGetWaypoints(51, expectedReplanCount: 2, buf.AsSpan(), out int count);

            Assert.True(ok);
            Assert.Equal(2, count);
        }

        [Fact]
        public void TryGetWaypoints_UnknownHandle_ReturnsFalse()
        {
            var buf = new NavWaypoint[4];
            bool ok = _registry.TryGetWaypoints(999, buf.AsSpan(), out int count);

            Assert.False(ok);
            Assert.Equal(0, count);
        }

        [Fact]
        public void IsCached_UnknownHandle_ReturnsFalse()
        {
            Assert.False(_registry.IsCached(888));
        }

        [Fact]
        public void TryGetSummary_TotalDistanceMeters_UsesTrajLengthWhenNotProvided()
        {
            // Register without providing a totalDist override (0f triggers fallback to traj.TotalLength).
            _pool.RegisterTrajectoryWithKey(new[] { new Vector2(0f, 0f), new Vector2(3f, 4f) }, 60);
            _registry.Register(60, replanCount: 0, totalDistanceMeters: 0f, primaryBackend: 0);

            _registry.TryGetSummary(60, out var summary);

            // Distance from (0,0) to (3,4) = 5 metres.
            Assert.Equal(5f, summary.TotalDistanceMeters, precision: 3);
        }

        /// <summary>
        /// ⭐ Buildings 5d-3 (N4) — a door mark the planner put on a waypoint survives the trajectory pool and comes back out of the
        /// registry (both read-backs): it was forced to <see cref="TraversalKind.Walk"/> before.
        /// </summary>
        [Fact]
        public void Stage5d_TheDoorMark_SurvivesThePoolAndTheRegistry()
        {
            var positions = new[] { new Vector3(0, 0, 0), new Vector3(5, 0, 0), new Vector3(6, 0, 0), new Vector3(12, 0, 0) };
            var marks = new byte[] { 0, (byte)TraversalKind.Door, (byte)TraversalKind.Door, 0 };
            _pool.RegisterTrajectoryWithKey(positions, 77, marks);
            _registry.Register(77, 0, 12f, 0);

            Span<NavWaypoint> wps = stackalloc NavWaypoint[8];
            Assert.True(_registry.TryGetWaypoints(77, 0, wps, out int n));
            Assert.Equal(4, n);
            Assert.Equal(new[] { TraversalKind.Walk, TraversalKind.Door, TraversalKind.Door, TraversalKind.Walk },
                new[] { wps[0].Traversal, wps[1].Traversal, wps[2].Traversal, wps[3].Traversal });

            Assert.True(_registry.TryGetWaypointsSlice(77, 1, 2, wps, out int m));
            Assert.Equal(TraversalKind.Door, wps[0].Traversal);

            _pool.RegisterTrajectoryWithKey(positions, 78);                                // no marks ⇒ all Walk
            Assert.True(_pool.TryGetTrajectory(78, out var plain));
            for (int i = 0; i < plain.Waypoints.Length; i++) Assert.Equal(0, plain.Waypoints[i].Traversal);
        }
    }
}
