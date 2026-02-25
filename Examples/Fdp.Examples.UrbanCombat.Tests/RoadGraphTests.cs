using System.Numerics;
using CarKinem.Road;
using Fdp.Examples.UrbanCombat.Setup;
using Xunit;

namespace Fdp.Examples.UrbanCombat.Tests
{
    /// <summary>
    /// BCS-P7-T3 road graph geometry tests.
    /// Verifies the 4-way city intersection created by <see cref="DemoEnvironmentSetup.CreateCityIntersection"/>.
    /// </summary>
    public class RoadGraphTests
    {
        // ── Test 1: Node count ──────────────────────────────────────────────────────

        [Fact]
        public void DemoEnvironment_Intersection_Has5Nodes()
        {
            using var blob = DemoEnvironmentSetup.CreateCityIntersection();
            Assert.Equal(5, blob.Nodes.Length);
        }

        // ── Test 2: Segment count ───────────────────────────────────────────────────

        [Fact]
        public void DemoEnvironment_Intersection_Has8Segments()
        {
            using var blob = DemoEnvironmentSetup.CreateCityIntersection();
            Assert.Equal(8, blob.Segments.Length);
        }

        // ── Test 3: Centre node at origin ───────────────────────────────────────────

        [Fact]
        public void DemoEnvironment_Intersection_CenterNodeAtOrigin()
        {
            using var blob = DemoEnvironmentSetup.CreateCityIntersection();
            // Node index 0 is the intersection centre (see DemoEnvironmentSetup XML doc).
            var centre = blob.Nodes[0].Position;
            Assert.Equal(0f, centre.X);
            Assert.Equal(0f, centre.Y);
        }

        // ── Test 4 (bonus): Arm endpoints at ±100m ──────────────────────────────────

        [Fact]
        public void DemoEnvironment_Intersection_ArmEndpointsAt100m()
        {
            using var blob = DemoEnvironmentSetup.CreateCityIntersection();

            // Node 1 = North (0, +100)
            var north = blob.Nodes[1].Position;
            Assert.Equal(0f,    north.X);
            Assert.Equal(100f,  north.Y);

            // Node 2 = South (0, -100)
            var south = blob.Nodes[2].Position;
            Assert.Equal(0f,   south.X);
            Assert.Equal(-100f, south.Y);

            // Node 3 = East (+100, 0)
            var east = blob.Nodes[3].Position;
            Assert.Equal(100f, east.X);
            Assert.Equal(0f,   east.Y);

            // Node 4 = West (-100, 0)
            var west = blob.Nodes[4].Position;
            Assert.Equal(-100f, west.X);
            Assert.Equal(0f,    west.Y);
        }
    }
}
