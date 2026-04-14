using Fdp.Kernel;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Perception.Events;
using Hrot.Map.Common;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Verifies that the new domain-event types from EDIT1-E001 and EDIT1-E002
    /// are correctly registered in the SimHost component registries,
    /// so that <c>Bus.Publish</c> succeeds without an unregistered-stream exception.
    /// </summary>
    public class EventRegistrationTests
    {
        // ── CognitiveComponentRegistry (EDIT1-E001) ───────────────────────────

        [Fact]
        public void CognitiveRegistry_PublishEmbarkEntityCommand_DoesNotThrow()
        {
            using var world = new EntityRepository();
            HrotSharedComponentRegistry.RegisterAll(world);
            CognitiveComponentRegistry.RegisterAll(world);

            var ex = Record.Exception(() =>
            {
                world.Bus.Publish(new EmbarkEntityCommand
                {
                    Passenger = new Entity(1, 1),
                    Vehicle   = new Entity(2, 1),
                });
            });

            Assert.Null(ex);
        }

        [Fact]
        public void CognitiveRegistry_PublishDisembarkEntityCommand_DoesNotThrow()
        {
            using var world = new EntityRepository();
            HrotSharedComponentRegistry.RegisterAll(world);
            CognitiveComponentRegistry.RegisterAll(world);

            var ex = Record.Exception(() =>
            {
                world.Bus.Publish(new DisembarkEntityCommand
                {
                    Passenger = new Entity(3, 1),
                });
            });

            Assert.Null(ex);
        }

        // ── CombatComponentRegistry (EDIT1-E002) ─────────────────────────────

        [Fact]
        public void CombatRegistry_PublishSeedTargetCommand_DoesNotThrow()
        {
            using var world = new EntityRepository();
            HrotSharedComponentRegistry.RegisterAll(world);
            CombatComponentRegistry.RegisterAll(world);

            var ex = Record.Exception(() =>
            {
                world.Bus.Publish(new SeedTargetCommand
                {
                    Perceiver  = new Entity(5, 1),
                    Target     = new Entity(6, 1),
                    ScoreBoost = 50f,
                });
            });

            Assert.Null(ex);
        }
    }
}
