using System;
using System.Numerics;
using Fdp.Toolkit.Squad.DangerArea;
using Xunit;

namespace Fdp.Toolkit.Tests.Squad
{
    /// <summary>
    /// ⭐ <c>CE-3072</c> B3 — the danger areas along a route (docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.7), over test-town's
    /// two roads: Main Street y 190–210 and Cross Street x 190–210, crossing at the centre. ⭐ <c>CE-3128</c> (R-231) — the roads
    /// are now the road GRAPH (<see cref="TestTownRoads"/>: the same footprint as bands of 4 × 5 m lanes), not polygons.
    /// </summary>
    public sealed class DangerAlongRouteClassifierTests
    {
        private static DangerAreaDescriptor[] Run(params Vector3[] route)
        {
            var areas = new DangerAreaDescriptor[8];
            using var roads = TestTownRoads.Build();
            int n = DangerAlongRouteClassifier.Classify(route, in roads, null, 10f, areas);
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

        /// <summary>⭐ CE-3128 — a road graph whose node joins only two segments (a bend, not a junction) is never an intersection.</summary>
        [Fact]
        public void CE3128_ANodeOfDegreeTwo_IsNotAJunction()
        {
            var b = new global::CarKinem.Road.RoadNetworkBuilder();
            b.AddNode(new Vector2(0, 100)); b.AddNode(new Vector2(200, 100)); b.AddNode(new Vector2(400, 100));
            b.AddSegment(new Vector2(0, 100), new Vector2(200, 0), new Vector2(200, 100), new Vector2(200, 0), laneWidth: 5f, laneCount: 2, startNodeIdx: 0, endNodeIdx: 1);
            b.AddSegment(new Vector2(200, 100), new Vector2(200, 0), new Vector2(400, 100), new Vector2(200, 0), laneWidth: 5f, laneCount: 2, startNodeIdx: 1, endNodeIdx: 2);
            using var track = b.Build(10f, 40, 40);
            var areas = new DangerAreaDescriptor[8];
            int n = DangerAlongRouteClassifier.Classify(new[] { new Vector3(200, 50, 0), new Vector3(200, 150, 0) }, in track, null, 10f, areas);
            Assert.Equal(1, n);
            Assert.Equal(DangerAreaKind.StreetCrossing, areas[0].Kind);
            Assert.InRange(areas[0].ExtentsXY.X * 2f, 9f, 11f);   // a 10 m wide track (2 × 5 m lanes)
        }

        [Fact]
        public void CE3072_B3_NoRoad_NoAreas_AndCapacityIsRespected()
        {
            Assert.Empty(Run(new Vector3(20, 20, 0), new Vector3(150, 150, 0)));
            var one = new DangerAreaDescriptor[1];
            using var roads = TestTownRoads.Build();
            int n = DangerAlongRouteClassifier.Classify(
                new[] { new Vector3(100, 100, 0), new Vector3(100, 300, 0), new Vector3(300, 300, 0) }, in roads, null, 10f, one);
            Assert.Equal(1, n);
        }
    }
}
