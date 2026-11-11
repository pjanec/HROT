using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4391</c> S-3 / R-4196 (docs/DESIGN_Terrain_Import_UTM.md §8.2 B-10, B-12) — <see cref="StaticObstacleBakeSystem"/> follows the building damage: a PASSAGE or a COLLAPSE goes into
    /// its input hash and is baked through the SAME debounced, one-running-plus-one-pending, off-thread bake (no second requester); a HOLE never triggers one; and a node with NO navmesh (a display node)
    /// never lays damage over its terrain — "no querying or modification allowed" (user).
    /// </summary>
    public sealed class BuildingDamageBakeTests : IDisposable
    {
        private const string House = """
            { "type": "FeatureCollection", "hrot": { "schemaVersion": 1, "bounds": [0, 0, 60, 60], "groundZ": 0 }, "features": [ { "type": "Feature",
                "properties": { "kind": "building", "label": "H",
                  "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                    "storeys": [ { "height": 3, "walls": [
                      { "from": [0,0], "to": [10,0], "thickness": 0.3 }, { "from": [10,0], "to": [10,8], "thickness": 0.3 },
                      { "from": [10,8], "to": [0,8], "thickness": 0.3 }, { "from": [0,8], "to": [0,0], "thickness": 0.3 } ] } ] } },
                "geometry": { "type": "Point", "coordinates": [20, 20] } } ] }
            """;

        private readonly EntityRepository _world = new();
        private readonly string _staging = Path.Combine(Path.GetTempPath(), "s3-" + Guid.NewGuid().ToString("N"));
        private readonly TerrainWorld _house = TerrainWorldParser.Parse(House, "range");
        private readonly SwitchableNavmeshProvider _node = new();
        private double _wall;
        private Entity _damage;

        public BuildingDamageBakeTests()
        {
            SimHostComponentRegistry.RegisterAll(_world);
            Directory.CreateDirectory(_staging);
        }

        public void Dispose()
        {
            _world.Dispose();
            try { Directory.Delete(_staging, recursive: true); } catch { }
        }

        private StaticObstacleBakeSystem Sys() => new(() => _wall);

        private sealed class SlowFactory : INavmeshFactory
        {
            private readonly INavmeshFactory _inner; private readonly int _ms;
            public SlowFactory(INavmeshFactory inner, int ms) { _inner = inner; _ms = ms; }
            public INavmeshProvider? Build(TerrainWorld world) { Thread.Sleep(_ms); return _inner.Build(world); }
        }

        private static RecastNavmeshFactory Recast() => new() { Layers = NavLayerMask.Infantry, TileCache = new NavTileCache() };

        /// <summary>A node holding the house terrain; with <paramref name="muscle"/> it also holds a navmesh factory (the muscle's), baked once over the whole house.</summary>
        private TerrainResidency Node(bool muscle, int bakeMs = 0)
        {
            var residency = new TerrainResidency(_staging, new RoadNetworkHolder());
            INavmeshProvider? baseline = null;
            if (muscle)
            {
                INavmeshFactory factory = Recast();
                baseline = factory.Build(_house);   // what the terrain load baked: the commit publishes it
                residency.AttachNavmesh(bakeMs > 0 ? new SlowFactory(factory, bakeMs) : factory, _node);
            }
            residency.Commit(_world, new TerrainResidency.Staged { Definition = new TerrainDefinition { Name = "range" }, World = _house, TerrainName = "range", Navmesh = baseline });
            Assert.True(_world.HasSingletonManaged<StaticObstacleBakery>());
            Assert.Equal(muscle, residency.BakesDamage);
            return residency;
        }

        private int Panel(Func<TerrainWallPanel, bool> pick) => Array.FindIndex(_house.Panels.ToArray(), p => pick(p));
        private int South() => Panel(p => MathF.Abs(p.A.Y - 20f) < 1e-3f && MathF.Abs(p.B.Y - 20f) < 1e-3f);
        private int North() => Panel(p => MathF.Abs(p.A.Y - 28f) < 1e-3f && MathF.Abs(p.B.Y - 28f) < 1e-3f);

        /// <summary>Adds <paramref name="b"/> to this node's damage entity (made on first use) — what <c>BuildingDamageSystem</c> does, written here directly.</summary>
        private void Damage(WallBreach b)
        {
            if (_damage.IsNull)
            {
                _damage = _world.CreateEntity();
                _world.AddComponent(_damage, new BuildingDamage { Building = 0 });
                _world.SetManagedComponent(_damage, new TerrainObjectKey { Key = "range/H" });
            }
            var d = _world.GetComponentRO<BuildingDamage>(_damage);
            BuildingDamages.Add(ref d, in b, _house);
            _world.SetComponent(_damage, d);
        }

        private void Collapse()
        {
            _damage = _world.CreateEntity();
            _world.AddComponent(_damage, new BuildingDamage { Building = 0, Collapsed = 1 });
            _world.SetManagedComponent(_damage, new TerrainObjectKey { Key = "range/H" });
        }

        private void RunUntilBaked(StaticObstacleBakeSystem sys, int bakes)
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (sys.Bakes < bakes && DateTime.UtcNow < deadline)
            {
                sys.Execute(_world, 0.016f);
                if (sys.Bakes < bakes) Thread.Sleep(5);
            }
            Assert.Equal(bakes, sys.Bakes);
        }

        private static TerrainPrism Car(float x) => TerrainObstacles.PrismOf(new Vector3(x, 40f, 0f), 0f, new ObstacleShape { Length = 4.5f, Width = 1.8f, Height = 1.5f }, TerrainMaterialLibrary.Shared.Get("concrete", "t"), "obstacle:1");

        /// <summary>A node whose residency committed the baseline load bake of the house terrain by the SAME factory (its serial recorded, as <c>Prepare</c> does).</summary>
        private (TerrainResidency Residency, RecastNavmeshFactory Factory) IncrementalNode()
        {
            var residency = new TerrainResidency(_staging, new RoadNetworkHolder());
            var factory = Recast();
            var baseline = factory.Build(_house);
            residency.AttachNavmesh(factory, _node);
            residency.Commit(_world, new TerrainResidency.Staged { Definition = new TerrainDefinition { Name = "range" }, World = _house, TerrainName = "range", Navmesh = baseline, BakeSerial = factory.BakeSerial });
            return (residency, factory);
        }

        /// <summary>
        /// ⭐ O-7 A (CE-4418, §18e A-2) — the residency's dirty region for a MOVED obstacle covers its OLD and its NEW place; the bake that uses it reuses tiles (and the result still has the new wall); a changed base
        /// (a bake begun on the previous base version) and a node with no committed baseline give NO region (a full bake). 🔴 Red-proof: leave out the removed prisms' bounds ⇒ the old place is missing;
        /// ignore the base version ⇒ the stale-base case returns a region.
        /// </summary>
        [Fact]
        public void O7A_AMovedObstacle_DirtiesItsOldAndNewPlace_AndABaseChangeDirtiesNothingKnown()
        {
            var (residency, factory) = IncrementalNode();
            var none = new System.Collections.Generic.Dictionary<int, ulong>();
            var first = residency.BakeObstacles(new[] { Car(10f) });
            Assert.True(residency.CommitObstacles(_world, first));

            var moved = Car(12f);
            var regions = residency.DirtyRegions(new[] { moved }, none, _house, residency.BaseVersion, out bool diffed);
            Assert.True(diffed);
            var old = Car(10f);
            Assert.Contains(regions, r => r.Min == old.Min && r.Max == old.Max);       // where it was
            Assert.Contains(regions, r => r.Min == moved.Min && r.Max == moved.Max);  // where it is
            Assert.Equal(2, regions.Count);

            var second = residency.BakeObstacles(new[] { moved });
            Assert.True(factory.LastStats.Reused > 0, "tiles outside the two boxes were reused");
            Assert.True(residency.CommitObstacles(_world, second));
            Assert.Contains(_world.GetSingletonManaged<TerrainWorld>()!.Prisms, p => p.Min == moved.Min);

            // an unchanged set: nothing is dirty (and every tile is reused)
            Assert.Empty(residency.DirtyRegions(new[] { moved }, none, _house, residency.BaseVersion, out _));

            // a base change: a bake begun on the old version has no region; neither has a residency with no baseline yet
            int stale = residency.BaseVersion;
            residency.Commit(_world, new TerrainResidency.Staged { Definition = new TerrainDefinition { Name = "range" }, World = _house, TerrainName = "range" });
            residency.DirtyRegions(new[] { moved }, none, _house, stale, out bool afterBase);
            Assert.False(afterBase);
            var fresh = new TerrainResidency(_staging, new RoadNetworkHolder());
            fresh.DirtyRegions(new[] { moved }, none, _house, fresh.BaseVersion, out bool firstEver);
            Assert.False(firstEver);
        }

        private static readonly Vector3 Outside = new(25f, 10f, 0f), Inside = new(25f, 24f, 0f);

        /// <summary>
        /// ⭐ A passage becomes WALKABLE after the bake COMMITS — the inside was an island, then the node's navmesh lets a path in, and the node's terrain holds the gap. 🔴 Red-proof: drop the
        /// <c>navHash</c> from the signature in <c>StaticObstacleBakeSystem.Execute</c> ⇒ no bake runs and the inside stays an island.
        /// </summary>
        [Fact]
        public void APassage_BecomesWalkableAfterTheBakeCommits_AndAPathUsesIt()
        {
            Node(muscle: true);
            var sys = Sys();
            Assert.False(_node.PathExists(Outside, Inside, (uint)NavLayerMask.Infantry));
            Assert.True(_world.GetSingletonManaged<TerrainWorld>()!.SegmentBlocked(new Vector3(25f, 15f, 1f), new Vector3(25f, 25f, 1f)));

            Damage(new WallBreach(South(), 4f, 6.2f, 0f, 2.2f, BreachKind.Passage));
            sys.Execute(_world, 0.016f);
            _wall = 0.6; sys.Execute(_world, 0.016f);
            Assert.False(_node.PathExists(Outside, Inside, (uint)NavLayerMask.Infantry), "not before the bake commits");
            RunUntilBaked(sys, 1);

            Assert.True(_node.PathExists(Outside, Inside, (uint)NavLayerMask.Infantry));
            Span<NavWaypoint> wps = stackalloc NavWaypoint[64];
            Assert.True(_node.PlanPath(Outside, Inside, wps, (uint)NavLayerMask.Infantry) >= 2);
            Assert.False(_world.GetSingletonManaged<TerrainWorld>()!.SegmentBlocked(new Vector3(25f, 15f, 1f), new Vector3(25f, 25f, 1f)));   // the resident world has the gap
        }

        /// <summary>
        /// ⭐ CE-3436 (HOT PATH) — on a node that BAKES DAMAGE (the muscle's) a steady frame — an obstacle present, a passage breach laid, the bake committed, nothing changing — allocates NOTHING: neither the obstacle walk
        /// nor <c>DoorStates.Of</c> / the damage hash. 🔴 Red-proof: restore the per-frame <c>CollectPrisms</c> in <c>StaticObstacleBakeSystem.Execute</c>.
        /// </summary>
        [Fact]
        public void CE3436_ASteadyFrame_OnANodeThatBakesDamage_AllocatesNothing()
        {
            Node(muscle: true);
            var sys = Sys();
            var car = _world.CreateEntity();
            _world.AddComponent(car, new SimTransform { Position = new Vector3(40f, 40f, 0f), Rotation = Quaternion.Identity });
            _world.AddComponent(car, new StaticObstacle { Material = new FixedString32("car-body") });
            _world.AddComponent(car, new ObstacleShape { Length = 4.5f, Width = 1.8f, Height = 1.5f });
            Damage(new WallBreach(South(), 4f, 6.2f, 0f, 2.2f, BreachKind.Passage));
            sys.Execute(_world, 0.016f);
            _wall = 0.6; sys.Execute(_world, 0.016f);
            RunUntilBaked(sys, 1);
            for (int i = 0; i < 30; i++) sys.Execute(_world, 0.016f);   // warm: the caches and the buffers have grown
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) sys.Execute(_world, 0.016f);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(1, sys.Bakes);   // and nothing re-baked
        }

        /// <summary>
        /// ⭐ CE-4391 S-4 (Q-D3) — a scenario that STARTS pre-breached: the saved damage (read from the scenario text, made into entities the way the load does) is in the world before the bake system's first
        /// tick, and the muscle's navmesh follows it with no further change — the inside is reachable after the first bake. 🔴 Red-proof: pass no damage to the bake (<c>damage = null</c>) in
        /// <c>StaticObstacleBakeSystem.Execute</c> ⇒ no bake runs for a loaded world.
        /// </summary>
        [Fact]
        public void AScenarioLoadedPreBreached_IsBakedIntoTheMusclesNavmesh()
        {
            Node(muscle: true);
            var sys = Sys();
            Assert.False(_node.PathExists(Outside, Inside, (uint)NavLayerMask.Infantry));
            var saved = TerrainObjectsSection.ReadBuildingDamage(
                $"{{ \"TerrainObjects\": {{ \"range/H\": {{ \"breaches\": [[{BuildingPanelIndex.ToLocal(_house, South())}, 4, 6.2, 0, 2.2, \"passage\"]] }} }} }}");
            foreach (var (key, building, entities) in BuildingDamageLoad.Resolve(_house, saved, null))
                foreach (var d in entities)
                {
                    var e = _world.CreateEntity();
                    _world.AddComponent(e, d);
                    _world.SetManagedComponent(e, new TerrainObjectKey { Key = key });
                }
            sys.Execute(_world, 0.016f);
            _wall = 0.6; sys.Execute(_world, 0.016f);
            RunUntilBaked(sys, 1);
            Assert.True(_node.PathExists(Outside, Inside, (uint)NavLayerMask.Infantry));
        }

        /// <summary>
        /// ⭐ CE-4391 S-4 (R-4012, AAR seek) — on a muscle-style node (a navmesh) an AAR SEEK restores a keyframe into the LIVE world (<c>PlaybackSystem.ApplyFrame</c>: clear, then a raw blit): the damage the
        /// keyframe holds changes the NavHash, so <c>StaticObstacleBakeSystem</c> (no replay guard: <c>EveryFrame</c>, BeforeSync) REBAKES — the navmesh matches the moment shown. B (a passage) → A (none) → B:
        /// the inside is reachable, then not, then reachable again, one bake per seek. 🔴 Red-proof: pass no damage to the bake (<c>damage = null</c>) ⇒ the first bake never opens the wall.
        /// </summary>
        [Fact]
        public void ASeekIntoALiveWorld_MakesTheMusclesNavmeshFollowTheKeyframesDamage()
        {
            Node(muscle: true);
            var sys = Sys();
            byte[] Keyframe(bool damaged)
            {
                using var src = new EntityRepository();
                SimHostComponentRegistry.RegisterAll(src);
                if (damaged)
                {
                    var e = src.CreateEntity();
                    var d = new BuildingDamage { Building = 0 };
                    BuildingDamages.Add(ref d, new WallBreach(South(), 4f, 6.2f, 0f, 2.2f, BreachKind.Passage), _house);
                    src.AddComponent(e, d);
                    src.SetManagedComponent(e, new TerrainObjectKey { Key = "range/H" });
                }
                using var ms = new MemoryStream();
                using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) new Fdp.Core.FlightRecorder.RecorderSystem().RecordKeyframe(src, bw, damaged ? 20L : 10L);
                return ms.ToArray();
            }
            byte[] a = Keyframe(false), b = Keyframe(true);
            var playback = new Fdp.Core.FlightRecorder.PlaybackSystem();
            void Seek(byte[] k) { using var br = new BinaryReader(new MemoryStream(k)); playback.ApplyFrame(_world, br, processEvents: false); }
            bool Reachable() => _node.PathExists(Outside, Inside, (uint)NavLayerMask.Infantry);

            Assert.False(Reachable());
            Seek(b); sys.Execute(_world, 0.016f); _wall += 0.6; sys.Execute(_world, 0.016f); RunUntilBaked(sys, 1);
            Assert.True(Reachable());
            Seek(a); sys.Execute(_world, 0.016f); _wall += 0.6; sys.Execute(_world, 0.016f); RunUntilBaked(sys, 2);
            Assert.False(Reachable());
            Seek(b); sys.Execute(_world, 0.016f); _wall += 0.6; sys.Execute(_world, 0.016f); RunUntilBaked(sys, 3);
            Assert.True(Reachable());
        }

        /// <summary>⭐ CE-4391 S-5 (UTM §8.7) — a MEASUREMENT, not a rail (skipped unless <c>HROT_RUN_MEASURE=1</c>): what <c>StaticObstacleBakeSystem.Execute</c> costs on a steady frame of a muscle node, with and without baked damage.</summary>
        [Fact]
        public void M5_TheBakeSystemsSteadyFrame()
        {
            if (Environment.GetEnvironmentVariable("HROT_RUN_MEASURE") != "1") return;
            Node(muscle: true);
            var sys = Sys();
            double Frame(out long bytes)
            {
                for (int i = 0; i < 2000; i++) sys.Execute(_world, 0.016f);
                _world.Tick();
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                const int n = 20000;
                for (int i = 0; i < n; i++) sys.Execute(_world, 0.016f);
                sw.Stop();
                bytes = (GC.GetAllocatedBytesForCurrentThread() - b0) / n;
                return sw.Elapsed.TotalMilliseconds * 1000 / n;
            }
            double us0 = Frame(out long b0_);
            Damage(new WallBreach(South(), 4f, 6.2f, 0f, 2.2f, BreachKind.Passage));
            sys.Execute(_world, 0.016f); _wall = 0.6; sys.Execute(_world, 0.016f);
            RunUntilBaked(sys, 1);
            double us1 = Frame(out long b1_);
            Console.WriteLine($"M5 StaticObstacleBakeSystem steady frame: no damage {us0:0.00} us / {b0_} B; with one baked passage {us1:0.00} us / {b1_} B");
            Assert.True(us0 >= 0 && us1 >= 0);
        }

        /// <summary>
        /// ⭐ Ten passages inside ONE debounce give ONE bake (and none after it): each change moves the hash and restarts the quiet period, the bake runs once for all of them. 🔴 Red-proof: drop the
        /// quiet-period restart (<c>_seenAt = now</c>) ⇒ more than one bake.
        /// </summary>
        [Fact]
        public void TenPassagesInsideOneDebounce_GiveOneBake()
        {
            Node(muscle: true);
            var sys = Sys();
            int south = South(), north = North();
            for (int i = 0; i < 5; i++)
            {
                Damage(new WallBreach(south, 0.3f + 2f * i, 1.3f + 2f * i, 0f, 2f, BreachKind.Passage));
                _wall += 0.2; sys.Execute(_world, 0.016f); _wall += 0.2; sys.Execute(_world, 0.016f);   // new hash, then the same hash again: quiet 0.2 s < the 0.5 s debounce
                Damage(new WallBreach(north, 0.3f + 2f * i, 1.3f + 2f * i, 0f, 2f, BreachKind.Passage));
                _wall += 0.2; sys.Execute(_world, 0.016f); _wall += 0.2; sys.Execute(_world, 0.016f);
            }
            Assert.Equal(0, sys.Bakes);
            Assert.False(sys.Baking);
            Assert.Equal(10, _world.GetComponentRO<BuildingDamage>(_damage).Count);

            _wall += 1.0;
            RunUntilBaked(sys, 1);
            for (int i = 0; i < 10; i++) { _wall += 1.0; sys.Execute(_world, 0.016f); }
            Assert.Equal(1, sys.Bakes);
            Assert.False(sys.Baking);
        }

        /// <summary>A HOLE changes no walkable: it never moves the hash, so it never triggers a bake. 🔴 Red-proof: hash holes too (drop the <c>Kind</c> test in <c>ComputeNavHash</c>) ⇒ a bake runs.</summary>
        [Fact]
        public void AHole_ProducesNoBake()
        {
            Node(muscle: true);
            var sys = Sys();
            var before = _world.GetSingletonManaged<TerrainWorld>();
            for (int i = 0; i < 6; i++) Damage(new WallBreach(South(), i * 1.2f, i * 1.2f + 0.6f, 1f, 1.6f, BreachKind.Hole));
            for (int i = 0; i < 12; i++) { _wall += 1.0; sys.Execute(_world, 0.016f); }
            Assert.Equal(0, sys.Bakes);
            Assert.False(sys.Baking);
            Assert.Same(before, _world.GetSingletonManaged<TerrainWorld>());
        }

        /// <summary>
        /// ⭐ The swap never runs on the frame: the <c>Execute</c> that STARTS a (400 ms) bake returns at once with the bake running and the world and the navmesh untouched; the commit comes on a later
        /// <c>Execute</c>. 🔴 Red-proof: run the bake synchronously in <c>Execute</c> (<c>.Wait()</c> on the task) ⇒ that call takes ≥ 400 ms.
        /// </summary>
        [Fact]
        public void TheBakeRunsOffTheFrame_TheCallThatStartsItReturnsAtOnce()
        {
            Node(muscle: true, bakeMs: 400);
            var sys = Sys();
            var world0 = _world.GetSingletonManaged<TerrainWorld>();
            uint version0 = _node.QueryVersion();
            Damage(new WallBreach(South(), 4f, 6.2f, 0f, 2.2f, BreachKind.Passage));
            sys.Execute(_world, 0.016f);
            _wall = 0.6;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            sys.Execute(_world, 0.016f);   // the debounce has elapsed: this call starts the bake
            long ms = sw.ElapsedMilliseconds;
            Assert.True(sys.Baking, "the bake was started");
            Assert.True(ms < 150, $"the frame that starts a bake must not wait for it ({ms} ms)");
            Assert.Same(world0, _world.GetSingletonManaged<TerrainWorld>());
            Assert.Equal(version0, _node.QueryVersion());
            RunUntilBaked(sys, 1);
            Assert.NotEqual(version0, _node.QueryVersion());
        }

        /// <summary>
        /// ⭐⭐ B-12 (R-4196): a node with NO navmesh factory — a display node — never lays damage over its terrain. A collapse leaves its resident <see cref="TerrainWorld"/> the very same object with the
        /// building's walls, slab and no mound, and nothing is baked. 🔴 Red-proof: drop the <c>BakesDamage</c> gate in <c>StaticObstacleBakeSystem.Execute</c> ⇒ a bake runs (and, without the residency's
        /// own gate, would rewrite the world).
        /// </summary>
        [Fact]
        public void ADisplayNode_NeverBakesDamage_ACollapseLeavesItsTerrainUnchanged()
        {
            Node(muscle: false);
            var sys = Sys();
            var before = _world.GetSingletonManaged<TerrainWorld>()!;
            int prisms = before.Prisms.Count, walkables = before.Walkables.Count;
            Collapse();
            Damage(new WallBreach(South(), 4f, 6.2f, 0f, 2.2f, BreachKind.Passage));
            for (int i = 0; i < 12; i++) { _wall += 1.0; sys.Execute(_world, 0.016f); }
            Assert.Equal(0, sys.Bakes);
            Assert.False(sys.Baking);
            var after = _world.GetSingletonManaged<TerrainWorld>()!;
            Assert.Same(before, after);
            Assert.Equal(prisms, after.Prisms.Count);
            Assert.Equal(walkables, after.Walkables.Count);
            Assert.Empty(after.BakedCollapsed);
            Assert.True(after.SegmentBlocked(new Vector3(25f, 15f, 1f), new Vector3(25f, 25f, 1f)));
        }

        /// <summary>The same collapse on the MUSCLE (a navmesh factory) is baked: the resident world drops the building's pieces and holds the mound.</summary>
        [Fact]
        public void TheSameCollapse_OnTheMuscle_IsBaked()
        {
            Node(muscle: true);
            var sys = Sys();
            Collapse();
            sys.Execute(_world, 0.016f);
            _wall = 0.6;
            RunUntilBaked(sys, 1);
            var after = _world.GetSingletonManaged<TerrainWorld>()!;
            Assert.Equal(new[] { 0 }, after.BakedCollapsed.ToArray());
            Assert.DoesNotContain(after.Prisms, p => p.Panel >= 0);
            Assert.Empty(after.Walkables);
            Assert.DoesNotContain(3f, after.SurfacesAt(25f, 24f, out _));   // SurfaceZ / SurfacesAt answer for the damaged world right after the bake
        }
    }
}
