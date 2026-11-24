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

        /// <summary>⭐ <c>CE-2463</c> — walking ALONG Cross Street through the centre square: the street is no crossing, but the junction on
        /// it is an Intersection, halted at the building line (<see cref="DangerAlongRouteClassifier.JunctionStandOff"/> short of the junction).</summary>
        [Fact]
        public void CE2463_AlongAStreet_ThroughAJunction_IsAnIntersection_HaltedShortOfIt()
        {
            var a = Assert.Single(Run(new Vector3(200, 20, 0), new Vector3(200, 380, 0)));
            Assert.Equal(DangerAreaKind.Intersection, a.Kind);
            Assert.InRange(a.DistanceAlongRoute, 169f, 171f);               // the junction's band starts at y 190 (radius 10)
            Assert.InRange(a.NearSideHandle.Y, 177f, 179f);                  // 12 m short of it — not the crossing's 4 m
            Assert.InRange(a.FarSideHandle.Y, 213f, 215f);                   // 4 m beyond it
            Assert.InRange(a.Center.Y, 198f, 202f);
        }

        /// <summary>⭐ <c>CE-2463</c> — the junction's id holds while the unit walks up the street to it (the drill keys on it).</summary>
        [Fact]
        public void CE2463_AlongAStreet_TheJunctionsFeatureId_IsStable_AsTheUnitWalksUp()
        {
            var far = Assert.Single(Run(new Vector3(200, 20, 0), new Vector3(200, 380, 0)));
            var near = Assert.Single(Run(new Vector3(200, 150, 0), new Vector3(200, 380, 0)));
            Assert.Equal(far.FeatureId, near.FeatureId);
            Assert.InRange(near.DistanceAlongRoute, 39f, 41f);
        }

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
            var folder = Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!), "test-town");
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

        /// <summary>
        /// ⭐⭐ <c>CE-4583</c> (AQ101 §R D, R-2071 E-4, R-4227) — <b>the shelled area</b>: three bursts within 60 s and 15 m ⇒ ONE shelled point, and a route
        /// through it gets one <see cref="DangerAreaKind.Shelled"/> area (through the gas seam: one footprint, one run detection); two bursts ⇒ none;
        /// three spread over more than 60 s ⇒ none; a burst 40 m off is a point of its own; two minutes with no burst ⇒ gone. The kind is wanted only when
        /// asked (never in the v1 default). 🔴 Red-proofs: the 60 s window not applied ⇒ the slow three shell · the footprint's kind ignored by the
        /// classifier ⇒ a GasCloud area.
        /// </summary>
        [Fact]
        public void CE4583_ThreeBurstsInAMinute_MakeOneShelledArea_TwoDoNot_AndItCoolsAfterTwoMinutes()
        {
            using var world = new Fdp.Core.EntityRepository();
            var heat = ShelledAreas.For(world);
            var into = new GasFootprint[8];
            heat.AddBurst(new Vector3(50, 0, 0), 10);
            heat.AddBurst(new Vector3(58, 4, 0), 30);
            Assert.Equal(0, ShelledAreas.Footprints(world, into));                     // two bursts: not shelled
            heat.AddBurst(new Vector3(90, 0, 0), 35);                                  // 40 m off: a point of its own
            Assert.Equal(2, heat.Points.Length);
            heat.AddBurst(new Vector3(46, -3, 0), 55);                                 // the third within 60 s and 15 m ⇒ shelled
            Assert.Equal(1, ShelledAreas.Footprints(world, into));
            Assert.Equal(DangerAreaKind.Shelled, into[0].Kind);

            var areas = new DangerAreaDescriptor[8];
            var none = default(global::CarKinem.Road.RoadNetworkBlob);
            var route = new[] { new Vector3(0, 0, 0), new Vector3(150, 0, 0) };
            Assert.Equal(1, DangerAlongRouteClassifier.Classify(route, in none, null, null, 10f, areas, into.AsSpan(0, 1)));
            Assert.Equal(DangerAreaKind.Shelled, areas[0].Kind);
            Assert.NotEqual(DangerAlongRouteClassifier.GasFeatureId(into[0].CloudId), areas[0].FeatureId);   // never a gas cloud's id
            Assert.True(DangerAreaKindsOf.Wants(DangerAreaKinds.Shelled, DangerAreaKind.Shelled));
            Assert.False(DangerAreaKindsOf.Wants(DangerAreaKinds.Crossings, DangerAreaKind.Shelled));

            heat.Cool(55 + 119);
            Assert.Equal(1, ShelledAreas.Footprints(world, into));                     // still cooling
            heat.Cool(55 + 121);
            Assert.Equal(0, ShelledAreas.Footprints(world, into));                     // two minutes with no burst: gone
            Assert.Equal(0, heat.Points.Length);

            heat.AddBurst(new Vector3(0, 200, 0), 300);
            heat.AddBurst(new Vector3(2, 200, 0), 340);
            heat.AddBurst(new Vector3(4, 200, 0), 370);                                // three, but over 70 s
            Assert.Equal(0, ShelledAreas.Footprints(world, into));
        }

        /// <summary>
        /// ⭐ <c>CE-5045</c> (Stechovice showcase, terrain lane) — the junction test reads each node's degree and radius from the graph's one derived index
        /// (<see cref="Fdp.Toolkit.Navigation.RoadGraphIndex"/>) instead of scanning every segment per node per point (19 257 × 19 691 per route point on
        /// Stechovice: the EQS commit held for minutes). On a jittered lattice (junctions of 3 and 4, two lane widths) the first junction is the
        /// former scan's, point for point; and a point on a 20 k-node graph is answered within a budget.
        /// 🔴 Red-proof: the index's radius left at 0 ⇒ no junction is ever found ⇒ red.
        /// </summary>
        [Fact]
        public void CE5045_TheJunctionTest_IsTheFormerScan_AndALargeGraphAnswersFast()
        {
            var rng = new Random(17);
            var b = new global::CarKinem.Road.RoadNetworkBuilder();
            const int W = 20;
            var at = new Vector2[W * W];
            for (int y = 0; y < W; y++) for (int x = 0; x < W; x++)
            { at[y * W + x] = new Vector2(x * 40f + (float)rng.NextDouble() * 6f, y * 40f + (float)rng.NextDouble() * 6f); b.AddNode(at[y * W + x]); }
            void Seg(int a, int c) { var d = at[c] - at[a]; b.AddSegment(at[a], d, at[c], d, 13.9f, 3.5f, rng.Next(1, 4), a, c); }
            for (int y = 0; y < W; y++) for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                if (x + 1 < W && rng.Next(5) > 0) Seg(i, i + 1);
                if (y + 1 < W && rng.Next(5) > 0) Seg(i, i + W);
            }
            using var roads = new RoadsHolder(b.Build(100f, 10, 10));
            int junctions = 0, bands = 0;
            for (int k = 0; k < 3000; k++)
            {
                var n = at[rng.Next(at.Length)];
                var p = n + new Vector2((float)(rng.NextDouble() - 0.5) * 16f, (float)(rng.NextDouble() - 0.5) * 16f);
                ulong mask = DangerAlongRouteClassifier.RoadMask(roads.Blob, p);
                if (mask == 0) continue;
                int old = FormerJunction(roads.Blob, p);
                if ((mask & DangerAlongRouteClassifier.JunctionBit) != 0) { junctions++; Assert.Equal(DangerAlongRouteClassifier.JunctionBit | (1ul << (old % 63)), mask); }
                else { bands++; Assert.Equal(-1, old); }
            }
            Assert.True(junctions > 100 && bands > 100, $"both answers exercised: junctions {junctions}, bands {bands}");

            var big = new global::CarKinem.Road.RoadNetworkBuilder();
            const int B = 140;
            for (int y = 0; y < B; y++) for (int x = 0; x < B; x++) big.AddNode(new Vector2(x * 25f, y * 25f));
            for (int y = 0; y < B; y++) for (int x = 0; x < B; x++)
            {
                int i = y * B + x; var pa = new Vector2(x * 25f, y * 25f);
                if (x + 1 < B) big.AddSegment(pa, new Vector2(25, 0), pa + new Vector2(25, 0), new Vector2(25, 0), 13.9f, 3.5f, 2, i, i + 1);
                if (y + 1 < B) big.AddSegment(pa, new Vector2(0, 25), pa + new Vector2(0, 25), new Vector2(0, 25), 13.9f, 3.5f, 2, i, i + B);
            }
            using var large = new RoadsHolder(big.Build(200f, 20, 20));
            DangerAlongRouteClassifier.RoadMask(large.Blob, new Vector2(1000f, 1000f));   // builds the index once
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int k = 0; k < 20; k++) Assert.NotEqual(0ul, DangerAlongRouteClassifier.RoadMask(large.Blob, new Vector2(1000f + k * 25f, 1000f)));
            sw.Stop();
            Assert.True(sw.Elapsed.TotalMilliseconds / 20 < 20.0, $"a point on a 19 600-node graph took {sw.Elapsed.TotalMilliseconds / 20:0.0} ms (the former scan: seconds)");
        }

        // The former junction test, verbatim: the first node (ascending) with three or more incident segments within its widest band; −1 none.
        private static int FormerJunction(in global::CarKinem.Road.RoadNetworkBlob roads, Vector2 p)
        {
            for (int n = 0; n < roads.Nodes.Length; n++)
            {
                int degree = 0; float radius = 0f;
                for (int i = 0; i < roads.Segments.Length; i++)
                {
                    var seg = roads.Segments[i];
                    if (seg.StartNodeIndex != n && seg.EndNodeIndex != n) continue;
                    degree++;
                    radius = MathF.Max(radius, 0.5f * seg.LaneWidth * Math.Max(1, seg.LaneCount));
                }
                if (degree >= 3 && Vector2.Distance(p, roads.Nodes[n].Position) <= radius) return n;
            }
            return -1;
        }

        private sealed class RoadsHolder : IDisposable
        {
            public global::CarKinem.Road.RoadNetworkBlob Blob;
            public RoadsHolder(global::CarKinem.Road.RoadNetworkBlob blob) => Blob = blob;
            public void Dispose() => Blob.Dispose();
        }
    }
}
