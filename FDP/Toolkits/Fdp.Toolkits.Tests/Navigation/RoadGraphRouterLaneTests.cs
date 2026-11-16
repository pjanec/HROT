using System.Collections.Generic;
using System.Numerics;
using CarKinem.Road;
using Fdp.Toolkit.Navigation;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// ⭐ <c>CE-9003</c> (C-7, R-9006; 📄 docs/designs/vehicle-tactics/DESIGN_Road_Convoy.md) — a road leg drives its LANE: on a two-lane road (one lane each way) the
    /// emitted points (when the planner asks — <c>RoadUse.StronglyPrefer</c>, the convoy's) sit half a lane right of the centre line in the direction of travel — east-bound on the south side, west-bound on the north side; a
    /// one-lane road keeps the centre line. 🔴 Red-proof: <c>LaneOffset</c> returning 0 ⇒ both directions on the centre line ⇒ red.
    /// </summary>
    public sealed class RoadGraphRouterLaneTests
    {
        private static RoadNetworkBlob Road(int lanes, bool centres = true)
        {
            var b = new RoadNetworkBuilder();
            b.AddNode(new Vector2(0, 0)); b.AddNode(new Vector2(200, 0));
            b.AddSegment(new Vector2(0, 0), new Vector2(200, 0), new Vector2(200, 0), new Vector2(200, 0), 13.9f, 3.5f, lanes, 0, 1);
            var blob = b.Build(50f, 8, 8);
            blob.LinesAreCentres = centres;
            return blob;
        }

        private static List<Vector3> Leg(in RoadNetworkBlob road, Vector2 from, Vector2 to, bool keepLane = true)
        {
            var router = new RoadGraphRouter { KeepLane = keepLane };
            var a = RoadGraphRouter.NearestAccess(road, from, 30f); var b = RoadGraphRouter.NearestAccess(road, to, 30f);
            Assert.True(router.Route(road, a, b, out _));
            var pts = new List<Vector3>();
            router.EmitLeg(road, pts, (_, _, _) => 0f);
            return pts;
        }

        [Fact]
        public void ATwoLaneRoad_IsDrivenInTheRightLane_EachWay_AOneLaneRoad_OnItsCentreLine()
        {
            var two = Road(2);
            try
            {
                var east = Leg(two, new Vector2(20, 0), new Vector2(180, 0));
                var west = Leg(two, new Vector2(180, 0), new Vector2(20, 0));
                Assert.All(east.GetRange(1, east.Count - 2), p => Assert.Equal(-1.75f, p.Y, 2));   // east-bound: south of the centre line
                Assert.All(west.GetRange(1, west.Count - 2), p => Assert.Equal(1.75f, p.Y, 2));    // west-bound: north of it
                Assert.All(Leg(two, new Vector2(20, 0), new Vector2(180, 0), keepLane: false), p => Assert.Equal(0f, p.Y, 2));   // not asked: the centre line, as before
            }
            finally { two.Dispose(); }
            var laneLines = Road(2, centres: false);   // a net whose lines ARE lanes (imported from SUMO): driven as drawn
            try { Assert.All(Leg(laneLines, new Vector2(20, 0), new Vector2(180, 0)), p => Assert.Equal(0f, p.Y, 2)); }
            finally { laneLines.Dispose(); }
            var one = Road(1);
            try { Assert.All(Leg(one, new Vector2(20, 0), new Vector2(180, 0)), p => Assert.Equal(0f, p.Y, 2)); }
            finally { one.Dispose(); }
        }
    }
}
