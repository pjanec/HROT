using System;
using System.IO;
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

        /// <summary>W9 — a world inside both fixed grids reports nothing; one that leaves them is named, per grid.</summary>
        [Fact]
        public void GridCoverage_NamesEveryGridTheWorldLeaves_W9()
        {
            Assert.Empty(TerrainGridCoverage.Problems(Parse()));   // [0,200]² fits both

            var big = TerrainWorldParser.Parse(
                """{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[-100,0,1200,500]},"features":[]}""");
            var problems = TerrainGridCoverage.Problems(big);
            Assert.Equal(2, problems.Count);
            Assert.Contains(problems, p => p.Contains("perception"));
            Assert.Contains(problems, p => p.Contains("collider"));
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
    }
}
