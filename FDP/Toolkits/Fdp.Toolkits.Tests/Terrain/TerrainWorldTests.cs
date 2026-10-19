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

        /// <summary>
        /// ⭐ CE-1165 (civilian U-4, docs/DESIGN_Civilians_And_Crowd_Control.md §8 U4-b) — a prism you can see AND walk through (a gas or smoke
        /// cloud's obstacle, B1-c) is not meshed: it would render as a solid block and enter the navmesh though it blocks nothing. Today's
        /// see-through materials (glass, hedge, chain-link) all block movement and stay meshed.
        /// </summary>
        [Fact]
        public void CE1165_ASeeAndWalkThroughPrism_IsNotMeshed_AWallIs()
        {
            static TerrainPrism Box(float x, TerrainMaterial m) => new()
            {
                Kind = TerrainPrismKind.Wall, BaseZ = 0f, TopZ = 3f, Material = m, Triangles = new[] { 0, 1, 2, 0, 2, 3 },
                Footprint = new[] { new Vector2(x, 0), new Vector2(x + 4, 0), new Vector2(x + 4, 4), new Vector2(x, 4) },
                Min = new Vector2(x, 0), Max = new Vector2(x + 4, 4),
            };
            var concrete = new TerrainMaterial("concrete", 0f, 300f, 40f, true);
            var smoke = new TerrainMaterial("smoke", 0.15f, 0f, 0f, false);
            TerrainWorldMesh.Build(new TerrainWorld { Prisms = new[] { Box(0, concrete) } }, out var wallOnly, out var wallIdx, cellSize: 2f);
            TerrainWorldMesh.Build(new TerrainWorld { Prisms = new[] { Box(0, concrete), Box(10, smoke) } }, out var both, out var bothIdx, cellSize: 2f);
            Assert.Equal((wallOnly.Length, wallIdx.Length), (both.Length, bothIdx.Length));   // the cloud added nothing
            Assert.True(TerrainWorldMesh.IsVolumeEffect(Box(10, smoke)));
            foreach (var name in new[] { "glass", "hedge", "fence-chainlink" })
                Assert.True(TerrainMaterialLibrary.Shared.TryGet(name, out var m) && !TerrainWorldMesh.IsVolumeEffect(Box(0, m!)), name);
        }

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

        /// <summary>
        /// ⭐ 5d-2 (live, bt-doors) — a doorway's LINTEL (a piece from 2.1 to 3 m) is overhead: an agent walking through the
        /// doorway stays on the ground. 🔴 It used to count as solid, took the ground away, and the agent came out on the upper
        /// floor at 3 m. The upper floor is still where an agent already up there stands.
        /// </summary>
        [Fact]
        public void Stage5d2_UnderADoorwaysLintel_TheGroundStays_TheUpperFloorIsStillReachableFromAbove()
        {
            var w = HouseWorld();
            Assert.Equal(0f, w.SurfaceZ(104.8f, 100f, zHint: 0f));     // in the front doorway (104.5..105.5), under its lintel
            Assert.Equal(0f, w.SurfaceZ(105f, 106.4f, zHint: 0f));     // in the hall doorway in the inner wall
            Assert.Equal(3f, w.SurfaceZ(102f, 104f, zHint: 3f));       // already on the upper floor: stays there
            // ⛔ SUPERSEDED (CE-3111 live, 2026-10-08): "inside a FULL-HEIGHT wall piece the ground is still taken away" — see below.
            Assert.Equal(0f, w.SurfaceZ(103.5f, 100f, zHint: 0f));
        }

        /// <summary>
        /// ⭐ CE-3111 (live, bt-doors with real 0.9 m doors) — a WALL PANEL never takes the ground away: an agent whose centre clips a
        /// door's jamb by a few centimetres (a mover turning through a narrow doorway) stays on the ground. 🔴 It took the ground away,
        /// the wall's top (3 m) became the only surface, and from then on the agent's Z hint kept it on the UPPER FLOOR's slab across
        /// the whole storey (probe: z 3.0 at (106.4, 107.5), the east room, right after the back door) — some runs never reached the
        /// back door's reach. The same rule as the ground mesh (<c>TerrainWorldMesh</c>: a panel is thin and keeps its ground). A SOLID
        /// building still takes it (the roof rail above).
        /// </summary>
        [Fact]
        public void CE3111_InsideAWallPanel_TheGroundStays_TheAgentNeverJumpsOntoTheUpperFloor()
        {
            var w = HouseWorld();
            Assert.Equal(0f, w.SurfaceZ(103.5f, 100f, zHint: 0f));          // in the front wall beside the doorway
            Assert.Equal(0f, w.SurfaceZ(104.45f, 100.02f, zHint: 0f));      // clipping the doorway's west jamb by 5 cm
            Assert.Equal(0f, w.SurfaceZ(105.02f, 104f, zHint: 0f));         // in the inner wall (x 105)
            Assert.Equal(3f, w.SurfaceZ(102f, 104f, zHint: 3f));            // an agent already upstairs stays upstairs
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
            var open = Fdp.Toolkit.Terrain.Tests.DoorFixtures.States(w, (w.Doors[0].Key, TerrainDoorState.Open));   // R-219: the view's doors
            Assert.False(w.SegmentBlocked(new Vector3(104.8f, 90, 1.6f), new Vector3(104.8f, 104, 1.6f), open));
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

            Assert.Equal(TerrainDoorState.Closed, DoorStates.Authored(w)[door]);
            Assert.True(w.SegmentBlocked(from, to));
            var leaf = Assert.Single(w.QuerySight(from, to).Crossed);
            Assert.Equal("door", leaf.Kind);
            Assert.Equal("range/H/front", leaf.Label);
            var fire = Assert.Single(w.QueryFire(from, to));
            Assert.Equal(TerrainWorld.DoorMaterial, fire.Material);
            Assert.Equal(60f * TerrainWorld.DoorLeafThickness, fire.ResistanceMmRha, 2);   // a wooden door — a rifle goes through

            DoorStates As(TerrainDoorState st) => Fdp.Toolkit.Terrain.Tests.DoorFixtures.States(w, ("range/H/front", st));
            Assert.False(w.SegmentBlocked(from, to, As(TerrainDoorState.Open)));
            Assert.Empty(w.QueryFire(from, to, As(TerrainDoorState.Open)));
            Assert.True(w.SegmentBlocked(from, to, As(TerrainDoorState.Locked)));
            Assert.False(w.SegmentBlocked(from, to, As(TerrainDoorState.Destroyed)));
            Assert.True(w.SegmentBlocked(from, to));                         // no table: as the terrain authored it (closed)

            Assert.DoesNotContain(w.Prisms, p => p.Label == "range/H/front");   // the navmesh (and SurfaceZ) read Prisms only
        }

        /// <summary>
        /// ⭐ R-219 — a view's door table comes from the door ENTITIES in that view: authored where there is none, the entity's state
        /// where there is one, keyed by <see cref="TerrainObjectKey"/>; a key the terrain does not define is ignored.
        /// </summary>
        [Fact]
        public void R219_AViewsDoorTable_IsBuiltFromItsDoorEntities()
        {
            var w = TerrainWorldParser.Parse(OneDoorHouse, "range");
            int door = w.DoorIndexOf("range/H/front");
            using var repo = Fdp.Toolkit.Terrain.Tests.DoorFixtures.WorldWithDoors(w);
            Assert.Equal(TerrainDoorState.Closed, DoorStates.Of(repo, w)[door]);                       // no entity: authored

            var e = repo.CreateEntity();
            repo.AddComponent(e, new DoorState { State = TerrainDoorState.Open });
            repo.SetManagedComponent(e, new TerrainObjectKey { Key = "range/H/front" });
            var stray = repo.CreateEntity();
            repo.AddComponent(stray, new DoorState { State = TerrainDoorState.Locked });
            repo.SetManagedComponent(stray, new TerrainObjectKey { Key = "elsewhere/X/y" });           // not this terrain's door
            Assert.Equal(TerrainDoorState.Open, DoorStates.Of(repo, w)[door]);
            Assert.Equal(TerrainDoorState.Open, DoorStates.Of(repo)![door]);                           // the resident terrain's
            Assert.False(w.SegmentBlocked(new Vector3(25f, 10f, 1.5f), new Vector3(25f, 24f, 1.5f), DoorStates.Of(repo)));

            Assert.Equal(e, TerrainObjects.Find(repo, "range/H/front"));
            Assert.Contains("range/H/front", TerrainObjects.ExistingKeys(repo));
        }

        /// <summary>
        /// ⭐⭐ R-219 — THE reason for the rule. A background module runs on a SNAPSHOT of the world (<c>SyncFrom</c>, as the GDB/SoD
        /// providers do). A door flipped on the live world AFTER the sync must not be visible to a query on the snapshot — and before
        /// R-219 it was: the state lived on the <see cref="TerrainWorld"/>, which a snapshot shares by reference.
        /// </summary>
        [Fact]
        public void R219_AQueryOnASnapshot_SeesTheDoorsOfThatSnapshot_NotALaterFlipOnTheLiveWorld()
        {
            var w = TerrainWorldParser.Parse(OneDoorHouse, "range");
            using var live = Fdp.Toolkit.Terrain.Tests.DoorFixtures.WorldWithDoors(w, ("range/H/front", TerrainDoorState.Open));
            using var snapshot = new Fdp.Core.EntityRepository();
            snapshot.RegisterComponent<DoorState>();
            snapshot.RegisterManagedComponent<TerrainObjectKey>();
            snapshot.SyncFrom(live);
            var from = new Vector3(25f, 10f, 1.5f); var to = new Vector3(25f, 24f, 1.5f);

            var door = TerrainObjects.Find(live, "range/H/front");
            live.SetComponent(door, new DoorState { State = TerrainDoorState.Locked });   // the main thread locks it after the sync

            Assert.True(w.SegmentBlocked(from, to, DoorStates.Of(live, w)));             // the live world sees it locked
            Assert.False(w.SegmentBlocked(from, to, DoorStates.Of(snapshot, w)));        // the snapshot still sees it open
        }

        /// <summary>
        /// ⭐⭐ R-220 — the per-tick queries a background batch runs allocate NOTHING once warm (GC stutters): the sight test, the fire
        /// trace into a reused list, and the view's door table while its doors are unchanged. Over a house with walls, door leaves,
        /// a window and floors/stairs, so every loop runs. ⚠ <see cref="TerrainWorld.QuerySight"/> is a diagnostic and allocates its answer.
        /// </summary>
        [Fact]
        public void R220_SightFireAndTheDoorTable_AllocateNothingPerCall()
        {
            var w = HouseWorld();
            using var repo = Fdp.Toolkit.Terrain.Tests.DoorFixtures.WorldWithDoors(w, ("range/H/front", TerrainDoorState.Closed));
            var crossings = new System.Collections.Generic.List<TerrainWorld.FireCrossing>(16);
            var lines = new[]
            {
                (new Vector3(104.8f, 90, 1.6f), new Vector3(104.8f, 104, 1.6f)),   // through the front door
                (new Vector3(102.1f, 90, 1.6f), new Vector3(102.1f, 104, 1.6f)),   // through the window
                (new Vector3(102, 104, 4.5f), new Vector3(102, 104, 1.0f)),        // down through a floor
                (new Vector3(90, 104, 1.6f), new Vector3(120, 104, 1.6f)),         // across every wall
            };
            DoorStates doors = DoorStates.Of(repo, w);
            bool blockedAny = false;
            void Run()
            {
                doors = DoorStates.Of(repo, w);
                foreach (var (a, b) in lines)
                {
                    blockedAny |= w.SegmentBlocked(a, b, doors) | w.SegmentBlocked(a, b);
                    w.QueryFire(a, b, crossings, doors);
                }
            }
            Run(); Run();   // warm-up: the lazy door leaves, the thread's scratch

            var first = doors;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) Run();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.True(blockedAny);
            Assert.Equal(0L, allocated);
            Assert.Same(first, doors);                                    // unchanged doors ⇒ the same table
        }

        /// <summary>
        /// ⭐ R-220 — the door table's no-allocation path never serves a stale table: it re-reads the door entities on EVERY call, so a
        /// door written within the same tick (no version moved) is seen at once; a table already handed out never changes.
        /// </summary>
        [Fact]
        public void R220_TheDoorTable_IsReusedOnlyWhileTheDoorsAreUnchanged_AndAHandedOutTableNeverChanges()
        {
            var w = TerrainWorldParser.Parse(OneDoorHouse, "range");
            using var repo = Fdp.Toolkit.Terrain.Tests.DoorFixtures.WorldWithDoors(w, ("range/H/front", TerrainDoorState.Open));
            var door = TerrainObjects.Find(repo, "range/H/front");

            var open = DoorStates.Of(repo, w);
            Assert.Same(open, DoorStates.Of(repo, w));
            repo.SetComponent(door, new DoorState { State = TerrainDoorState.Locked });   // same tick — no GlobalVersion change
            var locked = DoorStates.Of(repo, w);
            Assert.NotSame(open, locked);
            Assert.Equal(TerrainDoorState.Locked, locked[0]);
            Assert.Equal(TerrainDoorState.Open, open[0]);                                    // the old table is untouched
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
            Assert.Equal(new[] { "House A", "House B", "CQB House" }, w.Buildings.Select(b => b.Label));   // UI-3 added the CQB house; the polygon block stays a plain prism
            Assert.Contains(w.Prisms, p => p.Label == "Solid Block" && p.Kind == TerrainPrismKind.Building && p.Panel == -1);
            foreach (var m in new[] { "concrete", "brick", "fence-wood", "fence-chainlink", "fence-metal-sheet", "hedge" })
                Assert.Contains(w.Panels, p => p.Material.Name == m);
            Assert.Equal(TerrainDoorState.Locked, w.Doors.Single(d => d.Key == "bt-range/House A/front").Initial);
            Assert.Equal(TerrainDoorState.Open, w.Doors.Single(d => d.Key == "bt-range/House A/hall").Initial);
            Assert.Equal(TerrainDoorState.Closed, w.Doors.Single(d => d.Key == "bt-range/House B/front").Initial);   // template default
            Assert.Equal(new[] { 0f, 3f, 6f }, w.SurfacesAt(102, 104));   // inside House A's west room
        }
    
        /// <summary>
        /// ⭐ UI-3 (platoon demos hand-off, DESIGN_Building_Clearing §8 <c>img/cqb-house.svg</c>) — <c>bt-range</c> now stands THREE buildings: the
        /// CQB test house (14 × 10 m, one storey) 60 m west of House A on clear ground. Its doors are the picture's: front (W) and back (E) into the
        /// hallway, side N into A, side S into C, a / b / c / d hallway → A / B / C / D, and e from D into the closet — E has no other opening, so it
        /// is reached ONLY through D. Every door is 1.0 m wide (a narrower one does not bake); the four exits start Closed, the room doors Open.
        /// 🔴 Red-proof: move the closet door to the hallway wall ⇒ the "only e opens into E" assertion names it; shrink a door to 0.9 m ⇒ the width one.
        /// </summary>
        [Fact]
        public void UI3_BtRange_CqbHouse_StandsApartFromHouseA_WithTheFourExitsAndTheRoomDoorsOfThePicture()
        {
            var folder = ShippedTerrain("bt-range");
            var w = TerrainWorldParser.Parse(File.ReadAllText(Path.Combine(folder, "bt-range.world.geojson")), "bt-range",
                TerrainAssets.ForFolder(folder));
            var cqb = w.Buildings.Single(b => b.Label == "CQB House");
            var houseA = w.Buildings.Single(b => b.Label == "House A");
            Assert.Equal("cqb-house", cqb.Template);
            Assert.Equal(1, cqb.Storeys);
            Assert.Equal(0f, cqb.RotationDegrees);
            Assert.True(Vector2.Distance(cqb.Position, houseA.Position) >= 55f, $"CQB house at {cqb.Position}, House A at {houseA.Position}");
            float x0 = cqb.Footprint.Min(p => p.X), x1 = cqb.Footprint.Max(p => p.X), y0 = cqb.Footprint.Min(p => p.Y), y1 = cqb.Footprint.Max(p => p.Y);
            Assert.Equal((14f, 10f), (MathF.Round(x1 - x0, 2), MathF.Round(y1 - y0, 2)));
            // clear ground: no other panel of the range has an end within 2 m of the house's box
            foreach (var p in w.Panels.Where(p => p.Label != "CQB House"))
                foreach (var pt in new[] { p.A, p.B })
                    Assert.False(pt.X > x0 - 2 && pt.X < x1 + 2 && pt.Y > y0 - 2 && pt.Y < y1 + 2, $"'{p.Label}' has an end at {pt} inside the CQB house's box");

            // door id -> where it stands, in house-local metres (the picture; the house is placed at its south-west corner, rotation 0)
            var expected = new (string Id, float X, float Y)[]
            {
                ("front", 0f, 5f), ("back", 14f, 5f), ("north", 5.4f, 10f), ("south", 1.4f, 0f),
                ("a", 3.5f, 6f), ("b", 10.5f, 6f), ("c", 4.5f, 4f), ("d", 10.4f, 4f), ("e", 11.8f, 2f),
            };
            var doors = w.Doors.Where(d => d.Key.StartsWith("bt-range/CQB House/")).ToList();
            Assert.Equal(expected.Select(e => e.Id).OrderBy(i => i), doors.Select(d => d.Key.Split('/')[2]).OrderBy(i => i));
            foreach (var (id, lx, ly) in expected)
            {
                var d = doors.Single(x => x.Key == "bt-range/CQB House/" + id);
                Assert.True(Vector2.Distance(d.Center, cqb.Position + new Vector2(lx, ly)) < 0.01f, $"door '{id}' stands at {d.Center - cqb.Position}, the picture has ({lx}, {ly})");
                Assert.Equal(1.0f, w.Panels[d.Panel].Openings[d.Opening].Width);
                Assert.Equal(id is "front" or "back" or "north" or "south" ? TerrainDoorState.Closed : TerrainDoorState.Open, d.Initial);   // the four exits start Closed, the room doors are open doorways
            }
            // the closet E (x 11.8..14, y 0..4) is entered ONLY through D: the one door on its boundary is 'e'
            var intoE = doors.Where(d => d.Center.X >= cqb.Position.X + 11.7f && d.Center.X <= cqb.Position.X + 14.1f
                                      && d.Center.Y >= cqb.Position.Y - 0.1f && d.Center.Y <= cqb.Position.Y + 4.1f).Select(d => d.Key.Split('/')[2]);
            Assert.Equal(new[] { "e" }, intoE);
        }

        /// <summary>
        /// ⭐ UI-1 (platoon demos hand-off) — the SHARED templates folder <c>Recipes/Buildings/</c>: <c>bt-range</c> places
        /// <c>house-2f</c> with no copy of its own (<see cref="TerrainAssets.RecipesSibling"/> finds it from the source tree, the build
        /// output's <c>Recipes/Buildings</c> from the app), and no template is left in two places.
        /// 🔴 Red-proof: drop <c>RecipesSibling</c> from the lookup ⇒ "building template 'house-2f' was not found".
        /// </summary>
        [Fact]
        public void UI1_BtRange_FindsHouse2f_InTheSharedFolder_AndNoTemplateIsInTwoPlaces()
        {
            var folder = ShippedTerrain("bt-range");
            var shared = TerrainAssets.RecipesSibling(folder);
            Assert.True(File.Exists(Path.Combine(shared, "house-2f" + TerrainAssets.TemplateSuffix)), $"house-2f is not in '{shared}'");
            Assert.False(Directory.Exists(Path.Combine(folder, TerrainAssets.BuildingsFolderName))
                && Directory.GetFiles(Path.Combine(folder, TerrainAssets.BuildingsFolderName)).Length > 0, "bt-range keeps a template of its own");

            var w = TerrainWorldParser.Parse(File.ReadAllText(Path.Combine(folder, "bt-range.world.geojson")), "bt-range",
                TerrainAssets.ForFolder(folder, sharedBuildingsRoot: Path.Combine(Path.GetTempPath(), "no-such-shared-root")));
            Assert.Equal(new[] { "house-2f", "house-2f", "cqb-house" }, w.Buildings.Select(b => b.Template));   // UI-3 added the CQB house, found in the same shared folder

            var sharedNames = Directory.GetFiles(shared, "*" + TerrainAssets.TemplateSuffix).Select(Path.GetFileName).ToHashSet();
            var twice = Directory.GetDirectories(Path.GetDirectoryName(folder)!)
                .SelectMany(t => Directory.Exists(Path.Combine(t, TerrainAssets.BuildingsFolderName))
                    ? Directory.GetFiles(Path.Combine(t, TerrainAssets.BuildingsFolderName), "*" + TerrainAssets.TemplateSuffix) : Array.Empty<string>())
                .Where(f => sharedNames.Contains(Path.GetFileName(f))).ToArray();
            Assert.True(twice.Length == 0, "templates in a terrain's buildings/ AND the shared folder: " + string.Join(", ", twice));
        }

        /// <summary>
        /// ⭐ UI-2 (Defend §16 DA-22, <c>CE-2285</c>) — <c>house-3f</c>'s roof is a STOREY: its floor at 6 m, a 1 m parapet round the edge
        /// (a LOW wall), and a stair house whose lid is the template's <c>roof</c> rings (8.5 m) — no roof slab over the rest.
        /// The parapet is crouched cover by the stance thresholds; from the street a man on the roof is seen only above it.
        /// 🔴 Red-proof: ignore a wall's <c>height</c> ⇒ the parapet is 2.5 m and the head is hidden; read <c>roof</c> rings as
        /// "flat" ⇒ a slab at 8.5 m over the whole roof.
        /// </summary>
        [Fact]
        public void UI2_House3f_TheRoofIsAStorey_WithAStairHouseLid_AndAOneMetreParapet()
        {
            var folder = ShippedTerrain("bt-range");
            var w = TerrainWorldParser.Parse("""
                { "type": "FeatureCollection", "hrot": { "schemaVersion": 1, "bounds": [0, 0, 200, 200], "groundZ": 0 },
                  "features": [ { "type": "Feature", "properties": { "kind": "building", "template": "house-3f", "label": "H3" },
                                  "geometry": { "type": "Point", "coordinates": [100, 100] } } ] }
                """, "range", TerrainAssets.ForFolder(folder));
            var b = Assert.Single(w.Buildings);
            Assert.Equal(new[] { 0f, 3f, 6f, 8.5f }, b.StoreyZ);

            var parapet = w.Panels.Where(p => p.Storey == 2 && MathF.Abs(p.TopZ - p.BaseZ - 1f) < 1e-3f).ToArray();
            Assert.Equal(5, parapet.Length);
            Assert.All(parapet, p => Assert.Equal(6f, p.BaseZ, 3));
            Assert.Equal(4, w.Panels.Count(p => p.Storey == 2 && MathF.Abs(p.TopZ - 8.5f) < 1e-3f));   // the stair house, full height
            Assert.Contains(w.Doors, d => d.Key == "range/H3/roof" && MathF.Abs(d.SillZ - 6f) < 1e-3f);

            Assert.Equal(new[] { 0f, 3f, 6f }, w.SurfacesAt(102, 104));             // the west room: no lid over the open roof
            Assert.Equal(new[] { 0f, 3f, 6f, 8.5f }, w.SurfacesAt(109.3f, 103));   // inside the stair house: its lid
            Assert.Equal(4.5f, w.SurfaceZ(109.3f, 105.5f, zHint: 4.4f), 1);         // on the second flight (y 107 → 104, 3 → 6)

            // a man on the roof behind the south parapet, seen from the street 30 m south
            var street = new Vector3(105, 70, 1.7f);
            Assert.True(w.SegmentBlocked(street, new Vector3(105, 101, 6.5f)), "his body below the parapet must be hidden");
            Assert.False(w.SegmentBlocked(street, new Vector3(105, 101, 7.6f)), "his head above the parapet must be seen");

            // crouched cover on the roof floor along the parapet
            var cover = Fdp.Toolkit.Spatial.Eqs.TerrainCoverProvider.Build(w).Points
                .Where(p => MathF.Abs(p.PositionZ - 6f) < 0.1f && p.PositionY < 101.5f && p.PositionX < 106f).ToArray();
            Assert.NotEmpty(cover);
            Assert.All(cover, p => Assert.Equal(1, p.StanceHeight));
        }

        /// <summary>
        /// ⭐ UI-4 (HANDOFF_Behaviors_To_Ui_Platoon_Demos, <c>CE-2290</c>) — <c>cqb-village</c> is DESIGN_Platoon_Attack_Defend_Clear §12.1's map
        /// (<c>img/cqb-village.svg</c>, 1 m = 2.2 px): OBJ (house-3f) between H1 and H3 south of Main Street, fronts north; H6, H4, H5 (the ambush
        /// house) and SNP (house-3f, the sniper hide) north of it, fronts south; CMP's 2 m concrete walls with a west gate, four 1.2 m sandbag posts
        /// and a solid shed; the 2 m hedge row, the 1 m low wall, the two 1 m berms (one the support line, 112 m south of OBJ) and the wreck.
        /// </summary>
        [Fact]
        public void UI4_CqbVillage_IsTheDesignsMap()
        {
            var folder = ShippedTerrain("cqb-village");
            var w = TerrainWorldParser.Parse(File.ReadAllText(Path.Combine(folder, "cqb-village.world.geojson")), "cqb-village",
                TerrainAssets.ForFolder(folder));
            var expected = new (string Label, string Template, float X0, float Y0, float X1, float Y1)[]
            {
                ("H1", "house-2f", 75, 152, 85, 160), ("OBJ", "house-3f", 115, 153, 125, 161), ("H3", "house-2f", 155, 152, 165, 160),
                ("H6", "house-2f", 40, 204, 50, 212), ("H4", "house-2f", 100, 204, 110, 212), ("H5", "house-2f", 230, 204, 240, 212),
                ("SNP", "house-3f", 300, 204, 310, 212),
            };
            Assert.Equal(expected.Select(e => e.Label).OrderBy(l => l), w.Buildings.Where(b => !b.Solid).Select(b => b.Label).OrderBy(l => l));
            foreach (var e in expected)
            {
                var b = w.Buildings.Single(x => x.Label == e.Label);
                Assert.Equal(e.Template, b.Template);
                Assert.Equal((e.X0, e.Y0, e.X1, e.Y1), (MathF.Round(b.Footprint.Min(p => p.X), 2), MathF.Round(b.Footprint.Min(p => p.Y), 2),
                    MathF.Round(b.Footprint.Max(p => p.X), 2), MathF.Round(b.Footprint.Max(p => p.Y), 2)));
            }
            // fronts face Main Street (y 184..196): the front door is on the street side of each house
            foreach (var e in expected)
            {
                var front = w.Doors.Single(d => d.Key == $"cqb-village/{e.Label}/front");
                Assert.True(e.Y0 > 196 ? MathF.Abs(front.Center.Y - e.Y0) < 0.01f : MathF.Abs(front.Center.Y - e.Y1) < 0.01f, $"{e.Label}'s front door at {front.Center}");
            }
            TerrainWallPanel Panel(string label) => w.Panels.Single(p => p.Label == label);
            foreach (var side in new[] { "south", "east", "north", "west" })
                Assert.Equal((2f, "concrete"), (Panel($"CMP {side} wall").TopZ - Panel($"CMP {side} wall").BaseZ, Panel($"CMP {side} wall").Material.Name));
            var gate = Assert.Single(Panel("CMP west wall").Openings);
            Assert.Equal((TerrainOpeningKind.Gap, 4f), (gate.Kind, gate.Width));
            Assert.Equal(4, w.Panels.Count(p => p.Label!.StartsWith("CMP sandbag post") && MathF.Abs(p.TopZ - p.BaseZ - 1.2f) < 1e-3f && p.Material.Name == "sandbags"));
            Assert.Equal((2f, "hedge"), (Panel("hedge row").TopZ, Panel("hedge row").Material.Name));
            Assert.Equal(1f, Panel("low wall").TopZ);
            Assert.All(new[] { "berm", "SBF berm" }, l => Assert.Equal((1f, "earth-berm"), (Panel(l).TopZ, Panel(l).Material.Name)));
            Assert.Equal(40f, Panel("SBF berm").A.Y);   // the support line: ~112 m south of OBJ's south wall (y 153), as the picture
            Assert.Contains(w.Prisms, p => p.Label == "CMP shed" && p.Panel == -1 && p.TopZ == 3f);
            Assert.Contains(w.Prisms, p => p.Label == "wreck" && p.Panel == -1 && p.Material!.Name == "car-body");
        }

        /// <summary>
        /// ⭐ UI-11 (HANDOFF_Behaviors_To_Ui_Demo_City_Quarters) — demo-city's LANDING SQUARE (565..615 × 295..345, behind the battle
        /// street's west row) is clear: no building, wall or slab stands in it, and a helicopter coming in from the open south edge at
        /// 60 m and settling onto it — the rotor disc's centre and both tips (±8 m) — passes nothing. 🔴 Red-proof: a lot moved into
        /// the square fails the first assertion.
        /// </summary>
        [Fact]
        public void UI11_DemoCity_TheLandingSquareIsClear_AndAHelicopterComesInFromTheSouthWithoutACollision()
        {
            var folder = ShippedTerrain("demo-city");
            var w = TerrainWorldParser.Parse(File.ReadAllText(Path.Combine(folder, "demo-city.world.geojson")), "demo-city",
                TerrainAssets.ForFolder(folder));
            const float X0 = 565, Y0 = 295, X1 = 615, Y1 = 345;
            bool In(Vector2 p) => p.X > X0 && p.X < X1 && p.Y > Y0 && p.Y < Y1;
            Assert.DoesNotContain(w.Buildings, b => b.Footprint.Any(In) || In(new Vector2((X0 + X1) / 2, (Y0 + Y1) / 2)) && PolygonMath.Contains(b.Footprint, new Vector2((X0 + X1) / 2, (Y0 + Y1) / 2)));
            Assert.DoesNotContain(w.Prisms, p => p.Footprint.Any(q => In(new Vector2(q.X, q.Y))));
            for (float x = X0 + 2; x < X1; x += 4)
                for (float y = Y0 + 2; y < Y1; y += 4)
                    Assert.Equal(new[] { 0f }, w.SurfacesAt(x, y));                  // nothing over the ground anywhere in it

            var pad = new Vector2(590, 320);
            foreach (float dx in new[] { -8f, 0f, 8f })
            {
                Assert.False(w.SegmentBlocked(new Vector3(pad.X + dx, 120, 60), new Vector3(pad.X + dx, pad.Y, 12)), $"the approach at {dx:+0;-0;0} m");
                Assert.False(w.SegmentBlocked(new Vector3(pad.X + dx, pad.Y, 12), new Vector3(pad.X + dx, pad.Y, 0.5f)), $"the descent at {dx:+0;-0;0} m");
            }
        }

        /// <summary>⭐ CE-3128 (R-231) — a road POLYGON is refused, naming where roads live now: the terrain's road network.</summary>
        [Fact]
        public void CE3128_SurfaceRoad_IsRetired_AndTheErrorNamesTheRoadNetwork()
        {
            var json = """
                {"type":"FeatureCollection","features":[
                  {"type":"Feature","properties":{"kind":"surface","surface":"road"},
                   "geometry":{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}}]}
                """;
            var ex = Assert.Throws<ArgumentException>(() => TerrainWorldParser.Parse(json));
            Assert.Contains("roadNetworks", ex.Message);
        }
}
}
