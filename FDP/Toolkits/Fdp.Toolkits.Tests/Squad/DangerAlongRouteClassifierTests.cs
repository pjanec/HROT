using System;
using System.Numerics;
using Fdp.Toolkit.Squad.DangerArea;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Tests.Squad
{
    /// <summary>
    /// ⭐ <c>CE-3072</c> B3 — the danger areas along a route (docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.7), over test-town's
    /// two roads: Main Street y 190–210 and Cross Street x 190–210, crossing at the centre.
    /// </summary>
    public sealed class DangerAlongRouteClassifierTests
    {
        private static TerrainWorld TestTown() => new()
        {
            Name = "test-town", BoundsMin = Vector2.Zero, BoundsMax = new Vector2(400, 400), GroundZ = 0f,
            Surfaces = new[]
            {
                Road(new Vector2(0, 190), new Vector2(400, 210)),   // Main Street
                Road(new Vector2(190, 0), new Vector2(210, 400)),   // Cross Street
            },
        };

        private static TerrainSurface Road(Vector2 min, Vector2 max) => new()
        {
            Type = TerrainSurfaceType.Road,
            Polygon = new[] { min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y) },
            Min = min, Max = max,
        };

        private static DangerAreaDescriptor[] Run(params Vector3[] route)
        {
            var areas = new DangerAreaDescriptor[8];
            int n = DangerAlongRouteClassifier.Classify(route, TestTown(), 10f, areas);
            return areas.AsSpan(0, n).ToArray();
        }

        [Fact]
        public void CE3072_B3_CrossingOneRoad_IsOneStreetCrossing_WithHandlesEitherSide()
        {
            var a = Assert.Single(Run(new Vector3(100, 300, 0), new Vector3(300, 300, 0)));
            Assert.Equal(DangerAreaKind.StreetCrossing, a.Kind);
            Assert.InRange(a.DistanceAlongRoute, 89f, 91f);                 // the road starts at x 190
            Assert.InRange(a.NearSideHandle.X, 184f, 187f);                  // 4 m short of it
            Assert.InRange(a.FarSideHandle.X, 213f, 216f);                   // 4 m beyond it
            Assert.InRange(a.Center.X, 198f, 202f);
            Assert.Equal(0f, a.ThreatRating);                                // ⛔ the solve never rates
            Assert.NotEqual(0u, a.FeatureId);
        }

        [Fact]
        public void CE3072_B3_TwoCrossings_InRouteOrder()
        {
            var areas = Run(new Vector3(100, 100, 0), new Vector3(100, 300, 0), new Vector3(300, 300, 0));
            Assert.Equal(2, areas.Length);
            Assert.True(areas[0].DistanceAlongRoute < areas[1].DistanceAlongRoute);
            Assert.InRange(areas[0].Center.Y, 198f, 202f);                   // Main Street, crossed going north
            Assert.InRange(areas[1].Center.X, 198f, 202f);                   // then Cross Street, going east
            Assert.NotEqual(areas[0].FeatureId, areas[1].FeatureId);
        }

        [Fact]
        public void CE3072_B3_FeatureId_IsStable_AsTheUnitWalksUpToAndThroughTheCrossing()
        {
            uint far = Assert.Single(Run(new Vector3(100, 300, 0), new Vector3(300, 300, 0))).FeatureId;
            uint near = Assert.Single(Run(new Vector3(170, 300, 0), new Vector3(300, 300, 0))).FeatureId;
            var inside = Assert.Single(Run(new Vector3(195, 300, 0), new Vector3(300, 300, 0)));
            Assert.Equal(far, near);
            Assert.Equal(far, inside.FeatureId);
            Assert.Equal(0f, inside.DistanceAlongRoute);                     // already on the road
        }

        [Fact]
        public void CE3072_B3_WalkingAlongAStreet_IsNotACrossing()
            => Assert.Empty(Run(new Vector3(200, 20, 0), new Vector3(200, 150, 0)));

        [Fact]
        public void CE3072_B3_ThroughTheCentreSquare_IsAnIntersection()
        {
            var a = Assert.Single(Run(new Vector3(170, 170, 0), new Vector3(230, 230, 0)));
            Assert.Equal(DangerAreaKind.Intersection, a.Kind);
        }

        [Fact]
        public void CE3072_B3_NoRoad_NoAreas_AndCapacityIsRespected()
        {
            Assert.Empty(Run(new Vector3(20, 20, 0), new Vector3(150, 150, 0)));
            var one = new DangerAreaDescriptor[1];
            int n = DangerAlongRouteClassifier.Classify(
                new[] { new Vector3(100, 100, 0), new Vector3(100, 300, 0), new Vector3(300, 300, 0) }, TestTown(), 10f, one);
            Assert.Equal(1, n);
        }
    }
}
