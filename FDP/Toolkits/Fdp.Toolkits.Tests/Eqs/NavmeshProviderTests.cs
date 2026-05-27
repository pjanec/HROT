using System;
using System.Numerics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Spatial.Eqs;
using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>
    /// Unit tests for <see cref="StubNavmeshProvider"/> (NAV-P0-T3).
    /// </summary>
    public class NavmeshProviderTests
    {
        // T-NP1: StubNavmeshProvider.PathCost returns flat-earth Euclidean (XZ) distance.
        [Fact]
        public void StubNavmeshProvider_PathCost_ReturnsEuclideanDistance()
        {
            var nav = new StubNavmeshProvider();
            // 3-4-5 right triangle in the XZ plane; Y is intentionally non-zero to confirm it is ignored.
            var from = new Vector3(0f, 99f, 0f);
            var to   = new Vector3(3f, 99f, 4f);
            float cost = nav.PathCost(from, to);
            Assert.True(Math.Abs(cost - 5f) < 0.001f, "Expected flat-earth XZ distance 5");
        }

        // T-NP2: StubNavmeshProvider.PlanPath returns two waypoints (start + end).
        [Fact]
        public void StubNavmeshProvider_PlanPath_ReturnsTwoWaypoints()
        {
            var nav  = new StubNavmeshProvider();
            var from = new Vector3(0f, 0f, 0f);
            var to   = new Vector3(10f, 0f, 5f);
            Span<NavWaypoint> waypoints = stackalloc NavWaypoint[4];
            int count = nav.PlanPath(from, to, waypoints);
            Assert.Equal(2, count);
            Assert.Equal(from, waypoints[0].Position);
            Assert.Equal(to,   waypoints[1].Position);
        }
    }
}
