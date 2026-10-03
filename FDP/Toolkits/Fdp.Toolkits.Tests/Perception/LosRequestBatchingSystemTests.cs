using System;
using System.Numerics;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Interfaces;
using Fdp.Toolkit.Perception.LineOfSight;
using Fdp.Toolkit.Perception.Systems;
using Fdp.Toolkit.Perception.Translators;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Physics.Components;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// Unit tests for <see cref="LosRequestBatchingSystem"/>.
    ///
    /// Test pattern (<see cref="IModuleSystem"/>):
    ///   1. Publish <see cref="LosCheckRequestEvent"/>s to the bus.
    ///   2. <c>world.Bus.SwapBuffers()</c> so they are visible to <c>ReadEvents</c>.
    ///   3. <c>sys.Execute(view, 0f)</c>.
    ///   4. Flush the ECB: <c>ecb.Playback(world)</c>.
    ///   5. <c>world.Bus.SwapBuffers()</c> to expose events published by the system.
    ///   6. Assert <c>world.Bus.Consume&lt;TargetVisibleEvent&gt;()</c>.
    /// </summary>
    public class LosRequestBatchingSystemTests
    {
        private static EntityRepository CreateWorldWithPhysics()
        {
            var world = PerceptionTestWorldFactory.Create();
            world.RegisterComponent<PhysicsCollider>();
            return world;
        }

        private static void FlushEcbAndSwap(ISimulationView view, EntityRepository world)
        {
            var ecb = (EntityCommandBuffer)view.GetCommandBuffer();
            ecb.Playback(world);
            world.Bus.SwapBuffers();
        }

        // ── Test 1 ───────────────────────────────────────────────────────────────

        [Fact]
        public void LosRequestBatching_MockMode_EmitsTargetVisibleEvent_ForEachRequest()
        {
            // Arrange
            var world = PerceptionTestWorldFactory.Create();
            var sys   = new LosRequestBatchingSystem(mockMode: true);

            // Build two entity pairs with full Entity handles (Index + Generation).
            var obs1 = new Entity(1, 1);
            var tgt1 = new Entity(2, 1);
            var obs2 = new Entity(3, 1);
            var tgt2 = new Entity(4, 1);

            // Publish two LOS requests then swap so the system can ReadEvents them.
            world.Bus.Publish(new LosCheckRequestEvent { Observer = obs1, Target = tgt1 });
            world.Bus.Publish(new LosCheckRequestEvent { Observer = obs2, Target = tgt2 });
            world.Bus.SwapBuffers();

            // Act — execute on background view (EntityRepository implements ISimulationView).
            ISimulationView view = world;
            sys.Execute(view, 0f);
            FlushEcbAndSwap(view, world);

            // Assert — two TargetVisibleEvents, one per request, in order.
            var events = world.Bus.Read<TargetVisibleEvent>();
            Assert.Equal(2, events.Length);
            Assert.Equal(obs1, events[0].Observer);
            Assert.Equal(tgt1, events[0].Target);
            Assert.Equal(obs2, events[1].Observer);
            Assert.Equal(tgt2, events[1].Target);
        }

        // ── Test 2 ───────────────────────────────────────────────────────────────

        [Fact]
        public void LosRequestBatching_ProductionMode_SkipsDeadEntities()
        {
            // Arrange — ghost entity handles (not alive in world).
            var world = CreateWorldWithPhysics();
            var sys   = new LosRequestBatchingSystem(mockMode: false);

            world.Bus.Publish(new LosCheckRequestEvent { Observer = new Entity(5, 1), Target = new Entity(6, 1) });
            world.Bus.SwapBuffers();

            // Act
            ISimulationView view = world;
            sys.Execute(view, 0f);
            FlushEcbAndSwap(view, world);

            // Production mode skips dead/missing entities — no TargetVisibleEvents.
            var events = world.Bus.Read<TargetVisibleEvent>();
            Assert.Equal(0, events.Length);
        }

        // ── Test 3 ───────────────────────────────────────────────────────────────

        private static Func<ISimulationView, Entity, float> PhysicsRadiusReader() =>
            (view, e) => view.HasComponent<PhysicsCollider>(e)
                ? view.GetComponentRO<PhysicsCollider>(e).Radius : 0f;

        [Fact]
        public void LosRequestBatching_ProductionMode_EmitsVisible_WhenLOSisClear()
        {
            // Arrange — observer and target in open field (no occluder).
            var world = CreateWorldWithPhysics();
            var sys   = new LosRequestBatchingSystem(
                mockMode: false,
                colliderRadiusReader: PhysicsRadiusReader());

            var obs = world.CreateEntity();
            world.AddComponent(obs, new SimTransform { Position = new Vector3(0f, 0f, 0f) });
            world.AddComponent(obs, new EntityInfo { ForceId = ForceId.Friend });
            world.AddComponent(obs, new TargetMemory());

            var tgt = world.CreateEntity();
            world.AddComponent(tgt, new SimTransform { Position = new Vector3(100f, 0f, 0f) });
            world.AddComponent(tgt, new PhysicsCollider { Radius = 2f });
            world.AddComponent(tgt, new EntityInfo { ForceId = ForceId.Hostile });

            world.Bus.Publish(new LosCheckRequestEvent { Observer = obs, Target = tgt });
            world.Bus.SwapBuffers();

            // Act
            ISimulationView view = world;
            sys.Execute(view, 0f);
            FlushEcbAndSwap(view, world);

            // Assert — no occluder → TargetVisibleEvent emitted.
            var events = world.Bus.Read<TargetVisibleEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(obs, events[0].Observer);
            Assert.Equal(tgt, events[0].Target);
        }

        // ── Test 4 ───────────────────────────────────────────────────────────────

        [Fact]
        public void LosRequestBatching_ProductionMode_DoesNotEmit_WhenOccluded()
        {
            // Arrange — wall entity directly on the observer→target segment.
            var world = CreateWorldWithPhysics();
            var sys   = new LosRequestBatchingSystem(
                mockMode: false,
                colliderRadiusReader: PhysicsRadiusReader());

            var obs = world.CreateEntity();
            world.AddComponent(obs, new SimTransform { Position = new Vector3(0f, 0f, 0f) });
            world.AddComponent(obs, new EntityInfo { ForceId = ForceId.Friend });
            world.AddComponent(obs, new TargetMemory());

            var tgt = world.CreateEntity();
            world.AddComponent(tgt, new SimTransform { Position = new Vector3(100f, 0f, 0f) });
            world.AddComponent(tgt, new PhysicsCollider { Radius = 2f });
            world.AddComponent(tgt, new EntityInfo { ForceId = ForceId.Hostile });

            // Wall sits directly on the observer→target segment at mid-range.
            var wall = world.CreateEntity();
            world.AddComponent(wall, new SimTransform { Position = new Vector3(50f, 0f, 0f) });
            world.AddComponent(wall, new PhysicsCollider { Radius = 10f });

            world.Bus.Publish(new LosCheckRequestEvent { Observer = obs, Target = tgt });
            world.Bus.SwapBuffers();

            // Act
            ISimulationView view = world;
            sys.Execute(view, 0f);
            FlushEcbAndSwap(view, world);

            // Assert — wall blocks LOS → no TargetVisibleEvent.
            var events = world.Bus.Read<TargetVisibleEvent>();
            Assert.Equal(0, events.Length);
        }
        // ── 3-D sight through the terrain world (docs/DESIGN_Terrain_World.md §4.3, R-181/R-182) ─────────────

        private const string TwelveMetreBuildingAt50 = """
            {"type":"FeatureCollection","features":[{"type":"Feature","properties":{"kind":"building","height":12},
             "geometry":{"type":"Polygon","coordinates":[[[45,-5],[55,-5],[55,5],[45,5],[45,-5]]]}}]}
            """;

        private const string HalfMetreWallAt10 = """
            {"type":"FeatureCollection","features":[{"type":"Feature","properties":{"kind":"wall","height":0.5,"thickness":0.3},
             "geometry":{"type":"LineString","coordinates":[[10,-5],[10,5]]}}]}
            """;

        private static (EntityRepository World, Entity Obs, Entity Tgt) TwoSoldiers(float targetX)
        {
            var world = CreateWorldWithPhysics();
            var obs = world.CreateEntity();
            world.AddComponent(obs, new SimTransform { Position = new Vector3(0f, 0f, 0f) });
            var tgt = world.CreateEntity();
            world.AddComponent(tgt, new SimTransform { Position = new Vector3(targetX, 0f, 0f) });
            return (world, obs, tgt);
        }

        private static int VisibleCount(EntityRepository world, ILosStrategy strategy, Entity obs, Entity tgt)
        {
            var sys = new LosRequestBatchingSystem(mockMode: false, losStrategy: strategy);
            world.Bus.Publish(new LosCheckRequestEvent { Observer = obs, Target = tgt });
            world.Bus.SwapBuffers();
            ISimulationView view = world;
            sys.Execute(view, 0f);
            FlushEcbAndSwap(view, world);
            return world.Bus.Read<TargetVisibleEvent>().Length;
        }

        private static TerrainWorldLosStrategy Strategy(TerrainWorld? terrain, StanceId observerStance = StanceId.Standing,
            StanceId targetStance = StanceId.Standing, Entity observer = default)
            => new(() => terrain, PhysicsRadiusReader(),
                (v, e) => v.HasComponent<PhysicsCollider>(e) ? v.GetComponentRO<PhysicsCollider>(e).Height : 0f,
                (v, e) => e.Index == observer.Index ? observerStance : targetStance);

        [Fact]
        public void TerrainLos_ABuildingBetween_BlocksSight_AndOpenGroundDoesNot()
        {
            var (world, obs, tgt) = TwoSoldiers(100f);
            Assert.Equal(0, VisibleCount(world, Strategy(TerrainWorldParser.Parse(TwelveMetreBuildingAt50)), obs, tgt));

            var (open, o2, t2) = TwoSoldiers(100f);
            Assert.Equal(1, VisibleCount(open, Strategy(TerrainWorldParser.Parse("""{"type":"FeatureCollection","features":[]}""")), o2, t2));
        }

        /// <summary>🔒 R-182 — <i>"infantry can lay on ground or squat, sensor height must follow posture"</i>:
        /// over a 0.5 m wall a STANDING observer sees a prone target, a PRONE observer does not.</summary>
        [Fact]
        public void TerrainLos_EyeHeightFollowsPosture_StandingSeesOverALowWall_ProneDoesNot()
        {
            var wall = TerrainWorldParser.Parse(HalfMetreWallAt10);

            var (w1, o1, t1) = TwoSoldiers(20f);
            Assert.Equal(1, VisibleCount(w1, Strategy(wall, StanceId.Standing, StanceId.Prone, o1), o1, t1));

            var (w2, o2, t2) = TwoSoldiers(20f);
            Assert.Equal(0, VisibleCount(w2, Strategy(wall, StanceId.Prone, StanceId.Prone, o2), o2, t2));
        }

        [Fact]
        public void TerrainLos_ASensorMount_OverridesTheDefaultEyeHeight()
        {
            var wall = TerrainWorldParser.Parse(HalfMetreWallAt10);
            var (world, obs, tgt) = TwoSoldiers(20f);
            // A prone sensor mounted HIGH (a periscope) sees over the wall the default prone eye cannot.
            world.AddComponent(obs, new SensorMount { Standing = 1.7f, Crouched = 1.1f, Prone = 1.5f });
            Assert.Equal(1, VisibleCount(world, Strategy(wall, StanceId.Prone, StanceId.Prone, obs), obs, tgt));
        }

        /// <summary>A collider blocks only within its HEIGHT; height 0 (unknown) blocks at every height, as the
        /// 2-D sweep always did.</summary>
        [Fact]
        public void TerrainLos_ACollider_BlocksWithinItsHeight_AndUnknownHeightBlocksAlways()
        {
            foreach (var (height, visible) in new[] { (1.0f, 1), (2.5f, 0), (0f, 0) })
            {
                var (world, obs, tgt) = TwoSoldiers(20f);
                var car = world.CreateEntity();
                world.AddComponent(car, new SimTransform { Position = new Vector3(10f, 0f, 0f) });
                world.AddComponent(car, new PhysicsCollider { Radius = 2f, Height = height });
                Assert.Equal(visible, VisibleCount(world, Strategy(null), obs, tgt));
            }
        }

        [Fact]
        public void PerceptionTkbTranslator_ProjectsPostureEyeHeights_IntoASensorMount()
        {
            var world = CreateWorldWithPhysics();
            var e = world.CreateEntity();
            var template = new TkbTemplate("Scout", 1);
            template.AddDescriptor(new SensorCapabilitiesDto { VisionRange = 300f, EyeHeightStanding = 1.8f, EyeHeightProne = 0.3f });
            new PerceptionTkbTranslator().Inject(world, e, template);

            var mount = world.GetComponentRO<SensorMount>(e);
            Assert.Equal(1.8f, mount.Standing);
            Assert.Equal(1.8f, mount.Crouched);   // unset ⇒ the standing height
            Assert.Equal(0.3f, mount.Prone);

            var bare = world.CreateEntity();
            var plain = new TkbTemplate("Plain", 2);
            plain.AddDescriptor(new SensorCapabilitiesDto { VisionRange = 300f });
            new PerceptionTkbTranslator().Inject(world, bare, plain);
            Assert.False(world.HasComponent<SensorMount>(bare));   // ⇒ the strategy's default mount
        }
    }
}
