using System.Linq;
using System.Numerics;
using Fdp.Toolkit.World;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ CE-4377 (docs/DESIGN_Map_3D_Mode.md §6ai R-1) — an imported pitched roof's CAP (a building prism with no floors, labelled "…-roof") is DRAWN as a gable: the ridge
    /// along the longest side at the cap's top, the eaves at its base, two end walls in the house's material; no flat cap box. Other prisms are drawn as before, and the
    /// world keeps the cap (sight and fire). 🔴 Red-proof (compiles): <c>IsRoofCap</c> always false ⇒ the box is drawn and the rail reds.
    /// </summary>
    public class TerrainChunkRoofsTests
    {
        private static TerrainWorld World() => TerrainWorldParser.Parse("""
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,100,100],"groundZ":0},"features":[
             {"type":"Feature","properties":{"kind":"building","height":6,"baseZ":0,"floors":2,"material":"brick","label":"h"},
              "geometry":{"type":"Polygon","coordinates":[[[40,40],[60,40],[60,50],[40,50],[40,40]]]}},
             {"type":"Feature","properties":{"kind":"building","height":3,"baseZ":6,"floors":0,"material":"brick","label":"h-roof"},
              "geometry":{"type":"Polygon","coordinates":[[[40,40],[60,40],[60,50],[40,50],[40,40]]]}}]}
            """, "roofs");

        [Fact]
        public void ACap_IsDrawnAsAGable_AndTheWorldKeepsIt()
        {
            var w = World();
            Assert.Single(w.Prisms, p => TerrainWorldMesh.IsRoofCap(p));
            var q = new TerrainWorldQuery(w);
            q.BuildChunk(0, 0, 0, TerrainChunkPart.ChunkObjects, out var v, out var i, out var k, out var m);
            var roof = Enumerable.Range(0, k.Length).Where(t => m[t] == TerrainWorldMesh.PitchedRoofMaterial).ToArray();
            Assert.NotEmpty(roof);
            var roofVerts = roof.SelectMany(t => new[] { v[i[3 * t]], v[i[3 * t + 1]], v[i[3 * t + 2]] }).ToArray();
            Assert.Equal(9f, roofVerts.Max(p => p.Z), 3);     // the ridge at the cap's top
            Assert.Equal(6f, roofVerts.Min(p => p.Z), 3);     // the eaves at its base
            Assert.All(roofVerts.Where(p => System.MathF.Abs(p.Z - 9f) < 1e-3f), p => Assert.Equal(45f, p.Y, 3));   // the ridge runs along the long (x) side, mid-width
            Assert.All(roof, t => Assert.Equal(TerrainSurfaceKind.Roof, k[t]));
            // the two gable ends (each split at the ridge) in the house's material; no flat box top at 9 m
            int ends = Enumerable.Range(0, k.Length).Count(t => m[t] == "brick" && new[] { v[i[3 * t]], v[i[3 * t + 1]], v[i[3 * t + 2]] }.Any(p => p.Z > 6.01f));
            var dump = string.Join("\n", Enumerable.Range(0, k.Length).Where(t => new[] { v[i[3 * t]], v[i[3 * t + 1]], v[i[3 * t + 2]] }.Any(p => p.Z > 6.01f)).Select(t => $"{k[t]} {m[t]} {v[i[3*t]]} {v[i[3*t+1]]} {v[i[3*t+2]]}"));
            Assert.True(ends == 4, dump);   // each gable end is a triangle, split at the ridge into two
            Assert.DoesNotContain(Enumerable.Range(0, k.Length), t => m[t] == "brick" && k[t] == TerrainSurfaceKind.Roof && new[] { v[i[3 * t]], v[i[3 * t + 1]], v[i[3 * t + 2]] }.All(p => System.MathF.Abs(p.Z - 9f) < 1e-3f));
            // the house itself as before: its flat roof at 6 m
            Assert.Contains(Enumerable.Range(0, k.Length), t => k[t] == TerrainSurfaceKind.Roof && m[t] == "brick" && new[] { v[i[3 * t]], v[i[3 * t + 1]], v[i[3 * t + 2]] }.All(p => System.MathF.Abs(p.Z - 6f) < 1e-3f));
            // the world still has the cap (sight and fire)
            Assert.True(w.SegmentBlocked(new Vector3(50, 0, 8f), new Vector3(50, 100, 8f)));
        }
    }
}
