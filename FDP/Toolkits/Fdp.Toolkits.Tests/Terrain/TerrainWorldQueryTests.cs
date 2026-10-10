using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.World;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ CE-1035 Q1 (<c>docs/DESIGN_World_Query_Seam.md</c> §3.2) — the terrain stand-in behind <see cref="IWorldQuery"/>. Every answer
    /// must equal what the terrain says directly (the consumers moved onto the seam must not change behaviour), and the per-view lookup
    /// must be cheap (R-220) and bound to that view's doors (R-219).
    /// </summary>
    public sealed class TerrainWorldQueryTests
    {
        private static TerrainWorld BtRange()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Hrot", "Subsystems"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var folder = Path.Combine(dir!.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "bt-range");
            return TerrainWorldParser.Parse(File.ReadAllText(Path.Combine(folder, "bt-range.world.geojson")), "bt-range",
                TerrainAssets.ForFolder(folder));
        }

        private static IEnumerable<(Vector3 A, Vector3 B)> Segments(TerrainWorld w, int n)
        {
            var rng = new Random(1035);
            var min = w.BoundsMin; var max = w.BoundsMax;
            Vector3 P() => new(min.X + (float)rng.NextDouble() * (max.X - min.X), min.Y + (float)rng.NextDouble() * (max.Y - min.Y), (float)rng.NextDouble() * 12f);
            for (int i = 0; i < n; i++)
            {
                var a = P();
                float ang = (float)(rng.NextDouble() * Math.PI * 2), len = 2f + (float)rng.NextDouble() * 60f;
                yield return (a, new Vector3(a.X + MathF.Cos(ang) * len, a.Y + MathF.Sin(ang) * len, (float)rng.NextDouble() * 12f));
            }
        }

        [Fact]
        public void Q1_TheQueryAnswersExactlyAsTheTerrain_WithTheBoundDoors()
        {
            var w = BtRange();
            Assert.True(w.Doors.Count > 0, "bt-range must have doors, or the door binding is not exercised");
            var doors = DoorStates.Authored(w);
            IWorldQuery q = new TerrainWorldQuery(w, doors);
            var fire = new List<TerrainWorld.FireCrossing>();
            var traced = new List<TraceCrossing>();
            int crossed = 0, barriers = 0;
            foreach (var (a, b) in Segments(w, 4000))
            {
                Assert.Equal(w.SegmentBlocked(a, b, doors), q.SightBlocked(a, b));

                w.QueryFire(a, b, fire, doors);
                q.Trace(a, b, TracePurpose.Fire, traced);
                Assert.Equal(fire.Count, traced.Count);
                for (int i = 0; i < fire.Count; i++)
                {
                    var f = fire[i]; var t = traced[i];
                    Assert.Equal(f.T, t.T);
                    Assert.Equal(f.PathMetres, t.PathMetres);
                    Assert.Equal(f.ResistanceMmRha, t.Loss);
                    Assert.Equal(f.TopZ, t.TopZ);
                    Assert.Equal(f.Kind is "slab" or "ramp" or "door" || (f.Kind == "panel" && f.Building != null), t.ClosedBarrier);
                    if (t.ClosedBarrier) barriers++;
                }
                if (fire.Count > 0) crossed++;

                var sight = w.QuerySight(a, b, doors);
                q.Trace(a, b, TracePurpose.Sight, traced);
                Assert.Equal(sight.Crossed.Count, traced.Count);
                for (int i = 0; i < traced.Count; i++) Assert.Equal(sight.Crossed[i].Transmittance, traced[i].Loss);
            }
            Assert.True(crossed > 100 && barriers > 0, $"the segments must cross terrain and enclosures ({crossed} crossed, {barriers} barriers)");
            Assert.Throws<NotSupportedException>(() => q.Trace(Vector3.Zero, Vector3.One, TracePurpose.Sound, traced));
        }

        [Fact]
        public void Q1_StandingAndHeights_MatchTheTerrain()
        {
            var w = BtRange();
            IWorldQuery q = new TerrainWorldQuery(w);
            var rng = new Random(7);
            int refused = 0;
            for (int i = 0; i < 4000; i++)
            {
                float x = w.BoundsMin.X + (float)rng.NextDouble() * (w.BoundsMax.X - w.BoundsMin.X);
                float y = w.BoundsMin.Y + (float)rng.NextDouble() * (w.BoundsMax.Y - w.BoundsMin.Y);
                float hint = (float)rng.NextDouble() * 8f;
                Assert.Equal(w.ResolveLevel(x, y, 0), q.GroundHeightAt(x, y));
                Assert.Equal(w.SurfaceZ(x, y, hint), q.SurfaceZ(x, y, hint));
                Assert.Equal(w.ResolveLevel(x, y, 1), q.ResolveLevel(x, y, 1));
                bool inside = Fdp.Toolkit.Spatial.Eqs.EqsTerrainSight.InsideSolid(w, new Vector2(x, y));
                Assert.Equal(!inside, q.TryStandAt(x, y, hint, out float z));
                if (inside) refused++; else Assert.Equal(w.SurfaceZ(x, y, hint), z);
            }
            Assert.True(refused > 0, "some points must fall inside a building");
        }

        [Fact]
        public void Q1_WorldQueryOf_IsNullWithoutTerrain_AndReusedWhileNothingChanged()
        {
            using var repo = new EntityRepository();
            Assert.Null(WorldQuery.Of(repo));

            var w = BtRange();
            repo.SetSingletonManaged(w);
            var first = WorldQuery.Of(repo);
            Assert.NotNull(first);
            Assert.Same(first, WorldQuery.Of(repo));   // R-220 — the same doors table ⇒ the same query, no allocation
            var tq = Assert.IsType<TerrainWorldQuery>(first);
            Assert.Same(w, tq.World);
            Assert.NotNull(tq.Doors);                 // R-219 — bound to this view's doors

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) WorldQuery.Of(repo);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
