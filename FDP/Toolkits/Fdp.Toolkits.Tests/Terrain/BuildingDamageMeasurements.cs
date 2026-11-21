#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Replication.Components;
using Xunit;
using Xunit.Abstractions;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ <c>CE-4391</c> S-5 (UTM §8.7) — the NUMBERS of destructible buildings, measured in-process on demo-city (800 m, 136 enterable buildings). Not rails: they print figures. Skipped unless
    /// <c>HROT_RUN_MEASURE=1</c>; run with <c>HROT_RUN_MEASURE=1 dotnet test --no-build --filter FullyQualifiedName~BuildingDamageMeasurements --logger "console;verbosity=detailed"</c>.
    /// ⚠ This VM (4 cores, shared) is not scale-1 and not Release: the figures are an order of magnitude, the scale-1 run is backend's Windows machine's (UTM §8.7).
    /// </summary>
    public sealed class BuildingDamageMeasurements
    {
        private readonly ITestOutputHelper _o;
        public BuildingDamageMeasurements(ITestOutputHelper o) => _o = o;
        private static bool On => Environment.GetEnvironmentVariable("HROT_RUN_MEASURE") == "1";

        private static TerrainWorld DemoCity()
        {
            string folder = System.IO.Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!), "demo-city");
            return TerrainWorldParser.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "demo-city.world.geojson")), "demo-city", TerrainAssets.ForFolder(folder));
        }

        /// <summary>A passage on the longest ground-storey panel of <paramref name="building"/> (floor to 2.1 m, 1.2 m wide, in the middle).</summary>
        private static WallBreach PassageOf(TerrainWorld w, int building)
        {
            int best = -1; float len = 0;
            for (int i = 0; i < w.Panels.Count; i++)
                if (w.Panels[i].Building == building && w.Panels[i].Storey == 0 && w.Panels[i].Length > len) { best = i; len = w.Panels[i].Length; }
            var p = w.Panels[best];
            return new WallBreach(best, len * 0.5f - 0.6f, len * 0.5f + 0.6f, p.BaseZ, p.BaseZ + 2.1f, BreachKind.Passage);
        }

        private static List<int> BuildingsWithPanels(TerrainWorld w)
            => Enumerable.Range(0, w.Buildings.Count).Where(b => w.Panels.Any(p => p.Building == b && p.Storey == 0)).ToList();

        private static (double Ns, long Bytes) PerCall(int n, Action call)
        {
            for (int i = 0; i < 1000; i++) call();
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++) call();
            sw.Stop();
            return (sw.Elapsed.TotalMilliseconds * 1e6 / n, (GC.GetAllocatedBytesForCurrentThread() - b0) / n);
        }

        /// <summary>BuildingDamageTable.Of (and DoorStates.Of with the table) per call, warm and after a write; S-1's open question.</summary>
        [Fact]
        public void M1_TheTableViewPerCall()
        {
            if (!On) return;
            var w = DemoCity();
            var buildings = BuildingsWithPanels(w);
            using var repo = BreachFixtures.World(w);
            repo.RegisterComponent<DoorState>();
            // 10 buildings hit five times each (50 breaches), three of them collapsed
            var entities = new List<Entity>();
            foreach (int b in buildings.Take(10))
            {
                var pb = Enumerable.Range(0, w.Panels.Count).Where(i => w.Panels[i].Building == b && w.Panels[i].Storey == 0).Take(5).ToArray();
                var e = BreachFixtures.Damage(repo, w, b, pb.Select(i => new WallBreach(i, 0.5f, 1.7f, 0f, 2.1f, BreachKind.Passage)).ToArray());
                entities.Add(e);
            }
            for (int k = 0; k < 3; k++) { var d = repo.GetComponentRO<BuildingDamage>(entities[k]); d.Collapsed = 1; repo.SetComponent(entities[k], d); }
            repo.Tick();   // a NEW frame: the setup's writes are in the past (R-220: a table is rebuilt in the frame of a write, then held)
            var table = BuildingDamageTable.Of(repo, w)!;
            repo.Tick();
            BuildingDamageTable.Of(repo, w);
            _o.WriteLine($"M1 table: {table.Count} breaches, {table.CollapsedCount} collapsed, {entities.Count} entities");
            var warm = PerCall(1_000_000, () => BuildingDamageTable.Of(repo, w));
            _o.WriteLine($"M1 BuildingDamageTable.Of warm: {warm.Ns:0.0} ns/call, {warm.Bytes} B/call");
            var doors = PerCall(1_000_000, () => DoorStates.Of(repo, w));
            _o.WriteLine($"M1 DoorStates.Of (with the table) warm: {doors.Ns:0.0} ns/call, {doors.Bytes} B/call");
            // a segment trace through the world with the table on: the per-query cost a breach adds
            var from = new Vector3(300f, 300f, 1.2f); var to = new Vector3(330f, 300f, 1.2f);
            var ds = DoorStates.Of(repo, w);
            var trace = PerCall(100_000, () => w.SegmentBlocked(from, to, ds));
            using var clean = BreachFixtures.World(w); clean.RegisterComponent<DoorState>(); clean.Tick();
            var ds0 = DoorStates.Of(clean, w);
            var trace0 = PerCall(100_000, () => w.SegmentBlocked(from, to, ds0));
            _o.WriteLine($"M1 SegmentBlocked (30 m): with the 50-breach table {trace.Ns:0} ns/call, {trace.Bytes} B; with no damage {trace0.Ns:0} ns/call, {trace0.Bytes} B");
            // the rebuild after a write: a new breach on one entity, then Of
            var e0 = entities[5]; var d0 = repo.GetComponentRO<BuildingDamage>(e0);
            var sw = Stopwatch.StartNew(); long b0 = GC.GetAllocatedBytesForCurrentThread();
            const int writes = 2000;
            for (int i = 0; i < writes; i++) { d0.Collapsed = (byte)(i & 1); repo.SetComponent(e0, d0); BuildingDamageTable.Of(repo, w); }
            sw.Stop();
            _o.WriteLine($"M1 BuildingDamageTable.Of after a write (a rebuild of {table.Count} breaches): {sw.Elapsed.TotalMilliseconds * 1000 / writes:0.0} us, {(GC.GetAllocatedBytesForCurrentThread() - b0) / writes} B");
        }

        private static (RecastNavmeshFactory Factory, int Building) Pick(TerrainWorld w) => (new RecastNavmeshFactory { Layers = NavLayerMask.Infantry, TileCache = new NavTileCache() }, BuildingsWithPanels(w).First(b => w.Buildings[b].Label!.StartsWith("house-1f")));

        private static BuildingDamageTable TableOf(TerrainWorld w, Action<EntityRepository> damage)
        {
            var repo = BreachFixtures.World(w);
            damage(repo);
            return DoorStates.Of(repo, w).Damage!;
        }

        /// <summary>Rebake seconds for one building's tiles with the tile cache warm; how many tiles move.</summary>
        [Fact]
        public void M2_TheRebakeOfOneBuilding()
        {
            if (!On) return;
            var w = DemoCity();
            var (factory, b) = Pick(w);
            var node = new SwitchableNavmeshProvider();
            var sw = Stopwatch.StartNew();
            node.Publish(factory.Build(w)); sw.Stop();
            _o.WriteLine($"M2 base bake, cache cold: {sw.Elapsed.TotalSeconds:0.00} s");
            sw.Restart(); node.Publish(factory.Build(w)); sw.Stop();
            _o.WriteLine($"M2 the same world again, cache warm: {sw.Elapsed.TotalSeconds:0.00} s");
            uint seq0 = node.ChangeSequence;
            int tiles = (int)Math.Ceiling(800f / node.TileMetres);

            foreach (var (name, table) in new[]
            {
                ("one passage", TableOf(w, r => BreachFixtures.Damage(r, w, b, PassageOf(w, b)))),
                ("a collapse", TableOf(w, r => BreachFixtures.Collapsed(r, w, b))),
            })
            {
                sw.Restart();
                var derived = TerrainObstacles.With(w, Array.Empty<TerrainPrism>(), table);
                double overlayMs = sw.Elapsed.TotalMilliseconds;
                var mesh = factory.Build(derived);
                sw.Stop();
                uint before = node.ChangeSequence;
                node.Publish(mesh);
                int moved = 0;
                for (int tx = 0; tx < tiles; tx++) for (int tz = 0; tz < tiles; tz++) if (node.TileVersion(tx, tz) > before) moved++;
                _o.WriteLine($"M2 {name} ({w.Buildings[b].Label}): overlay {overlayMs:0.0} ms, overlay + bake (cache warm) {sw.Elapsed.TotalSeconds:0.00} s, tiles whose version moved {moved} of {tiles * tiles}");
                node.Publish(factory.Build(w));   // back to the base for the next one
            }
            _ = seq0;
        }

        /// <summary>The replans one passage breach causes among N men on paths across the city; and the per-frame stamp check for N men.</summary>
        [Fact]
        public void M3_TheReplansOfOnePassage()
        {
            if (!On) return;
            var w = DemoCity();
            var (factory, _) = Pick(w);
            var node = new SwitchableNavmeshProvider();
            node.Publish(factory.Build(w));
            uint planned = node.ChangeSequence;
            var rnd = new Random(42);
            var pool = new TrajectoryPoolManager();
            var paths = new List<CustomTrajectory>();
            Span<NavWaypoint> wps = stackalloc NavWaypoint[256];
            var mesh = node;   // SwitchableNavmeshProvider is the node's INavmeshProvider
            double totalLen = 0;
            int attempts = 0;
            while (paths.Count < 1000 && attempts++ < 20000)
            {
                var a = new Vector3(20f + (float)rnd.NextDouble() * 760f, 20f + (float)rnd.NextDouble() * 760f, 0f);
                var d = Vector2.Normalize(new Vector2((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f)) * (100f + (float)rnd.NextDouble() * 200f);
                var b = new Vector3(Math.Clamp(a.X + d.X, 20f, 780f), Math.Clamp(a.Y + d.Y, 20f, 780f), 0f);
                if (!mesh.IsWalkable(a) || !mesh.IsWalkable(b)) continue;
                int n = mesh.PlanPath(a, b, wps, (uint)NavLayerMask.Infantry);
                if (n < 2) continue;
                var pts = new Vector3[n];
                for (int i = 0; i < n; i++) pts[i] = wps[i].Position;
                int id = pool.RegisterTrajectory(pts);
                if (pool.TryGetTrajectory(id, out var t)) { paths.Add(t); for (int i = 1; i < n; i++) totalLen += Vector3.Distance(pts[i - 1], pts[i]); }
            }
            _o.WriteLine($"M3 {paths.Count} men on {paths.Count} planned paths across demo-city, mean length {totalLen / Math.Max(1, paths.Count):0} m ({attempts} attempts)");

            var houses = BuildingsWithPanels(w).Where(b => w.Buildings[b].Label!.StartsWith("house") || w.Buildings[b].Label!.StartsWith("shop")).Take(12).ToList();
            var counts = new List<int>();
            foreach (int b in houses)
            {
                var table = TableOf(w, r => BreachFixtures.Damage(r, w, b, PassageOf(w, b)));
                node.Publish(factory.Build(TerrainObstacles.With(w, Array.Empty<TerrainPrism>(), table)));
                int stale = 0;
                for (int i = 0; i < paths.Count; i++) { var p = paths[i]; if (NavigationExecutionSystem.CrossesChangedTile(in p, 0f, node, planned)) stale++; }
                counts.Add(stale);
                node.Publish(factory.Build(w)); planned = node.ChangeSequence;   // back to the base; the men are re-stamped
            }
            _o.WriteLine($"M3 replans per passage breach (12 buildings, {paths.Count} men moving): {string.Join(", ", counts)}; mean {counts.Average():0.0}, max {counts.Max()}");
            var all = paths.ToArray();
            var stampCheck = PerCall(2000, () => { int s = 0; for (int i = 0; i < all.Length; i++) if (NavigationExecutionSystem.CrossesChangedTile(in all[i], 0f, node, planned)) s++; });
            _o.WriteLine($"M3 CrossesChangedTile over {all.Length} paths with nothing changed: {stampCheck.Ns / 1000:0.0} us per frame, {stampCheck.Bytes} B (the per-entity gate is one integer compare; this is the walk itself)");
        }

        /// <summary>A barrage frame: BuildingDamageSystem with no event (steady), and with a breach landing on an entity that exists.</summary>
        [Fact]
        public void M4_TheBarrageFrame()
        {
            if (!On) return;
            var w = DemoCity();
            using var repo = new EntityRepository();
            repo.RegisterComponent<BuildingDamage>(); repo.RegisterComponent<NetworkAuthority>(); repo.RegisterComponent<DoorState>();
            repo.RegisterManagedComponent<TerrainObjectKey>(); repo.RegisterEvent<BuildingDamageEvent>();
            repo.SetSingletonManaged(w);
            var buildings = BuildingsWithPanels(w);
            var entities = new Dictionary<int, Entity>();
            foreach (int b in buildings.Take(60)) entities[b] = BreachFixtures.Damage(repo, w, b);
            var sys = new BuildingDamageSystem();
            repo.Tick();
            var steady = PerCall(100_000, () => { repo.Bus.SwapBuffers(); sys.Execute(repo, 0.016f); });
            _o.WriteLine($"M4 BuildingDamageSystem, a frame with no breach: {steady.Ns / 1000:0.00} us, {steady.Bytes} B");

            var rnd = new Random(7);
            int count = 0;
            var panelsOf = new Dictionary<int, int[]>();
            foreach (int b in buildings.Take(60)) panelsOf[b] = Enumerable.Range(0, w.Panels.Count).Where(i => w.Panels[i].Building == b && w.Panels[i].Storey == 0).ToArray();
            double worst = 0, sum = 0; long alloc = 0;
            const int frames = 3000;   // 3000 breaches on 60 buildings ~ a barrage far above 'tens a minute'
            for (int f = 0; f < frames; f++)
            {
                int b = buildings[rnd.Next(60)];
                var panels = panelsOf[b];
                int p = panels[rnd.Next(panels.Length)];
                float len = w.Panels[p].Length, at = (float)rnd.NextDouble() * Math.Max(0.1f, len - 1.5f);
                repo.Bus.Publish(new BuildingDamageEvent { Building = b, Panel = p, From = at, To = at + 1.2f, Bottom = 0f, Top = 2.1f, Kind = (byte)BreachKind.Passage });
                repo.Bus.SwapBuffers();
                repo.Tick();
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                var sw = Stopwatch.StartNew();
                sys.Execute(repo, 0.016f);
                BuildingDamageTable.Of(repo, w);   // what the next reader pays
                sw.Stop();
                alloc += GC.GetAllocatedBytesForCurrentThread() - a0;
                double ms = sw.Elapsed.TotalMilliseconds; sum += ms; worst = Math.Max(worst, ms); count++;
                if (f > 0 && f % 200 == 0) foreach (var (_, e) in entities) { var d = repo.GetComponentRO<BuildingDamage>(e); d.Count = 0; repo.SetComponent(e, d); }   // keep the entities from filling
            }
            _o.WriteLine($"M4 a breach frame (event -> entity write -> the table rebuilt by the first reader): mean {sum / count * 1000:0.0} us, worst {worst:0.00} ms, {alloc / count} B per breach frame");
        }
    }
}
