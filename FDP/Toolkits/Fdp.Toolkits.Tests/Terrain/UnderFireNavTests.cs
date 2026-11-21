using System;
using System.Numerics;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Recast;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐⭐ CE-2468 UF-5 (docs/designs/minefields/DESIGN_Minefields.md §9b.1 K4, K8, K12) — ground a known enemy covers COSTS the side that knows it: two streets either side of a wood
    /// (wheels may not cross it — the <c>MinefieldNavTests</c> barrier), an enemy known to Blue on the short west street. Blue drives the long east street, Red (who knows nothing)
    /// the short west one; the same on the road graph. Real Recast mesh, the real <see cref="UnderFireSightSystem"/> publishing the maps in the serial pass.
    /// </summary>
    public sealed class UnderFireNavTests
    {
        private const int Blue = 1, Red = 2;
        private const uint Wheeled = (uint)NavLayerMask.Vehicle;

        /// <summary>300 × 600 m open ground, a wood across x 90..210, y 60..540: a short street west of it, a long one east.</summary>
        private const string TwoStreets = """
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,300,600],"groundZ":0},"features":[
             {"type":"Feature","properties":{"kind":"surface","surface":"forest"},
              "geometry":{"type":"Polygon","coordinates":[[[90,60],[210,60],[210,540],[90,540],[90,60]]]}}]}
            """;

        private static readonly Vector3 From = new(45, 20, 0), To = new(45, 580, 0), Enemy = new(45, 300, 0), RedsOwn = new(280, 300, 0);

        private sealed class Rig
        {
            public readonly EntityRepository R = new();
            public readonly TerrainWorld Terrain = TerrainWorldParser.Parse(TwoStreets, "t");
            public readonly UnderFireSightSystem Sight = new();

            public Rig()
            {
                R.RegisterComponent<UnderFireKnowledge>();
                R.SetSingletonManaged(Terrain);
            }

            /// <summary>Side <paramref name="side"/> knows an enemy at <paramref name="at"/> covering <paramref name="rangeM"/> (a <see cref="UnderFireCause.Direct"/> record).</summary>
            public void Know(int side, Vector3 at, float rangeM, UnderFireCause cause = UnderFireCause.Direct)
            {
                var k = new UnderFireKnowledge { Cause = cause, Point = at, RangeM = rangeM, EnemyStrength = 1f };
                k.Header.SubjectKind = SubjectKind.Place; k.Header.Kind = PlaceKnowledgeKind.UnderFire; k.Header.ObjectIndex = -1;
                k.Header.Subject = UnderFireRules.SubjectOf(cause, at); k.Header.SidePlus1 = (byte)(side + 1);
                k.Header.State = UnderFireKnowledge.StateActive; k.Header.LearnedAt = (SimInstant)1.0;
                R.AddComponent(R.CreateEntity(), k);
            }

            /// <summary>The serial pass, then the view's doors (what a path query on a worker thread is handed).</summary>
            public DoorStates Doors() { R.Tick(); Sight.Execute(R, 0f); return DoorStates.Of(R, Terrain); }
        }

        private static uint Mask(int side) => Wheeled | NavLayerSelection.SideBits(side);

        private static float MaxX(DotRecastNavmeshProvider nav, int side, DoorStates doors)
        {
            Span<NavWaypoint> w = stackalloc NavWaypoint[256];
            int n = nav.PlanPath(From, To, w, Mask(side), doors);
            Assert.True(n > 1, "a route exists");
            float x = 0f;
            for (int i = 0; i < n; i++) x = MathF.Max(x, w[i].Position.X);
            return x;
        }

        /// <summary>
        /// ⭐⭐ K4 / K8 — the covered street is avoided for the knowing side ONLY; an <see cref="UnderFireCause.Indirect"/> record is not priced.
        /// 🔴 Red-proof: drop the lookup in <c>DoorAwareQueryFilter.GetCost</c> ⇒ Blue takes the west street; drop the side test in <c>UnderFireSight.Covered</c> (Red's map prices Blue's grid too) ⇒ Red goes east too.
        /// </summary>
        [Fact]
        public void UF5_TheCoveredStreet_IsAvoidedByTheSideThatKnowsIt_Only()
        {
            var rig = new Rig();
            var nav = (DotRecastNavmeshProvider)new RecastNavmeshFactory { TileCache = null }.Build(rig.Terrain)!;
            rig.Know(Red, Enemy, 150f, UnderFireCause.Indirect);   // K8 — shelling is not ground a route pays for
            var doors = rig.Doors();
            Assert.True(MaxX(nav, Red, doors) < 90f, "Indirect: Red keeps the short west street");

            rig.Know(Blue, Enemy, 400f);   // the whole west street is in its sight (the polygons' corners too: K4 prices the covered SHARE)
            rig.Know(Red, RedsOwn, 30f);   // Red knows an enemy of its own, off its route: Red HAS a cost map, so the side test is exercised
            doors = rig.Doors();
            Assert.True(doors.UnderFireSight.HasSide((byte)(Blue + 1)) && doors.UnderFireSight.HasSide((byte)(Red + 1)));
            Assert.True(MaxX(nav, Blue, doors) >= 200f, "Blue knows the enemy on the west street: it drives east (round the wood, x 210)");
            Assert.True(MaxX(nav, Red, doors) < 90f, "Red knows nothing of it: the short west street");
            Assert.True(nav.PathCost(From, To, Mask(Blue), doors) > nav.PathCost(From, To, Mask(Red), doors));
        }

        /// <summary>
        /// ⭐⭐ UF-6 (§9b, K8, K10) — the danger solve reads the unit's SIDE: a Direct record of Blue's is ONE <see cref="Fdp.Toolkit.Squad.DangerArea.DangerAreaKind.CoveredByFire"/> area on a route through
        /// its seen ground (entered where the grid starts, left where it ends) and nothing for Red; an Indirect record of Red's is one <c>Shelled</c> footprint for Red only.
        /// 🔴 Red-proofs: drop the side test in <c>CoveredByFireAreas</c> ⇒ Red gets the area; drop it in <c>ShelledFootprints</c> ⇒ Blue gets Red's shelling.
        /// </summary>
        [Fact]
        public void UF6_TheDangerSolve_ReadsTheUnitsSide_CoveredByFireForDirect_ShelledForIndirect()
        {
            var rig = new Rig();
            rig.Know(Blue, Enemy, 150f);
            rig.Know(Red, new Vector3(45, 100, 0), 25f, UnderFireCause.Indirect);
            rig.Know(Red, RedsOwn, 30f);   // a Direct grid of Red's own, off the route: Red's side has grids, so the side test is exercised
            var doors = rig.Doors();
            var route = new[] { From, To };
            var areas = new Fdp.Toolkit.Squad.DangerArea.DangerAreaDescriptor[8];
            Assert.Equal(1, Fdp.Toolkit.Squad.DangerArea.DangerAlongRouteSolve.CoveredByFireAreas(doors.UnderFireSight, route, (byte)(Blue + 1), areas));
            var a = areas[0];
            Assert.Equal(Fdp.Toolkit.Squad.DangerArea.DangerAreaKind.CoveredByFire, a.Kind);
            Assert.InRange(a.Center.Y, 145f, 160f);            // the route enters the enemy's 150 m at y 150
            Assert.InRange(a.FarSideHandle.Y, 445f, 460f);     // and leaves it at y 450
            Assert.True(a.NearSideHandle.Y < a.Center.Y && a.FeatureId != 0);
            Assert.InRange(a.DistanceAlongRoute, 125f, 140f);
            Assert.Equal(0, Fdp.Toolkit.Squad.DangerArea.DangerAlongRouteSolve.CoveredByFireAreas(doors.UnderFireSight, route, (byte)(Red + 1), areas));

            var gas = new Fdp.Toolkit.Squad.DangerArea.GasFootprint[8];
            Assert.Equal(0, Fdp.Toolkit.Squad.DangerArea.DangerAlongRouteSolve.ShelledFootprints(doors.UnderFire, (byte)(Blue + 1), gas));   // Blue's record is Direct: no circle
            Assert.Equal(1, Fdp.Toolkit.Squad.DangerArea.DangerAlongRouteSolve.ShelledFootprints(doors.UnderFire, (byte)(Red + 1), gas));
            Assert.Equal((Fdp.Toolkit.Squad.DangerArea.DangerAreaKind.Shelled, 25f), (gas[0].Kind, gas[0].Radius));
            Assert.True(Fdp.Toolkit.Squad.DangerArea.DangerAreaKindsOf.Wants(Fdp.Toolkit.Squad.DangerArea.DangerAreaKinds.CoveredByFire, Fdp.Toolkit.Squad.DangerArea.DangerAreaKind.CoveredByFire));
            Assert.False(Fdp.Toolkit.Squad.DangerArea.DangerAreaKindsOf.Wants(Fdp.Toolkit.Squad.DangerArea.DangerAreaKinds.Crossings, Fdp.Toolkit.Squad.DangerArea.DangerAreaKind.CoveredByFire));
        }

        /// <summary>
        /// ⭐⭐ CE-2469 SP-1 (R-2140 R3) — the danger answer carries the planned route's STEER POINT, from the same per-side priced route: a Friend that knows the enemy on the
        /// west street steers east round the wood, a Hostile that does not steers straight up the west street; the answer reaches the sensor's buffer and
        /// <see cref="Fdp.Toolkit.Squad.DangerArea.DangerRouteSteer.Of"/> reads it while fresh (4 s), never after. 🔴 Red-proofs: steer from the straight fallback
        /// (no plan) ⇒ the unplannable target in the wood answers a steer; drop the copy in <c>DangerAreaSensorSystem.Apply</c> ⇒ <c>Of</c> answers false.
        /// </summary>
        [Fact]
        public void CE2469_TheSteerPoint_TakesThePerSidePricedRoute_AndReachesTheSensor()
        {
            var rig = new Rig();
            var r = rig.R;
            r.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>(); r.RegisterComponent<Fdp.Toolkit.Spatial.Eqs.EqsSensor>();
            r.RegisterComponent<Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(); r.RegisterComponent<Fdp.Toolkit.Perception.Components.SensorTag>();
            r.RegisterComponent<Fdp.Toolkit.Scenario.ScenarioIgnoreTag>(); r.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorOwnedPart>();
            r.RegisterComponent<Fdp.Toolkit.Squad.DangerArea.DangerAreaSensor>(); r.RegisterComponent<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>();
            r.RegisterComponent<SimTransform>(); r.RegisterComponent<EntityInfo>(); r.RegisterComponent<NavAgentProfile>();
            r.RegisterComponent<NavigationIntent>(); r.RegisterComponent<Fdp.Toolkit.Spatial.Eqs.EqsContextPoints>();
            r.RegisterManagedEvent<Fdp.Toolkit.Squad.DangerArea.DangerAreaResultEvent>();
            r.RegisterEvent<Fdp.Toolkit.Perception.Events.SensorChangedEvent>();
            var nav = (DotRecastNavmeshProvider)new RecastNavmeshFactory { TileCache = null }.Build(rig.Terrain)!;
            r.SetSingletonManaged<INavmeshProvider>(nav);
            rig.Know(Blue, Enemy, 400f);
            rig.Doors();
            var planner = new RoutePlanner();

            (Vector3 Steer, bool Has, Entity Unit) SolveFor(int side, Vector3? to = null)
            {
                var unit = r.CreateEntity();
                r.AddComponent(unit, new SimTransform { Position = From, Rotation = Quaternion.Identity });
                r.AddComponent(unit, new EntityInfo { ForceId = (ForceId)side });
                r.AddComponent(unit, new NavAgentProfile { PreferredLayerMask = Wheeled });
                var child = Fdp.Toolkit.Squad.DangerArea.DangerAreaChildSensor.Ensure(r, unit, 1, Fdp.Toolkit.Squad.DangerArea.DangerAreaSettings.ToPoint(to ?? To));
                var sensor = r.GetComponentRO<Fdp.Toolkit.Spatial.Eqs.EqsSensor>(child);
                var areas = new Fdp.Toolkit.Squad.DangerArea.DangerAreaDescriptor[8];
                int n = Fdp.Toolkit.Squad.DangerArea.DangerAlongRouteSolve.Solve(r, child, in sensor, areas, planner, null, out var steer, out bool has);
                r.Bus.PublishManaged(new Fdp.Toolkit.Squad.DangerArea.DangerAreaResultEvent
                {
                    ParentNetworkId = 0, LocalChildIndex = child.Index, Epoch = sensor.Epoch, RefreshTick = 3, Areas = areas, Count = n,
                    SteerPoint = steer, HasSteer = has ? (byte)1 : (byte)0,
                });
                return (steer, has, unit);
            }
            var friend = SolveFor(Blue);
            var hostile = SolveFor(Red);
            var nowhere = SolveFor(Red, new Vector3(150, 300, 0));   // inside the wood: no wheeled plan ⇒ the straight leg, and NO steer
            Assert.False(nowhere.Has, $"a straight fallback carries no steer: {nowhere.Steer}");
            Assert.True(friend.Has && hostile.Has);
            Assert.InRange(Vector3.Distance(From, friend.Steer), 19f, 21f);
            Assert.True(friend.Steer.X > 55f, $"the knowing side steers east round the wood: {friend.Steer}");
            Assert.InRange(hostile.Steer.X, 40f, 50f);
            Assert.InRange(hostile.Steer.Y, 35f, 45f);   // straight up the west street

            r.SetSimulationTime(10f);
            r.Bus.SwapBuffers();
            new Fdp.Toolkit.Squad.Systems.DangerAreaSensorSystem().Execute(r, 0.1f);
            Assert.True(Fdp.Toolkit.Squad.DangerArea.DangerRouteSteer.Of(r, friend.Unit, out var read));
            Assert.Equal(friend.Steer, read);
            Assert.True(Fdp.Toolkit.Squad.DangerArea.DangerRouteSteer.Of(r, hostile.Unit, out read) && read == hostile.Steer);
            r.SetSimulationTime(15f);
            Assert.False(Fdp.Toolkit.Squad.DangerArea.DangerRouteSteer.Of(r, friend.Unit, out _), "an answer 5 s old is not used");
        }

        /// <summary>
        /// ⭐⭐ CE-2468 (behaviors' note NOTE_Behaviors_To_Backend_UnderFire_Not_Reported.md: "the route danger solve never reports CoveredByFire") — the EQS danger solve and the path
        /// solver run on a SNAPSHOT of the world (SlowBackground, <c>SyncFrom</c>): the snapshot must carry the published sight, or every background query reads Empty — no
        /// CoveredByFire area, no route price. And it is the sight of the snapshot's MOMENT (R-219): a record learned on the live world after the sync is not in it.
        /// 🔴 Red-proof: the old per-world weak table (a snapshot is another instance) ⇒ Empty on the snapshot.
        /// </summary>
        [Fact]
        public void CE2468_ABackgroundSnapshot_CarriesTheSight_OfItsMoment()
        {
            var rig = new Rig();
            var nav = (DotRecastNavmeshProvider)new RecastNavmeshFactory { TileCache = null }.Build(rig.Terrain)!;
            rig.Know(Blue, Enemy, 150f);
            rig.Doors();
            using var snapshot = new EntityRepository();
            snapshot.SyncFrom(rig.R);
            var seen = DoorStates.Of(snapshot, rig.Terrain).UnderFireSight;
            Assert.True(seen.HasSide((byte)(Blue + 1)), "the snapshot sees the sight published before it was taken");
            var snapDoors = DoorStates.Of(snapshot, rig.Terrain);   // what the path solver hands a query on its snapshot (PathfindingSolverSystem)
            Assert.True(nav.PathCost(From, To, Mask(Blue), snapDoors) > nav.PathCost(From, To, Mask(Red), snapDoors) + 50f, "the route price reaches a query on the snapshot");
            var route = new[] { From, To };
            var areas = new Fdp.Toolkit.Squad.DangerArea.DangerAreaDescriptor[8];
            Assert.Equal(1, Fdp.Toolkit.Squad.DangerArea.DangerAlongRouteSolve.CoveredByFireAreas(seen, route, (byte)(Blue + 1), areas));

            rig.Know(Red, Enemy, 150f);   // learned on the live world AFTER the sync
            rig.Doors();
            Assert.True(DoorStates.Of(rig.R, rig.Terrain).UnderFireSight.HasSide((byte)(Red + 1)));
            Assert.False(DoorStates.Of(snapshot, rig.Terrain).UnderFireSight.HasSide((byte)(Red + 1)), "R-219: the snapshot keeps its moment");
        }

        /// <summary>
        /// ⭐⭐ UF-4 (K3, K5) — the sight is a pure function of the authored terrain and the record: two LOADS build the same grid cell for cell; the grids and cost maps are built in
        /// the serial pass only (a path query builds nothing); a steady frame of the pass + the readers allocates 0 B (R-2084).
        /// 🔴 Red-proof: pass the live doors to <c>SegmentBlocked</c> in the grid build ⇒ an opened door changes the grid (not run here); drop the unchanged-table early return ⇒ the steady frame rebuilds (Builds grows, bytes &gt; 0).
        /// </summary>
        [Fact]
        public void UF4_TheSight_IsTheSameOnTwoLoads_BuiltOnlyInTheSerialPass_AndASteadyFrameAllocatesNothing()
        {
            UnderFireSightGrid Load(out Rig rig, out DotRecastNavmeshProvider nav)
            {
                rig = new Rig();
                nav = (DotRecastNavmeshProvider)new RecastNavmeshFactory { TileCache = null }.Build(rig.Terrain)!;
                rig.Know(Blue, Enemy, 150f);
                var s = rig.Doors().UnderFireSight;
                Assert.Equal(1, s.GridCount);
                return s.Grid(0).Grid;
            }
            var g1 = Load(out _, out _);
            var g2 = Load(out var rig, out var nav);
            Assert.True(g1.SeenCount > 100 && g1.SeenCount == g2.SeenCount && g1.Cells == g2.Cells);
            for (float x = 0; x <= 300; x += 5) for (float y = 0; y <= 600; y += 5) Assert.Equal(g1.Covers(new Vector2(x, y)), g2.Covers(new Vector2(x, y)));
            Assert.True(g1.Covers(new Vector2(45, 200)) && !g1.Covers(new Vector2(250, 300)), "the street is seen; behind the wood is not");

            var doors = rig.Doors();
            int builds = rig.Sight.Builds;
            Assert.True(nav.PathCost(From, To, Mask(Blue), doors) < float.MaxValue);
            Assert.Equal(builds, rig.Sight.Builds);   // the query looked the map up, built nothing

            for (int i = 0; i < 3; i++) { rig.Sight.Execute(rig.R, 0f); DoorStates.Of(rig.R, rig.Terrain); }   // warm
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++) { rig.Sight.Execute(rig.R, 0f); _ = DoorStates.Of(rig.R, rig.Terrain).UnderFire.NearestFresh(Blue, Enemy, 50f, out _); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(builds, rig.Sight.Builds);
        }

        /// <summary>
        /// ⭐ K12 — a road leg is priced too: a segment whose midpoint is covered costs × <see cref="UnderFireSight.FactorOf"/>(1). Two roads round the wood: Blue takes the east one.
        /// 🔴 Red-proof: leave <c>PriceSegments</c> at 1 ⇒ Blue's road is the west one.
        /// </summary>
        [Fact]
        public void UF5_ARoadLeg_ThroughCoveredGround_IsPricedForTheKnowingSide()
        {
            var rig = new Rig();
            rig.Know(Blue, Enemy, 400f);
            var sight = rig.Doors().UnderFireSight;
            var b = new RoadNetworkBuilder();
            Vector2 a = new(45, 20), w = new(45, 300), e1 = new(250, 60), e2 = new(250, 540), z = new(45, 580);
            b.AddNode(a); b.AddNode(w); b.AddNode(z); b.AddNode(e1); b.AddNode(e2);
            void Seg(Vector2 p, Vector2 q, int s, int t) { var d = q - p; b.AddSegment(p, d, q, d, 13.9f, 3.5f, 1, s, t); }
            Seg(a, w, 0, 1); Seg(w, z, 1, 2);                   // the west road: through the covered ground
            Seg(a, e1, 0, 3); Seg(e1, e2, 3, 4); Seg(e2, z, 4, 2);   // the east road: round the wood
            var roads = b.Build(50f, 8, 8);
            try
            {
                var router = new RoadGraphRouter();
                var entry = RoadGraphRouter.NearestAccess(roads, a, 30f); var exit = RoadGraphRouter.NearestAccess(roads, z, 30f);
                Assert.True(entry.IsValid && exit.IsValid, $"access {entry.IsValid} {exit.IsValid}");
                Assert.True(router.Route(roads, entry, exit, out float red, sight, (byte)(Red + 1)), "red route");
                Assert.True(router.Route(roads, entry, exit, out float blue, sight, (byte)(Blue + 1)), "blue route");
                Assert.True(red < 600f, $"Red: the west road, {red:0.0} m");
                Assert.True(blue > red, $"Blue pays for the covered road or drives round: {blue:0.0} vs {red:0.0}");
                var pts = new System.Collections.Generic.List<Vector3>();
                router.EmitLeg(roads, pts, (_, _, _) => 0f);
                float maxX = 0f; foreach (var p in pts) maxX = MathF.Max(maxX, p.X);
                Assert.True(maxX >= 200f, "Blue's road leg goes round the wood");
            }
            finally { roads.Dispose(); }
        }
    }
}
