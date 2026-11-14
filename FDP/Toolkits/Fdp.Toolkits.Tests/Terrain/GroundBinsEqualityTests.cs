using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Orchestration;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ <c>CE-4363</c> — the ground mesh's binned cut / forest test (<c>TerrainWorldMesh.GroundBins</c>) gives the SAME ground as the plain
    /// scan of every prism and surface it replaced: each ground quad's presence and its material, on the shipped terrains with buildings,
    /// water and woods. The plain scan is re-stated here as the reference.
    /// </summary>
    public class GroundBinsEqualityTests
    {
        private static TerrainWorld Load(string name)
        {
            var folder = Path.Combine(ContentLayout.TerrainAssets(ContentLayout.TryFindSourceTree()!), name);
            var world = Directory.GetFiles(folder, "*.world.geojson").Single();
            return TerrainWorldParser.Parse(File.ReadAllText(world), name, TerrainAssets.ForFolder(folder));
        }

        private static bool InBox(Vector2 p, Vector2 min, Vector2 max) => p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;

        private static bool PlainExcluded(TerrainWorld w, Vector2 p)
            => w.Prisms.Any(pr => pr.Panel < 0 && InBox(p, pr.Min, pr.Max) && PolygonMath.Contains(pr.Footprint, p))
               || w.Surfaces.Any(s => s.Type == TerrainSurfaceType.Water && InBox(p, s.Min, s.Max) && PolygonMath.Contains(s.Polygon, p));

        private static bool PlainForest(TerrainWorld w, Vector2 p)
            => w.Surfaces.Any(s => s.Type == TerrainSurfaceType.Forest && InBox(p, s.Min, s.Max) && PolygonMath.Contains(s.Polygon, p));

        [Theory]
        [InlineData("demo-city")]
        [InlineData("test-town")]
        [InlineData("highway-rest")]
        [InlineData("bt-range")]
        public void CE4363_TheBinnedGround_IsThePlainScansGround(string terrain)
        {
            var w = Load(terrain);
            TerrainWorldMesh.Build(w, out var v, out var idx, out var tags);

            // the mesh's ground quads by their centre → forest or not (a quad's triangles are (b, b+1, b+2) and (b, b+2, b+3): b and b+2 are opposite corners)
            var built = new Dictionary<(int, int), bool>();
            for (int t = 0; t < tags.Length; t += 2)
            {
                if (tags[t].Kind != TerrainTriangleKind.Ground) continue;
                var a = v[idx[t * 3]]; var c = v[idx[t * 3 + 2]];
                var centre = new Vector2((a.X + c.X) * 0.5f, (a.Y + c.Y) * 0.5f);
                built[Key(centre)] = tags[t].Material == TerrainWorldMesh.ForestMaterial;
            }
            Assert.NotEmpty(built);

            // the reference: the same cells (TerrainWorldMesh.GroundLayout, re-stated), each kept unless the plain scan cuts its centre
            float cell = w.Height?.CellSize ?? DefaultCell(w, built.Count);
            var min = w.BoundsMin; var max = w.BoundsMax;
            if (w.Height is { } g)
                min = new Vector2(System.MathF.Floor((min.X - g.Origin.X) / cell) * cell + g.Origin.X,
                                  System.MathF.Floor((min.Y - g.Origin.Y) / cell) * cell + g.Origin.Y);
            int nx = System.Math.Max(1, (int)System.MathF.Ceiling((max.X - min.X) / cell));
            int ny = System.Math.Max(1, (int)System.MathF.Ceiling((max.Y - min.Y) / cell));
            int expected = 0;
            for (int iy = 0; iy < ny; iy++)
                for (int ix = 0; ix < nx; ix++)
                {
                    float x0 = min.X + ix * cell, y0 = min.Y + iy * cell;
                    var p = new Vector2((x0 + System.MathF.Min(x0 + cell, max.X)) * 0.5f, (y0 + System.MathF.Min(y0 + cell, max.Y)) * 0.5f);
                    if (PlainExcluded(w, p)) continue;
                    expected++;
                    Assert.True(built.TryGetValue(Key(p), out bool f), $"{terrain}: the plain scan keeps the cell at {p}, the mesh has no quad there");
                    Assert.Equal(PlainForest(w, p), f);
                }
            Assert.Equal(expected, built.Count);
        }

        // no height grid: the mesh's own default cell — read back from the quads (every full cell is cell x cell)
        private static float DefaultCell(TerrainWorld w, int _) => TerrainWorldMesh.DefaultCellSize(w);

        private static (int, int) Key(Vector2 p) => ((int)System.MathF.Round(p.X * 100f), (int)System.MathF.Round(p.Y * 100f));

    }
}
