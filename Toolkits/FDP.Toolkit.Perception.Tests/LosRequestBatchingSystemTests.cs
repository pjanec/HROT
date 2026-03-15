using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Perception.Systems;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
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
        public void LosRequestBatching_ProductionMode_DoesNotEmitTargetVisibleEvent()
        {
            // Arrange
            var world = PerceptionTestWorldFactory.Create();
            var sys   = new LosRequestBatchingSystem(mockMode: false);

            world.Bus.Publish(new LosCheckRequestEvent { Observer = new Entity(5, 1), Target = new Entity(6, 1) });
            world.Bus.SwapBuffers();

            // Act
            ISimulationView view = world;
            sys.Execute(view, 0f);
            FlushEcbAndSwap(view, world);

            // Assert — production mode queues into raycast batch (TODO); no TargetVisibleEvents.
            var events = world.Bus.Consume<TargetVisibleEvent>();
            Assert.Equal(0, events.Length);
        }
    }
}
