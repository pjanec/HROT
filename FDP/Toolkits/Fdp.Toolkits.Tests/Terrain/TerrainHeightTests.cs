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
            // ⭐ CE-1048 — per layer: asked with ALL layers, the infantry path alone satisfied this while the Vehicle layer was empty
            Assert.True(provider.PathExists(low, high, (uint)Fdp.Toolkit.Navigation.NavLayerMask.Infantry), "infantry must climb the slope");
            Assert.True(provider.PathExists(low, high, (uint)Fdp.Toolkit.Navigation.NavLayerMask.Vehicle), "vehicles must climb the slope");
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
        [InlineData(Fdp.Toolkit.Navigation.NavLayerMask.Vehicle)]   // ⭐ CE-1048 — was empty on any slope until the step reached one voxel
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
    
    
        // ── H5c — roads carry their own height (TH-L): over the bridge on the deck, under it on the ground, at every join ──

        /// <summary>The overpass's roads: "Bridge Road" east–west over the deck (its nodes at the ramp feet 0, the deck ends +6), and
        /// "Under Road" north–south beneath it on the ground. Nodes: 0–5 Bridge Road west→east, 6–8 Under Road south→north.</summary>
        private static global::CarKinem.Road.RoadNetworkBlob OverpassRoads()
        {
            var b = new global::CarKinem.Road.RoadNetworkBuilder();
            var nodes = new (Vector2 P, float Up)[]
            {
                (new(20, 100), 0f), (new(50, 100), 0f), (new(80, 100), 6f), (new(120, 100), 6f), (new(150, 100), 0f), (new(180, 100), 0f),
                (new(100, 20), 0f), (new(100, 100), 0f), (new(100, 180), 0f),
            };
            foreach (var n in nodes) b.AddNode(n.P, n.Up);
            void Seg(int a, int c)
            {
                var t = nodes[c].P - nodes[a].P;
                b.AddSegment(nodes[a].P, t, nodes[c].P, t, laneWidth: 3.5f, laneCount: 2, startNodeIdx: a, endNodeIdx: c);
            }
            Seg(0, 1); Seg(1, 2); Seg(2, 3); Seg(3, 4); Seg(4, 5);
            Seg(6, 7); Seg(7, 8);
            return b.Build(cellSize: 10f, gridWidth: 20, gridHeight: 20);
        }

        [Fact]
        public void H5c_ARoadPoint_IsOnTheDeckOverTheBridge_OnTheRamp_AndOnTheGroundUnderIt()
        {
            var w = Overpass();
            var q = new TerrainWorldQuery(w);
            using var roads = new RoadsHolder(OverpassRoads());
            float deck = w.GroundHeightAt(100f, 100f) + 6f;

            var onDeck = Fdp.Toolkit.Navigation.RoadGraphRouter.NearestAccess(roads.Blob, new Vector2(100f, 100.5f), 1f);
            Assert.True(onDeck.IsValid);
            float bridgeZ = Fdp.Toolkit.Navigation.RoadHeights.At(roads.Blob, 2, 20f, new Vector2(100f, 100f), q);   // Bridge Road, mid-deck
            Assert.Equal(deck, bridgeZ, 1);
            float rampZ = Fdp.Toolkit.Navigation.RoadHeights.At(roads.Blob, 1, 15f, new Vector2(65f, 100f), q);      // half-way up the west ramp
            Assert.InRange(rampZ, w.GroundHeightAt(65f, 100f) + 2.4f, w.GroundHeightAt(65f, 100f) + 3.4f);
            float underZ = Fdp.Toolkit.Navigation.RoadHeights.At(roads.Blob, 5, 80f, new Vector2(100f, 100f), q);    // Under Road, beneath the deck
            Assert.Equal(w.GroundHeightAt(100f, 100f), underZ, 2);
        }

        [Fact]
        public void H5c_AUnitUnderTheBridge_SentAlongTheBridgeRoad_JoinsItOnTheDeck_AndTheUnderRoadStaysOnTheGround()
        {
            var w = Overpass();
            using var roads = new RoadsHolder(OverpassRoads());
            var planner = new Fdp.Toolkit.Navigation.RoutePlanner { World = new TerrainWorldQuery(w) };
            float g(float x, float y) => w.GroundHeightAt(x, y);

            // standing on the ground under the deck, nearest to Bridge Road in plan: the road is joined AT THE DECK, not at his level
            var over = new Fdp.Toolkit.Navigation.RouteQuery(new Vector3(92f, 101f, g(92f, 101f)), new Vector3(178f, 100f, g(178f, 100f)),
                Fdp.Toolkit.Navigation.RoadUse.StronglyPrefer, Fdp.Toolkit.Navigation.NavigationBackend.NavRoadGraph);
            Assert.NotNull(planner.Plan(in over, in roads.Blob, navmesh: null, doors: null, out _));
            var deckPoints = planner.Points.Where(p => p.X > 82f && p.X < 118f && MathF.Abs(p.Y - 100f) < 1f).ToList();
            Assert.NotEmpty(deckPoints);
            Assert.All(deckPoints, p => Assert.InRange(p.Z, g(p.X, p.Y) + 5.5f, g(p.X, p.Y) + 6.5f));

            // the road beneath the bridge, south to north: every point on the ground, including under the deck
            var under = new Fdp.Toolkit.Navigation.RouteQuery(new Vector3(100f, 30f, g(100f, 30f)), new Vector3(100f, 170f, g(100f, 170f)),
                Fdp.Toolkit.Navigation.RoadUse.StronglyPrefer, Fdp.Toolkit.Navigation.NavigationBackend.NavRoadGraph);
            Assert.NotNull(planner.Plan(in under, in roads.Blob, navmesh: null, doors: null, out _));
            Assert.All(planner.Points, p => Assert.Equal(g(p.X, p.Y), p.Z, 1));
        }

        /// <summary>⭐ AQ104 I (R-2079) — the planner's half-metre short move is two points only on ONE level: a man on the ground under
        /// the deck sent to the point straight above him gets no two-point "path" (it would arrive at once, a floor short); his own level
        /// still gets the two points.</summary>
        [Fact]
        public void AQ104I_AShortMove_ToTheLevelAbove_IsNotTheTwoPoints()
        {
            var w = Overpass();
            var planner = new Fdp.Toolkit.Navigation.RoutePlanner { World = new TerrainWorldQuery(w) };
            float g = w.GroundHeightAt(100f, 100f);
            var none = default(global::CarKinem.Road.RoadNetworkBlob);
            var up = new Fdp.Toolkit.Navigation.RouteQuery(new Vector3(100f, 100f, g), new Vector3(100.2f, 100f, g + 6f), Fdp.Toolkit.Navigation.RoadUse.Neutral);
            Assert.Null(planner.Plan(in up, in none, navmesh: null, doors: null, out _));   // no navmesh, no roads: no way up
            var same = new Fdp.Toolkit.Navigation.RouteQuery(new Vector3(100f, 100f, g), new Vector3(100.2f, 100f, g), Fdp.Toolkit.Navigation.RoadUse.Neutral);
            Assert.NotNull(planner.Plan(in same, in none, navmesh: null, doors: null, out _));
            Assert.Equal(2, planner.Points.Count);
        }

        [Fact]
        public void H5c_AVehicleBornOnTheBridgeRoad_StandsOnTheDeck_AndOneBesideIt_OnTheGround()
        {
            var w = Overpass();
            using var repo = new Fdp.Core.EntityRepository();
            repo.SetSingletonManaged(w);
            repo.SetSingletonManaged(new global::CarKinem.Road.RoadNetworkHolder(OverpassRoads(), takeOwnership: true));
            var query = Fdp.Toolkit.World.WorldQuery.Of(repo);
            Assert.NotNull(query);

            Assert.Equal(w.GroundHeightAt(100f, 100f) + 6f,
                global::CarKinem.Systems.VehicleCommandSystem.OnRoadHeight(repo, new Vector2(100f, 101f), query)!.Value, 1);
            Assert.Null(global::CarKinem.Systems.VehicleCommandSystem.OnRoadHeight(repo, new Vector2(60f, 140f), query));   // off any road
            repo.GetSingletonManaged<global::CarKinem.Road.RoadNetworkHolder>()!.Dispose();
        }
    
        // ── H5d — the highway-rest test terrain (U-TH5): a highway under an overpass to a drivers' rest area ──

        private static string HighwayRestFolder()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Hrot", "Subsystems"))) dir = dir.Parent;
            return System.IO.Path.Combine(dir!.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "highway-rest");
        }

        internal static TerrainWorld HighwayRest()
        {
            var folder = HighwayRestFolder();
            return TerrainWorldParser.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "highway-rest.world.geojson")),
                "highway-rest", TerrainAssets.ForFolder(folder));
        }

        [Fact]
        public void H5d_HighwayRest_LoadsOnItsGrid_WithTheOverpassOverTheHighway_AndNoWarnings()
        {
            var w = HighwayRest();
            Assert.NotNull(w.Height);                                   // a real grid, not the flat bed (R-248)
            Assert.Empty(w.Warnings);                                   // both ramps within the vehicles' 20°
            Assert.Equal(3, w.Walkables.Count);                         // the deck and two ramps
            float g = w.GroundHeightAt(200f, 150f);
            Assert.Equal(g + 6f, w.SurfaceZ(200f, 150f, g + 6f), 1);    // on the deck over the highway
            Assert.Equal(g, w.SurfaceZ(200f, 150f, g), 1);              // on the highway under it
            var levels = w.SurfacesAt(200f, 150f);
            Assert.True(levels.Length >= 2, "two levels over the highway");
        }

        [Theory]
        [InlineData(Fdp.Toolkit.Navigation.NavLayerMask.Infantry)]
        [InlineData(Fdp.Toolkit.Navigation.NavLayerMask.Vehicle)]
        public void H5d_HighwayRest_TheNavmesh_CrossesTheHighwayByTheBridge_AndRunsAlongTheHighwayUnderIt(Fdp.Toolkit.Navigation.NavLayerMask layer)
        {
            var w = HighwayRest();
            var provider = new RecastNavmeshFactory { TileCache = null }.Build(w)!;
            uint mask = (uint)layer;
            float g(float x, float y) => w.GroundHeightAt(x, y);

            // on the deck, and the highway under it
            Assert.True(provider.ProjectToNavmesh(new Vector3(200f, 150f, g(200f, 150f) + 6.5f), out var onDeck, mask), "no navmesh on the deck");
            Assert.InRange(onDeck.Z, g(200f, 150f) + 5.6f, g(200f, 150f) + 6.4f);
            Assert.True(provider.ProjectToNavmesh(new Vector3(200f, 150f, g(200f, 150f) + 0.2f), out var under, mask), "no navmesh under the deck");
            Assert.InRange(under.Z, g(200f, 150f) - 0.4f, g(200f, 150f) + 0.4f);
            // a non-road, navmesh-navigated entity reaches the rest area from the near side THROUGH the deck (both ramps join the ground)
            Assert.True(provider.PathExists(onDeck, new Vector3(200f, 60f, g(200f, 60f)), mask), "the south ramp does not join the near side");
            Assert.True(provider.PathExists(onDeck, new Vector3(246f, 238f, g(246f, 238f)), mask), "the north ramp does not join the rest area");
            // and along the highway, under the bridge, west to east
            Assert.True(provider.PathExists(new Vector3(120f, 150f, g(120f, 150f)), new Vector3(280f, 150f, g(280f, 150f)), mask), "no passage under the deck");
        }

        [Fact]
        public void H5d_HighwayRest_TheLocalRoadGoesOverTheDeck_AndTheHighwayUnderIt()
        {
            var w = HighwayRest();
            using var roads = new RoadsHolder(global::CarKinem.Road.RoadNetworkLoader.LoadFromJson(
                System.IO.Path.Combine(HighwayRestFolder(), "roads.json")));
            var planner = new Fdp.Toolkit.Navigation.RoutePlanner { World = new TerrainWorldQuery(w) };
            float g(float x, float y) => w.GroundHeightAt(x, y);

            var toRest = new Fdp.Toolkit.Navigation.RouteQuery(new Vector3(200f, 20f, g(200f, 20f)), new Vector3(244f, 240f, g(244f, 240f)),
                Fdp.Toolkit.Navigation.RoadUse.StronglyPrefer, Fdp.Toolkit.Navigation.NavigationBackend.NavRoadGraph);
            Assert.NotNull(planner.Plan(in toRest, in roads.Blob, navmesh: null, doors: null, out _));
            var overHighway = planner.Points.Where(p => p.Y > 140f && p.Y < 160f).ToList();
            Assert.NotEmpty(overHighway);
            Assert.All(overHighway, p => Assert.InRange(p.Z, g(p.X, p.Y) + 5.5f, g(p.X, p.Y) + 6.5f));   // on the deck

            var alongHighway = new Fdp.Toolkit.Navigation.RouteQuery(new Vector3(20f, 150f, g(20f, 150f)), new Vector3(380f, 150f, g(380f, 150f)),
                Fdp.Toolkit.Navigation.RoadUse.StronglyPrefer, Fdp.Toolkit.Navigation.NavigationBackend.NavRoadGraph);
            Assert.NotNull(planner.Plan(in alongHighway, in roads.Blob, navmesh: null, doors: null, out _));
            Assert.All(planner.Points, p => Assert.Equal(g(p.X, p.Y), p.Z, 1));                          // under it, on the ground
        }

        // ── H4 — basic-desert's ridge and wadi on a real grid (CE-3086, TH-G; §5d) ──

        private static string RepoRoot()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Hrot", "Subsystems"))) dir = dir.Parent;
            return dir!.FullName;
        }

        internal static TerrainWorld BasicDesert()
        {
            var folder = System.IO.Path.Combine(RepoRoot(), "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "basic-desert");
            return TerrainWorldParser.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "basic-desert.world.geojson")),
                "basic-desert", TerrainAssets.ForFolder(folder));
        }

        /// <summary>
        /// ⭐ H4 — the relief is where the design puts it, within the vehicles' 20°, and it moves NOTHING that already stood on
        /// basic-desert: the ground is 0 under every rock, the compound, and every grounded entity of every scenario on this terrain.
        /// </summary>
        [Fact]
        public void H4_BasicDesert_HasItsRidgeAndWadi_AndThePlainUnderEverythingThatStoodOnItIsUnchanged()
        {
            var w = BasicDesert();
            Assert.NotNull(w.Height);
            Assert.Empty(w.Warnings);
            Assert.Equal(10f, w.GroundHeightAt(275f, 210f), 1);      // the crest
            Assert.Equal(-3f, w.GroundHeightAt(520f, 400f), 1);      // the wadi's bed
            Assert.Equal(0f, w.GroundHeightAt(275f, 295f), 2);       // the track, north of the ridge's foot

            // steepest ground anywhere: a 1 m probe in four directions, every 5 m
            float steepest = 0f;
            for (float x = 0; x < 600; x += 5)
            for (float y = 0; y < 600; y += 5)
            {
                float z = w.GroundHeightAt(x, y);
                steepest = MathF.Max(steepest, MathF.Max(MathF.Abs(w.GroundHeightAt(x + 1, y) - z), MathF.Abs(w.GroundHeightAt(x, y + 1) - z)));
            }
            Assert.True(MathF.Atan(steepest) * 180f / MathF.PI < 20f, $"steepest ground {MathF.Atan(steepest) * 180f / MathF.PI:F1}° — vehicles climb 20°");

            // under every feature (each vertex and its box, every 2 m)
            Assert.NotEmpty(w.Prisms);
            foreach (var b in w.Prisms.Select(pr => pr.Footprint))   // the rocks, the hut and the compound wall
            {
                float x0 = b.Min(p => p.X), x1 = b.Max(p => p.X), y0 = b.Min(p => p.Y), y1 = b.Max(p => p.Y);
                for (float x = x0; x <= x1; x += 2f)
                for (float y = y0; y <= y1; y += 2f)
                    Assert.Equal(0f, w.GroundHeightAt(x, y), 3);
            }

            // under every grounded entity of every scenario that stood on basic-desert when H4 raised its relief (§5d: "nothing that
            //   stood on basic-desert moves"). ⭐ A scenario placed AFTER H4 may stand on the ridge or in the wadi: a Z-0 entity on relief
            //   needs no special case, the motion model settles it (R-249, §4a) — 📐 ua-air-attack's column lead on the wadi bank.
            int checkedEntities = 0;
            foreach (var name in new[] { "ua-air-strike", "ua-fire-distribution", "ua-weapon-choice" })
            {
                var file = System.IO.Path.Combine(RepoRoot(), "scenarios", name, "scenario.json");
                using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(file));
                if (!doc.RootElement.TryGetProperty("header", out var h) || !h.TryGetProperty("terrainName", out var t) || t.GetString() != "basic-desert") continue;
                foreach (var e in doc.RootElement.GetProperty("entities").EnumerateObject())
                {
                    if (!e.Value.TryGetProperty("SimTransform", out var st)) continue;
                    var p = st.GetProperty("Position");
                    if (p[2].GetSingle() != 0f) continue;                // airborne
                    Assert.True(MathF.Abs(w.GroundHeightAt(p[0].GetSingle(), p[1].GetSingle())) < 0.01f, $"{file}: {e.Name} would stand off the ground");
                    checkedEntities++;
                }
            }
            Assert.True(checkedEntities >= 10, $"only {checkedEntities} grounded entities found — the scenarios moved?");   // the 3 have them
        }

        /// <summary>
        /// ⭐ H4 — the ridge does the job a ridge is for (two men either side cannot see each other, a round between them meets the
        /// ground), yet adds no block to the weapon-choice demo's lines (U5: the Bradley to the insurgent and to the T-72).
        /// </summary>
        [Fact]
        public void H4_BasicDesert_TheRidgeHidesOneSideFromTheOther_AndLeavesTheWeaponChoiceLinesOpen()
        {
            var w = BasicDesert();
            float G(float x, float y) => w.GroundHeightAt(x, y);
            var north = new Vector3(275f, 270f, G(275f, 270f) + 1.8f);
            var south = new Vector3(275f, 150f, G(275f, 150f) + 1.8f);
            Assert.True(w.Height!.Crosses(north, south), "the ridge must hide one side from the other");
            Assert.False(w.Height.Crosses(new Vector3(275f, 160f, 12f), new Vector3(275f, 260f, 12f)), "a line over the crest is clear");

            var bradley = new Vector3(100f, 520f, 2.5f);
            Assert.False(w.Height.Crosses(bradley, new Vector3(100f, 220f, 1.6f)), "U5: the Bradley to the insurgent");
            Assert.False(w.Height.Crosses(bradley, new Vector3(480f, 160f, 2.2f)), "U5: the Bradley to the T-72");
        }

        /// <summary>
        /// ⭐ H4 — the navmesh bakes over the ridge and through the wadi on BOTH layers (CE-1048 measured an empty vehicle layer on
        /// slopes once), and the track drapes into the wadi like a car drives it (TH-J).
        /// </summary>
        [Theory]
        [InlineData(Fdp.Toolkit.Navigation.NavLayerMask.Infantry)]
        [InlineData(Fdp.Toolkit.Navigation.NavLayerMask.Vehicle)]
        public void H4_BasicDesert_TheNavmesh_GoesOverTheRidge_AndThroughTheWadi(Fdp.Toolkit.Navigation.NavLayerMask layer)
        {
            var w = BasicDesert();
            var provider = new RecastNavmeshFactory { TileCache = null }.Build(w)!;
            uint mask = (uint)layer;
            float g(float x, float y) => w.GroundHeightAt(x, y);

            Assert.True(provider.ProjectToNavmesh(new Vector3(275f, 210f, 10.5f), out var crest, mask), "no navmesh on the crest");
            Assert.InRange(crest.Z, 9.4f, 10.6f);
            Assert.True(provider.PathExists(new Vector3(275f, 280f, g(275f, 280f)), crest, mask), "no way up the north face");
            Assert.True(provider.PathExists(crest, new Vector3(275f, 140f, g(275f, 140f)), mask), "no way down the south face");
            Assert.True(provider.ProjectToNavmesh(new Vector3(520f, 400f, -2.5f), out var bed, mask), "no navmesh in the wadi");
            Assert.InRange(bed.Z, -3.6f, -2.4f);
            Assert.True(provider.PathExists(new Vector3(480f, 400f, 0f), new Vector3(560f, 400f, 0f), mask), "no way across the wadi");
        }

        [Fact]
        public void H4_BasicDesert_TheTrack_DipsIntoTheWadi_OnTheGround()
        {
            var w = BasicDesert();
            using var roads = new RoadsHolder(global::CarKinem.Road.RoadNetworkLoader.LoadFromJson(System.IO.Path.Combine(
                RepoRoot(), "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "basic-desert", "roads.json")));
            var planner = new Fdp.Toolkit.Navigation.RoutePlanner { World = new TerrainWorldQuery(w) };
            var q = new Fdp.Toolkit.Navigation.RouteQuery(new Vector3(20f, 295f, 0f), new Vector3(580f, 295f, 0f),
                Fdp.Toolkit.Navigation.RoadUse.StronglyPrefer, Fdp.Toolkit.Navigation.NavigationBackend.NavRoadGraph);
            Assert.NotNull(planner.Plan(in q, in roads.Blob, navmesh: null, doors: null, out _));
            Assert.All(planner.Points, p => Assert.Equal(w.GroundHeightAt(p.X, p.Y), p.Z, 1));
            Assert.Contains(planner.Points, p => p.X > 510f && p.X < 530f && p.Z < -2f);
        }
    }
}
