using System.Numerics;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Tests for <see cref="FakeNavmeshProvider"/>.
    /// All polygons are squares in the (X, Z) plane for simplicity.
    /// </summary>
    public class FakeNavmeshProviderTests
    {
        // Shared test layer: two adjacent squares + one isolated square.
        //   Poly 1: (0,0)-(2,2)  Poly 2: (2,0)-(4,2)  -- adjacent along X=2
        //   Poly 3: (10,10)-(12,12)  -- disconnected

        private static FakeNavLayer BuildTwoAdjacentLayer()
        {
            var poly1 = new NavPolygon
            {
                Id       = 1,
                Vertices = new[]
                {
                    new Vector3(0, 0, 0), new Vector3(2, 0, 0),
                    new Vector3(2, 0, 2), new Vector3(0, 0, 2),
                },
            };
            var poly2 = new NavPolygon
            {
                Id       = 2,
                Vertices = new[]
                {
                    new Vector3(2, 0, 0), new Vector3(4, 0, 0),
                    new Vector3(4, 0, 2), new Vector3(2, 0, 2),
                },
            };
            return new FakeNavLayer
            {
                Layer    = 1u,
                Polygons = new[] { poly1, poly2 },
                Adjacency = new[]
                {
                    new[] { 1 }, // poly1 (index 0) is adjacent to poly2 (index 1)
                    new[] { 0 }, // poly2 (index 1) is adjacent to poly1 (index 0)
                },
            };
        }

        private static FakeNavLayer BuildIsolatedLayer()
        {
            var poly3 = new NavPolygon
            {
                Id       = 3,
                Vertices = new[]
                {
                    new Vector3(10, 0, 10), new Vector3(12, 0, 10),
                    new Vector3(12, 0, 12), new Vector3(10, 0, 12),
                },
            };
            return new FakeNavLayer
            {
                Layer     = 1u,
                Polygons  = new[] { poly3 },
                Adjacency = new[] { System.Array.Empty<int>() },
            };
        }

        // ── Test 1: IsWalkable inside polygon ────────────────────────────────────

        [Fact]
        public void IsWalkable_InsidePolygon_ReturnsTrue()
        {
            var provider = new FakeNavmeshProvider(BuildTwoAdjacentLayer());
            // Center of poly1
            Assert.True(provider.IsWalkable(new Vector3(1, 0, 1)));
        }

        // ── Test 2: IsWalkable outside all polygons ──────────────────────────────

        [Fact]
        public void IsWalkable_OutsideAllPolygons_ReturnsFalse()
        {
            var provider = new FakeNavmeshProvider(BuildTwoAdjacentLayer());
            // Well outside all polygons
            Assert.False(provider.IsWalkable(new Vector3(100, 0, 100)));
        }

        // ── Test 3: IsWalkable blocked polygon ───────────────────────────────────

        [Fact]
        public void IsWalkable_BlockedPolygon_ReturnsFalse()
        {
            var layer    = BuildTwoAdjacentLayer();
            var provider = new FakeNavmeshProvider(layer);

            provider.BlockPolygon(1);

            // Center of poly1 should now be non-walkable
            Assert.False(provider.IsWalkable(new Vector3(1, 0, 1)));
            // poly2 still walkable
            Assert.True(provider.IsWalkable(new Vector3(3, 0, 1)));
        }

        // ── Test 4: PathExists connected polygons ────────────────────────────────

        [Fact]
        public void PathExists_ConnectedPolygons_ReturnsTrue()
        {
            var provider = new FakeNavmeshProvider(BuildTwoAdjacentLayer());
            Assert.True(provider.PathExists(new Vector3(1, 0, 1), new Vector3(3, 0, 1)));
        }

        // ── Test 5: PathExists disconnected polygons ─────────────────────────────

        [Fact]
        public void PathExists_DisconnectedPolygons_ReturnsFalse()
        {
            var layer1 = BuildTwoAdjacentLayer();
            var layer2 = BuildIsolatedLayer();
            // Merge into a single layer with no connection between groups.
            var combined = new FakeNavLayer
            {
                Layer    = 1u,
                Polygons = new[]
                {
                    layer1.Polygons[0], layer1.Polygons[1], layer2.Polygons[0],
                },
                Adjacency = new[]
                {
                    new[] { 1 },                         // poly1 -> poly2
                    new[] { 0 },                         // poly2 -> poly1
                    System.Array.Empty<int>(),           // poly3 isolated
                },
            };
            var provider = new FakeNavmeshProvider(combined);
            Assert.False(provider.PathExists(new Vector3(1, 0, 1), new Vector3(11, 0, 11)));
        }

        // ── Test 6: PlanPath includes off-mesh link waypoints ────────────────────

        [Fact]
        public void PlanPath_IncludesOffMeshLinkWaypoints()
        {
            // poly1 and poly3 are NOT adjacent by normal edges; connected only via off-mesh link.
            var poly1 = new NavPolygon
            {
                Id       = 1,
                Vertices = new[]
                {
                    new Vector3(0, 0, 0), new Vector3(2, 0, 0),
                    new Vector3(2, 0, 2), new Vector3(0, 0, 2),
                },
            };
            var poly3 = new NavPolygon
            {
                Id       = 3,
                Vertices = new[]
                {
                    new Vector3(10, 0, 10), new Vector3(12, 0, 10),
                    new Vector3(12, 0, 12), new Vector3(10, 0, 12),
                },
            };
            var link = new OffMeshLink
            {
                FromPolygonId = 1,
                ToPolygonId   = 3,
                StartPos      = new Vector3(2, 0, 1),
                EndPos        = new Vector3(10, 0, 11),
                Kind          = TraversalKind.Jump,
                Cost          = 1f,
            };
            var layer = new FakeNavLayer
            {
                Layer        = 1u,
                Polygons     = new[] { poly1, poly3 },
                Adjacency    = new[] { System.Array.Empty<int>(), System.Array.Empty<int>() },
                OffMeshLinks = new[] { link },
            };
            var provider = new FakeNavmeshProvider(layer);

            var buf = new NavWaypoint[10];
            int n = provider.PlanPath(new Vector3(1, 0, 1), new Vector3(11, 0, 11), buf.AsSpan());

            Assert.True(n >= 3, $"Expected at least 3 waypoints (start + link end + dest), got {n}");
            // At least one waypoint should have TraversalKind.Jump (the off-mesh link end).
            bool hasJump = false;
            for (int i = 0; i < n; i++)
                if (buf[i].Traversal == TraversalKind.Jump) { hasJump = true; break; }
            Assert.True(hasJump, "PlanPath should include a Jump waypoint for the off-mesh link");
        }

        // ── Test 7: BlockPolygon bumps version ───────────────────────────────────

        [Fact]
        public void BlockPolygon_BumpsVersion()
        {
            var provider = new FakeNavmeshProvider(BuildTwoAdjacentLayer());
            uint before = provider.QueryVersion();
            provider.BlockPolygon(1);
            uint after = provider.QueryVersion();
            Assert.True(after > before, "Version should increase after BlockPolygon");
        }

        // ── Test 8: QueryVersion returns max layer version ───────────────────────

        [Fact]
        public void QueryVersion_ReturnsMaxLayerVersion()
        {
            var layer1 = BuildTwoAdjacentLayer();
            layer1.Version = 3u;
            var layer2 = BuildIsolatedLayer();
            layer2.Version = 7u;
            var provider = new FakeNavmeshProvider(layer1, layer2);
            Assert.Equal(7u, provider.QueryVersion());
        }

        // ── Test 9: IsWalkable respects layer mask exclusion ─────────────────────

        [Fact]
        public void IsWalkable_LayerMaskExclusion_RespectsMask()
        {
            var vehicleLayer = new FakeNavLayer
            {
                Layer    = 2u,
                Polygons = new[]
                {
                    new NavPolygon
                    {
                        Id       = 10,
                        Vertices = new[]
                        {
                            new Vector3(20, 0, 20), new Vector3(22, 0, 20),
                            new Vector3(22, 0, 22), new Vector3(20, 0, 22),
                        },
                    },
                },
                Adjacency = new[] { System.Array.Empty<int>() },
            };
            var provider = new FakeNavmeshProvider(vehicleLayer);
            var point = new Vector3(21, 0, 21); // inside vehicleLayer poly

            // Infantry mask (1) excludes the vehicle layer (2).
            Assert.False(provider.IsWalkable(point, layerMask: 1u));
            // Vehicle mask (2) includes the layer.
            Assert.True(provider.IsWalkable(point, layerMask: 2u));
        }

        // ── Test 10: ProjectToNavmesh - point inside polygon ─────────────────────

        [Fact]
        public void ProjectToNavmesh_PointInPolygon_ReturnsSamePoint()
        {
            var provider = new FakeNavmeshProvider(BuildTwoAdjacentLayer());
            bool found = provider.ProjectToNavmesh(new Vector3(1, 0, 1), out var snapped);
            Assert.True(found);
            Assert.Equal(1f, snapped.X);
            Assert.Equal(1f, snapped.Z);
        }

        // ── Test 11: ProjectToNavmesh - point outside returns false ──────────────

        [Fact]
        public void ProjectToNavmesh_PointOutsidePolygon_ReturnsFalse()
        {
            var provider = new FakeNavmeshProvider(BuildTwoAdjacentLayer());
            bool found = provider.ProjectToNavmesh(new Vector3(100, 0, 100), out _);
            Assert.False(found);
        }

        // ── Test 12: PathExists - blocked intermediate polygon ───────────────────

        [Fact]
        public void PathExists_BlockedIntermediatePolygon_FalseAfterBlock()
        {
            var poly1 = new NavPolygon
            {
                Id = 1, Vertices = new[]
                {
                    new Vector3(0, 0, 0), new Vector3(2, 0, 0),
                    new Vector3(2, 0, 2), new Vector3(0, 0, 2),
                },
            };
            var poly2 = new NavPolygon
            {
                Id = 2, Vertices = new[]
                {
                    new Vector3(2, 0, 0), new Vector3(4, 0, 0),
                    new Vector3(4, 0, 2), new Vector3(2, 0, 2),
                },
            };
            var poly3 = new NavPolygon
            {
                Id = 3, Vertices = new[]
                {
                    new Vector3(4, 0, 0), new Vector3(6, 0, 0),
                    new Vector3(6, 0, 2), new Vector3(4, 0, 2),
                },
            };
            var layer = new FakeNavLayer
            {
                Layer     = 1u,
                Polygons  = new[] { poly1, poly2, poly3 },
                Adjacency = new[]
                {
                    new[] { 1 },       // poly1 -> poly2
                    new[] { 0, 2 },    // poly2 -> poly1, poly3
                    new[] { 1 },       // poly3 -> poly2
                },
            };
            var provider = new FakeNavmeshProvider(layer);

            // Before block: reachable.
            Assert.True(provider.PathExists(new Vector3(1, 0, 1), new Vector3(5, 0, 1)));

            // Block intermediate polygon.
            provider.BlockPolygon(2);

            // After block: unreachable.
            Assert.False(provider.PathExists(new Vector3(1, 0, 1), new Vector3(5, 0, 1)));
        }

        // ── Test 13: PathCost - straight corridor equals Euclidean distance ──────

        [Fact]
        public void PathCost_StraightCorridor_EqualsEuclideanDistance()
        {
            var provider = new FakeNavmeshProvider(BuildTwoAdjacentLayer());
            float cost = provider.PathCost(new Vector3(1, 0, 1), new Vector3(3, 0, 1));
            Assert.True(MathF.Abs(cost - 2f) < 0.1f,
                $"Expected PathCost ~2.0, got {cost}");
        }

        // ── Test 14: PathCost - off-mesh link produces finite positive cost ───────

        [Fact]
        public void PathCost_WithOffMeshLink_IncludesLinkCost()
        {
            var poly1 = new NavPolygon
            {
                Id = 1, Vertices = new[]
                {
                    new Vector3(0, 0, 0), new Vector3(2, 0, 0),
                    new Vector3(2, 0, 2), new Vector3(0, 0, 2),
                },
            };
            var poly3 = new NavPolygon
            {
                Id = 3, Vertices = new[]
                {
                    new Vector3(10, 0, 10), new Vector3(12, 0, 10),
                    new Vector3(12, 0, 12), new Vector3(10, 0, 12),
                },
            };
            var link = new OffMeshLink
            {
                FromPolygonId = 1,
                ToPolygonId   = 3,
                StartPos      = new Vector3(2, 0, 1),
                EndPos        = new Vector3(10, 0, 11),
                Kind          = TraversalKind.Jump,
                Cost          = 5f,
            };
            var layer = new FakeNavLayer
            {
                Layer        = 1u,
                Polygons     = new[] { poly1, poly3 },
                Adjacency    = new[] { System.Array.Empty<int>(), System.Array.Empty<int>() },
                OffMeshLinks = new[] { link },
            };
            var provider = new FakeNavmeshProvider(layer);

            float cost = provider.PathCost(new Vector3(1, 0, 1), new Vector3(11, 0, 11));

            Assert.True(cost > 0f && cost < float.MaxValue,
                $"Expected a finite positive cost through the off-mesh link, got {cost}");
        }

        // ── Test 15: Determinism - same map + same queries yield same results ─────

        [Fact]
        public void SameMap_SameQueries_SameResults()
        {
            var p1 = new FakeNavmeshProvider(BuildTwoAdjacentLayer());
            var p2 = new FakeNavmeshProvider(BuildTwoAdjacentLayer());

            var from = new Vector3(1, 0, 1);
            var to   = new Vector3(3, 0, 1);

            Assert.Equal(p1.IsWalkable(from),     p2.IsWalkable(from));
            Assert.Equal(p1.PathExists(from, to), p2.PathExists(from, to));
            Assert.Equal(p1.PathCost(from, to),   p2.PathCost(from, to));
        }
    }
}
