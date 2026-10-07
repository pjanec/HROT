using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// The terrain WORLD model (docs/DESIGN_Terrain_World.md §2–§4): the GeoJSON parse, the surface Z an entity
    /// stands on (ground / roof / floor, chosen by its current Z), sight lines through prisms and slabs, and the
    /// one catalog that resolves a terrain name.
    /// </summary>
    public sealed class TerrainWorldTests
    {
        // A 12 m building at (40..90, 40..90); a 0.5 m wall along y=20 from x=0..30; a garage deck at z=3 over
        // (100..140, 0..40) with a ramp from (90..100, 0..10) rising 0→3; a forest and a pond.
        private const string World = """
        {
          "type": "FeatureCollection",
          "hrot": { "schemaVersion": 1, "bounds": [0, 0, 200, 200], "groundZ": 0 },
          "features": [
            { "type": "Feature", "properties": { "kind": "building", "height": 12, "floors": 3, "label": "A" },
              "geometry": { "type": "Polygon", "coordinates": [[[40,40],[90,40],[90,90],[40,90],[40,40]]] } },
            { "type": "Feature", "properties": { "kind": "wall", "height": 0.5, "thickness": 0.3 },
              "geometry": { "type": "LineString", "coordinates": [[0,20],[30,20]] } },
            { "type": "Feature", "properties": { "kind": "slab", "label": "Deck" },
              "geometry": { "type": "Polygon", "coordinates": [[[100,0,3],[140,0,3],[140,40,3],[100,40,3],[100,0,3]]] } },
            { "type": "Feature", "properties": { "kind": "ramp" },
              "geometry": { "type": "Polygon", "coordinates": [[[90,0,0],[100,0,3],[100,10,3],[90,10,0],[90,0,0]]] } },
            { "type": "Feature", "properties": { "kind": "surface", "surface": "forest" },
              "geometry": { "type": "Polygon", "coordinates": [[[150,150],[190,150],[190,190],[150,190],[150,150]]] } },
            { "type": "Feature", "properties": { "kind": "surface", "surface": "water" },
              "geometry": { "type": "Polygon", "coordinates": [[[150,100],[180,100],[180,130],[150,130],[150,100]]] } }
          ]
        }
        """;

        private static TerrainWorld Parse() => TerrainWorldParser.Parse(World);

        [Fact]
        public void Parse_ReadsEveryKind()
        {
            var w = Parse();
            Assert.Equal(2, w.Prisms.Count);                 // building + one wall segment
            Assert.Equal(2, w.Walkables.Count);              // slab + ramp
            Assert.Equal(2, w.Surfaces.Count);
            Assert.Equal(new Vector2(0, 0), w.BoundsMin);
            Assert.Equal(new Vector2(200, 200), w.BoundsMax);
            var building = w.Prisms[0];
            Assert.Equal(12f, building.TopZ);
            Assert.Equal(3, building.Floors);
            Assert.Equal(6, building.Triangles.Length);      // a quad → two triangles
        }

        // ── the navmesh input soup (docs/DESIGN_Terrain_World.md §4.1) ───────────────────────────────────

        private static (Vector3[] V, int[] I) Mesh()
        {
            TerrainWorldMesh.Build(Parse(), out var v, out var i, cellSize: 2f);
            return (v, i);
        }

        private static Vector3 Normal(Vector3[] v, int[] idx, int t)
            => Vector3.Cross(v[idx[t + 1]] - v[idx[t]], v[idx[t + 2]] - v[idx[t]]);

        [Fact]
        public void Mesh_EveryHorizontalTriangleFacesUp_AndWallsAreVertical()
        {
            var (v, idx) = Mesh();
            Assert.True(idx.Length > 0 && idx.Length % 3 == 0);
            for (int t = 0; t < idx.Length; t += 3)
            {
                var n = Vector3.Normalize(Normal(v, idx, t));
                Assert.True(n.Z >= -1e-4f, $"triangle {t / 3} faces down: {n}");
            }
        }

        [Fact]
        public void Mesh_GroundSkipsBuildingsAndWater_ButKeepsTheGarageGroundFloor()
        {
            var (v, idx) = Mesh();
            bool GroundAt(float x, float y)
            {
                for (int t = 0; t < idx.Length; t += 3)
                {
                    var a = v[idx[t]]; var b = v[idx[t + 1]]; var c = v[idx[t + 2]];
                    if (a.Z != 0f || b.Z != 0f || c.Z != 0f) continue;
                    if (PolygonMath.HeightOnTriangle(x, y, a, b, c) is not null) return true;
                }
                return false;
            }
            Assert.True(GroundAt(10, 100));     // open ground
            Assert.False(GroundAt(65, 65));     // inside the 12 m building
            Assert.False(GroundAt(165, 115));   // the pond
            Assert.True(GroundAt(120, 20));     // under the deck: the garage's ground floor
        }

        [Fact]
        public void Mesh_CarriesTheRoof_TheDeck_AndTheRamp()
        {
            var (v, idx) = Mesh();
            float? TopAt(float x, float y, float near)
            {
                for (int t = 0; t < idx.Length; t += 3)
                {
                    var z = PolygonMath.HeightOnTriangle(x, y, v[idx[t]], v[idx[t + 1]], v[idx[t + 2]]);
                    if (z is float h && MathF.Abs(h - near) < 0.01f) return h;
                }
                return null;
            }
            Assert.NotNull(TopAt(65, 65, 12f));   // building roof
            Assert.NotNull(TopAt(120, 20, 3f));   // garage deck
            Assert.NotNull(TopAt(95, 5, 1.5f));   // halfway up the ramp
        }

        /// <summary>
        /// W9 / CE-3018 — the grids are FITTED to the world. ⛔ The expectation changed deliberately: this rail used to
        /// assert that a world leaving the fixed grids is reported per grid (slice 1, check only). Now both grids rebase,
        /// so a big world is covered and a world that already fits leaves the placement exactly at the default.
        /// </summary>
        [Fact]
        public void GridCoverage_FitsBothGridsToTheWorld_W9()
        {
            var small = Parse();                                                   // [0,200]² fits both
            Assert.Empty(TerrainGridCoverage.Problems(small));
            Assert.Equal(TerrainGridCoverage.PerceptionGrid(null), TerrainGridCoverage.PerceptionGrid(small));
            Assert.Equal(TerrainGridCoverage.ColliderGrid(null), TerrainGridCoverage.ColliderGrid(small));

            var big = TerrainWorldParser.Parse(
                """{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[-100,0,1200,500]},"features":[]}""");
            Assert.Empty(TerrainGridCoverage.Problems(big));                       // was 2 before CE-3018
            var p = TerrainGridCoverage.PerceptionGrid(big);
            Assert.True(p.OriginX <= -100f && p.OriginX + 200 * p.CellSize >= 1200f);
            Assert.Equal(0f, p.OriginY);                                           // the side it does not leave stays put
            Assert.Contains("perception grid origin", TerrainGridCoverage.Describe(big));

            // ⚠ An EMPTY world (a terrain with no world file) is "no terrain": the default placement, not a 0×0 fit.
            Assert.Equal(TerrainGridCoverage.PerceptionGrid(null), TerrainGridCoverage.PerceptionGrid(new TerrainWorld()));
        }

        [Fact]
        public void SurfaceZ_OpenGround_IsGround()
            => Assert.Equal(0f, Parse().SurfaceZ(10, 100, 0f));

        [Fact]
        public void SurfaceZ_InsideASolidFootprint_IsTheRoof()
            => Assert.Equal(12f, Parse().SurfaceZ(60, 60, 0f));      // spawned at Z=0 inside → lands on the roof

        [Fact]
        public void SurfaceZ_UnderTheDeck_StaysOnTheGround_OnTheDeck_StaysOnTheDeck()
        {
            var w = Parse();
            Assert.Equal(0f, w.SurfaceZ(120, 20, 0f));        // walking under the deck
            Assert.Equal(3f, w.SurfaceZ(120, 20, 3f));        // driving on the deck
        }

        [Fact]
        public void SurfaceZ_OnTheRamp_Interpolates()
        {
            float z = Parse().SurfaceZ(95, 5, 1.4f);
            Assert.InRange(z, 1.45f, 1.55f);
        }

        [Fact]
        public void SegmentBlocked_ThroughATallBuilding_AtEyeHeight_IsBlocked()
            => Assert.True(Parse().SegmentBlocked(new Vector3(20, 65, 1.7f), new Vector3(120, 65, 1.7f)));

        [Fact]
        public void SegmentBlocked_OverTheBuilding_IsClear()
            => Assert.False(Parse().SegmentBlocked(new Vector3(20, 65, 15f), new Vector3(120, 65, 15f)));

        [Fact]
        public void SegmentBlocked_LowWall_StandingSeesOver_ProneDoesNot()
        {
            var w = Parse();
            Assert.False(w.SegmentBlocked(new Vector3(15, 10, 1.7f), new Vector3(15, 30, 1.7f)));
            Assert.True(w.SegmentBlocked(new Vector3(15, 10, 0.3f), new Vector3(15, 30, 0.3f)));
        }

        [Fact]
        public void SegmentBlocked_FromUnderTheDeckToAboveIt_IsBlockedByTheSlab()
            => Assert.True(Parse().SegmentBlocked(new Vector3(120, 20, 1.7f), new Vector3(125, 25, 6f)));

        [Fact]
        public void SegmentBlocked_StandingOnTheDeck_SeesAcrossIt()
            => Assert.False(Parse().SegmentBlocked(new Vector3(105, 5, 4.7f), new Vector3(135, 35, 4.7f)));

        [Fact]
        public void SurfaceTypeAt_ReadsTheArea()
        {
            var w = Parse();
            Assert.Equal(TerrainSurfaceType.Forest, w.SurfaceTypeAt(170, 170));
            Assert.Equal(TerrainSurfaceType.Water, w.SurfaceTypeAt(160, 110));
            Assert.Equal(TerrainSurfaceType.Open, w.SurfaceTypeAt(10, 100));
        }

        [Theory]
        [InlineData("""{"type":"FeatureCollection","features":[{"type":"Feature","properties":{"kind":"tree"},"geometry":{"type":"Point","coordinates":[0,0]}}]}""", "unknown kind")]
        [InlineData("""{"type":"FeatureCollection","features":[{"type":"Feature","properties":{"kind":"building"},"geometry":{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}}]}""", "'height' is required")]
        [InlineData("""{"type":"FeatureCollection","features":[{"type":"Feature","properties":{"kind":"building","height":3},"geometry":{"type":"Polygon","coordinates":[[[0,0],[9,0],[9,9],[0,0]],[[1,1],[2,1],[2,2],[1,1]]]}}]}""", "holes")]
        [InlineData("""{"type":"Nope"}""", "FeatureCollection")]
        public void Parse_FailsLoudly_OnWhatItDoesNotUnderstand(string json, string expected)
        {
            var ex = Assert.Throws<ArgumentException>(() => TerrainWorldParser.Parse(json));
            Assert.Contains(expected, ex.Message);
        }

        [Fact]
        public void Definition_V2_CarriesTheWorldFile()
        {
            var def = TerrainDefinitionParser.Parse("""{"schemaVersion":2,"name":"t","world":"t.world.geojson"}""");
            Assert.Equal("t.world.geojson", def.World);
            Assert.False(def.IsEmpty);
        }

        [Fact]
        public void Catalog_ResolvesFolders_InRootOrder_AndLists()
        {
            var a = Path.Combine(Path.GetTempPath(), "tcat_a_" + Guid.NewGuid().ToString("N")[..8]);
            var b = Path.Combine(Path.GetTempPath(), "tcat_b_" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                Directory.CreateDirectory(Path.Combine(a, "town"));
                Directory.CreateDirectory(Path.Combine(b, "town"));
                Directory.CreateDirectory(Path.Combine(b, "eu", "desert"));
                File.WriteAllText(Path.Combine(a, "town", TerrainCatalog.DefinitionFileName), "{}");
                File.WriteAllText(Path.Combine(b, "town", TerrainCatalog.DefinitionFileName), "{}");
                File.WriteAllText(Path.Combine(b, "eu", "desert", TerrainCatalog.DefinitionFileName), "{}");

                var cat = new TerrainCatalog(new[] { a, b });
                Assert.StartsWith(a, cat.ResolveDefinition("town"));            // first root wins
                Assert.StartsWith(b, cat.ResolveDefinition("eu/desert"));       // subfolder names work
                Assert.Null(cat.ResolveDefinition("nowhere"));
                Assert.Equal(new[] { "eu/desert", "town" }, cat.List());
                Assert.Throws<ArgumentException>(() => cat.ResolveDefinition("../escape"));
            }
            finally
            {
                if (Directory.Exists(a)) Directory.Delete(a, true);
                if (Directory.Exists(b)) Directory.Delete(b, true);
            }
        }

        // ── ⭐ CE-1017 S2 — terrain LEVELS (docs/DESIGN_Add_Entity_Picker.md §2b–§2c) ─────────────────────

        [Fact]
        public void CE1017_SurfacesAt_GroundIsLevel0_EvenInsideABuilding_RoofAndDeckAbove_RampFootMerges()
        {
            var w = Parse();
            Assert.Equal(new[] { 0f }, w.SurfacesAt(10, 10, out int g0));            // open ground
            Assert.Equal(0, g0);
            Assert.Equal(new[] { 0f, 12f }, w.SurfacesAt(60, 60, out int g1));       // 🔒 rev 5: ground stays level 0 inside
            Assert.Equal(0, g1);
            Assert.Equal(new[] { 0f, 3f }, w.SurfacesAt(120, 20));                   // under/on the garage deck
            Assert.Equal(new[] { 0f }, w.SurfacesAt(90.5f, 5));                      // ramp foot (z 0.15) merges into the ground

            Assert.Equal(0f,  w.ResolveLevel(60, 60, 0));
            Assert.Equal(12f, w.ResolveLevel(60, 60, 1));
            Assert.Equal(12f, w.ResolveLevel(60, 60, 99));                           // past the top ⇒ the highest
            Assert.Equal(0f,  w.ResolveLevel(60, 60, -3));                           // past the bottom ⇒ the lowest
        }

        [Fact]
        public void CE1017_SurfacesAt_BasementIsMinus1_CloseSurfacesMergeToTheHigher()
        {
            // three slabs over one square: a basement at -3, a deck at 3 and a second skin at 3.2 (within 0.3 m ⇒ one level)
            var w = TerrainWorldParser.Parse("""
            {
              "type": "FeatureCollection",
              "hrot": { "schemaVersion": 1, "bounds": [0, 0, 20, 20], "groundZ": 0 },
              "features": [
                { "type": "Feature", "properties": { "kind": "slab" },
                  "geometry": { "type": "Polygon", "coordinates": [[[0,0,-3],[10,0,-3],[10,10,-3],[0,10,-3],[0,0,-3]]] } },
                { "type": "Feature", "properties": { "kind": "slab" },
                  "geometry": { "type": "Polygon", "coordinates": [[[0,0,3],[10,0,3],[10,10,3],[0,10,3],[0,0,3]]] } },
                { "type": "Feature", "properties": { "kind": "slab" },
                  "geometry": { "type": "Polygon", "coordinates": [[[0,0,3.2],[10,0,3.2],[10,10,3.2],[0,10,3.2],[0,0,3.2]]] } }
              ]
            }
            """);
            var levels = w.SurfacesAt(5, 5, out int ground);
            Assert.Equal(1, ground);
            Assert.Equal(3, levels.Length);
            Assert.Equal(-3f, levels[0]);
            Assert.Equal(0f, levels[1]);
            Assert.Equal(3.2f, levels[2], 3);
            Assert.Equal(-3f, w.ResolveLevel(5, 5, -1));
            Assert.Equal(-3f, w.ResolveLevel(5, 5, -7));
            Assert.Equal(3.2f, w.ResolveLevel(5, 5, 1), 3);
        }

        // ── ⭐ Buildings programme Stage 1 — templates, panels, openings, materials (docs/DESIGN_Building_Interiors.md §3a, §3c) ──

        // A 10 x 8 m two-storey template: front wall with a door (closed) and a window, an inner wall with a door, stairs.
        private const string House = """
        { "name": "house", "material": "brick", "footprint": [[0,0],[10,0],[10,8],[0,8]],
          "storeys": [
            { "height": 3.0,
              "walls": [
                { "from": [0,0], "to": [10,0], "thickness": 0.3,
                  "openings": [ { "kind": "door", "at": 4.5, "width": 1.0, "doorId": "front", "initial": "closed" },
                                { "kind": "window", "at": 1.5, "width": 1.2 } ] },
                { "from": [10,0], "to": [10,8] }, { "from": [10,8], "to": [0,8] }, { "from": [0,8], "to": [0,0] },
                { "from": [5,0], "to": [5,8], "thickness": 0.15, "material": "concrete",
                  "openings": [ { "kind": "door", "at": 6.0, "width": 0.9, "doorId": "hall" } ] } ],
              "stairs": [ { "from": [8,1], "to": [8,6], "width": 1.0 } ] },
            { "height": 3.0, "walls": [ { "from": [0,0], "to": [10,0] } ] } ] }
        """;

        private static TerrainWorld HouseWorld(string instanceProps = "\"template\": \"house\", \"label\": \"H\", \"doors\": { \"front\": \"locked\" }", float x = 100, float y = 100)
            => TerrainWorldParser.Parse($$"""
            { "type": "FeatureCollection", "hrot": { "schemaVersion": 1, "bounds": [0, 0, 200, 200], "groundZ": 0 },
              "features": [ { "type": "Feature", "properties": { "kind": "building", {{instanceProps}} },
                              "geometry": { "type": "Point", "coordinates": [{{x}}, {{y}}] } } ] }
            """, "range", new TerrainAssets { Templates = n => n == "house" ? House : null });

        [Fact]
        public void Stage1_Template_ExpandsIntoPanelsWithOpenings_DoorKeys_AndInstanceOverrides()
        {
            var w = HouseWorld();
            var b = Assert.Single(w.Buildings);
            Assert.Equal(new[] { 0f, 3f, 6f }, b.StoreyZ);
            Assert.Equal(6, w.Panels.Count);                              // 5 ground-storey walls + 1 upper
            var front = w.Panels[0];
            Assert.Equal("brick", front.Material.Name);
            Assert.Equal(new[] { 1.5f, 4.5f }, front.Openings.Select(o => o.At));   // sorted along the wall
            var window = front.Openings[0];
            Assert.Equal((0.9f, 2.1f), (window.SillZ, window.HeadZ));
            Assert.Equal("concrete", w.Panels[4].Material.Name);          // per-wall material overrides the template's

            Assert.Equal(new[] { "range/H/front", "range/H/hall" }, w.Doors.Select(d => d.Key).OrderBy(k => k));
            Assert.Equal(TerrainDoorState.Locked, w.Doors.Single(d => d.Key == "range/H/front").Initial);   // instance override
            Assert.Equal(TerrainDoorState.Open, w.Doors.Single(d => d.Key == "range/H/hall").Initial);      // default
            Assert.All(w.Prisms, p => Assert.True(p.Panel >= 0));         // every prism is a panel piece
        }

        [Fact]
        public void Stage1_InsideAnEnterableBuilding_TheGroundIsAFloor_StoreysAreLevels_StairsClimb()
        {
            var w = HouseWorld();
            // inside the west room: ground, the upper floor, the roof (CE-1031 — the ground is no longer skipped)
            Assert.Equal(new[] { 0f, 3f, 6f }, w.SurfacesAt(102, 104));
            Assert.Equal(0f, w.SurfaceZ(102, 104, zHint: 0f));
            Assert.Equal(3f, w.SurfaceZ(102, 104, zHint: 3f));
            // on the stairs, halfway up the 5 m run (8,1)→(8,6) from 0 to 3
            Assert.Equal(1.5f, w.SurfaceZ(108, 103.5f, zHint: 1.2f), 2);
        }

        [Fact]
        public void Stage1_SightPassesAWindowAndADoorway_ButNotTheWallBesideThem()
        {
            var w = HouseWorld();
            // the front wall runs y = 100 (x 100..110); look from outside (y = 90) to inside (y = 104) at eye height 1.6
            Assert.False(w.SegmentBlocked(new Vector3(102.1f, 90, 1.6f), new Vector3(102.1f, 104, 1.6f)));   // window 101.5..102.7
            Assert.True(w.SegmentBlocked(new Vector3(102.1f, 90, 0.5f), new Vector3(102.1f, 104, 0.5f)));    // under the sill
            // the doorway 104.5..105.5 — ⭐ Stage 5: a gap only while its door is OPEN (this instance's front is locked)
            Assert.True(w.SegmentBlocked(new Vector3(104.8f, 90, 1.6f), new Vector3(104.8f, 104, 1.6f)));
            w.SetDoorState(w.DoorIndexOf(w.Doors[0].Key), TerrainDoorState.Open);
            Assert.False(w.SegmentBlocked(new Vector3(104.8f, 90, 1.6f), new Vector3(104.8f, 104, 1.6f)));
            Assert.True(w.SegmentBlocked(new Vector3(103.5f, 90, 1.6f), new Vector3(103.5f, 104, 1.6f)));    // solid wall
        }

        [Fact]
        public void Stage1_RotationTurnsTheTemplateCounterClockwise()
        {
            var w = HouseWorld("\"template\": \"house\", \"label\": \"R\", \"rotation\": 90");
            var front = w.Panels[0];                                      // local (0,0)→(10,0) turned 90° ⇒ (100,100)→(100,110)
            Assert.Equal(100f, front.A.X, 3); Assert.Equal(100f, front.A.Y, 3);
            Assert.Equal(100f, front.B.X, 3); Assert.Equal(110f, front.B.Y, 3);
        }

        [Theory]
        [InlineData("\"template\": \"nope\"", "building template 'nope' was not found")]
        [InlineData("\"building\": { \"material\": \"cheese\", \"storeys\": [ { \"height\": 3, \"walls\": [ { \"from\": [0,0], \"to\": [5,0] } ] } ] }", "unknown material 'cheese'")]
        [InlineData("\"building\": { \"storeys\": [ { \"height\": 3, \"walls\": [ { \"from\": [0,0], \"to\": [5,0], \"openings\": [ { \"kind\": \"door\", \"at\": 4.5, \"width\": 1 } ] } ] } ] }", "does not fit")]
        [InlineData("\"building\": { \"storeys\": [ { \"height\": 3, \"walls\": [ { \"from\": [0,0], \"to\": [5,0], \"openings\": [ { \"kind\": \"door\", \"at\": 1, \"width\": 1 }, { \"kind\": \"window\", \"at\": 1.5, \"width\": 1 } ] } ] } ] }", "overlap")]
        public void Stage1_BadBuildings_FailLoudly(string props, string expected)
        {
            var ex = Assert.Throws<ArgumentException>(() => HouseWorld(props));
            Assert.Contains(expected, ex.Message);
        }

        [Fact]
        public void Stage1_FenceIsAWallOfFenceWood_AndAMaterialIsNamedOnAnyWall()
        {
            var w = TerrainWorldParser.Parse("""
            { "type": "FeatureCollection", "features": [
              { "type": "Feature", "properties": { "kind": "fence", "height": 1.2 }, "geometry": { "type": "LineString", "coordinates": [[0,0],[10,0],[20,0]] } },
              { "type": "Feature", "properties": { "kind": "wall", "height": 2, "material": "fence-chainlink" }, "geometry": { "type": "LineString", "coordinates": [[0,5],[10,5]] } } ] }
            """);
            Assert.Equal(3, w.Panels.Count);
            Assert.Equal(new[] { "fence-wood", "fence-wood", "fence-chainlink" }, w.Panels.Select(p => p.Material.Name));
            Assert.Equal(0.85f, w.Panels[2].Material.SightTransmittance);
        }

        [Fact]
        public void Stage1_QuerySight_MultipliesMaterialTransmittance_AndNamesEachCrossing()
        {
            var w = TerrainWorldParser.Parse("""
            { "type": "FeatureCollection", "features": [
              { "type": "Feature", "properties": { "kind": "fence", "height": 2, "material": "fence-chainlink", "label": "Mesh 1" }, "geometry": { "type": "LineString", "coordinates": [[0,10],[20,10]] } },
              { "type": "Feature", "properties": { "kind": "fence", "height": 2, "material": "fence-chainlink", "label": "Mesh 2" }, "geometry": { "type": "LineString", "coordinates": [[0,20],[20,20]] } },
              { "type": "Feature", "properties": { "kind": "wall", "height": 2, "material": "hedge", "thickness": 0.8, "label": "Hedge" }, "geometry": { "type": "LineString", "coordinates": [[30,10],[50,10]] } } ] }
            """);
            var two = w.QuerySight(new Vector3(10, 0, 1.5f), new Vector3(10, 30, 1.5f));
            Assert.Equal(0.85f * 0.85f, two.Transmittance, 4);
            Assert.Equal(new[] { "Mesh 1", "Mesh 2" }, two.Crossed.Select(c => c.Label));
            Assert.True(two.Transmittance >= TerrainWorld.SightThreshold);

            var hedge = w.QuerySight(new Vector3(40, 0, 1.5f), new Vector3(40, 30, 1.5f));
            Assert.Equal(0.3f, hedge.Transmittance, 4);
            Assert.Equal("hedge", Assert.Single(hedge.Crossed).Material);

            var over = w.QuerySight(new Vector3(10, 0, 2.5f), new Vector3(10, 30, 2.5f));   // above the 2 m fences
            Assert.Empty(over.Crossed);
            Assert.Equal(1f, over.Transmittance);
        }

        [Fact]
        public void Stage1_QuerySight_ThroughAHouseWindow_IsClear_ThroughItsFloor_IsOpaque()
        {
            var w = HouseWorld();
            Assert.Equal(1f, w.QuerySight(new Vector3(102.1f, 90, 1.6f), new Vector3(102.1f, 104, 1.6f)).Transmittance);
            var down = w.QuerySight(new Vector3(102, 104, 4.5f), new Vector3(102, 104, 1.0f));   // from storey 2 down through its floor
            Assert.Equal(0f, down.Transmittance);
            Assert.Equal("slab", Assert.Single(down.Crossed).Kind);
            Assert.Equal(1.5f, down.Crossed[0].Along, 3);
        }

        [Fact]
        public void Stage3_SegmentBlocked_IsSightTransmittanceBelowHalf_ChainLinkSeesThrough_FiveDoNot_AHedgeDoesNot()
        {
            var w = TerrainWorldParser.Parse("""
            { "type": "FeatureCollection", "features": [
              { "type": "Feature", "properties": { "kind": "fence", "height": 2, "material": "fence-chainlink" }, "geometry": { "type": "LineString", "coordinates": [[0,10],[20,10]] } },
              { "type": "Feature", "properties": { "kind": "fence", "height": 2, "material": "fence-chainlink" }, "geometry": { "type": "LineString", "coordinates": [[0,20],[20,20]] } },
              { "type": "Feature", "properties": { "kind": "fence", "height": 2, "material": "fence-chainlink" }, "geometry": { "type": "LineString", "coordinates": [[0,30],[20,30]] } },
              { "type": "Feature", "properties": { "kind": "fence", "height": 2, "material": "fence-chainlink" }, "geometry": { "type": "LineString", "coordinates": [[0,40],[20,40]] } },
              { "type": "Feature", "properties": { "kind": "fence", "height": 2, "material": "fence-chainlink" }, "geometry": { "type": "LineString", "coordinates": [[0,50],[20,50]] } },
              { "type": "Feature", "properties": { "kind": "wall", "height": 2, "material": "hedge", "thickness": 0.8 }, "geometry": { "type": "LineString", "coordinates": [[30,10],[50,10]] } } ] }
            """);
            var eye = new Vector3(10, 0, 1.6f);
            Assert.False(w.SegmentBlocked(eye, new Vector3(10, 15, 1.6f)));        // one chain-link: 0.85
            Assert.False(w.SegmentBlocked(eye, new Vector3(10, 35, 1.6f)));        // three: 0.61
            Assert.False(w.SegmentBlocked(eye, new Vector3(10, 45, 1.6f)));        // four: 0.85⁴ = 0.522 ≥ 0.5
            Assert.True(w.SegmentBlocked(eye, new Vector3(10, 55, 1.6f)));         // five: 0.44
            Assert.True(w.SegmentBlocked(new Vector3(40, 0, 1.6f), new Vector3(40, 15, 1.6f)));   // hedge 0.3
            // ⭐ the route and perception agree, by construction
            foreach (var to in new[] { new Vector3(10, 15, 1.6f), new Vector3(10, 35, 1.6f), new Vector3(10, 45, 1.6f), new Vector3(10, 55, 1.6f) })
                Assert.Equal(w.QuerySight(eye, to).Transmittance < TerrainWorld.SightThreshold, w.SegmentBlocked(eye, to));
        }

        // ── ⭐ Stage 3, FIRE half (§3d P2, R-217) ───────────────────────────────────────────────────────────────

        [Fact]
        public void Stage3Fire_QueryFire_ResistanceIsMaterialTimesThePathInside_ObliqueResistsMore_OverTheTopNothing_ASlabCounts()
        {
            var w = TerrainWorldParser.Parse("""
            { "type": "FeatureCollection", "features": [
              { "type": "Feature", "properties": { "kind": "wall", "height": 2, "thickness": 0.2, "material": "concrete" }, "geometry": { "type": "LineString", "coordinates": [[0,10],[40,10]] } },
              { "type": "Feature", "properties": { "kind": "slab" }, "geometry": { "type": "Polygon", "coordinates": [[[0,20,3],[10,20,3],[10,30,3],[0,30,3],[0,20,3]]] } } ] }
            """);
            var straight = Assert.Single(w.QueryFire(new Vector3(5, 0, 1.6f), new Vector3(5, 15, 1.6f)));
            Assert.Equal("concrete", straight.Material);
            Assert.Equal(0.2f, straight.PathMetres, 3);
            Assert.Equal(300f, straight.ResistanceMmRha, 1);                              // 1500 mm/m × 0.2 m
            Assert.Equal(9.9f / 15f, straight.T, 3);                                     // enters at y = 9.9

            var oblique = Assert.Single(w.QueryFire(new Vector3(0, 0, 1.6f), new Vector3(20, 20, 1.6f)));
            Assert.Equal(0.2f * MathF.Sqrt(2f), oblique.PathMetres, 3);                  // 45° — the path, not the thickness

            Assert.Empty(w.QueryFire(new Vector3(5, 0, 2.5f), new Vector3(5, 15, 2.5f))); // over the 2 m wall

            var slab = Assert.Single(w.QueryFire(new Vector3(5, 25, 5f), new Vector3(5, 25, 1f)));
            Assert.Equal("slab", slab.Kind);
            Assert.Equal(1500f * TerrainWorld.SlabThicknessMetres, slab.ResistanceMmRha, 1);
            Assert.Equal(0.5f, slab.T, 3);
        }

        // ── ⭐ Stage 5 — a closed door is a panel for sight and fire; the navmesh never sees it ─────────────────

        private const string OneDoorHouse = """
        { "type": "FeatureCollection", "features": [ { "type": "Feature",
            "properties": { "kind": "building", "label": "H", "doors": { "front": "closed" },
              "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                "storeys": [ { "height": 3, "walls": [ { "from": [0,0], "to": [10,0], "thickness": 0.3,
                  "openings": [ { "kind": "door", "at": 4.5, "width": 1, "doorId": "front" } ] } ] } ] } },
            "geometry": { "type": "Point", "coordinates": [20, 20] } } ] }
        """;

        [Fact]
        public void Stage5_AClosedDoorBlocksSight_AndIsADoorForFire_OpenOrDestroyedIsAGap_TheNavmeshNeverSeesIt()
        {
            var w = TerrainWorldParser.Parse(OneDoorHouse, "range");
            int door = w.DoorIndexOf("range/H/front");
            Assert.Equal(0, door);
            var from = new Vector3(25f, 10f, 1.5f); var to = new Vector3(25f, 24f, 1.5f);   // straight through the doorway (24.5–25.5)

            Assert.Equal(TerrainDoorState.Closed, w.DoorState(door));
            Assert.True(w.SegmentBlocked(from, to));
            var leaf = Assert.Single(w.QuerySight(from, to).Crossed);
            Assert.Equal("door", leaf.Kind);
            Assert.Equal("range/H/front", leaf.Label);
            var fire = Assert.Single(w.QueryFire(from, to));
            Assert.Equal(TerrainWorld.DoorMaterial, fire.Material);
            Assert.Equal(60f * TerrainWorld.DoorLeafThickness, fire.ResistanceMmRha, 2);   // a wooden door — a rifle goes through

            w.SetDoorState(door, TerrainDoorState.Open);
            Assert.False(w.SegmentBlocked(from, to));
            Assert.Empty(w.QueryFire(from, to));
            w.SetDoorState(door, TerrainDoorState.Locked);
            Assert.True(w.SegmentBlocked(from, to));
            w.SetDoorState(door, TerrainDoorState.Destroyed);
            Assert.False(w.SegmentBlocked(from, to));

            Assert.DoesNotContain(w.Prisms, p => p.Label == "range/H/front");   // the navmesh (and SurfaceZ) read Prisms only
        }

        /// <summary>
        /// ⭐ Stage 5b — the door ENTITY's replicated state reaches the terrain through <see cref="DoorStateMirrorSystem"/>: the
        /// terrain's own initial value until the entity says otherwise, then the entity's, on every change; a new terrain (a commit
        /// swaps the singleton) is re-indexed. 📄 docs/DESIGN_Building_Interiors.md §3j.
        /// </summary>
        [Fact]
        public void Stage5b_TheDoorEntitysState_IsMirroredIntoTheTerrain_AndFollowsEveryChange()
        {
            using var repo = new Fdp.Core.EntityRepository();
            repo.RegisterComponent<DoorState>();
            repo.RegisterManagedComponent<TerrainObjectKey>();
            var w = TerrainWorldParser.Parse(OneDoorHouse, "range");
            repo.SetSingletonManaged(w);
            int door = w.DoorIndexOf("range/H/front");
            var from = new Vector3(25f, 10f, 1.5f); var to = new Vector3(25f, 24f, 1.5f);

            var mirror = new DoorStateMirrorSystem();
            mirror.Execute(repo, 0f);
            Assert.Equal(TerrainDoorState.Closed, w.DoorState(door));   // no door entity yet: the terrain's own state stands
            Assert.Equal(0, mirror.Writes);

            var e = repo.CreateEntity();
            repo.AddComponent(e, new DoorState { State = TerrainDoorState.Open });
            repo.SetManagedComponent(e, new TerrainObjectKey { Key = "range/H/front" });
            mirror.Execute(repo, 0f);
            Assert.Equal(TerrainDoorState.Open, w.DoorState(door));
            Assert.False(w.SegmentBlocked(from, to));

            repo.SetComponent(e, new DoorState { State = TerrainDoorState.Locked });
            mirror.Execute(repo, 0f);
            Assert.True(w.SegmentBlocked(from, to));
            mirror.Execute(repo, 0f);
            Assert.Equal(2, mirror.Writes);                              // only changes are written

            var w2 = TerrainWorldParser.Parse(OneDoorHouse, "range");   // a terrain commit swaps the singleton
            repo.SetSingletonManaged(w2);
            mirror.Execute(repo, 0f);
            Assert.Equal(TerrainDoorState.Locked, w2.DoorState(w2.DoorIndexOf("range/H/front")));

            Assert.Equal(e, TerrainObjects.Find(repo, "range/H/front"));
            Assert.Equal(new[] { "range/H/front" }, TerrainObjects.ExistingKeys(repo));
        }

        private static string ShippedTerrain(string name)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", name);
        }

        [Fact]
        public void Stage1_TestTown_IsUnchanged_EveryWallConcrete_NoBuildingsOrDoors()
        {
            var folder = ShippedTerrain("test-town");
            var w = TerrainWorldParser.Parse(File.ReadAllText(Path.Combine(folder, "test-town.world.geojson")), "test-town",
                TerrainAssets.ForFolder(folder));
            Assert.Empty(w.Buildings);
            Assert.Empty(w.Doors);
            Assert.All(w.Prisms, p => Assert.Equal("concrete", p.Material!.Name));
            Assert.All(w.Prisms.Where(p => p.Kind == TerrainPrismKind.Building), p => Assert.Equal(-1, p.Panel));
        }

        [Fact]
        public void Stage1_BtRange_Loads_TwoHouses_EachMaterial_AFenceRow_AndTheLockedFrontDoor()
        {
            var folder = ShippedTerrain("bt-range");
            var w = TerrainWorldParser.Parse(File.ReadAllText(Path.Combine(folder, "bt-range.world.geojson")), "bt-range",
                TerrainAssets.ForFolder(folder));
            Assert.Equal(new[] { "House A", "House B" }, w.Buildings.Select(b => b.Label));   // the polygon block stays a plain prism
            Assert.Contains(w.Prisms, p => p.Label == "Solid Block" && p.Kind == TerrainPrismKind.Building && p.Panel == -1);
            foreach (var m in new[] { "concrete", "brick", "fence-wood", "fence-chainlink", "fence-metal-sheet", "hedge" })
                Assert.Contains(w.Panels, p => p.Material.Name == m);
            Assert.Equal(TerrainDoorState.Locked, w.Doors.Single(d => d.Key == "bt-range/House A/front").Initial);
            Assert.Equal(TerrainDoorState.Open, w.Doors.Single(d => d.Key == "bt-range/House A/hall").Initial);
            Assert.Equal(TerrainDoorState.Closed, w.Doors.Single(d => d.Key == "bt-range/House B/front").Initial);   // template default
            Assert.Equal(new[] { 0f, 3f, 6f }, w.SurfacesAt(102, 104));   // inside House A's west room
        }
    }
}
