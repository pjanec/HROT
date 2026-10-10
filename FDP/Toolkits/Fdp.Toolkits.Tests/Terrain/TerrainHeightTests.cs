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
