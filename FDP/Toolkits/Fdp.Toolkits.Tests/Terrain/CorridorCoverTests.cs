using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Spatial.Eqs;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4673</c> (HANDOFF_Coordinator_To_Terrain_Street_Fight_Scenario + its STEER; 📄 docs/DESIGN_Building_Interiors.md §3l.8) — IS THE COVER THERE along the street fight's
    /// ground, <c>wide-corridor</c> (a 30 m open corridor, centreline y 200, between detached houses)? Measured on the COMMITTED terrain through the one cover database the EQS
    /// cover search reads (<see cref="TerrainCoverProvider"/>): nothing stands in the corridor; every house faces it with a front door; every house front facing the corridor has
    /// ground cover points with no gap over 3 m and none more than 1.5 m in from a corner; the corridor's edges (garden walls, fences, hedges) carry points on the corridor side.
    /// ⭐ The measured answer for behaviors: from a post ON the centreline, NO point lies within 10 m (the report's SearchRadius) and dozens within 25 m — the nearest at the
    /// corridor's edge, ~14.5 m away. "No cover found" at 10 m is the GEOMETRY of a 30 m corridor, not missing data. ⭐ And the imported terrains: city-battle / demo-city
    /// facades carry points too (every walled building; the only bare records measured were city-battle's two river BRIDGES — a deck has no walls, so no cover is right). 🔴 Red-proof: drop the panel faces from <c>Build</c> ⇒ the facade rows are empty ⇒ red.
    /// </summary>
    public sealed class CorridorCoverTests
    {
        private static TerrainWorld World(string name)
        {
            string root = Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!;
            string def = Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(root), name, "terrain.json");
            return TerrainFolderWorld.ReadWorld(def, TerrainFolderWorld.ReadDefinition(def), name)!;
        }

        private const float South = 185f, North = 215f, CentreY = 200f;

        [Fact]
        public void CE4673_TheCorridorIsOpen_AndEveryHouseFacesItWithADoor()
        {
            var w = World("wide-corridor");
            Assert.DoesNotContain(w.Prisms, p => p.Max.Y > South + 0.01f && p.Min.Y < North - 0.01f && p.Max.X > 60f && p.Min.X < 540f);
            var houses = w.Buildings.Where(b => b.Label.StartsWith("House ", StringComparison.Ordinal)).ToList();
            Assert.True(houses.Count >= 20, $"houses {houses.Count}");
            foreach (var h in houses)
            {
                bool north = h.Footprint.Min(v => v.Y) > CentreY;
                float face = north ? h.Footprint.Min(v => v.Y) : h.Footprint.Max(v => v.Y);
                Assert.Contains(w.Doors, d => d.Key.EndsWith("/" + h.Label + "/front", StringComparison.Ordinal) && MathF.Abs(d.Center.Y - face) < 0.5f);
            }
        }

        [Fact]
        public void CE4673_EveryFrontFacingTheCorridor_HasCoverPoints_AtMost3mApart()
        {
            var w = World("wide-corridor");
            var pts = TerrainCoverProvider.Build(w).Points;
            foreach (var h in w.Buildings.Where(b => b.Label.StartsWith("House ", StringComparison.Ordinal)))
            {
                bool north = h.Footprint.Min(v => v.Y) > CentreY;
                float face = north ? h.Footprint.Min(v => v.Y) : h.Footprint.Max(v => v.Y);
                float x0 = h.Footprint.Min(v => v.X), x1 = h.Footprint.Max(v => v.X);
                var xs = pts.Where(p => p.PositionZ < 1f && p.PositionX >= x0 - 0.1f && p.PositionX <= x1 + 0.1f
                                        && (north ? p.PositionY < face && p.PositionY > face - 1.5f : p.PositionY > face && p.PositionY < face + 1.5f))
                            .Select(p => p.PositionX).OrderBy(x => x).ToList();
                Assert.True(xs.Count >= 4, $"{h.Label}: {xs.Count} points on its corridor front");
                Assert.True(xs[0] - x0 <= 1.5f && x1 - xs[^1] <= 1.5f, $"{h.Label}: a corner left bare ({xs[0] - x0:0.0} / {x1 - xs[^1]:0.0} m)");
                for (int i = 1; i < xs.Count; i++) Assert.True(xs[i] - xs[i - 1] <= 3f, $"{h.Label}: a {xs[i] - xs[i - 1]:0.0} m gap at x {xs[i - 1]:0.0}");
            }
        }

        [Fact]
        public void CE4673_FromAPostOnTheCentreline_NoCoverWithin10m_DozensWithin25m()
        {
            var w = World("wide-corridor");
            var pts = TerrainCoverProvider.Build(w).Points;
            foreach (float x in new[] { 300f, 340f, 380f, 400f, 420f, 440f, 460f, 480f })
            {
                var post = new Vector2(x, CentreY);
                float Dist(CoverPoint p) => Vector2.Distance(new Vector2(p.PositionX, p.PositionY), post);
                Assert.Equal(0, pts.Count(p => Dist(p) <= 10f));
                Assert.True(pts.Count(p => Dist(p) <= 25f) >= 40, $"post x {x}: {pts.Count(p => Dist(p) <= 25f)} within 25 m");
                float nearest = pts.Min(Dist);
                Assert.InRange(nearest, 13.5f, 16f);   // the corridor's edge: the garden walls / fences / hedges
            }
        }

        [Theory]
        [InlineData("city-battle", 0)]
        [InlineData("demo-city", 0)]
        public void CE4673_ImportedFacades_CarryCoverPoints(string terrain, int bareAllowed)
        {
            var w = World(terrain);
            var pts = TerrainCoverProvider.Build(w).Points;
            var grid = pts.GroupBy(p => ((int)MathF.Floor(p.PositionX / 10f), (int)MathF.Floor(p.PositionY / 10f))).ToDictionary(g => g.Key, g => g.ToArray());
            // a building record with no wall panel is a STRUCTURE (a bridge: a deck and its ramps) — no walls, so no cover is right (city-battle's two river bridges, measured)
            var walled = new System.Collections.Generic.HashSet<int>(w.Panels.Where(p => p.Building >= 0).Select(p => p.Building));
            var shapes = w.Buildings.Where((b, i) => walled.Contains(i)).Select(b => b.Footprint)
                .Concat(w.Prisms.Where(p => p.Kind == TerrainPrismKind.Building && p.Building < 0 && p.Panel < 0).Select(p => p.Footprint));
            int bare = 0;
            var named = new System.Collections.Generic.List<string>();
            foreach (var fp in shapes)
            {
                if (fp.Length < 3) continue;
                bool any = false;
                for (int e = 0; e < fp.Length && !any; e++)
                {
                    var a = fp[e]; var b = fp[(e + 1) % fp.Length]; var mid = (a + b) * 0.5f;
                    int cx = (int)MathF.Floor(mid.X / 10f), cy = (int)MathF.Floor(mid.Y / 10f);
                    for (int dx = -1; dx <= 1 && !any; dx++)
                        for (int dy = -1; dy <= 1 && !any; dy++)
                            if (grid.TryGetValue((cx + dx, cy + dy), out var cell)) any = cell.Any(p => DistToSeg(new Vector2(p.PositionX, p.PositionY), a, b) < 1.5f);
                }
                if (!any)
                {
                    bare++;
                    var c = fp.Aggregate(Vector2.Zero, (s, v) => s + v) / fp.Length;
                    var e0 = fp[1] - fp[0]; var n = Vector2.Normalize(new Vector2(e0.Y, -e0.X));
                    var mid0 = (fp[0] + fp[1]) * 0.5f;
                    named.Add($"({c.X:0},{c.Y:0}) {fp.Length} corners, first side {e0.Length():0.0} m, 0.75 m off it inside a solid: {Fdp.Toolkit.Spatial.Eqs.EqsTerrainSight.InsideSolid(w, mid0 + n * 0.75f)} / {Fdp.Toolkit.Spatial.Eqs.EqsTerrainSight.InsideSolid(w, mid0 - n * 0.75f)}");
                }
            }
            Assert.True(bare <= bareAllowed, $"{terrain}: {bare} buildings with no cover point round them: {string.Join(" · ", named)}");
        }

        private static float DistToSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
