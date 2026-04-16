using System.Numerics;
using Fdp.Core;
using Hrot.Map.Common.Events;
using Xunit;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// Round-trip publish/consume tests for the zone managed events introduced by EDIT1-E003.
    /// Verifies that managed events can be published and consumed without registration.
    /// </summary>
    public class ZoneCommandRoundTripTests
    {
        // ── SpawnZoneObstacleCommand ──────────────────────────────────────────

        [Fact]
        public void SpawnZoneObstacleCommand_RoundTrip_ReturnsCorrectZoneName()
        {
            using var bus = new FdpEventBus();

            bus.PublishManaged(new SpawnZoneObstacleCommand
            {
                ZoneName = "Obstacle_Alpha",
                Position = new Vector2(10f, 20f),
                Radius   = 5f,
            });
            bus.SwapBuffers();

            var events = bus.ReadManaged<SpawnZoneObstacleCommand>();

            var evt = Assert.Single(events);
            Assert.Equal("Obstacle_Alpha", evt.ZoneName);
        }

        [Fact]
        public void SpawnZoneObstacleCommand_RoundTrip_ReturnsCorrectRadius()
        {
            using var bus = new FdpEventBus();

            bus.PublishManaged(new SpawnZoneObstacleCommand
            {
                ZoneName = "z1",
                Position = new Vector2(0f, 0f),
                Radius   = 7.25f,
            });
            bus.SwapBuffers();

            var events = bus.ReadManaged<SpawnZoneObstacleCommand>();

            var evt = Assert.Single(events);
            Assert.Equal(7.25f, evt.Radius);
        }

        // ── UpdateZoneConfigCommand ──────────────────────────────────────────

        [Fact]
        public void UpdateZoneConfigCommand_RoundTrip_ReturnsCorrectRoadNetworkPath()
        {
            using var bus = new FdpEventBus();

            bus.PublishManaged(new UpdateZoneConfigCommand
            {
                ZoneName        = "Zone_North",
                RoadNetworkPath = "data/zones/north_roads.json",
            });
            bus.SwapBuffers();

            var events = bus.ReadManaged<UpdateZoneConfigCommand>();

            var evt = Assert.Single(events);
            Assert.Equal("Zone_North",                   evt.ZoneName);
            Assert.Equal("data/zones/north_roads.json", evt.RoadNetworkPath);
        }

        [Fact]
        public void UpdateZoneConfigCommand_RoundTrip_NullRoadNetworkPath_IsPreserved()
        {
            using var bus = new FdpEventBus();

            bus.PublishManaged(new UpdateZoneConfigCommand
            {
                ZoneName        = "Zone_South",
                RoadNetworkPath = null,
            });
            bus.SwapBuffers();

            var events = bus.ReadManaged<UpdateZoneConfigCommand>();

            var evt = Assert.Single(events);
            Assert.Null(evt.RoadNetworkPath);
        }
    }
}
