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
    
        // ── H5a — bridges: ground-relative walkables (TH-I), the buried ramp foot (TH-K), the steep-ramp warning ──

        private static string Feature(string kind, string props, string ring)
            => "{ \"type\": \"Feature\", \"properties\": { \"kind\": \"" + kind + "\"" + props + " }, \"geometry\": { \"type\": \"Polygon\", \"coordinates\": [ " + ring + " ] } }";

        [Fact]
        public void H5a_ASlabAboveGround_StandsThatHighAboveTheSlopeAtEveryVertex()
        {
            var world = SlopeFixture.World(Feature("slab", ", \"aboveGround\": 6", "[ [80, 60], [120, 60], [120, 140], [80, 140], [80, 60] ]"));
            var deck = Assert.Single(world.Walkables);
            Assert.All(deck.Vertices, v => Assert.Equal(SlopeFixture.Rise * v.Y + 6f, v.Z, 3));
            Assert.Equal(SlopeFixture.Rise * 100f + 6f, world.SurfaceZ(100f, 100f, 50f), 3);   // standing on the deck
            Assert.Equal(SlopeFixture.Rise * 100f, world.SurfaceZ(100f, 100f, SlopeFixture.Rise * 100f), 3);   // and under it
            Assert.Empty(world.Warnings);
        }

        [Fact]
        public void H5a_ARampFromTheGroundToADeck_IsBuriedAtItsFoot_SoTheSurfaceRisesWithoutAStep()
        {
            // a ramp eastwards: its west edge AT the ground (0), its east edge 4 m up — heights above the ground per vertex
            var world = SlopeFixture.World(Feature("ramp", ", \"zRef\": \"ground\"", "[ [40, 90, 0], [64, 90, 4], [64, 110, 4], [40, 110, 0], [40, 90, 0] ]"));
            var ramp = Assert.Single(world.Walkables);
            Assert.Equal(SlopeFixture.Rise * 90f - TerrainWorldParser.RampFootSink, ramp.Vertices.Min(v => v.Z), 3);   // the foot is sunk
            Assert.Equal(SlopeFixture.Rise * 90f + 4f, ramp.Vertices.Where(v => v.Y < 95f).Max(v => v.Z), 3);

            // drive east along y = 100 from the ground: every step up stays within the VEHICLE layer's 0.1 m, and it ends on the ramp
            float z = world.GroundHeightAt(30f, 100f), maxStep = 0f;
            for (float x = 30f; x <= 63f; x += 0.15f)
            {
                float next = world.SurfaceZ(x, 100f, z);
                maxStep = MathF.Max(maxStep, next - z);
                z = next;
            }
            Assert.True(maxStep < 0.1f, $"a step of {maxStep:F3} m where the ramp meets the ground");
            Assert.InRange(z, SlopeFixture.Rise * 100f + 3.6f, SlopeFixture.Rise * 100f + 4.01f);   // near the top (the sunk foot steepens it slightly)
            Assert.Empty(world.Warnings);   // 4 m over 24 m is 9.5°
        }

        [Fact]
        public void H5a_ARampTooSteepForVehicles_LoadsWithAWarning_AndAbsoluteHeightsAreUnchanged()
        {
            var steep = SlopeFixture.World(Feature("ramp", ", \"zRef\": \"ground\"", "[ [40, 90, 0], [48, 90, 6], [48, 110, 6], [40, 110, 0], [40, 90, 0] ]"));
            Assert.Contains(steep.Warnings, w => w.Contains("steeper than the vehicle navmesh"));

            var absolute = SlopeFixture.World(Feature("slab", ", \"z\": 30", "[ [80, 60], [120, 60], [120, 140], [80, 140], [80, 60] ]"));
            Assert.All(Assert.Single(absolute.Walkables).Vertices, v => Assert.Equal(30f, v.Z, 3));
        }
    
        // ── H5b — the navmesh on a bridge (TH-K): the deck AND the ground under it, the ramp feet joined, both layers ──

        /// <summary>An east–west overpass on the slope: a deck 6 m above the ground (x 80–120), a 30 m ramp each side, 10 m wide.</summary>
        internal static TerrainWorld Overpass() => SlopeFixture.World(string.Join(", ",
            Feature("slab", ", \"aboveGround\": 6", "[ [80, 95], [120, 95], [120, 105], [80, 105], [80, 95] ]"),
            Feature("ramp", ", \"zRef\": \"ground\"", "[ [50, 95, 0], [80, 95, 6], [80, 105, 6], [50, 105, 0], [50, 95, 0] ]"),
            Feature("ramp", ", \"zRef\": \"ground\"", "[ [120, 95, 6], [150, 95, 0], [150, 105, 0], [120, 105, 6], [120, 95, 6] ]")));

        [Theory]
        [InlineData(Fdp.Toolkit.Navigation.NavLayerMask.Infantry)]
        // ⛔ the Vehicle layer bakes NO navmesh on any slope (CE-1048): its 0.1 m step is below the 0.2 m voxel, so Recast's climb is 0 cells
        [InlineData(Fdp.Toolkit.Navigation.NavLayerMask.Vehicle, Skip = "CE-1048 — the Vehicle layer has no navmesh on relief until its step is at least one voxel")]
        public void H5b_TheNavmesh_HasTheDeckAndTheGroundUnderIt_AndTheRampFeetJoinTheGround(Fdp.Toolkit.Navigation.NavLayerMask layer)
        {
            var w = Overpass();
            Assert.Empty(w.Warnings);
            var provider = new RecastNavmeshFactory { TileCache = null }.Build(w);
            Assert.NotNull(provider);
            uint mask = (uint)layer;
            float g(float x, float y) => w.GroundHeightAt(x, y);

            // ① the deck is baked: projected from just above it, a point lands ON the deck
            Assert.True(provider!.ProjectToNavmesh(new Vector3(100f, 100f, g(100f, 100f) + 6.5f), out var onDeck, mask), "no navmesh on the deck");
            Assert.InRange(onDeck.Z, g(100f, 100f) + 5.6f, g(100f, 100f) + 6.4f);
            // ② and so is the ground under it: projected from the ground, the same XY lands on the ground
            Assert.True(provider.ProjectToNavmesh(new Vector3(100f, 100f, g(100f, 100f) + 0.2f), out var under, mask), "no navmesh under the deck");
            Assert.InRange(under.Z, g(100f, 100f) - 0.4f, g(100f, 100f) + 0.4f);
            // ③ the deck is reached from the ground beyond EACH ramp — only through the buried feet
            Assert.True(provider.PathExists(onDeck, new Vector3(30f, 100f, g(30f, 100f)), mask), "the west ramp's foot does not join the ground");
            Assert.True(provider.PathExists(onDeck, new Vector3(170f, 100f, g(170f, 100f)), mask), "the east ramp's foot does not join the ground");
            // ④ and the road under the bridge passes from south to north beneath it
            Assert.True(provider.PathExists(new Vector3(100f, 60f, g(100f, 60f)), new Vector3(100f, 140f, g(100f, 140f)), mask), "no passage under the deck");
        }
    
    }
}
