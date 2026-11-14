using System;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.World;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ (ui, `2026-11-10`; docs/DESIGN_Map_3D_Mode.md §6ai R-5) — a big SOLID building is drawn with PAINTED windows: one row per storey, the sill 0.9 m over the storey's floor,
    /// spaced along every wall clear of the corners, just outside the face. A small one (a garage, a shed) stays plain, and so does a roof cap. The world is untouched (sight and
    /// fire keep the solid prism). 🔴 Red-proof (compiles): <c>HasPaintedWindows</c> always false ⇒ no window triangles and the rail reds.
    /// </summary>
    public class TerrainChunkWindowsTests
    {
        private static TerrainWorld World() => TerrainWorldParser.Parse("""
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,100,100],"groundZ":0},"features":[
             {"type":"Feature","properties":{"kind":"building","height":6,"baseZ":0,"floors":2,"material":"brick","label":"house"},
              "geometry":{"type":"Polygon","coordinates":[[[40,40],[60,40],[60,50],[40,50],[40,40]]]}},
             {"type":"Feature","properties":{"kind":"building","height":3,"baseZ":6,"floors":0,"material":"brick","label":"house-roof"},
              "geometry":{"type":"Polygon","coordinates":[[[40,40],[60,40],[60,50],[40,50],[40,40]]]}},
             {"type":"Feature","properties":{"kind":"building","height":2.5,"baseZ":0,"floors":1,"material":"brick","label":"shed"},
              "geometry":{"type":"Polygon","coordinates":[[[10,10],[15,10],[15,14],[10,14],[10,10]]]}}]}
            """, "windows");

        [Fact]
        public void ABigSolidBuilding_HasPaintedWindowsPerStorey_ASmallOneNone()
        {
            var w = World();
            Assert.True(TerrainWorldMesh.HasPaintedWindows(w.Prisms.Single(p => p.Label == "house")));
            Assert.False(TerrainWorldMesh.HasPaintedWindows(w.Prisms.Single(p => p.Label == "shed")));
            Assert.False(TerrainWorldMesh.HasPaintedWindows(w.Prisms.Single(p => p.Label == "house-roof")));

            var q = new TerrainWorldQuery(w);
            q.BuildChunk(0, 0, 0, TerrainChunkPart.ChunkObjects, out var v, out var i, out var k, out var m);
            var win = Enumerable.Range(0, m.Length).Where(t => m[t] == TerrainWorldMesh.PaintedWindowMaterial).ToArray();
            // 20 m walls: 7 windows each, 10 m walls: 3 each ⇒ 20 per storey, two storeys, two triangles a pane
            Assert.Equal(80, win.Length);
            var pts = win.SelectMany(t => new[] { v[i[3 * t]], v[i[3 * t + 1]], v[i[3 * t + 2]] }).ToArray();
            Assert.All(pts, p => Assert.True(p.X > 39f && p.X < 61f && p.Y > 39f && p.Y < 51f, $"{p} is not on the house"));   // none on the shed
            var zs = pts.Select(p => MathF.Round(p.Z, 2)).Distinct().OrderBy(z => z).ToArray();
            Assert.Equal(new[] { 0.9f, 2.2f, 3.9f, 5.2f }, zs);   // a row per storey, the sill 0.9 m over each floor
            // just OUTSIDE the face (2 cm), never inside the block, and clear of the corners
            Assert.All(pts, p => Assert.True(MathF.Abs(p.Y - 39.98f) < 1e-3f || MathF.Abs(p.Y - 50.02f) < 1e-3f || MathF.Abs(p.X - 39.98f) < 1e-3f || MathF.Abs(p.X - 60.02f) < 1e-3f, $"{p} is not on a face"));
            Assert.All(pts, p => Assert.True(p.X >= 40.79f && p.X <= 59.21f || p.Y >= 40.79f && p.Y <= 49.21f, $"{p} is at a corner"));
            // the world keeps the solid prism: sight through the house is still blocked at window height
            Assert.True(w.SegmentBlocked(new Vector3(50, 30, 1.5f), new Vector3(50, 60, 1.5f)));
        }
    }
}
