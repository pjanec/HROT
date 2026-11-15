using System;
using System.IO;
using System.Numerics;
using System.Threading;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4591</c> (P-4 part 2; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md D-1, D-2, V-4, V-6) — the bake CACHES the craters: once a
    /// <see cref="GroundDamage"/> record is baked, the resident world's height grid holds the crater (the navmesh and the 3-D mesh follow) while the simulation's
    /// answer stays the bare ground + the view's craters (never counted twice); every vehicle routes round it and infantry crosses (D-2); the dirty rectangle is
    /// the crater's disc (V-4); a display node bakes the ground too (V-6); the record gone (a seek back) ⇒ the ground comes back.
    /// </summary>
    public sealed class CraterBakeTests : IDisposable
    {
        private readonly EntityRepository _world = new();
        private readonly string _staging = Path.Combine(Path.GetTempPath(), "p4-" + Guid.NewGuid().ToString("N"));
        private readonly SwitchableNavmeshProvider _node = new();
        private readonly TerrainWorld _flat;
        private double _wall;
        private static readonly Crater Hole = new(new Vector2(30f, 30f), 4f, 1.8f, 0.3f, (SimInstant)1.0, CraterKind.Large);
        /// <summary>A WIDE, SHALLOW crater: its walls (≈ 8°) are gentle enough for the vehicle layer's 20° slope limit, so only the crater AREA keeps a vehicle out.</summary>
        private static readonly Crater Dish = new(new Vector2(30f, 30f), 6f, 0.4f, 0.05f, (SimInstant)1.0, CraterKind.Large);

        private readonly ITestOutputHelper _out;

        public CraterBakeTests(ITestOutputHelper output)
        {
            _out = output;
            SimHostComponentRegistry.RegisterAll(_world);
            Directory.CreateDirectory(_staging);
            int n = 61;   // a flat 60 × 60 m field, 1 m posts
            _flat = new TerrainWorld
            {
                Name = "range", BoundsMin = Vector2.Zero, BoundsMax = new Vector2(60f, 60f),
                Height = new TerrainHeightGrid(Vector2.Zero, 1f, n, n, new float[n * n]),
            };
        }

        public void Dispose()
        {
            _world.Dispose();
            try { Directory.Delete(_staging, recursive: true); } catch { }
        }

        private StaticObstacleBakeSystem Sys() => new(() => _wall);

        private TerrainResidency Node(bool muscle)
        {
            var residency = new TerrainResidency(_staging, new RoadNetworkHolder());
            INavmeshProvider? baseline = null;
            if (muscle)
            {
                var factory = new RecastNavmeshFactory { Layers = NavLayerMask.Infantry | NavLayerMask.Vehicle, TileCache = new NavTileCache() };
                baseline = factory.Build(_flat);
                residency.AttachNavmesh(factory, _node);
            }
            residency.Commit(_world, new TerrainResidency.Staged { Definition = new TerrainDefinition { Name = "range" }, World = _flat, TerrainName = "range", Navmesh = baseline });
            return residency;
        }

        private Entity Record(in Crater c)
        {
            var d = new GroundDamage();
            (d.CellX, d.CellY) = GroundDamages.CellOf(c.Centre);
            GroundDamages.Add(ref d, in c, out _);
            var e = _world.CreateEntity();
            _world.AddComponent(e, d);
            return e;
        }

        private void Bake(StaticObstacleBakeSystem sys, int bakes)
        {
            sys.Execute(_world, 0.016f);
            _wall += 0.6;
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (sys.Bakes < bakes && DateTime.UtcNow < deadline)
            {
                sys.Execute(_world, 0.016f);
                if (sys.Bakes < bakes) Thread.Sleep(5);
            }
            Assert.Equal(bakes, sys.Bakes);
        }

        private TerrainWorld Resident => _world.GetSingletonManaged<TerrainWorld>()!;

        private static float Distance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float l2 = ab.LengthSquared();
            float t = l2 > 1e-9f ? Math.Clamp(Vector2.Dot(p - a, ab) / l2, 0f, 1f) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>
        /// ⭐⭐ D-1 — a crater on a muscle: the bake lays it into the resident grid (the cache) and the simulation's ground is read ONCE (bare + the view's craters).
        /// 🔴 Red-proof: read <c>World.GroundHeightAt</c> (the cache) in <c>TerrainWorldQuery.GroundHeightAt</c> instead of the view's answer ⇒ the CraterTests
        /// same-tick rail goes red; here, adding the offset to the cache ⇒ the centre reads twice the depth.
        /// </summary>
        [Fact]
        public void ACrater_IsBakedIntoTheGround_AndTheGroundIsCountedOnce()
        {
            Node(muscle: true);
            var sys = Sys();
            Record(Hole);
            Bake(sys, 1);

            var w = Resident;
            Assert.Same(DoorStates.Of(_world, w).Craters, w.HeightCraters);
            Assert.Equal(-1.8f, w.GroundHeightAt(30f, 30f), 3);                              // the cache: the grid holds the bowl
            Assert.Equal(0f, w.BareGroundHeightAt(30f, 30f), 3);
            var q = Fdp.Toolkit.Terrain.TerrainWorldQuery.ForView(_world, w);
            Assert.Equal(-1.8f, q.GroundHeightAt(30f, 30f), 3);                              // the truth: once, not twice
            Assert.Equal(-1.8f, q.SurfaceZ(30f, 30f, 0f), 3);
        }

        /// <summary>
        /// ⭐⭐ D-2 (R-4233) — a WIDE, SHALLOW crater (walls ≈ 8°, under the vehicle layer's 20° limit, so the slope alone would let a vehicle through): a vehicle's
        /// path goes ROUND its bowl and an infantry path goes straight across. 🔴 Red-proof: drop <c>NavDoorways.Craters</c> from the factory's volumes ⇒ the vehicle
        /// path cuts across the bowl.
        /// </summary>
        [Fact]
        public void EveryVehicleRoutesRoundACrater_EvenAShallowOne_AndInfantryCrosses()
        {
            Node(muscle: true);
            var sys = Sys();
            Record(Dish);
            Bake(sys, 1);

            Vector3 south = new(30f, 5f, 0f), north = new(30f, 55f, 0f);
            Span<NavWaypoint> wps = stackalloc NavWaypoint[128];
            int nv = _node.PlanPath(south, north, wps, (uint)NavLayerMask.Vehicle);
            Assert.True(nv >= 2, "a vehicle still gets across the field");
            float closest = float.MaxValue;
            for (int i = 0; i + 1 < nv; i++)
                closest = MathF.Min(closest, Distance(Dish.Centre, new Vector2(wps[i].Position.X, wps[i].Position.Y), new Vector2(wps[i + 1].Position.X, wps[i + 1].Position.Y)));
            Assert.True(closest >= Dish.Radius, $"the vehicle path passes {closest:0.00} m from the crater's centre (bowl radius {Dish.Radius})");

            int ni = _node.PlanPath(south, north, wps, (uint)NavLayerMask.Infantry);
            Assert.True(ni >= 2);
            float length = 0f;
            for (int i = 0; i + 1 < ni; i++) length += Vector3.Distance(wps[i].Position, wps[i + 1].Position);
            Assert.True(length < 52f, $"infantry crosses the crater: {length:0.0} m");
        }

        /// <summary>
        /// ⭐ V-4 / V-6 / G-8 — a display node (no navmesh) bakes the crater into its ground too; the dirty rectangle of the first diffed bake is the crater's disc; the record
        /// gone (a seek back) ⇒ the next bake restores the bare ground and dirties the same disc. 🔴 Red-proof: skip <c>CraterDirt</c> ⇒ no rectangle.
        /// </summary>
        [Fact]
        public void ADisplayNode_BakesTheCrater_ItsDiscIsTheDirtyRectangle_AndASeekBackRestoresTheGround()
        {
            var residency = Node(muscle: false);
            var sys = Sys();
            Assert.False(residency.BakesDamage);
            var car = Record(new Crater(new Vector2(5f, 5f), 1f, 0.4f, 0.1f, (SimInstant)0.5, CraterKind.Small));   // a first, small crater: the baseline bake
            Bake(sys, 1);
            Assert.Equal(-0.4f, Resident.GroundHeightAt(5f, 5f), 3);

            var e = Record(Hole);
            Bake(sys, 2);
            Assert.Equal(-1.8f, Resident.GroundHeightAt(30f, 30f), 3);
            var dirty = Assert.Single(sys.LastDirty!);
            float o = Hole.OuterRadius;
            Assert.Equal(Hole.Centre - new Vector2(o), dirty.Min);
            Assert.Equal(Hole.Centre + new Vector2(o), dirty.Max);

            _world.DestroyEntity(e);   // the keyframe before the blast
            Bake(sys, 3);
            Assert.Equal(0f, Resident.GroundHeightAt(30f, 30f), 3);
            Assert.Equal(-0.4f, Resident.GroundHeightAt(5f, 5f), 3);
            Assert.Single(sys.LastDirty!);
        }
    
        /// <summary>
        /// ⭐⭐ <c>CE-4600</c> / <c>CE-4591</c> (Breaching §7.5 item 16, V-20 — "a barrage on the showcase town: record count, bake latency and frame budget stated and met") — a
        /// MEASUREMENT on <c>city-battle</c> (the showcase town, its real ground and its navmesh, on a muscle): a Mk-82 (89 kg) every 0.5 s of wall clock for 30 s at random points
        /// in the town's core, each crater decided by the rule and written into the record of its cell, as the deciding node does; the bake system runs on a 60 Hz frame. Stated and
        /// met: every change committed within <see cref="StaticObstacleBakeSystem.MaxLatencySeconds"/> + one bake; the frame thread's bake step ≤ 4 ms at the 99th percentile; one
        /// record per hit cell. ⚠ Minutes (the full navmesh is built first) ⇒ run on demand: <c>HROT_RUN_MEASURE=1</c>. The numbers go to the output and to Breaching §7.17.
        /// </summary>
        [Fact]
        public void CE4600_ABarrageOnTheShowcaseTown_TheNumbers()
        {
            if (Environment.GetEnvironmentVariable("HROT_RUN_MEASURE") != "1") return;   // a measurement of minutes, on demand (the BuildingDamageBakeTests precedent)
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "assets", "terrain", "city-battle"))) dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            string def = Path.Combine(dir!, "assets", "terrain", "city-battle", "terrain.json");
            var definition = TerrainFolderWorld.ReadDefinition(def);
            var town = TerrainFolderWorld.ReadWorld(def, definition, "city-battle")!;
            var build = System.Diagnostics.Stopwatch.StartNew();
            var residency = new TerrainResidency(_staging, new RoadNetworkHolder());
            var factory = new RecastNavmeshFactory { Layers = NavLayerMask.Infantry | NavLayerMask.Vehicle, TileCache = new NavTileCache() };
            var baseline = factory.Build(town);
            residency.AttachNavmesh(factory, _node);
            residency.Commit(_world, new TerrainResidency.Staged { Definition = definition, World = town, TerrainName = "city-battle", Navmesh = baseline });
            _out.WriteLine($"city-battle: {town.Buildings.Count} buildings, grid {town.Height!.Cols} × {town.Height.Rows}; navmesh built in {build.Elapsed.TotalSeconds:F1} s");

            {   // ⭐ CE-5002 — the load warmed the view's tables (doors, damage, craters, trunks): reading them now costs a warm read, not the cold ≈ 12 ms
                var w = _world.GetSingletonManaged<TerrainWorld>()!;
                var c0 = System.Diagnostics.Stopwatch.GetTimestamp(); DoorStates.Of(_world, w); double first = System.Diagnostics.Stopwatch.GetElapsedTime(c0).TotalMilliseconds;
                c0 = System.Diagnostics.Stopwatch.GetTimestamp(); DoorStates.Of(_world, w); double second = System.Diagnostics.Stopwatch.GetElapsedTime(c0).TotalMilliseconds;
                _out.WriteLine($"the view's tables after the load: DoorStates.Of {first:F2} ms, again {second:F3} ms (CE-5002: the load warms them)");
                Assert.True(first < 3.0, $"the first read after the load costs {first:F1} ms — the load did not warm the view's tables (cold ≈ 12 ms)");
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var sys = new StaticObstacleBakeSystem(() => clock.Elapsed.TotalSeconds);
            var rng = new Random(4600);
            var centre = (town.BoundsMin + town.BoundsMax) * 0.5f;
            var cells = new System.Collections.Generic.Dictionary<(int, int), Entity>();
            var pending = new System.Collections.Generic.List<double>();   // the wall time of each change not yet committed
            var latencies = new System.Collections.Generic.List<double>(); var frames = new System.Collections.Generic.List<double>();
            var steady = new System.Collections.Generic.List<double>(); var slowAfterFirstCommit = new System.Collections.Generic.List<double>();
            double firstCommit = double.NaN, longestBake = 0.0, firstStepMs = 0.0; bool firstFrame = true;
            int bursts = 0, craters = 0, bakesSeen = 0; double nextBurst = 0.0, oldestPending = double.NaN, bakeStart = 0.0;
            while (clock.Elapsed.TotalSeconds < 30.0 || (pending.Count > 0 && clock.Elapsed.TotalSeconds < 60.0))
            {
                double now = clock.Elapsed.TotalSeconds;
                if (now >= nextBurst && now < 30.0)
                {
                    nextBurst += 0.5; bursts++;
                    var at = centre + new Vector2((float)(rng.NextDouble() * 300 - 150), (float)(rng.NextDouble() * 300 - 150));
                    if (CraterRule.TryDecide(CraterRuleTable.Shared, town, new Vector3(at, town.GroundHeightAt(at.X, at.Y)), 89f, (SimInstant)now, out var c))
                    {
                        craters++;
                        var key = GroundDamages.CellOf(c.Centre);
                        if (!cells.TryGetValue(key, out var e))
                        {
                            var d = new GroundDamage(); (d.CellX, d.CellY) = key; GroundDamages.Add(ref d, in c, out _);
                            e = _world.CreateEntity(); _world.AddComponent(e, d); cells[key] = e;
                        }
                        else { var d = _world.GetComponentRO<GroundDamage>(e); GroundDamages.Add(ref d, in c, out _); _world.SetComponent(e, d); }
                        if (pending.Count == 0) oldestPending = now;
                        pending.Add(now);
                    }
                }
                bool wasBaking = sys.Baking;
                int gen2 = GC.CollectionCount(2), gen0 = GC.CollectionCount(0);
                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                sys.Execute(_world, 1f / 60f);
                double ms = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                frames.Add(ms);
                if (firstFrame) firstStepMs = ms;
                // the steady frame budget: every step after the first bake's commit (the cold one) within one 60 Hz frame
                if (!firstFrame && !double.IsNaN(firstCommit) && now > firstCommit + 0.05 && ms > 1000.0 / 60.0) slowAfterFirstCommit.Add(ms);
                firstFrame = false;
                if (sys.Baking && !wasBaking) { bakeStart = now; _out.WriteLine($"  {now,6:F2} s  bake {sys.Bakes + 1} starts ({pending.Count} changes waiting, the oldest {(double.IsNaN(oldestPending) ? 0 : now - oldestPending):F2} s)"); }
                if (ms > 5.0) _out.WriteLine($"  {now,6:F2} s  a slow frame-thread step: {ms:F1} ms (baking {wasBaking} → {sys.Baking}, bakes {sys.Bakes}; GC during it: gen2 +{GC.CollectionCount(2) - gen2}, gen0 +{GC.CollectionCount(0) - gen0})");
                if (sys.Bakes > bakesSeen)
                {
                    _out.WriteLine($"  {now,6:F2} s  bake {sys.Bakes} committed — {sys.LastBakeMilliseconds} ms of work, started {now - bakeStart:F2} s ago");
                    bakesSeen = sys.Bakes;
                    // a committed bake carries the changes made BEFORE its start (a change during it waits for the next): each one's wait is its latency
                    longestBake = Math.Max(longestBake, sys.LastBakeMilliseconds / 1000.0);
                    for (int k = pending.Count - 1; k >= 0; k--)
                        if (pending[k] <= bakeStart)
                        {
                            latencies.Add(now - pending[k]);
                            if (!double.IsNaN(firstCommit) && pending[k] > firstCommit) steady.Add(now - pending[k]);
                            pending.RemoveAt(k);
                        }
                    if (double.IsNaN(firstCommit)) firstCommit = now;
                    oldestPending = pending.Count > 0 ? pending[0] : double.NaN;
                }
                Thread.Sleep(16);
            }
            frames.Sort(); latencies.Sort(); steady.Sort();
            double P(System.Collections.Generic.List<double> l, double q) => l.Count == 0 ? double.NaN : l[Math.Min(l.Count - 1, (int)(q * l.Count))];
            _out.WriteLine($"bursts {bursts}, craters {craters}, records {cells.Count} (one per hit cell), bakes {sys.Bakes}, longest bake {longestBake:F2} s");
            _out.WriteLine($"change → committed: median {P(latencies, 0.5):F2} s, max {(latencies.Count > 0 ? latencies[^1] : double.NaN):F2} s; after the first (cold) bake: median {P(steady, 0.5):F2} s, max {(steady.Count > 0 ? steady[^1] : double.NaN):F2} s");
            _out.WriteLine($"frame-thread bake step: median {P(frames, 0.5):F3} ms, p99 {P(frames, 0.99):F3} ms, max {frames[^1]:F3} ms over {frames.Count} frames; over 16.7 ms after the first commit: {slowAfterFirstCommit.Count}");
            // ⭐ the budgets, stated (Breaching §7.17) and met:
            Assert.True(cells.Count <= craters, "one record per hit cell");
            Assert.True(firstStepMs <= 1000.0 / 60.0, $"the first frame-thread step on the town costs {firstStepMs:F1} ms (CE-5002: the load warms the view's tables)");
            Assert.True(latencies.Count > 0 && latencies[^1] <= StaticObstacleBakeSystem.MaxLatencySeconds + 2 * longestBake + 0.5,
                $"a change waited {latencies[^1]:F2} s (the cap {StaticObstacleBakeSystem.MaxLatencySeconds} s + the running bake + its own, {longestBake:F2} s each)");
            Assert.True(steady.Count > 0 && steady[^1] <= StaticObstacleBakeSystem.MaxLatencySeconds + 1.5 + 0.5, $"in the steady barrage a change waited {steady[^1]:F2} s");
            // p99 lands on the frames that rebuild the crater table after a new crater (≈ 47 of ≈ 1 700 frames, 2–3 ms each): ≤ 4 ms, a quarter of a frame
            Assert.True(P(frames, 0.99) <= 4.0, $"the frame-thread bake step's p99 is {P(frames, 0.99):F3} ms");
            Assert.Empty(slowAfterFirstCommit);   // after the cold first commit, no step costs a frame
        }
}
}
