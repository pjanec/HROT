using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3136</c> P-7a (O2, R-243, R-244) — <see cref="StaticObstacleBakeSystem"/>, the terrain lifecycle participant: a static
    /// obstacle is baked into the node's terrain in the BACKGROUND, after a WALL-CLOCK quiet period, in ONE bake for everything that
    /// arrived together — and its construction is acked only after that bake has COMMITTED. 📄 docs/DESIGN_Peek_And_Fire.md §9.2 O2, §9.4.
    /// </summary>
    public sealed class StaticObstacleBakeTests : IDisposable
    {
        private readonly EntityRepository _world = new();
        private readonly string _staging = Path.Combine(Path.GetTempPath(), "p7a-" + Guid.NewGuid().ToString("N"));
        private double _wall;

        public StaticObstacleBakeTests()
        {
            SimHostComponentRegistry.RegisterAll(_world);
            Directory.CreateDirectory(_staging);
        }

        public void Dispose()
        {
            _world.Dispose();
            try { Directory.Delete(_staging, recursive: true); } catch { }
        }

        private StaticObstacleBakeSystem System() => new(() => _wall);

        /// <summary>A node holding a terrain (flat, empty): the residency publishes its bakery.</summary>
        private TerrainResidency Terrain()
        {
            var residency = new TerrainResidency(_staging, new RoadNetworkHolder());
            residency.Unload(_world);   // ⭐ publishes the bakery over an empty base world
            Assert.True(_world.HasSingletonManaged<StaticObstacleBakery>());
            return residency;
        }

        private Entity Obstacle(float x, string material = "car-body")
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = new Vector3(x, 0, 0), Rotation = Quaternion.Identity });
            _world.AddComponent(e, new StaticObstacle { Material = new FixedString32(material) });
            _world.AddComponent(e, new ObstacleShape { Length = 4.5f, Width = 1.8f, Height = 1.5f });
            _world.SetLifecycleState(e, EntityLifecycle.Constructing);
            return e;
        }

        private int[] Acked()
        {
            _world.Bus.SwapBuffers();
            return _world.Bus.Read<ConstructionAck>().ToArray()
                .Where(a => a.ModuleId == TerrainObstacles.LifecycleModuleId).Select(a => a.Entity.Index).ToArray();
        }

        private void RunUntilBaked(StaticObstacleBakeSystem sys, int bakes)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (sys.Bakes < bakes && DateTime.UtcNow < deadline)
            {
                sys.Execute(_world, 0.016f);
                if (sys.Bakes < bakes) Thread.Sleep(5);
            }
            Assert.Equal(bakes, sys.Bakes);
        }

        /// <summary>
        /// ⭐⭐ CE-4656 — a baked PARKED CAR (a platform type, DIS kind 1: the entity layer draws it as a car) is collision / navmesh / sight only: its prism says
        /// <c>DrawnByEntity</c> and the 3-D terrain mesh draws no box there (the grey <c>car-body</c> block over the car, user observation); a sandbag wall (a
        /// cultural feature, no kit of its own) keeps its box. Both still block in the world. 🔴 Red-proof: <c>DrawsItsOwnBody</c> ⇒ false ⇒ the car's box is drawn (red).
        /// </summary>
        [Fact]
        public void CE4656_ABakedParkedCar_IsNotDrawnAsATerrainBox_ASandbagWallStillIs()
        {
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            Hrot.Map.Definitions.Tkb.NedTkbCatalog.RegisterObstacles(db);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            Entity Typed(float x, long type, string material)
            {
                var e = Obstacle(x, material);
                _world.AddComponent(e, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = (TkbTypeId)type });
                _world.SetLifecycleState(e, EntityLifecycle.Active);
                return e;
            }
            var car = Typed(0f, Hrot.Map.Common.TkbEntityTypes.Obstacle_Car, "car-body");
            var bags = Typed(20f, Hrot.Map.Common.TkbEntityTypes.Obstacle_SandbagWall, "sandbags");

            var prisms = new System.Collections.Generic.List<TerrainPrism>();
            StaticObstacleBakeSystem.CollectPrisms(_world, prisms);
            Assert.Equal(2, prisms.Count);
            var carPrism = prisms.Single(p => p.Label == TerrainObstacles.LabelPrefix + car.Index);
            var bagPrism = prisms.Single(p => p.Label == TerrainObstacles.LabelPrefix + bags.Index);
            Assert.True(carPrism.DrawnByEntity);
            Assert.False(bagPrism.DrawnByEntity);

            var flat = TerrainWorldParser.Parse("{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-50,-50,50,50],\"groundZ\":0},\"features\":[]}", "ce4656");
            var baked = TerrainObstacles.With(flat, prisms);
            Assert.Equal(2, baked.Prisms.Count);                                   // both still block (collision / navmesh / sight)
            // ⭐ CE-8309 — what is DRAWN is TerrainWorldMesh.BuildDrawn (the render geometry); the solid soup (Build — the navmesh's input) keeps the car
            TerrainWorldMesh.BuildDrawn(baked, out var verts, out var idx, out _);
            bool AnyAbove(Vector3[] vs, int[] ix, float x0, float x1)
            {
                for (int i = 0; i < ix.Length; i++) { var v = vs[ix[i]]; if (v.Z > 0.5f && v.X > x0 && v.X < x1) return true; }
                return false;
            }
            Assert.False(AnyAbove(verts, idx, -3f, 3f), "a terrain box drawn over the parked car");
            Assert.True(AnyAbove(verts, idx, 17f, 23f), "the sandbag wall's box is gone");
            TerrainWorldMesh.Build(baked, out var solidVerts, out var solidIdx, cellSize: 5f);
            Assert.True(AnyAbove(solidVerts, solidIdx, -3f, 3f), "CE-8309: the parked car left the SOLID soup (the navmesh would route through it)");
        }

        /// <summary>
        /// ⭐⭐⭐ <c>P7a_B1</c> — THE GATE: nothing is acked before the quiet period, nor while the bake runs; the ack comes only after the
        /// bake has committed — the node's world then holds the obstacle as a prism of its material, and the cover database has points
        /// round it. 🔴 Red-proof: ack the batch at bake START (move the Ack loop above <c>CommitObstacles</c>) and the "nothing acked
        /// while baking" assertion fails.
        /// </summary>
        [Fact]
        public void P7a_B1_AnObstacleIsAckedOnlyAfterItsBakeCommitted_AfterTheWallClockQuietPeriod()
        {
            Terrain();
            var sys = System();
            var car = Obstacle(10f);

            sys.Execute(_world, 0.016f);
            _wall = 0.4; sys.Execute(_world, 0.016f);
            Assert.False(sys.Baking, "inside the quiet period nothing is baked");
            Assert.Empty(Acked());

            _wall = 0.6; sys.Execute(_world, 0.016f);
            Assert.True(sys.Baking || sys.Bakes == 0, "the bake starts after the quiet period");
            Assert.Empty(Acked());

            RunUntilBaked(sys, 1);
            Assert.Contains(car.Index, Acked());

            var world = _world.GetSingletonManaged<TerrainWorld>()!;
            var prism = Assert.Single(world.Prisms);
            Assert.Equal("car-body", prism.Material!.Name);
            Assert.StartsWith(TerrainObstacles.LabelPrefix, prism.Label);
            Span<CoverPoint> pts = stackalloc CoverPoint[32];
            Assert.True(_world.GetSingletonManaged<ICoverProvider>()!.GetCoverPointsInRadius(new Vector2(10, 0), 5f, pts) > 0);
        }

        /// <summary>⭐⭐ <c>P7a_B2</c> — obstacles arriving in quick succession (a scenario's) are baked in ONE bake, all acked together.</summary>
        [Fact]
        public void P7a_B2_ObstaclesArrivingTogether_AreBakedOnce()
        {
            Terrain();
            var sys = System();
            var a = Obstacle(10f);
            sys.Execute(_world, 0.016f);
            _wall = 0.2; var b = Obstacle(20f, "sandbags"); sys.Execute(_world, 0.016f);
            _wall = 0.4; var c = Obstacle(30f, "concrete"); sys.Execute(_world, 0.016f);
            Assert.Equal(0, sys.Bakes);
            _wall = 1.0;
            RunUntilBaked(sys, 1);
            var acked = Acked();
            Assert.Contains(a.Index, acked); Assert.Contains(b.Index, acked); Assert.Contains(c.Index, acked);
            Assert.Equal(3, _world.GetSingletonManaged<TerrainWorld>()!.Prisms.Count);
        }

        /// <summary>
        /// ⭐⭐ CE-4427 R-5 — a construction that waits for the terrain module ALONE but can never be baked is ACKED by the bake system (with a Warn) instead of hanging for ever (the wait is unbounded): an obstacle missing its
        /// <c>ObstacleShape</c>, or a world where <c>StaticObstacle</c> is not even registered. A bakeable one is left to the normal path. 🔴 Red-proof: drop <c>AckTheUnbakeable(world)</c> from <c>Execute</c> ⇒ neither is acked.
        /// </summary>
        [Fact]
        public void CE4427_R5_AnObstacleTheBakeCanNeverServe_IsAckedInsteadOfHanging()
        {
            Terrain();
            var elm = new Fdp.Toolkit.Lifecycle.EntityLifecycleModule(null, new[] { TerrainObstacles.LifecycleModuleId }, timeoutFrames: 10);
            elm.WaitWithoutTimeout(TerrainObstacles.LifecycleModuleId);
            var sys = new StaticObstacleBakeSystem(() => _wall) { Lifecycle = elm };
            using var cmd = new Fdp.Core.EntityCommandBuffer();

            var noShape = _world.CreateEntity();   // a StaticObstacle with no shape: the bake's query never sees it
            _world.AddComponent(noShape, new SimTransform { Position = new Vector3(5, 0, 0), Rotation = Quaternion.Identity });
            _world.AddComponent(noShape, new StaticObstacle { Material = new FixedString32("car-body") });
            _world.SetLifecycleState(noShape, EntityLifecycle.Constructing);
            elm.BeginConstruction(noShape, 1, 0, cmd);

            var ok = Obstacle(10f);                // a whole one: left to the normal, baked path
            elm.BeginConstruction(ok, 1, 0, cmd);

            sys.Execute(_world, 0.016f);
            var acked = Acked();
            Assert.Contains(noShape.Index, acked);
            Assert.DoesNotContain(ok.Index, acked);

            // a world with no StaticObstacle type at all
            using var bare = new EntityRepository();
            bare.RegisterComponent<SimTransform>(); bare.RegisterEvent<ConstructionAck>();
            var e = bare.CreateEntity();
            var elm2 = new Fdp.Toolkit.Lifecycle.EntityLifecycleModule(null, new[] { TerrainObstacles.LifecycleModuleId }, timeoutFrames: 10);
            elm2.WaitWithoutTimeout(TerrainObstacles.LifecycleModuleId);
            elm2.BeginConstruction(e, 1, 0, cmd);
            new StaticObstacleBakeSystem(() => _wall) { Lifecycle = elm2 }.Execute(bare, 0.016f);
            bare.Bus.SwapBuffers();
            Assert.Contains(bare.Bus.Read<ConstructionAck>().ToArray(), a => a.Entity == e && a.ModuleId == TerrainObstacles.LifecycleModuleId);
        }

        /// <summary>⭐ <c>P7a_B3</c> — a node holding no terrain (no bakery) has nothing to bake: the obstacle is acked at once.</summary>
        [Fact]
        public void P7a_B3_ANodeWithNoTerrain_AcksAtOnce()
        {
            var sys = System();
            var car = Obstacle(10f);
            sys.Execute(_world, 0.016f);
            Assert.Contains(car.Index, Acked());
            Assert.Equal(0, sys.Bakes);
        }

        /// <summary>
        /// ⭐ <c>P7a_B5</c> — float noise is not a change: a replica's position arrives through the geo transform and wobbles by
        /// micrometres every update; the bake signature is quantised to 1 cm, so the world is baked ONCE (📐 measured on the
        /// live <c>bt-window-duel</c> before the fix: ≈ 150 re-bakes). A real move (5 cm) still re-bakes. 🔴 Red-proof: mix the raw
        /// float bits into the signature and the noise loop re-bakes.
        /// </summary>
        [Fact]
        public void P7a_B5_SubCentimetreNoise_DoesNotRebake_ARealMoveDoes()
        {
            Terrain();
            var sys = System();
            var car = Obstacle(10f);
            sys.Execute(_world, 0.016f);
            _wall = 1.0;
            RunUntilBaked(sys, 1);
            _world.SetLifecycleState(car, EntityLifecycle.Active);

            for (int i = 0; i < 20; i++)
            {
                ref var t = ref _world.GetComponentRW<SimTransform>(car);
                t.Position = new Vector3(10f + ((i % 2 == 0 ? 1 : -1) * 0.0004f), 0.0002f * (i % 3), 0f);
                _wall += 1.0; sys.Execute(_world, 0.016f);
            }
            Assert.Equal(1, sys.Bakes);

            ref var moved = ref _world.GetComponentRW<SimTransform>(car);
            moved.Position = new Vector3(10.05f, 0f, 0f);
            _wall += 0.1; sys.Execute(_world, 0.016f);
            _wall += 1.0;
            RunUntilBaked(sys, 2);
        }

        /// <summary>⭐ <c>P7a_B4</c> — removing the obstacle re-bakes the world without it (a runtime change, debounced the same way).</summary>
        [Fact]
        public void P7a_B4_RemovingAnObstacle_RebakesWithoutIt()
        {
            Terrain();
            var sys = System();
            var car = Obstacle(10f);
            sys.Execute(_world, 0.016f);
            _wall = 1.0;
            RunUntilBaked(sys, 1);
            _world.SetLifecycleState(car, EntityLifecycle.Active);

            _world.DestroyEntity(car);
            _wall = 1.1; sys.Execute(_world, 0.016f);
            _wall = 2.0;
            RunUntilBaked(sys, 2);
            Assert.Empty(_world.GetSingletonManaged<TerrainWorld>()!.Prisms);
        }


        // ── CE-4596 (P-0; docs/DESIGN_Ground_Patches_And_Road_Blockage.md §9.1 V-1, V-3) ─────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐ <c>CE-4596</c> (V-1) — a BARRAGE: an obstacle every 0.3 s of wall clock for 10 s never leaves the quiet period, yet the first bake
        /// starts within <see cref="StaticObstacleBakeSystem.MaxLatencySeconds"/> of the first change, and the bakes keep coming while it lasts.
        /// 🔴 Red-proof: drop the <c>overdue</c> test ⇒ no bake starts until the barrage ends (Bakes 0 at 10 s).
        /// </summary>
        [Fact]
        public void CE4596_UnderABarrage_TheBakeStillCommits_WithinTheLatencyCap()
        {
            Terrain();
            var sys = System();
            double firstStart = double.NaN;
            int startedBefore10 = 0;
            for (int i = 0; i < 34; i++)
            {
                _wall = i * 0.3;
                Obstacle(10f + i * 6f);
                bool was = sys.Baking;
                sys.Execute(_world, 0.016f);
                if (sys.Baking && !was) { startedBefore10++; if (double.IsNaN(firstStart)) firstStart = _wall; }
                // let a started bake finish before the next "impact" (the frame thread keeps polling it)
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (sys.Baking && DateTime.UtcNow < deadline) { Thread.Sleep(2); sys.Execute(_world, 0.016f); }
            }
            Assert.False(double.IsNaN(firstStart), "no bake started during the barrage");
            Assert.True(firstStart <= StaticObstacleBakeSystem.MaxLatencySeconds + 0.3 + 1e-6, $"the first bake started at {firstStart:F1} s");
            Assert.True(startedBefore10 >= 3, $"only {startedBefore10} bake(s) during a 10 s barrage");
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4596</c> (V-3) — a host with NO live overlay (a cluster node, a display node, the player — everything but the editor) publishes
        /// every committed bake's dirty rectangles (<see cref="LiveTerrainBakeInfo"/>), so the shared 3-D map rebuilds only the chunks under them. The
        /// second bake (one obstacle added beside the first) knows where it changed: a rectangle round the new obstacle only. 🔴 Red-proof: publish
        /// only when <c>LiveOverlay</c> is set (the old gate) ⇒ no <see cref="LiveTerrainBakeInfo"/> here.
        /// </summary>
        [Fact]
        public void CE4596_AHostWithNoLiveOverlay_PublishesTheDirtyRectangles()
        {
            Terrain();
            var sys = System();
            Assert.Null(sys.LiveOverlay);
            Obstacle(10f);
            sys.Execute(_world, 0.016f);
            _wall = 1.0;
            RunUntilBaked(sys, 1);
            Assert.True(_world.HasSingletonManaged<LiveTerrainBakeInfo>());
            Assert.Equal(1, _world.GetSingletonManaged<LiveTerrainBakeInfo>()!.Bakes);

            Obstacle(60f);
            _wall = 1.1; sys.Execute(_world, 0.016f);
            _wall = 2.0;
            RunUntilBaked(sys, 2);
            var info = _world.GetSingletonManaged<LiveTerrainBakeInfo>()!;
            Assert.Equal(2, info.Bakes);
            var dirty = Assert.Single(info.Dirty!);
            Assert.True(dirty.Min.X > 50f && dirty.Max.X < 70f, $"the dirty rectangle {dirty.Min}..{dirty.Max} is not round the new obstacle at x=60");
        }
    
        // ── ⭐ CE-4692 BL-S1 — the WALKABLE DECK (a laid bridge) ──────────────────────────────────────────────────────────────────────────

        /// <summary>A 300 × 100 m flat terrain with a river across it (water at x 140..160, every y): nothing crosses it but a deck.</summary>
        private const string River = """
            { "type": "FeatureCollection", "hrot": { "schemaVersion": 1, "bounds": [0, 0, 300, 100], "groundZ": 0 }, "features": [
              { "type": "Feature", "properties": { "kind": "surface", "surface": "water" },
                "geometry": { "type": "Polygon", "coordinates": [[[140,-5],[160,-5],[160,105],[140,105],[140,-5]]] } } ] }
            """;

        /// <summary>A laid bridge over the river at x = 150: a WalkableDeck obstacle, 26 m between the deck ends (on the banks), 4 m wide, 1.2 m above them.</summary>
        private Entity Deck()
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = new Vector3(150, 50, 0), Rotation = Quaternion.Identity });
            _world.AddComponent(e, new StaticObstacle { Material = new FixedString32("steel-plate"), Kind = ObstacleKind.WalkableDeck });
            _world.AddComponent(e, new ObstacleShape { Length = 26f, Width = 4f, Height = 1.2f });
            _world.SetLifecycleState(e, EntityLifecycle.Constructing);
            return e;
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE-4692</c> BL-5 / BL-6 — a laid bridge is baked by the INCREMENTAL bake (dirty rectangles, every other tile reused): after it commits, a MAN and a GROUND VEHICLE
        /// each have a path from bank to bank, the deck stands in the world as walkables (no prism), its construction is acked only then; once the bridge is gone the next incremental
        /// bake leaves NO crossing. 🔴 Red-proof: leave <c>DeckDirt</c> out of <c>TerrainResidency.BakeObstacles</c> ⇒ the bake reuses the river's old tiles ⇒ no path over the deck.
        /// </summary>
        [Fact]
        public void CE4692_ALaidBridge_IsBakedIncrementally_AndCarriesAManAndAVehicle_UntilItIsGone()
        {
            var terrain = TerrainWorldParser.Parse(River, "river");
            var factory = new Fdp.Toolkit.Navigation.Recast.RecastNavmeshFactory { Layers = Fdp.Toolkit.Navigation.NavLayerMask.Infantry | Fdp.Toolkit.Navigation.NavLayerMask.Vehicle, TileCache = new Fdp.Toolkit.Navigation.Recast.NavTileCache() };
            var node = new Fdp.Toolkit.Navigation.SwitchableNavmeshProvider();
            var residency = new TerrainResidency(_staging, new RoadNetworkHolder());
            var baseline = factory.Build(terrain);
            residency.AttachNavmesh(factory, node);
            residency.Commit(_world, new TerrainResidency.Staged { Definition = new TerrainDefinition { Name = "river" }, World = terrain, TerrainName = "river", Navmesh = baseline, BakeSerial = factory.BakeSerial });
            var from = new Vector3(100, 50, 0); var to = new Vector3(200, 50, 0);
            var layers = new[] { Fdp.Toolkit.Navigation.NavLayerMask.Infantry, Fdp.Toolkit.Navigation.NavLayerMask.Vehicle };
            foreach (var l in layers) Assert.False(node.PathExists(from, to, (uint)l), $"{l}: the river alone has a crossing");

            // a first bake with nothing: the baseline the next ones diff against (the incremental path needs one)
            var sys = System();
            var crate = Obstacle(20f);
            sys.Execute(_world, 0.016f); _wall += 1.0;
            RunUntilBaked(sys, 1);
            var bridge = Deck();
            sys.Execute(_world, 0.016f); _wall += 1.0;
            RunUntilBaked(sys, 2);
            Assert.Contains(bridge.Index, Acked());
            var info = _world.GetSingletonManaged<LiveTerrainBakeInfo>();
            Assert.NotNull(info.Dirty);                                                                   // ⭐ the INCREMENTAL bake, not a full one
            var world = _world.GetSingletonManaged<TerrainWorld>();
            Assert.Equal(3, world.Walkables.Count(w => w.Obstacle == bridge.Index));                     // the deck and its two ramps
            Assert.DoesNotContain(world.Prisms, p => p.Label == TerrainObstacles.LabelPrefix + bridge.Index);   // ⛔ never a box
            Assert.InRange(world.SurfaceZ(150f, 50f, 1.2f), 1.1f, 1.3f);                                  // a man stands on the deck over the water
            foreach (var l in layers) Assert.True(node.PathExists(from, to, (uint)l), $"{l}: no way over the laid bridge");

            _world.DestroyEntity(bridge);
            sys.Execute(_world, 0.016f); _wall += 1.0;
            RunUntilBaked(sys, 3);
            Assert.NotNull(_world.GetSingletonManaged<LiveTerrainBakeInfo>().Dirty);
            Assert.DoesNotContain(_world.GetSingletonManaged<TerrainWorld>().Walkables, w => w.Obstacle >= 0);
            foreach (var l in layers) Assert.False(node.PathExists(from, to, (uint)l), $"{l}: the removed bridge still carries");
            GC.KeepAlive(crate);
        }
}
}
