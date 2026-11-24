using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;

namespace Fdp.Toolkit.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8004</c> (ST-1, M-1 / M-4; docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md §14) — a stochastic unit's
    /// PLANNED ROUTE: the waypoints the one <see cref="RoutePlanner"/> produced, the distance to each, and each segment's terrain factor (M-2,
    /// fixed at plan time). ⭐ Immutable (CLAUDE.md "NEW COMPONENTS"): a new plan is a new instance. ⭐ Derivable ⇒ NoScenario | NoReplay: a
    /// unit OnRoute without one re-plans from where it stands (a restored checkpoint, a late node).
    /// </summary>
    [ComponentId(GlobalComponentIds.UnitRoute)]
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]
    public sealed record UnitRoute
    {
        /// <summary>The waypoints, start first.</summary>
        public required Vector3[] Points { get; init; }
        /// <summary>The distance along the route to each waypoint (m); <c>Cumulative[0] = 0</c>.</summary>
        public required float[] Cumulative { get; init; }
        /// <summary>The terrain factor of the segment from waypoint i to i + 1 (M-2) — one fewer than <see cref="Points"/>.</summary>
        public required float[] SegmentFactor { get; init; }
        /// <summary>Which planner produced it (Navmesh, Hybrid, NavRoadGraph) — or null for the straight leg nothing could plan.</summary>
        public NavigationBackend? Backend { get; init; }

        /// <summary>The route's length (m).</summary>
        public float Length => Cumulative.Length == 0 ? 0f : Cumulative[^1];

        /// <summary>The segment holding distance <paramref name="d"/> (clamped to the route).</summary>
        public int SegmentAt(float d)
        {
            int n = Points.Length;
            if (n < 2) return 0;
            for (int i = 0; i < n - 2; i++) if (d < Cumulative[i + 1]) return i;
            return n - 2;
        }

        /// <summary>The point at distance <paramref name="d"/> along the route (clamped); <paramref name="segment"/> is its segment.</summary>
        public Vector3 PointAt(float d, out int segment)
        {
            segment = SegmentAt(d);
            if (Points.Length == 0) return default;
            if (Points.Length == 1) return Points[0];
            float a = Cumulative[segment], b = Cumulative[segment + 1];
            float t = b - a < 1e-4f ? 1f : Math.Clamp((d - a) / (b - a), 0f, 1f);
            return Vector3.Lerp(Points[segment], Points[segment + 1], t);
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-8004</c> (M-2) — the unit's speed: the SLOWEST holding's speed × the segment's terrain factor × the formation × the
    /// suppression. ⭐ Dismounted the slowest is the man (<see cref="FootMps"/>); mounted, the slowest operational vehicle (its TKB top speed
    /// through <see cref="VehicleTopSpeed"/>, else the mobility class default). The off-road / woods factors and the formation and suppression
    /// cuts are CALIBRATED (RV-17), not derived — named constants, tunable data.
    /// </summary>
    public static class UnitSpeedModel
    {
        /// <summary>A dismounted unit's march (m/s) — the soldier's walk (<c>LocomotionBlend.WalkSpeed</c>).</summary>
        public const float FootMps = 1.5f;
        /// <summary>A mounted unit's top speed when no TKB number is known (m/s): wheeled 60 km/h, tracked 45 km/h.</summary>
        public const float WheeledMps = 60f / 3.6f, TrackedMps = 45f / 3.6f;
        /// <summary>CALIBRATED — off the road: wheeled ×0.5, tracked ×0.7, on foot ×0.9.</summary>
        public const float OffRoadWheeled = 0.5f, OffRoadTracked = 0.7f, OffRoadFoot = 0.9f;
        /// <summary>CALIBRATED — in woods: wheeled ×0.1 (M-1 forbids them — the factor only bounds a plan that crosses one), tracked capped at
        /// <c>TerrainWoods.TrackedSpeedCap</c>, on foot ×0.6.</summary>
        public const float WoodsWheeled = 0.1f, WoodsFoot = 0.6f;
        /// <summary>CALIBRATED — a deployed line ×0.7, the assault ×0.5 of the column's pace.</summary>
        public const float LineFactor = 0.7f, AssaultFactor = 0.5f;
        /// <summary>CALIBRATED — a fully suppressed unit keeps this share of its pace.</summary>
        public const float SuppressedFloor = 0.25f;
        /// <summary>The distance a waypoint may be from a road and still be ON it (m).</summary>
        public const float OnRoadMetres = 8f;

        /// <summary>⭐ The TKB top speed of a vehicle type (m/s), or ≤ 0 when unknown. Set by the host that holds the TKB (the silent-default
        /// pattern: a production host MUST set it; null = the mobility defaults).</summary>
        public static Func<TkbTypeId, float>? VehicleTopSpeed { get; set; }

        /// <summary>The mobility that limits the unit: foot when dismounted; else the slowest operational vehicle's class.</summary>
        public static HoldingMobility LimitingMobility(in AggregateUnit u, in UnitHoldings h)
        {
            if (u.Posture != UnitPosture.Mounted) return HoldingMobility.NoMobility;
            var m = HoldingMobility.NoMobility;
            float slowest = float.MaxValue;
            for (int i = 0; i < h.Count; i++)
            {
                if (h.Items[i].Kind != HoldingKind.Vehicle || h.Items[i].Operational <= h.Items[i].Immobile) continue;
                float v = TopSpeed(in h.Items[i]);
                if (v < slowest) { slowest = v; m = h.Items[i].Mobility; }
            }
            return m;
        }

        /// <summary>The unit's base speed (m/s) before terrain, formation and suppression.</summary>
        public static float BaseSpeed(in AggregateUnit u, in UnitHoldings h)
        {
            if (u.Posture != UnitPosture.Mounted) return FootMps;
            float slowest = float.MaxValue;
            for (int i = 0; i < h.Count; i++)
            {
                if (h.Items[i].Kind != HoldingKind.Vehicle || h.Items[i].Operational <= h.Items[i].Immobile) continue;
                slowest = MathF.Min(slowest, TopSpeed(in h.Items[i]));
            }
            return slowest == float.MaxValue ? FootMps : slowest;   // mounted with no working vehicle walks
        }

        private static float TopSpeed(in UnitHolding x)
        {
            float tkb = VehicleTopSpeed?.Invoke(x.Type) ?? 0f;
            if (tkb > 0f) return tkb;
            return x.Mobility == HoldingMobility.Tracked ? TrackedMps : WheeledMps;
        }

        /// <summary>The terrain factor of a segment for <paramref name="mobility"/>: on a road 1; off it, or in woods, the calibrated cut.</summary>
        public static float TerrainFactor(HoldingMobility mobility, bool onRoad, bool inWoods, float baseSpeed)
        {
            if (inWoods)
                return mobility switch
                {
                    HoldingMobility.Wheeled => WoodsWheeled,
                    HoldingMobility.Tracked => baseSpeed <= 0f ? 1f : MathF.Min(1f, Fdp.Toolkit.Terrain.TerrainWoods.TrackedSpeedCap / baseSpeed),
                    _ => WoodsFoot,
                };
            if (onRoad) return 1f;
            return mobility switch { HoldingMobility.Wheeled => OffRoadWheeled, HoldingMobility.Tracked => OffRoadTracked, _ => OffRoadFoot };
        }

        /// <summary>The formation × suppression cut (the dynamic part of M-2).</summary>
        public static float DynamicFactor(in AggregateUnit u)
        {
            float f = u.Formation switch { UnitFormation.Line => LineFactor, UnitFormation.Assault => AssaultFactor, _ => 1f };
            float s = Math.Clamp(u.SuppressedShare, 0f, 1f);
            return f * (1f - (1f - SuppressedFloor) * s);
        }

        /// <summary>⭐ M-2's PREDICTION: the time (s) the unit's TAIL needs to reach the end of <paramref name="route"/> at a constant formation
        /// and suppression — the head runs the route, the tail <see cref="AggregateUnit.Depth"/> behind it (M-3). The follower's arrival is
        /// checked against it (ST-1's acceptance: within ± 10 %).</summary>
        public static double PredictSeconds(UnitRoute route, in AggregateUnit u, in UnitHoldings h)
        {
            float v0 = BaseSpeed(in u, in h) * DynamicFactor(in u);
            if (v0 <= 0f) return double.PositiveInfinity;
            double t = 0;
            for (int i = 0; i < route.SegmentFactor.Length; i++)
                t += (route.Cumulative[i + 1] - route.Cumulative[i]) / (v0 * MathF.Max(route.SegmentFactor[i], 1e-3f));
            int last = Math.Max(0, route.SegmentFactor.Length - 1);
            float endFactor = route.SegmentFactor.Length == 0 ? 1f : route.SegmentFactor[last];
            t += MathF.Max(0f, u.Depth) / (v0 * MathF.Max(endFactor, 1e-3f));   // the tail runs past the end at the last segment's pace
            return t;
        }
    }

    /// <summary>⭐ <c>CE-8004</c> (B-2) — the ONE way a play orders a stochastic unit to move: the destination, the formation, the depth of
    /// the column (M-3); the follower plans and drives. Writes ONLY the unit's own <see cref="AggregateUnit"/> (safe inside a per-entity phase).</summary>
    public static class UnitMoves
    {
        /// <summary>CALIBRATED — the spacing in a road column: a vehicle 50 m, a man 5 m (M-3: column length = elements × spacing).</summary>
        public const float VehicleSpacing = 50f, ManSpacing = 5f;

        /// <summary>A column's length for the unit as it stands: mounted ⇒ its working vehicles × <see cref="VehicleSpacing"/>; on foot ⇒ its
        /// present men × <see cref="ManSpacing"/>.</summary>
        public static float ColumnDepth(in AggregateUnit u, in UnitHoldings h, in UnitSlotTable slots)
        {
            if (u.Posture == UnitPosture.Mounted)
            {
                int v = 0;
                for (int i = 0; i < h.Count; i++) if (h.Items[i].Kind == HoldingKind.Vehicle) v += h.Items[i].Operational;
                if (v > 0) return (v - 1) * VehicleSpacing;
            }
            return MathF.Max(0, slots.Present() - 1) * ManSpacing;
        }

        /// <summary>Order the move: the unit re-plans from where it stands on the follower's next step.</summary>
        public static void Order(ref AggregateUnit u, WorldPoint destination, UnitFormation formation, float depth, SimInstant now)
        {
            u.Destination = destination;
            u.Formation = formation;
            u.Depth = MathF.Max(0f, depth);
            u.Move = UnitMoveState.AwaitingRoute;
            u.RouteRequestedAt = now;
            u.PathDistanceM = 0f;
        }

        /// <summary>Stop where it stands.</summary>
        public static void Stop(ref AggregateUnit u)
        {
            u.Move = UnitMoveState.Stopped;
            u.SpeedMps = 0f;
            u.Activity &= ~UnitActivity.Moving;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-8004</c> (ST-1, M-1 … M-4) — the stochastic unit's deterministic PATH FOLLOWER: plans an awaiting unit's route with THE
    /// <see cref="RoutePlanner"/> (M-1: mounted ⇒ the vehicle layer, roads preferred; dismounted ⇒ the infantry layer, neutral), then moves
    /// the HEAD along it at M-2's speed; the unit's point (<c>SimTransform</c>) is the head, its heading the segment's; it is
    /// <see cref="UnitMoveState.Arrived"/> when the TAIL (<see cref="AggregateUnit.Depth"/> behind) reaches the end (M-3).
    /// ⛔ No hull guard, no avoidance, no body kinematics (M-4). Runs where the unit's state is owned; plans with this node's navmesh and road
    /// graph — a node without a navmesh plans on the roads, without either the straight leg (CE-8005: a split cluster's brain node has no
    /// navmesh — the remote request is the follow-up). Serial; 0 B per steady frame (planning allocates once per order).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class StochasticUnitMovementSystem : IEcsModuleSystem
    {
        private const uint InfantryLayer = 1u, VehicleLayer = 2u;
        private readonly bool _gateOnAuthority;
        private readonly RoutePlanner _planner = new();
        private EntityQuery? _units;
        private EntityRepository? _queriesFor;

        public StochasticUnitMovementSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        // ── ⭐ CE-8005 — a node with NO navmesh (a split cluster's Brain-only node) asks the SOLVER over the path-request topic ─────────────

        /// <summary>The trajectory pool the path response's routes land in (the Brain path-request pair is composed with it). Null ⇒ never asks:
        /// the node plans with what it has (its navmesh, else its roads, else the straight leg).</summary>
        public Func<global::CarKinem.Trajectory.TrajectoryPoolManager?>? RemoteRoutes { get; init; }
        /// <summary>This node's id — the request's <c>SourceNodeId</c> (the Brain egress forwards only its own; 0 ⇒ never asks).</summary>
        public Fdp.Core.NodeId LocalNodeId { get; init; }
        /// <summary>How long a unit waits for the solver before it plans with what this node has (s of sim time).</summary>
        public const double RemoteWaitSeconds = 5.0;
        [Fdp.Core.ProtocolCache("placement asks in flight per unit (Id, To, Since) awaiting the solver answer; cleared at world move")]
        private readonly Dictionary<Entity, (long Id, Vector3 To, SimInstant Since)> _asked = new();
        private int _worldEpoch;

        private global::CarKinem.Trajectory.TrajectoryPoolManager? RemotePool(EntityRepository repo)
        {
            if (RemoteRoutes == null || (int)LocalNodeId == 0 || repo.HasSingletonManaged<INavmeshProvider>() || !repo.HasSingleton<PathfindingBatchData>()) return null;
            return RemoteRoutes();
        }

        /// <summary>⭐ CE-8005 — the request for <paramref name="e"/>'s route, the batch slot primed so a stale result is not read.</summary>
        internal long Ask(EntityRepository repo, Entity e, Vector3 from, in AggregateUnit u, in UnitHoldings h)
        {
            var mobility = UnitSpeedModel.LimitingMobility(in u, in h);
            bool mounted = u.Posture == UnitPosture.Mounted && mobility != HoldingMobility.NoMobility;
            long id = ((long)e.Index << 32) | (uint)repo.GlobalVersion;
            ref var batch = ref repo.GetSingleton<PathfindingBatchData>();
            batch.Results[(int)((uint)id % (uint)PathfindingBatchData.DefaultCapacity)] = new PathResult { RequestId = id, RouteHandle = -1 };
            repo.Bus.Publish(new PathfindingRequestEvent
            {
                RequestId = id, Start = from, End = u.Destination,
                MobilityProfile = (byte)(!mounted ? 2 : mobility == HoldingMobility.Tracked ? 1 : 0),   // 0 wheeled · 1 tracked · 2 infantry
                RoadUse = mounted ? RoadUse.Prefer : RoadUse.NavmeshFirst,   // ⭐ R-11018 — on foot: the navmesh first
                NavLayerMask = (int)(mounted ? VehicleLayer : InfantryLayer),
                SourceNodeId = LocalNodeId,
            });
            return id;
        }

        /// <summary>⭐ CE-8005 — the solver's answer to <paramref name="id"/> as the unit's route (the pool's copy released), or false while none.</summary>
        internal static bool TryAnswer(EntityRepository repo, global::CarKinem.Trajectory.TrajectoryPoolManager pool, long id, in AggregateUnit u, in UnitHoldings h,
                                       out UnitRoute? route)
        {
            route = null;
            ref readonly var r = ref repo.GetSingleton<PathfindingBatchData>().Results[(int)((uint)id % (uint)PathfindingBatchData.DefaultCapacity)];
            if (r.RequestId != id || !r.IsReachable || r.RouteHandle < 0 || !pool.TryGetTrajectory(r.RouteHandle, out var traj) || traj.Waypoints.Length < 2) return false;
            var pts = new Vector3[traj.Waypoints.Length];
            for (int i = 0; i < pts.Length; i++) pts[i] = traj.Waypoints[i].Position;
            pool.RemoveTrajectory(r.RouteHandle);
            var holder = repo.HasSingletonManaged<global::CarKinem.Road.RoadNetworkHolder>() ? repo.GetSingletonManaged<global::CarKinem.Road.RoadNetworkHolder>() : null;
            using var lease = holder?.Borrow();
            var terrain = repo.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() ? repo.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() : null;
            route = Build(pts, NavigationBackend.Auto /* the solver's (the wire carries no backend) */, UnitSpeedModel.LimitingMobility(in u, in h), UnitSpeedModel.BaseSpeed(in u, in h), lease?.Value ?? default, terrain?.Woods);
            return true;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<AggregateUnit>()
                || !repo.IsComponentTypeRegistered<SimTransform>()) return;   // ⚠ UnitRoute (managed) is registered by HrotSharedComponentRegistry
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(repo, ref _worldEpoch)) _asked.Clear();   // ⭐ R-4297 ② (CE-1980) — a world replacement (a reload reuses the handles, a restore) forgets the path asks in flight (they named the old world)
            if (!ReferenceEquals(_queriesFor, repo))
            {
                _units = repo.Query().With<AggregateUnit>().With<SimTransform>().WithOwnedWhen<AggregateUnit>(_gateOnAuthority).Build();
                _queriesFor = repo;
            }
            SimInstant now = repo.HasSingleton<GlobalTime>() ? (SimInstant)repo.GetSingleton<GlobalTime>().TotalTime : SimInstant.Zero;
            bool holdings = repo.IsComponentTypeRegistered<UnitHoldings>();
            foreach (var e in _units!)
            {
                ref var u = ref repo.GetComponentRW<AggregateUnit>(e);
                if (u.Effective != AggregateResolution.Stochastic) continue;
                var h = holdings && repo.HasComponent<UnitHoldings>(e) ? repo.GetComponentRO<UnitHoldings>(e) : default;
                if (u.Move == UnitMoveState.AwaitingRoute
                    || (u.Move == UnitMoveState.OnRoute && !repo.HasManagedComponent<UnitRoute>(e)))
                {
                    var from = repo.GetComponentRO<SimTransform>(e).Position;
                    UnitRoute? route = null;
                    if (RemotePool(repo) is { } pool)   // ⭐ CE-8005 — no navmesh here: the solver's route, waited for over several ticks (B-2)
                    {
                        if (!_asked.TryGetValue(e, out var ask) || ask.To != u.Destination)
                        {
                            _asked[e] = (Ask(repo, e, from, in u, in h), u.Destination, now);
                            continue;
                        }
                        if (!TryAnswer(repo, pool, ask.Id, in u, in h, out route) && (double)now - (double)ask.Since < RemoteWaitSeconds) continue;
                        _asked.Remove(e);   // answered — or the solver did not: plan with what this node has
                    }
                    route ??= Plan(repo, from, u.Destination, in u, in h);
                    if (route == null) { u.Move = UnitMoveState.RouteFailed; u.SpeedMps = 0f; u.Activity &= ~UnitActivity.Moving; continue; }
                    repo.SetManagedComponent(e, route);
                    u.Move = UnitMoveState.OnRoute;
                    u.PathDistanceM = 0f;
                }
                if (u.Move != UnitMoveState.OnRoute) continue;
                Step(repo, e, ref u, in h, repo.GetManagedComponentRO<UnitRoute>(e), deltaTime);
            }
            OnTheGround(repo);
        }

        /// <summary>⭐ F-19 (R-4286) — every stochastic unit stands ON THE TERRAIN: placed (an authored z of 0) or moved (a route point's z),
        /// its z is the ground under it (<c>IWorldQuery.GroundHeightAt</c>, the one ground answer) — its line of sight no longer starts
        /// underground. ⭐ Written only when it is off by more than <see cref="GroundTolerance"/>: a standing unit is not re-written every step.</summary>
        private void OnTheGround(EntityRepository repo)
        {
            var world = Fdp.Toolkit.World.WorldQuery.Of(repo);
            if (world == null) return;
            foreach (var e in _units!)
            {
                if (repo.GetComponentRO<AggregateUnit>(e).Effective != AggregateResolution.Stochastic) continue;
                var p = repo.GetComponentRO<SimTransform>(e).Position;
                float z = world.GroundHeightAt((float)p.X, (float)p.Y);
                if (MathF.Abs((float)p.Z - z) <= GroundTolerance) continue;
                ref var t = ref repo.GetComponentRW<SimTransform>(e);
                t.Position = new WorldPoint(p.X, p.Y, z);
            }
        }

        /// <summary>How far (m) off the ground a unit may be before it is put back on it.</summary>
        public const float GroundTolerance = 0.05f;

        /// <summary>One step of the follower (the test seam): advances the head, places the unit, marks the arrival.</summary>
        internal static void Step(EntityRepository repo, Entity e, ref AggregateUnit u, in UnitHoldings h, UnitRoute route, float dt)
        {
            float total = route.Length + MathF.Max(0f, u.Depth);
            float v0 = UnitSpeedModel.BaseSpeed(in u, in h) * UnitSpeedModel.DynamicFactor(in u);
            float d = u.PathDistanceM;
            float left = MathF.Max(0f, dt);
            // integrate segment by segment, so a step crossing a segment end takes the next one's pace for the rest of it
            while (left > 0f && d < total)
            {
                int seg = route.SegmentFactor.Length == 0 ? 0 : route.SegmentAt(MathF.Min(d, route.Length - 1e-3f));
                float f = route.SegmentFactor.Length == 0 ? 1f : route.SegmentFactor[Math.Min(seg, route.SegmentFactor.Length - 1)];
                float v = v0 * MathF.Max(f, 1e-3f);
                float segEnd = d < route.Length && route.Cumulative.Length > seg + 1 ? route.Cumulative[seg + 1] : total;
                float need = (segEnd - d) / v;
                if (need >= left) { d += v * left; left = 0f; u.SpeedMps = v; }
                else { d = segEnd; left -= need; u.SpeedMps = v; }
            }
            u.PathDistanceM = MathF.Min(d, total);
            var head = route.PointAt(u.PathDistanceM, out int s);
            ref var t = ref repo.GetComponentRW<SimTransform>(e);
            t.Position = head;
            if (route.Points.Length >= 2)
            {
                var dir = route.Points[s + 1] - route.Points[s];
                if (dir.X * dir.X + dir.Y * dir.Y > 1e-6f) u.Heading = MathF.Atan2(dir.X, dir.Y) * (180f / MathF.PI);
            }
            if (u.PathDistanceM >= total) { u.Move = UnitMoveState.Arrived; u.SpeedMps = 0f; u.Activity &= ~UnitActivity.Moving; }
            else u.Activity |= UnitActivity.Moving;
        }

        /// <summary>⭐ M-1 — the route for the unit with THE planner, its segments' terrain factors fixed (M-2). Null when the planner refuses
        /// and the unit is mounted on wheels over impassable ground; otherwise the straight leg when nothing can plan.</summary>
        internal UnitRoute? Plan(EntityRepository repo, Vector3 from, Vector3 to, in AggregateUnit u, in UnitHoldings h)
        {
            var mobility = UnitSpeedModel.LimitingMobility(in u, in h);
            bool mounted = u.Posture == UnitPosture.Mounted && mobility != HoldingMobility.NoMobility;
            var navmesh = repo.HasSingletonManaged<INavmeshProvider>() ? repo.GetSingletonManaged<INavmeshProvider>() : null;
            var holder = repo.HasSingletonManaged<global::CarKinem.Road.RoadNetworkHolder>() ? repo.GetSingletonManaged<global::CarKinem.Road.RoadNetworkHolder>() : null;
            using var lease = holder?.Borrow();
            var graph = lease?.Value ?? default;
            _planner.World = Fdp.Toolkit.World.WorldQuery.Of(repo);
            var q = new RouteQuery(from, to, mounted ? RoadUse.Prefer : RoadUse.NavmeshFirst, NavigationBackend.Auto, mounted ? VehicleLayer : InfantryLayer);
            Vector3[] pts;
            NavigationBackend? backend = null;
            if ((navmesh != null || RoadGraphRouter.HasRoads(graph))
                && (backend = _planner.Plan(in q, in graph, navmesh, Fdp.Toolkit.Terrain.DoorStates.Of(repo), out _)) != null)
            {
                pts = new Vector3[_planner.Points.Count];
                for (int i = 0; i < pts.Length; i++) pts[i] = _planner.Points[i];
            }
            else pts = new[] { from, to };   // nothing can plan it: the straight leg
            var terrain = repo.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() ? repo.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() : null;
            return Build(pts, backend, mobility, UnitSpeedModel.BaseSpeed(in u, in h), graph, terrain?.Woods);
        }

        /// <summary>The route record: distances and each segment's terrain factor (on a road ⇔ its midpoint within
        /// <see cref="UnitSpeedModel.OnRoadMetres"/> of the road graph; in woods ⇔ the woods grid's density there).</summary>
        public static UnitRoute Build(Vector3[] pts, NavigationBackend? backend, HoldingMobility mobility, float baseSpeed,
                                      in global::CarKinem.Road.RoadNetworkBlob roads, Fdp.Toolkit.Terrain.TerrainWoods? woods)
        {
            var cum = new float[pts.Length];
            var factor = new float[Math.Max(0, pts.Length - 1)];
            bool hasRoads = RoadGraphRouter.HasRoads(roads);
            for (int i = 1; i < pts.Length; i++)
            {
                cum[i] = cum[i - 1] + Vector2.Distance(new Vector2(pts[i].X, pts[i].Y), new Vector2(pts[i - 1].X, pts[i - 1].Y));
                var mid = (pts[i] + pts[i - 1]) * 0.5f;
                bool onRoad = hasRoads && RoadGraphRouter.NearestAccess(roads, new Vector2(mid.X, mid.Y), UnitSpeedModel.OnRoadMetres).IsValid;
                bool inWoods = woods != null && woods.DensityAt(mid.X, mid.Y) >= Fdp.Toolkit.Terrain.TerrainWoods.MinWoodsDensity;
                factor[i - 1] = UnitSpeedModel.TerrainFactor(mobility, onRoad, inWoods, baseSpeed);
            }
            return new UnitRoute { Points = pts, Cumulative = cum, SegmentFactor = factor, Backend = backend };
        }
    }
}
