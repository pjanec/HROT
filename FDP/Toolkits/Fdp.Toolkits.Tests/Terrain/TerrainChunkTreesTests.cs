using System.Linq;
using System.Numerics;
using Fdp.Toolkit.World;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ CE-4377 (docs/DESIGN_Map_3D_Mode.md §6ai T-1 … T-5) — the trees drawn by chunk: every lone tree gets ONE crown (16 triangles) over its trunk; a wood NEAR is
    /// one tree per <c>density × cell area</c> (24 triangles each), deterministic; a wood FAR is a canopy sheet (2 triangles a cell) with walls only on its open edges;
    /// none of it enters the ground or the objects (the simulation's prisms and the equality rail are unchanged). 🔴 Red-proof (compiles): the edge test made
    /// "always" ⇒ the inner cells grow walls and the far count reds.
    /// </summary>
    public class TerrainChunkTreesTests
    {
        // a 200 × 100 m wood of 115 / ha (8 × 4 cells of 25 m) and two lone trees outside it, on flat ground
        private static TerrainWorld World() => TerrainWorldParser.Parse("""
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,240,240],"groundZ":0},"features":[
             {"type":"Feature","properties":{"kind":"surface","surface":"forest","density":115,"canopy":15},
              "geometry":{"type":"Polygon","coordinates":[[[0,0],[200,0],[200,100],[0,100],[0,0]]]}},
             {"type":"Feature","properties":{"kind":"tree","label":"oak"},"geometry":{"type":"Point","coordinates":[30,180]}},
             {"type":"Feature","properties":{"kind":"tree","label":"ash","height":12},"geometry":{"type":"Point","coordinates":[150,200]}}]}
            """, "trees");

        private static (int Triangles, string?[] Materials, Vector3[] Verts, int[] Idx) Part(TerrainWorldQuery q, TerrainChunkPart part)
        {
            int tris = 0; var mats = new System.Collections.Generic.List<string?>(); var verts = new System.Collections.Generic.List<Vector3>(); var idx = new System.Collections.Generic.List<int>();
            var l = q.Chunks;
            for (int cy = 0; cy < l.Ny; cy++)
                for (int cx = 0; cx < l.Nx; cx++)
                {
                    q.BuildChunk(cx, cy, 0, part, out var v, out var i, out var k, out var m);
                    Assert.All(k, kind => Assert.Equal(TerrainSurfaceKind.Wall, kind));   // T-4: coloured by material
                    int b = verts.Count; verts.AddRange(v); idx.AddRange(i.Select(x => x + b));
                    tris += i.Length / 3; mats.AddRange(m);
                }
            return (tris, mats.ToArray(), verts.ToArray(), idx.ToArray());
        }

        [Fact]
        public void NearTrees_AreOneCrownPerLoneTree_PlusOneTreePerDensityCell_AndDeterministic()
        {
            var w = World();
            var q = new TerrainWorldQuery(w);
            int lone = w.Prisms.Count(p => p.Kind == TerrainPrismKind.Tree);
            Assert.Equal(2, lone);
            var near = Part(q, TerrainChunkPart.ChunkTrees);
            int perCell = (int)System.MathF.Round(115f * 25f * 25f / 10000f);   // 7
            int woodTrees = 8 * 4 * perCell;
            Assert.Equal((lone * 16) + (woodTrees * (16 + 8)), near.Triangles);
            Assert.Equal(woodTrees * 8, near.Materials.Count(m => m == TerrainWorldMesh.TrunkMaterial));
            Assert.All(near.Materials, m => Assert.Contains(m, new[] { TerrainWorldMesh.CrownMaterial, TerrainWorldMesh.CrownDarkMaterial, TerrainWorldMesh.TrunkMaterial }));
            // every wood tree stands inside the wood; the lone crowns over their trunks
            Assert.All(near.Verts, v => Assert.True((v.X <= 210f && v.Y <= 110f) || Vector2.Distance(new Vector2(v.X, v.Y), new Vector2(30, 180)) < 5f || Vector2.Distance(new Vector2(v.X, v.Y), new Vector2(150, 200)) < 5f));
            var again = Part(new TerrainWorldQuery(World()), TerrainChunkPart.ChunkTrees);
            Assert.Equal(near.Verts, again.Verts);   // the same placement every build
        }

        [Fact]
        public void FarTrees_AreTheLoneCrowns_PlusACanopySheet_WithWallsOnlyOnTheWoodsEdge()
        {
            var w = World();
            var far = Part(new TerrainWorldQuery(w), TerrainChunkPart.ChunkCanopy);
            int cells = 8 * 4, edges = (2 * 8) + (2 * 4);
            Assert.Equal((2 * 16) + (cells * 2) + (edges * 2), far.Triangles);
            // the sheet sits at the canopy (15 m over flat ground); the edge walls reach down to the crown base
            var canopy = Enumerable.Range(0, far.Triangles).Where(t => far.Materials[t] == TerrainWorldMesh.CanopyMaterial)
                .SelectMany(t => new[] { far.Verts[far.Idx[3 * t]], far.Verts[far.Idx[3 * t + 1]], far.Verts[far.Idx[3 * t + 2]] }).ToArray();
            Assert.All(canopy, v => Assert.True(System.MathF.Abs(v.Z - 15f) < 1e-3f || System.MathF.Abs(v.Z - (15f * TerrainWoods.CrownBase)) < 1e-3f));
        }

        [Fact]
        public void TheTrees_AreNotInTheGroundOrTheObjects()
        {
            var q = new TerrainWorldQuery(World());
            var l = q.Chunks;
            for (int cy = 0; cy < l.Ny; cy++)
                for (int cx = 0; cx < l.Nx; cx++)
                    foreach (var part in new[] { TerrainChunkPart.ChunkGround, TerrainChunkPart.ChunkObjects })
                    {
                        q.BuildChunk(cx, cy, 0, part, out _, out _, out _, out var m);
                        Assert.DoesNotContain(m, x => x is TerrainWorldMesh.CrownMaterial or TerrainWorldMesh.CrownDarkMaterial or TerrainWorldMesh.CanopyMaterial);
                    }
        }
    }
}
