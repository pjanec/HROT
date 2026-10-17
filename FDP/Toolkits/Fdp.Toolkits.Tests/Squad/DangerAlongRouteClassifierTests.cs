using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Terrain;
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
            int n = DangerAlongRouteClassifier.Classify(route, in roads, null, null, 10f, areas);
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
            int n = DangerAlongRouteClassifier.Classify(new[] { new Vector3(200, 50, 0), new Vector3(200, 150, 0) }, in track, null, null, 10f, areas);
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
                new[] { new Vector3(100, 100, 0), new Vector3(100, 300, 0), new Vector3(300, 300, 0) }, in roads, null, null, 10f, one);
            Assert.Equal(1, n);
        }

        // ── ⭐ CE-3081 (§10.7b) — OpenGround: an off-road run of ≥ 20 m with no crouch cover within the corridor ─────────────

        /// <summary>A flat world with the given solid pieces (walls: <paramref name="pieces"/> as min, max, height), and its cover map.</summary>
        private static ICoverProvider Cover(params (Vector2 Min, Vector2 Max, float Height)[] pieces)
            => TerrainCoverProvider.Build(new TerrainWorld
            {
                BoundsMin = Vector2.Zero, BoundsMax = new Vector2(400, 400),
                Prisms = pieces.Select(p => new TerrainPrism
                {
                    Kind = TerrainPrismKind.Wall,
                    Footprint = new[] { p.Min, new Vector2(p.Max.X, p.Min.Y), p.Max, new Vector2(p.Min.X, p.Max.Y) },
                    BaseZ = 0, TopZ = p.Height, Min = p.Min, Max = p.Max,
                }).ToArray(),
            });

        private static DangerAreaDescriptor[] RunWith(ICoverProvider cover, params Vector3[] route)
        {
            var areas = new DangerAreaDescriptor[8];
            using var roads = TestTownRoads.Build();
            int n = DangerAlongRouteClassifier.Classify(route, in roads, null, cover, 10f, areas);
            return areas.AsSpan(0, n).ToArray();
        }

        /// <summary>⭐⭐ <c>CE-3081</c> — an OPEN run is found on each side of a road, in route order, and the road run between them is
        /// still the same StreetCrossing it was without a cover map (A3: a road sample is never open). 🔴 Red before: no OpenGround kind.</summary>
        [Fact]
        public void CE3081_AnOpenRun_IsFound_EitherSideOfARoad_AndTheRoadIsStillAStreetCrossing()
        {
            var areas = RunWith(Cover(), new Vector3(100, 300, 0), new Vector3(300, 300, 0));
            Assert.Equal(new[] { DangerAreaKind.OpenGround, DangerAreaKind.StreetCrossing, DangerAreaKind.OpenGround }, areas.Select(a => a.Kind));
            Assert.Equal(0f, areas[0].DistanceAlongRoute);
            Assert.InRange(areas[0].ExtentsXY.X * 2f, 88f, 91f);              // 100 → 189.5
            Assert.InRange(areas[2].DistanceAlongRoute, 110f, 112f);          // from 210.5
            Assert.InRange(areas[2].NearSideHandle.X, 206f, 208f);           // 4 m short of it
            Assert.Equal(0f, areas[0].ThreatRating);
            var crossing = Assert.Single(Run(new Vector3(100, 300, 0), new Vector3(300, 300, 0)));   // the same route, no cover map
            Assert.Equal(crossing, areas[1]);
            Assert.NotEqual(areas[0].FeatureId, areas[2].FeatureId);
        }

        /// <summary>⭐ <c>CE-3081</c> (A1) — a 3 m wall 5 m beside the route is cover within the corridor: the run along it is not open,
        /// and the open ground resumes past it. ⭐ A 0.5 m wall (the Low Wall: prone-only cover) leaves the run open.</summary>
        [Fact]
        public void CE3081_AWallInsideTheCorridor_IsNotOpen_ALowWallIs()
        {
            var walled = RunWith(Cover((new Vector2(40, 55), new Vector2(120, 55.3f), 3f)), new Vector3(20, 50, 0), new Vector3(180, 50, 0));
            var past = Assert.Single(walled);                                 // 20 → ~30 is under 20 m; ~30 → ~130 has cover
            Assert.Equal(DangerAreaKind.OpenGround, past.Kind);
            Assert.InRange(past.DistanceAlongRoute, 105f, 115f);             // open again ~10 m past the wall's end (x 120)

            var low = Assert.Single(RunWith(Cover((new Vector2(40, 55), new Vector2(120, 55.3f), 0.5f)), new Vector3(20, 50, 0), new Vector3(180, 50, 0)));
            Assert.Equal(DangerAreaKind.OpenGround, low.Kind);
            Assert.InRange(low.ExtentsXY.X * 2f, 159f, 161f);
        }

        /// <summary>⭐ <c>CE-3081</c> (A2) — an open run shorter than <see cref="DangerAlongRouteClassifier.MinOpenRun"/> is a gap, not a
        /// field; and with no cover map there is no OpenGround at all (A6).</summary>
        [Fact]
        public void CE3081_AShortRun_IsNotOpen_AndNoCoverMapMeansNoOpenGround()
        {
            Assert.Empty(RunWith(Cover(), new Vector3(20, 20, 0), new Vector3(35, 20, 0)));
            Assert.Single(RunWith(Cover(), new Vector3(20, 20, 0), new Vector3(45, 20, 0)));
            Assert.Empty(Run(new Vector3(20, 20, 0), new Vector3(150, 20, 0)));
        }

        /// <summary>⭐ <c>CE-3081</c> (A4) — the FeatureId hashes the run's EXIT, so it holds while the unit walks up to and into the open
        /// ground (the route is re-planned from the unit, so the run's START moves).</summary>
        [Fact]
        public void CE3081_TheOpenRunsFeatureId_IsStable_AsTheUnitWalksIn()
        {
            var cover = Cover((new Vector2(0, 40), new Vector2(30, 60), 3f), (new Vector2(150, 40), new Vector2(170, 60), 3f));
            uint before = RunWith(cover, new Vector3(10, 30, 0), new Vector3(160, 30, 0)).Single(a => a.Kind == DangerAreaKind.OpenGround).FeatureId;
            uint inside = RunWith(cover, new Vector3(80, 30, 0), new Vector3(160, 30, 0)).Single(a => a.Kind == DangerAreaKind.OpenGround).FeatureId;
            Assert.Equal(before, inside);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3081</c> item B — THE DEMO STRETCH, on the REAL test-town (its world file and cover map): <c>ua-squad-march-loose</c>'s
        /// leg from (72, 58) to (175, 60) is ONE open run of ~103 m — Shed Row's cover is 50 m north, the Low Wall (y 100) is prone-only,
        /// the Cross Street band starts at x 190. Behaviours' <c>CE-2161</c> (the live "Bound" demo) marches a squad along it.
        /// </summary>
        [Fact]
        public void CE3081_TestTown_TheLooseMarchLeg_IsOneOpenRun()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Hrot", "Subsystems"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var folder = Path.Combine(dir!.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "test-town");
            var world = TerrainWorldParser.Parse(File.ReadAllText(Directory.GetFiles(folder, "*.world.geojson").Single()), "test-town",
                TerrainAssets.ForFolder(folder));
            var a = Assert.Single(RunWith(TerrainCoverProvider.Build(world), new Vector3(72, 58, 0), new Vector3(175, 60, 0)));
            Assert.Equal(DangerAreaKind.OpenGround, a.Kind);
            Assert.InRange(a.ExtentsXY.X * 2f, 100f, 104f);
        }

        /// <summary>
        /// ⭐ <c>CE-3227</c> (civilians, docs/DESIGN_Civilians_And_Crowd_Control.md §7.3d-B G-3/G-6) — a route through a gas footprint gets ONE
        /// GasCloud area, its handles the detour points short of and beyond it, its id the cloud's (stable as the unit walks up); with a
        /// crossing AFTER the gas but ending sooner, the areas still come out in route order. ✅ Red-proofs: no gas pass ⇒ no GasCloud ⇒ red;
        /// no sort ⇒ the crossing first ⇒ red.
        /// </summary>
        [Fact]
        public void CE3227_ARouteThroughGas_GetsOneGasArea_InRouteOrder()
        {
            var areas = new DangerAreaDescriptor[8];
            using var roads = TestTownRoads.Build();
            // a capsule from x 150 to x 240 along y 300 (radius 8): the route walks east through it and over Cross Street (x 190–210) inside it
            var gas = new[] { new GasFootprint(77L, new Vector2(150, 300), new Vector2(240, 300), 8f, -2f, 5f) };
            int n = DangerAlongRouteClassifier.Classify(new[] { new Vector3(100, 300, 0), new Vector3(300, 300, 0) }, in roads, null, null, 10f, areas, gas);
            Assert.Equal(2, n);
            Assert.Equal(DangerAreaKind.GasCloud, areas[0].Kind);
            Assert.Equal(DangerAreaKind.StreetCrossing, areas[1].Kind);
            Assert.True(areas[0].DistanceAlongRoute < areas[1].DistanceAlongRoute);
            Assert.InRange(areas[0].DistanceAlongRoute, 41f, 43f);          // the footprint starts at x 142
            Assert.InRange(areas[0].NearSideHandle.X, 137f, 139f);           // 4 m short of it
            Assert.InRange(areas[0].FarSideHandle.X, 251f, 253f);            // 4 m beyond its end (x 248)
            Assert.Equal(DangerAlongRouteClassifier.GasFeatureId(77L), areas[0].FeatureId);

            int again = DangerAlongRouteClassifier.Classify(new[] { new Vector3(130, 300, 0), new Vector3(300, 300, 0) }, in roads, null, null, 10f, areas, gas);
            Assert.Equal(DangerAlongRouteClassifier.GasFeatureId(77L), areas[0].FeatureId);   // nearer, the same area
            Assert.Equal(2, again);
        }

        /// <summary>
        /// ⭐ <c>CE-3227</c> (G-4) — a map with NO road graph still answers for gas (it answered nothing before); a route that misses the
        /// footprint, or passes under a cloud's height band, gets nothing. ✅ Red-proof: the old early return on no roads ⇒ 0 ⇒ red.
        /// </summary>
        [Fact]
        public void CE3227_WithNoRoads_GasStillAnswers_AndAMissIsNothing()
        {
            var areas = new DangerAreaDescriptor[8];
            var none = default(global::CarKinem.Road.RoadNetworkBlob);
            var gas = new[] { new GasFootprint(5L, new Vector2(50, 0), new Vector2(50, 0), 7f, -2f, 5f) };
            var route = new[] { new Vector3(0, 0, 0), new Vector3(100, 0, 0) };
            Assert.Equal(1, DangerAlongRouteClassifier.Classify(route, in none, null, null, 10f, areas, gas));
            Assert.Equal(DangerAreaKind.GasCloud, areas[0].Kind);
            Assert.Equal(0, DangerAlongRouteClassifier.Classify(route, in none, null, null, 10f, areas));     // no gas, no roads: nothing
            Assert.Equal(0, DangerAlongRouteClassifier.Classify(new[] { new Vector3(0, 20, 0), new Vector3(100, 20, 0) }, in none, null, null, 10f, areas, gas));
            Assert.Equal(0, DangerAlongRouteClassifier.Classify(new[] { new Vector3(0, 0, 30), new Vector3(100, 0, 30) }, in none, null, null, 10f, areas, gas));
            Assert.False(DangerAreaKindsOf.Wants(DangerAreaKinds.Crossings, DangerAreaKind.GasCloud));          // never in the v1 default
            Assert.True(DangerAreaKindsOf.Wants(DangerAreaKinds.GasCloud, DangerAreaKind.GasCloud));
        }
    }
}
