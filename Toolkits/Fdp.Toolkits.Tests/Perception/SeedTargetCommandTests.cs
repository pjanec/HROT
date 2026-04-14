using Fdp.Kernel;
using FDP.Toolkit.Perception.Events;
using Xunit;

namespace FDP.Toolkit.Perception.Tests
{
    /// <summary>
    /// Round-trip publish/consume tests for <see cref="SeedTargetCommand"/>
    /// introduced by EDIT1-E002.
    /// </summary>
    public class SeedTargetCommandTests
    {
        [Fact]
        public void SeedTargetCommand_RoundTrip_ReturnsSamePerceiverAndTarget()
        {
            using var bus = new FdpEventBus();

            var perceiver = new Entity(5, 1);
            var target    = new Entity(9, 2);

            bus.Publish(new SeedTargetCommand { Perceiver = perceiver, Target = target, ScoreBoost = 100f });
            bus.SwapBuffers();

            var events = bus.Consume<SeedTargetCommand>();

            var evt = Assert.Single(events.ToArray());
            Assert.Equal(perceiver, evt.Perceiver);
            Assert.Equal(target,    evt.Target);
        }

        [Fact]
        public void SeedTargetCommand_RoundTrip_ReturnsSameScoreBoost()
        {
            using var bus = new FdpEventBus();

            bus.Publish(new SeedTargetCommand
            {
                Perceiver   = new Entity(1, 1),
                Target      = new Entity(2, 1),
                ScoreBoost  = 75.5f,
            });
            bus.SwapBuffers();

            var events = bus.Consume<SeedTargetCommand>();

            var evt = Assert.Single(events.ToArray());
            Assert.Equal(75.5f, evt.ScoreBoost);
        }
    }
}
