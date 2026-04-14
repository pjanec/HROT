using Fdp.Kernel;
using Fdp.Toolkit.Behavior.Events;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// Round-trip publish/consume tests for the embarkation command events
    /// introduced by EDIT1-E001.
    /// </summary>
    public class EmbarkDisembarkCommandTests
    {
        // ── EmbarkEntityCommand ───────────────────────────────────────────────

        [Fact]
        public void EmbarkEntityCommand_RoundTrip_ReturnsSamePassenger()
        {
            using var bus = new FdpEventBus();

            var passenger = new Entity(7, 1);
            var vehicle   = new Entity(42, 2);

            bus.Publish(new EmbarkEntityCommand { Passenger = passenger, Vehicle = vehicle });
            bus.SwapBuffers();

            var events = bus.Consume<EmbarkEntityCommand>();

            var evt = Assert.Single(events.ToArray());
            Assert.Equal(passenger, evt.Passenger);
        }

        [Fact]
        public void EmbarkEntityCommand_RoundTrip_ReturnsSameVehicle()
        {
            using var bus = new FdpEventBus();

            var passenger = new Entity(7, 1);
            var vehicle   = new Entity(42, 2);

            bus.Publish(new EmbarkEntityCommand { Passenger = passenger, Vehicle = vehicle });
            bus.SwapBuffers();

            var events = bus.Consume<EmbarkEntityCommand>();

            var evt = Assert.Single(events.ToArray());
            Assert.Equal(vehicle, evt.Vehicle);
        }

        // ── DisembarkEntityCommand ────────────────────────────────────────────

        [Fact]
        public void DisembarkEntityCommand_RoundTrip_ReturnsSamePassenger()
        {
            using var bus = new FdpEventBus();

            var passenger = new Entity(13, 3);

            bus.Publish(new DisembarkEntityCommand { Passenger = passenger });
            bus.SwapBuffers();

            var events = bus.Consume<DisembarkEntityCommand>();

            var evt = Assert.Single(events.ToArray());
            Assert.Equal(passenger, evt.Passenger);
        }
    }
}
