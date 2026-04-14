using System;
using System.Numerics;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Perception.Systems;
using FDP.Toolkit.Physics.Components;
using Fdp.Kernel;
using Fdp.ModuleHost.Core.Abstractions;
using Xunit;

namespace FDP.Toolkit.Perception.Tests
{
    /// <summary>
    /// Unit tests for <see cref="LosRequestBatchingSystem"/>.
    ///
    /// Test pattern (<see cref="IModuleSystem"/>):
    ///   1. Publish <see cref="LosCheckRequestEvent"/>s to the bus.
    ///   2. <c>world.Bus.SwapBuffers()</c> so they are visible to <c>ConsumeEvents</c>.
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

            // Publish two LOS requests then swap so the system can ConsumeEvents them.
            world.Bus.Publish(new LosCheckRequestEvent { Observer = obs1, Target = tgt1 });
            world.Bus.Publish(new LosCheckRequestEvent { Observer = obs2, Target = tgt2 });
            world.Bus.SwapBuffers();

            // Act — execute on background view (EntityRepository implements ISimulationView).
            ISimulationView view = world;
            sys.Execute(view, 0f);
            FlushEcbAndSwap(view, world);

            // Assert — two TargetVisibleEvents, one per request, in order.
            var events = world.Bus.Consume<TargetVisibleEvent>();
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
            var events = world.Bus.Consume<TargetVisibleEvent>();
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
            world.AddComponent(obs, new Faction { FactionId = 1 });
            world.AddComponent(obs, new TargetMemory());

            var tgt = world.CreateEntity();
            world.AddComponent(tgt, new SimTransform { Position = new Vector3(100f, 0f, 0f) });
            world.AddComponent(tgt, new PhysicsCollider { Radius = 2f });
            world.AddComponent(tgt, new Faction { FactionId = 2 });

            world.Bus.Publish(new LosCheckRequestEvent { Observer = obs, Target = tgt });
            world.Bus.SwapBuffers();

            // Act
            ISimulationView view = world;
            sys.Execute(view, 0f);
            FlushEcbAndSwap(view, world);

            // Assert — no occluder → TargetVisibleEvent emitted.
            var events = world.Bus.Consume<TargetVisibleEvent>();
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
            world.AddComponent(obs, new Faction { FactionId = 1 });
            world.AddComponent(obs, new TargetMemory());

            var tgt = world.CreateEntity();
            world.AddComponent(tgt, new SimTransform { Position = new Vector3(100f, 0f, 0f) });
            world.AddComponent(tgt, new PhysicsCollider { Radius = 2f });
            world.AddComponent(tgt, new Faction { FactionId = 2 });

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
            var events = world.Bus.Consume<TargetVisibleEvent>();
            Assert.Equal(0, events.Length);
        }
    }
}
