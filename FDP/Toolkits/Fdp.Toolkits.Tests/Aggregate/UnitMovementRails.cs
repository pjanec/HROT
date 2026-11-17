using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Xunit;

namespace Fdp.Toolkits.Tests.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8004</c> (ST-1, M-1 … M-4; docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md §14) — the stochastic unit's
    /// movement: M-2's speed model, the follower's arrival against M-2's prediction (ST-1's acceptance: within ± 10 %), the column's tail, a
    /// lost route re-planned, 0 B per steady frame.
    /// </summary>
    public sealed class UnitMovementRails
    {
        private static (EntityRepository W, Entity Unit) World(Vector3 at, AggregateUnit u, UnitHoldings h = default, int men = 9)
        {
            var w = new EntityRepository();
            w.RegisterComponent<SimTransform>(); w.RegisterComponent<AggregateUnit>(); w.RegisterComponent<UnitHoldings>();
            w.RegisterComponent<UnitSlotTable>(); w.RegisterManagedComponent<UnitRoute>();
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            w.AddComponent(e, u);
            w.AddComponent(e, h);
            var t = new UnitSlotTable { Count = (byte)men };
            for (int i = 0; i < men; i++) t.Slots[i] = new UnitSlot { Role = SlotRole.Rifleman, Health = (Half)25f };
            w.AddComponent(e, t);
            return (w, e);
        }

        private static UnitHoldings Ifvs(int n, HoldingMobility m = HoldingMobility.Tracked)
        {
            var h = new UnitHoldings { Count = 2 };
            h.Items[0] = new UnitHolding { Type = new TkbTypeId(212), Count = (ushort)n, Operational = (ushort)n, Kind = HoldingKind.Vehicle, Mobility = m };
            h.Items[1] = new UnitHolding { Type = new TkbTypeId(5101), Count = 1, Operational = 1, Kind = HoldingKind.Weapon };
            return h;
        }

        [Fact]
        public void M2_TheSpeed_IsTheSlowestHolding_TimesTerrain_FormationAndSuppression()
        {
            var foot = new AggregateUnit { Resolution = AggregateResolution.Stochastic };
            Assert.Equal(UnitSpeedModel.FootMps, UnitSpeedModel.BaseSpeed(in foot, default));
            var mounted = new AggregateUnit { Resolution = AggregateResolution.Stochastic, Posture = UnitPosture.Mounted };
            var mixed = Ifvs(2, HoldingMobility.Wheeled);
            mixed.Count = 3;
            mixed.Items[2] = new UnitHolding { Type = new TkbTypeId(213), Count = 1, Operational = 1, Kind = HoldingKind.Vehicle, Mobility = HoldingMobility.Tracked };
            Assert.Equal(UnitSpeedModel.TrackedMps, UnitSpeedModel.BaseSpeed(in mounted, in mixed));            // the slowest limits
            Assert.Equal(HoldingMobility.Tracked, UnitSpeedModel.LimitingMobility(in mounted, in mixed));
            var dead = mixed; dead.Items[2].Operational = 0;
            Assert.Equal(UnitSpeedModel.WheeledMps, UnitSpeedModel.BaseSpeed(in mounted, in dead));             // a knocked-out one no longer does
            var none = new UnitHoldings();
            Assert.Equal(UnitSpeedModel.FootMps, UnitSpeedModel.BaseSpeed(in mounted, in none));                // mounted with no vehicle walks
            Assert.Equal(UnitSpeedModel.OffRoadWheeled, UnitSpeedModel.TerrainFactor(HoldingMobility.Wheeled, false, false, 16f));
            Assert.Equal(1f, UnitSpeedModel.TerrainFactor(HoldingMobility.Wheeled, true, false, 16f));
            Assert.Equal(UnitSpeedModel.WoodsWheeled, UnitSpeedModel.TerrainFactor(HoldingMobility.Wheeled, true, true, 16f));
            var line = foot; line.Formation = UnitFormation.Line; line.SuppressedShare = 1f;
            Assert.Equal(UnitSpeedModel.LineFactor * UnitSpeedModel.SuppressedFloor, UnitSpeedModel.DynamicFactor(in line), 4);
        }

        /// <summary>⭐ ST-1's acceptance — a mounted column on a three-segment route with mixed terrain arrives (its TAIL at the end) within ± 10 %
        /// of M-2's prediction. ✅ Red-proof: the follower ignoring the segment factor ⇒ arrives far early ⇒ red.</summary>
        [Fact]
        public void ST1_TheColumnArrives_WithinTenPercentOfThePrediction()
        {
            var u = new AggregateUnit { Resolution = AggregateResolution.Stochastic, Posture = UnitPosture.Mounted, Echelon = 1 };
            var h = Ifvs(4);
            var (w, e) = World(Vector3.Zero, u, h);
            using (w)
            {
                ref var au = ref w.GetComponentRW<AggregateUnit>(e);
                UnitMoves.Order(ref au, new WorldPoint(0, 0, 0), UnitFormation.Column, UnitMoves.ColumnDepth(in au, in h, w.GetComponentRO<UnitSlotTable>(e)), SimInstant.Zero);
                Assert.Equal(150f, au.Depth);                                                       // 4 vehicles × 50 m spacing (M-3)
                var pts = new[] { Vector3.Zero, new Vector3(0, 1000, 0), new Vector3(800, 1000, 0), new Vector3(800, 1600, 0) };
                var route = new UnitRoute
                {
                    Points = pts, Cumulative = new[] { 0f, 1000f, 1800f, 2400f },
                    SegmentFactor = new[] { 1f, UnitSpeedModel.OffRoadTracked, 1f },
                };
                w.SetManagedComponent(e, route);
                au.Move = UnitMoveState.OnRoute;
                double predicted = UnitSpeedModel.PredictSeconds(route, in au, in h);
                float dt = 0.25f; double t = 0;
                var sys = new StochasticUnitMovementSystem();
                while (w.GetComponentRO<AggregateUnit>(e).Move == UnitMoveState.OnRoute && t < predicted * 3)
                {
                    sys.Execute(w, dt); t += dt;
                }
                Assert.Equal(UnitMoveState.Arrived, w.GetComponentRO<AggregateUnit>(e).Move);
                Assert.InRange(t, predicted * 0.9, predicted * 1.1);
                Assert.Equal(new Vector3(800, 1600, 0), (Vector3)w.GetComponentRO<SimTransform>(e).Position);
                Assert.Equal(0f, w.GetComponentRO<AggregateUnit>(e).Heading.Degrees, 2);           // the last leg runs north
            }
        }

        /// <summary>⭐ M-1 / M-4 — an ordered unit with no navmesh and no roads plans the straight leg and walks it; its route lost (a restored
        /// checkpoint) while on it, it re-plans from where it stands; the steady frame allocates nothing.</summary>
        [Fact]
        public void ST1_AnOrderedUnit_PlansWalksReplansAndAllocatesNothingWhileWalking()
        {
            var (w, e) = World(new Vector3(10, 20, 0), new AggregateUnit { Resolution = AggregateResolution.Stochastic });
            using (w)
            {
                var sys = new StochasticUnitMovementSystem();
                UnitMoves.Order(ref w.GetComponentRW<AggregateUnit>(e), new WorldPoint(10, 320, 0), UnitFormation.Line, 0f, SimInstant.Zero);
                sys.Execute(w, 1f);
                var u = w.GetComponentRO<AggregateUnit>(e);
                Assert.Equal(UnitMoveState.OnRoute, u.Move);
                Assert.True(w.GetComponent<UnitRoute>(e).Length > 299f);
                Assert.Equal(UnitSpeedModel.FootMps * UnitSpeedModel.OffRoadFoot * UnitSpeedModel.LineFactor, u.SpeedMps, 3);
                Assert.True((u.Activity & UnitActivity.Moving) != 0);
                for (int i = 0; i < 5; i++) sys.Execute(w, 1f);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 50; i++) sys.Execute(w, 0.1f);
                Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
                var y = w.GetComponentRO<SimTransform>(e).Position.Y;
                w.RemoveComponent<UnitRoute>(e);                                            // the route is lost
                sys.Execute(w, 0f);
                Assert.True(w.HasManagedComponent<UnitRoute>(e));
                Assert.Equal(y, w.GetComponent<UnitRoute>(e).Points[0].Y, 2);              // re-planned from where it stands
                Assert.Equal(UnitMoveState.OnRoute, w.GetComponentRO<AggregateUnit>(e).Move);
            }
        }

        /// <summary>
        /// ⭐ CE-8005 — a node with NO navmesh (a split cluster's Brain-only node) asks the SOLVER over the path-request topic: the request leaves
        /// stamped with this node, the unit waits (AwaitingRoute) and follows the solver's route when it arrives (the pool's copy released); a
        /// solver that never answers costs <see cref="StochasticUnitMovementSystem.RemoteWaitSeconds"/>, then the unit plans with what the node has.
        /// ✅ Red-proof: the request not stamped with the node ⇒ red (the Brain egress forwards only its own).
        /// </summary>
        [Fact]
        public void CE8005_ANodeWithoutANavmesh_AsksTheSolver_AndFallsBackWhenItIsSilent()
        {
            var (w, e) = World(new Vector3(0, 0, 0), new AggregateUnit { Resolution = AggregateResolution.Stochastic });
            using (w)
            {
                w.RegisterComponent<GlobalTime>();
                w.RegisterEvent<Fdp.Toolkit.Navigation.PathfindingRequestEvent>();
                w.SetSingleton(new Fdp.Toolkit.Navigation.PathfindingBatchData
                {
                    Results = new Fdp.Core.Collections.NativeArray<Fdp.Toolkit.Navigation.PathResult>(Fdp.Toolkit.Navigation.PathfindingBatchData.DefaultCapacity, Fdp.Core.Collections.Allocator.Persistent),
                });
                using var pool = new global::CarKinem.Trajectory.TrajectoryPoolManager();
                var sys = new StochasticUnitMovementSystem { RemoteRoutes = () => pool, LocalNodeId = (Fdp.Core.NodeId)3 };
                double t = 0;
                void Step(float dt) { t += dt; w.SetSingleton(new GlobalTime { TotalTime = t }); sys.Execute(w, dt); w.Bus.SwapBuffers(); }

                UnitMoves.Order(ref w.GetComponentRW<AggregateUnit>(e), new WorldPoint(0, 300, 0), UnitFormation.Column, 0f, SimInstant.Zero);
                Step(0.1f);
                var asked = ((Fdp.ModuleHost.Abstractions.ISimulationView)w).ReadEvents<Fdp.Toolkit.Navigation.PathfindingRequestEvent>();
                Assert.Equal(1, asked.Length);
                Assert.Equal(3, (int)asked[0].SourceNodeId);                                          // the Brain egress forwards its own only
                Assert.Equal(300f, asked[0].End.Y, 2);
                Assert.Equal(UnitMoveState.AwaitingRoute, w.GetComponentRO<AggregateUnit>(e).Move);   // waits — never the straight leg at once
                long id = asked[0].RequestId;

                // the solver answers: a dog-leg round an obstacle the brain cannot see
                int handle = pool.RegisterTrajectory(new[] { new Vector3(0, 0, 0), new Vector3(80, 150, 0), new Vector3(0, 300, 0) });
                ref var batch = ref w.GetSingleton<Fdp.Toolkit.Navigation.PathfindingBatchData>();
                batch.Results[(int)((uint)id % (uint)Fdp.Toolkit.Navigation.PathfindingBatchData.DefaultCapacity)] =
                    new Fdp.Toolkit.Navigation.PathResult { RequestId = id, IsReachable = true, RouteHandle = handle };
                Step(0.1f);
                Assert.Equal(UnitMoveState.OnRoute, w.GetComponentRO<AggregateUnit>(e).Move);
                var route = w.GetComponent<UnitRoute>(e);
                Assert.Equal(3, route.Points.Length);
                Assert.Equal(80f, route.Points[1].X, 2);                                                // the solver's route, followed
                Assert.False(pool.TryGetTrajectory(handle, out _));                                    // its copy released

                // a new order, and a solver that stays silent: after the wait, the unit plans with what this node has (the straight leg)
                UnitMoves.Order(ref w.GetComponentRW<AggregateUnit>(e), new WorldPoint(200, 0, 0), UnitFormation.Column, 0f, (SimInstant)t);
                Step(0.1f);
                for (int i = 0; i < 40; i++) Step(0.1f);
                Assert.Equal(UnitMoveState.AwaitingRoute, w.GetComponentRO<AggregateUnit>(e).Move);   // 4 s: still waiting
                for (int i = 0; i < 15; i++) Step(0.1f);
                Assert.Equal(UnitMoveState.OnRoute, w.GetComponentRO<AggregateUnit>(e).Move);
                Assert.Equal(2, w.GetComponent<UnitRoute>(e).Points.Length);
                w.GetSingleton<Fdp.Toolkit.Navigation.PathfindingBatchData>().Results.Dispose();
            }
        }

        /// <summary>
        /// ⭐ F-19 (R-4286) — a stochastic unit stands ON THE TERRAIN: placed at an authored z of 0 on ground 12 m up, it is put on it; moved along
        /// a route whose points carry z 0, it stays on it; a unit already on the ground is not re-written (its position untouched).
        /// ✅ Red-proof: the ground pass skipped ⇒ z stays 0 ⇒ red.
        /// </summary>
        [Fact]
        public void F19_AStochasticUnit_StandsOnTheTerrain()
        {
            var (w, e) = World(new Vector3(100, 100, 0), new AggregateUnit { Resolution = AggregateResolution.Stochastic, Width = 40f });
            using (w)
            {
                w.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse("""
                    { "type": "FeatureCollection", "hrot": { "schemaVersion": 1, "bounds": [0, 0, 1200, 1200], "groundZ": 12 }, "features": [] }
                    """, "f19"));
                var sys = new StochasticUnitMovementSystem();
                sys.Execute(w, 1f);
                Assert.Equal(12f, (float)w.GetComponentRO<SimTransform>(e).Position.Z, 3);          // placed: on the ground
                ref var u = ref w.GetComponentRW<AggregateUnit>(e);
                UnitMoves.Order(ref u, new WorldPoint(300, 100, 0), UnitFormation.Line, 0f, SimInstant.Zero);
                for (int s = 0; s < 30; s++) sys.Execute(w, 1f);
                var p = w.GetComponentRO<SimTransform>(e).Position;
                Assert.True(p.X > 110f, "it moved");
                Assert.Equal(12f, (float)p.Z, 3);                                                     // moved: still on the ground
            }
        }

        [Fact]
        public void ST1_APhysicalUnit_IsNeverMovedByTheFollower()
        {
            var (w, e) = World(Vector3.Zero, new AggregateUnit { Resolution = AggregateResolution.Physical });
            using (w)
            {
                UnitMoves.Order(ref w.GetComponentRW<AggregateUnit>(e), new WorldPoint(0, 100, 0), UnitFormation.Line, 0f, SimInstant.Zero);
                new StochasticUnitMovementSystem().Execute(w, 1f);
                Assert.Equal(UnitMoveState.AwaitingRoute, w.GetComponentRO<AggregateUnit>(e).Move);
                Assert.Equal(Vector3.Zero, (Vector3)w.GetComponentRO<SimTransform>(e).Position);
            }
        }
    }
}
