using System;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Navigation.Recast;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ CE-1034 H1 (<c>docs/DESIGN_Terrain_Height.md</c> §5 H1) — the ground has height: the grid file, the ground at a point,
    /// surfaces and levels over relief, buildings standing on a slope, the navmesh soup following the ground — and a flat terrain
    /// unchanged.
    /// </summary>
    public sealed class TerrainHeightTests
    {
        [Fact]
        public void H1_TheAsciiGrid_ReadsCornerOrCentre_NorthRowFirst_AndNoData()
        {
            var corner = TerrainHeightGrid.ParseAsciiGrid("ncols 3\nnrows 2\nxllcorner 100\nyllcorner 200\ncellsize 10\nNODATA_value -9999\n1 2 3\n4 -9999 6\n", "t", noData: 7f);
            Assert.Equal(new Vector2(105, 205), corner.Origin);          // a corner ⇒ the first cell's centre
            Assert.Equal(4f, corner[0, 0]);                              // the file's LAST row is the south row
            Assert.Equal(7f, corner[1, 0]);                              // NODATA ⇒ the given value
            Assert.Equal(3f, corner[2, 1]);
            var centre = TerrainHeightGrid.ParseAsciiGrid("ncols 2\nnrows 2\nxllcenter 0\nyllcenter 0\ncellsize 5\n0 0\n0 0\n", "t");
            Assert.Equal(Vector2.Zero, centre.Origin);
            Assert.Throws<ArgumentException>(() => TerrainHeightGrid.ParseAsciiGrid("ncols 2\nnrows 2\nxllcenter 0\nyllcenter 0\ncellsize 5\n0 0 0\n", "t"));
        }

        [Fact]
        public void H1_TheGround_FollowsTheGrid_AndSurfacesAndLevelsStandOnIt()
        {
            var w = SlopeFixture.World();
            Assert.NotNull(w.Height);
            foreach (var (x, y) in new[] { (0f, 0f), (55f, 37f), (120f, 199f), (199f, 5f) })
            {
                float g = SlopeFixture.Rise * y;
                Assert.Equal(g, w.GroundHeightAt(x, y), 3);
                Assert.Equal(g, w.SurfaceZ(x, y, g + 0.2f), 3);            // standing on the slope
                Assert.Equal(g, w.ResolveLevel(x, y, 0), 3);               // level 0 is the ground HERE
                var levels = w.SurfacesAt(x, y, out int ground);
                Assert.Equal(g, levels[ground], 3);
            }
            Assert.Equal(20f, w.GroundHeightAt(50f, 500f), 3);             // past the grid the edge holds
            Assert.Equal(new TerrainWorldQuery(w).GroundHeightAt(50f, 120f), w.GroundHeightAt(50f, 120f));
        }

        [Fact]
        public void H1_OnASlope_ABuildingAndAWallStandOnTheLowestGroundUnderThem_TH_C()
        {
            var w = SlopeFixture.World(
                "{ \"type\": \"Feature\", \"properties\": { \"kind\": \"building\", \"height\": 6, \"label\": \"B\" }, " +
                "  \"geometry\": { \"type\": \"Polygon\", \"coordinates\": [[[40,60],[80,60],[80,100],[40,100],[40,60]]] } }," +
                "{ \"type\": \"Feature\", \"properties\": { \"kind\": \"wall\", \"height\": 1.5 }, " +
                "  \"geometry\": { \"type\": \"LineString\", \"coordinates\": [[120,30],[120,90]] } }," +
                "{ \"type\": \"Feature\", \"properties\": { \"kind\": \"building\", \"height\": 6, \"baseZ\": 50, \"label\": \"Raised\" }, " +
                "  \"geometry\": { \"type\": \"Polygon\", \"coordinates\": [[[150,150],[160,150],[160,160],[150,160],[150,150]]] } }");
            var building = w.Prisms.Single(p => p.Label == "B");
            Assert.Equal(SlopeFixture.Rise * 60f, building.BaseZ, 3);     // its downhill (south) edge
            var wall = w.Prisms.First(p => p.Kind == TerrainPrismKind.Wall);
            Assert.Equal(SlopeFixture.Rise * 30f, wall.BaseZ, 3);         // its lowest end
            Assert.Equal(50f, w.Prisms.Single(p => p.Label == "Raised").BaseZ);   // an explicit base still wins
        }

        [Fact]
        public void H1_TheNavmeshSoup_FollowsTheGround_AndAFlatTerrainIsUnchanged()
        {
            var w = SlopeFixture.World();
            TerrainWorldMesh.Build(w, out var v, out var idx, cellSize: 2f);
            Assert.NotEmpty(v);
            foreach (var p in v) Assert.Equal(w.GroundHeightAt(p.X, p.Y), p.Z, 3);   // every ground vertex sits on the grid
            Assert.Contains(v, p => p.Z > 15f);                                        // the high end is really high

            var flat = TerrainWorldParser.Parse("""{ "type": "FeatureCollection", "hrot": { "schemaVersion": 1, "bounds": [0, 0, 40, 40], "groundZ": 1.5 }, "features": [] }""");
            Assert.Null(flat.Height);
            TerrainWorldMesh.Build(flat, out var fv, out _, cellSize: 2f);
            Assert.All(fv, p => Assert.Equal(1.5f, p.Z));
            Assert.Equal(1.5f, flat.GroundHeightAt(10f, 10f));
        }

        // ── ⭐ CE-1034 H2 (TH-D) — a hill blocks sight and fire ──────────────────────────────────────────────

        [Fact]
        public void H2_AHill_BlocksSight_AndALineOverItOrAlongTheCrestIsClear()
        {
            var w = SlopeFixture.Ridge();
            float G(float y) => w.GroundHeightAt(100f, y);
            var southEye = new Vector3(100f, 20f, G(20f) + 1.8f);
            var northEye = new Vector3(100f, 180f, G(180f) + 1.8f);
            Assert.True(w.SegmentBlocked(southEye, northEye), "two men on either side of a 10 m ridge cannot see each other");
            Assert.False(w.SegmentBlocked(southEye with { Z = 40f }, northEye with { Z = 40f }), "a line well above the crest is clear");
            Assert.False(w.SegmentBlocked(new Vector3(20f, 100f, 11.8f), new Vector3(180f, 100f, 11.8f)), "along the crest is clear");
            Assert.False(w.SegmentBlocked(new Vector3(100f, 30f, G(30f)), new Vector3(100f, 40f, G(40f) + 1.8f)), "feet on the slope are not a hill in the way");
            Assert.False(w.SegmentBlocked(new Vector3(100f, 50f, -5f), northEye), "an end under the ground is malformed, not an occluder");

            var sight = w.QuerySight(southEye, northEye);
            Assert.Contains(sight.Crossed, c => c.Kind == TerrainWorld.GroundKind && c.Transmittance == 0f);
            Assert.Equal(0f, sight.Transmittance);
            Assert.True(new TerrainWorldQuery(w).SightBlocked(southEye, northEye));
        }

        [Fact]
        public void H2_AHill_StopsARound_AndItsCrestIsTheBlastShadowsTop()
        {
            var w = SlopeFixture.Ridge();
            var a = new Vector3(100f, 20f, w.GroundHeightAt(100f, 20f) + 1.5f);
            var b = new Vector3(100f, 180f, w.GroundHeightAt(100f, 180f) + 1.5f);
            var fire = w.QueryFire(a, b);
            var ground = Assert.Single(fire, c => c.Kind == TerrainWorld.GroundKind);
            Assert.Equal(TerrainWorld.GroundResistanceMmRha, ground.ResistanceMmRha);
            Assert.InRange(ground.TopZ, SlopeFixture.RidgeTop - 0.01f, SlopeFixture.RidgeTop + 0.01f);
            // a level line 1.5 m over the ground at y = 20 (z 3.5) meets the slope where the ground reaches 3.5 — y = 35, t = 15/160
            Assert.InRange(ground.T, 0.093f, 0.095f);

            float damage = 1f, pen = 2000f;
            Assert.True(Fdp.Toolkit.Combat.TerrainPenetration.Carry(new TerrainWorldQuery(w), a, b, ref damage, ref pen, out float stopT));
            Assert.InRange(stopT, 0.093f, 0.095f);
            var traced = new System.Collections.Generic.List<Fdp.Toolkit.World.TraceCrossing>();
            new TerrainWorldQuery(w).Trace(a, b, Fdp.Toolkit.World.TracePurpose.Fire, traced);
            Assert.False(traced.Single(c => c.Kind == TerrainWorld.GroundKind).ClosedBarrier);   // a hill diffracts a blast, like a wall
        }

        [Fact]
        public void H2_TheGroundTrace_AllocatesNothing_AndAFlatTerrainHasNone()
        {
            var w = SlopeFixture.Ridge();
            var a = new Vector3(100f, 20f, 3.8f); var b = new Vector3(100f, 180f, 3.8f);
            w.SegmentBlocked(a, b);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) w.SegmentBlocked(a, b);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

            var flat = TerrainWorldParser.Parse("""{ "type": "FeatureCollection", "hrot": { "schemaVersion": 1, "bounds": [0, 0, 200, 200], "groundZ": 0 }, "features": [] }""");
            Assert.False(flat.SegmentBlocked(new Vector3(0, 0, 0.1f), new Vector3(200, 200, 0.1f)));
            Assert.DoesNotContain(flat.QueryFire(new Vector3(0, 0, 0.1f), new Vector3(200, 200, 0.1f)), c => c.Kind == TerrainWorld.GroundKind);
        }

        // ── ⭐ CE-1034 H3 (TH-F) — points that arrive 2-D stand on the real ground ────────────────────────────

        [Fact]
        public void H3_ARoadRoute_OverASlope_PutsItsRoadPointsOnTheGround()
        {
            var w = SlopeFixture.World();
            using var roads = new RoadsHolder(Fdp.Toolkit.Tests.Squad.TestTownRoads.Build());
            var planner = new Fdp.Toolkit.Navigation.RoutePlanner { World = new TerrainWorldQuery(w) };
            var q = new Fdp.Toolkit.Navigation.RouteQuery(new Vector3(20f, 200f, w.GroundHeightAt(20f, 200f)),
                new Vector3(200f, 20f, w.GroundHeightAt(200f, 20f)), Fdp.Toolkit.Navigation.RoadUse.StronglyPrefer,
                Fdp.Toolkit.Navigation.NavigationBackend.NavRoadGraph);
            Assert.NotNull(planner.Plan(in q, in roads.Blob, navmesh: null, doors: null, out _));
            Assert.True(planner.Points.Count >= 3);
            foreach (var p in planner.Points) Assert.Equal(w.GroundHeightAt(p.X, p.Y), p.Z, 2);   // never at 0 on a hill
        }

        private sealed class RoadsHolder : IDisposable
        {
            public global::CarKinem.Road.RoadNetworkBlob Blob;
            public RoadsHolder(global::CarKinem.Road.RoadNetworkBlob blob) => Blob = blob;
            public void Dispose() => Blob.Dispose();
        }

        [Fact]
        public void H1_TheNavmesh_BakesOverTheSlope_AndAPathClimbsIt()
        {
            var w = SlopeFixture.World();
            var provider = new RecastNavmeshFactory().Build(w);
            Assert.NotNull(provider);
            var low = new Vector3(100f, 10f, SlopeFixture.Rise * 10f);
            var high = new Vector3(100f, 190f, SlopeFixture.Rise * 190f);
            Assert.True(provider!.PathExists(low, high), "a path must climb the 10 % slope");
        }
    }
}
