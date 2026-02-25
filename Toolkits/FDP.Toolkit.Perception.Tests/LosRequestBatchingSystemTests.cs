using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Perception.Systems;
using Fdp.Kernel;
using Xunit;

namespace FDP.Toolkit.Perception.Tests
{
    /// <summary>
    /// Unit tests for <see cref="LosRequestBatchingSystem"/>.
    ///
    /// Test pattern (ComponentSystem):
    ///   1. Publish <see cref="LosCheckRequestEvent"/>s to the bus.
    ///   2. <c>world.Bus.SwapBuffers()</c> so they are visible to <c>Consume</c>.
    ///   3. <c>sys.Run()</c>.
    ///   4. <c>world.Bus.SwapBuffers()</c> to expose events published <i>by</i> the system.
    ///   5. Assert <c>world.Bus.Consume&lt;TargetVisibleEvent&gt;()</c>.
    /// </summary>
    public class LosRequestBatchingSystemTests
    {
        // ── Test 1 ───────────────────────────────────────────────────────────────

        [Fact]
        public void LosRequestBatching_MockMode_EmitsTargetVisibleEvent_ForEachRequest()
        {
            // Arrange
            var world = PerceptionTestWorldFactory.Create();
            var sys   = new LosRequestBatchingSystem(mockMode: true);
            sys.Create(world);

            // Build two entity pairs with full Entity handles (Index + Generation).
            var obs1 = new Entity(1, 1);
            var tgt1 = new Entity(2, 1);
            var obs2 = new Entity(3, 1);
            var tgt2 = new Entity(4, 1);

            // Publish two LOS requests.
            world.Bus.Publish(new LosCheckRequestEvent { Observer = obs1, Target = tgt1 });
            world.Bus.Publish(new LosCheckRequestEvent { Observer = obs2, Target = tgt2 });
            // Swap so the system can Consume them.
            world.Bus.SwapBuffers();

            // Act
            sys.Run();
            // Swap again to expose the TargetVisibleEvents the system just published.
            world.Bus.SwapBuffers();

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
            var sys   = new LosRequestBatchingSystem(mockMode: false); // production path
            sys.Create(world);

            world.Bus.Publish(new LosCheckRequestEvent { Observer = new Entity(5, 1), Target = new Entity(6, 1) });
            world.Bus.SwapBuffers();

            // Act
            sys.Run();
            world.Bus.SwapBuffers();

            // Assert — production mode queues into raycast batch (TODO); no TargetVisibleEvents.
            var events = world.Bus.Consume<TargetVisibleEvent>();
            Assert.Equal(0, events.Length);
        }
    }
}
