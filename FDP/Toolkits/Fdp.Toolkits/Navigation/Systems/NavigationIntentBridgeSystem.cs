using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using CarKinem.Core;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using NLog;

namespace Fdp.Toolkit.Navigation.Systems
{
    /// <summary>
    /// Bridges <see cref="NavigationIntent"/> (CQRS command written by the Brain tier via
    /// <see cref="Executors.MoveToExecutor"/>) into <see cref="NavState"/> (physics input
    /// consumed by <see cref="CarKinem.Systems.CarKinematicsSystem"/>).
    ///
    /// <para>
    /// This system is the "nervous system" adapter for the CQRS navigation contract
    /// (MOD1-P1T1/P1T2). It must run <b>after</b> <c>LocomotionDispatcherSystem</c>
    /// (so executors have already written <see cref="NavigationIntent"/>) and
    /// <b>before</b> <c>CarKinematicsSystem</c> (so the updated <see cref="NavState"/>
    /// is visible to the physics layer in the same tick).
    /// </para>
    ///
    /// <para>
    /// <b>Mapping rules:</b>
    /// <list type="bullet">
    ///   <item>If <see cref="NavigationIntent.Mode"/> is <see cref="NavigationMode.None"/> →
    ///     halt navigation by setting <c>Mode=None</c> and <c>TargetSpeed=0</c> on <see cref="NavState"/>.</item>
    ///   <item><see cref="NavigationMode.DirectPoint"/> → <c>KinematicsMode.Direct</c>:
    ///     copy <c>FinalDestination</c>, <c>TargetSpeed</c>, <c>ArrivalRadius</c>.</item>
    ///   <item><see cref="NavigationMode.RoadGraph"/> → <c>KinematicsMode.RoadGraph</c>:
    ///     copy <c>TargetNodeId</c> to <see cref="NavState.CurrentSegmentId"/>.</item>
    ///   <item><see cref="NavigationMode.FollowRoute"/> → <c>KinematicsMode.CustomTrajectory</c>:
    ///     copy <c>TrajectoryId</c>.  When <c>IntentId</c> changes, <c>NavState.ProgressS</c>
    ///     is reset to 0 so the vehicle restarts the route from the beginning.</item>
    /// </list>
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public class NavigationIntentBridgeSystem : IEcsModuleSystem
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        // ⭐ R-3039 S1 — what was already applied (the intent id, the locomotion action instance) is on the mover (NavigationApplied), not
        //   here: a checkpoint restored into a fresh system would otherwise apply every intent again. IMPORTANT (STR-D21 F6 fix): the action
        //   instance is only recorded when crowd registration succeeds — if RegisterAgent returns false (crowd not yet initialized) it is NOT
        //   recorded, so the bridge retries on the next tick.

        // ⭐ CE-3026 — PathToPoint intents whose crowd registration is waiting for the crowd provider (by IntentId).
        private readonly Dictionary<Entity, uint> _pendingCrowd = new();

        private readonly TrajectoryPoolManager? _trajectoryPool;
        private readonly IDtCrowdProvider? _dtCrowd;

        private uint _lastScanTick;
        private int _epoch;   // ⭐ CE-3294 — the world the caches below describe

        /// <summary>⭐ CE-2171 — FollowRoute intents whose route entity is not compiled on this node yet: retried every tick.</summary>
        private readonly Dictionary<Entity, uint> _pendingRoute = new();

        /// <summary>
        /// ⭐⭐ <c>CE-2171</c> / Air Ops B1 — resolves <see cref="NavigationIntent.RouteEntityId"/> to THIS node's compiled trajectory. The
        /// host that compiles routes passes it (SimHost / the editor's Muscle: <c>SimHostCoreLogicPack</c>); null = the intent's local
        /// <see cref="NavigationIntent.TrajectoryId"/> only (as before).
        /// </summary>
        public IRouteSource? RouteSource { get; init; }

        /// <summary>
        /// Creates an instance without trajectory pool access.
        /// FollowPath and ReleasePath actions requiring pool queries will treat all handles as invalid.
        /// </summary>
        public NavigationIntentBridgeSystem() { }

        /// <summary>
        /// Creates an instance with access to the shared <see cref="TrajectoryPoolManager"/>
        /// for FollowPath handle validation and ReleasePath cleanup.
        /// </summary>
        public NavigationIntentBridgeSystem(TrajectoryPoolManager? trajectoryPool)
        {
            _trajectoryPool = trajectoryPool;
        }

        /// <summary>
        /// Creates an instance with access to the crowd provider for infantry crowd registration.
        /// </summary>
        public NavigationIntentBridgeSystem(TrajectoryPoolManager? trajectoryPool, IDtCrowdProvider? dtCrowd)
        {
            _trajectoryPool = trajectoryPool;
            _dtCrowd = dtCrowd;
        }

        /// <summary>⭐ CE-2171 — the trajectory a FollowRoute intent drives on THIS node: the route entity's own compiled one when the
        /// intent names a route and this node compiled it, else the intent's local <see cref="NavigationIntent.TrajectoryId"/>.</summary>
        /// <summary>
        /// ⭐ <c>CE-4032</c> (behaviors, cross-lane) — where a route starts for <paramref name="entity"/>: the point of a one-shot route
        /// nearest to where it stands, so a vehicle authored partway along its route drives ON. ⛔ Was always 0: steering aims a few metres
        /// past the progress point, so a vehicle standing past the route's first waypoint turned back to it — 📌 measured live on
        /// <c>uc-urban-battle</c>: the lead APC (10 m past the start) turned round while the IFV behind it drove on and took the lead, every
        /// run. A looped route, an unknown trajectory or no position ⇒ 0, as before.
        /// </summary>
        private float StartProgress(EntityRepository repo, Entity entity, in NavigationIntent intent, int trajectory)
        {
            if (!repo.HasComponent<SimTransform>(entity)) return 0f;
            var at3 = repo.GetComponentRO<SimTransform>(entity).Position;
            var at = new Vector2(at3.X, at3.Y);
            if (_trajectoryPool != null && _trajectoryPool.TryGetTrajectory(trajectory, out var traj)) return NearestProgress(in traj, at);
            // the host's route source (SimHost passes no pool): the route entity's own waypoints — the polyline its trajectory is compiled from
            if (RouteSource == null || intent.RouteEntityId == 0) return 0f;
            _routePoints.Clear();
            if (!RouteSource.TryGetRoute(repo, intent.RouteEntityId, _routePoints, out bool loop, out _) || loop) return 0f;
            float best = float.MaxValue, progress = 0f, cumulative = 0f;
            for (int k = 0; k < _routePoints.Count - 1; k++)
            {
                var pa = new Vector2(_routePoints[k].Position.X, _routePoints[k].Position.Y);
                var ab = new Vector2(_routePoints[k + 1].Position.X, _routePoints[k + 1].Position.Y) - pa;
                float len = ab.Length();
                float along = len < 1e-4f ? 0f : Math.Clamp(Vector2.Dot(at - pa, ab / len), 0f, len);
                float d = Vector2.DistanceSquared(at, len < 1e-4f ? pa : pa + ab / len * along);
                if (d < best) { best = d; progress = cumulative + along; }
                cumulative += len;
            }
            return progress;
        }

        private readonly List<NavRouteWaypoint> _routePoints = new();

        /// <summary>⭐ <c>CE-4032</c> — the arc length (XY) of the point of a one-shot <paramref name="traj"/> nearest <paramref name="at"/>
        /// (0 for a looped one); public for the rails.</summary>
        public static float NearestProgress(in CustomTrajectory traj, Vector2 at)
        {
            int n = traj.Waypoints.Length;
            if (traj.IsLooped != 0 || n < 2) return 0f;
            float best = float.MaxValue, progress = 0f;
            for (int k = 0; k < n - 1; k++)
            {
                var a = traj.Waypoints[k];
                var pa = new Vector2(a.Position.X, a.Position.Y);
                var ab = new Vector2(traj.Waypoints[k + 1].Position.X, traj.Waypoints[k + 1].Position.Y) - pa;
                float len = ab.Length();
                float along = len < 1e-4f ? 0f : Math.Clamp(Vector2.Dot(at - pa, ab / len), 0f, len);
                float d = Vector2.DistanceSquared(at, pa + (len < 1e-4f ? Vector2.Zero : ab / len * along));
                if (d < best) { best = d; progress = a.CumulativeDistance + along; }
            }
            return progress;
        }

        private int ResolveRouteTrajectory(EntityRepository repo, in NavigationIntent intent)
        {
            if (intent.RouteEntityId != 0 && RouteSource != null && RouteSource.TryGetTrajectory(repo, intent.RouteEntityId, out int own))
                return own;
            return intent.RouteEntityId != 0 && RouteSource != null ? 0 : intent.TrajectoryId;
        }

        /// <summary>⭐ R-3039 S1 — the intent already applied to <paramref name="e"/> (null: none).</summary>
        private static uint? AppliedIntent(EntityRepository repo, Entity e) =>
            repo.HasComponent<NavigationApplied>(e) && repo.GetComponentRO<NavigationApplied>(e) is { HasIntent: 1 } a ? a.IntentId : null;

        /// <summary>⭐ R-3039 S1 — the locomotion action instance already applied to <paramref name="e"/> (null: none).</summary>
        private static uint? AppliedAction(EntityRepository repo, Entity e) =>
            repo.HasComponent<NavigationApplied>(e) && repo.GetComponentRO<NavigationApplied>(e) is { HasAction: 1 } a ? a.ActionInstanceId : null;

        private static void MarkIntentApplied(EntityRepository repo, Entity e, uint intentId)
        {
            var a = repo.HasComponent<NavigationApplied>(e) ? repo.GetComponentRO<NavigationApplied>(e) : default;
            a.IntentId = intentId; a.HasIntent = 1;
            repo.SetComponent(e, a);
        }

        private static void MarkActionApplied(EntityRepository repo, Entity e, uint actionInstanceId)
        {
            var a = repo.HasComponent<NavigationApplied>(e) ? repo.GetComponentRO<NavigationApplied>(e) : default;
            a.ActionInstanceId = actionInstanceId; a.HasAction = 1;
            repo.SetComponent(e, a);
        }

        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(NavigationIntentBridgeSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");
            // ⭐ R-3039 S1 — loud, not a silent no-op: without its memory component the bridge would re-apply every intent each scan.
            if (!repo.IsComponentTypeRegistered<NavigationApplied>())
                throw new InvalidOperationException(
                    $"{nameof(NavigationIntentBridgeSystem)} needs {nameof(NavigationApplied)} registered (what each mover already applied).");

            // STRICT ARCHITECTURAL BOUNDARY: Detect time-travel (snapshot restore or world clear)
            // and invalidate tracking caches to force a full baseline re-evaluation.
            // ⭐ CE-3294 — and a NEW WORLD: a reload's units come back under the same handles (the editor's SoftClear restarts the
            //   generations) and GlobalVersion keeps counting, so the check above never saw it — an order whose ids matched last run's
            //   was skipped as already applied.
            bool newWorld = Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _epoch);
            if (newWorld || repo.GlobalVersion < _lastScanTick)
            {
                _lastScanTick = 0;   // a full re-scan; what was applied is on each mover, restored with the world
                _pendingRoute.Clear();
            }
            if (newWorld) _pendingCrowd.Clear();

            // ⭐ CE-498 — Constructing entities too, not only Active (the query default). An intent written while the entity
            //   is still CONSTRUCTING was otherwise lost for good: becoming Active changes no component version, so this
            //   DELTA query never revisited the entity (measured 2026-10-01: NavState.Mode stayed None, velocity 0 — a
            //   spawn-then-move order never moved). Applying NavState early is safe: the kinematics run on Active entities
            //   only, so motion still starts at activation. ⚠ Ghost → Active PROMOTION has the same blind spot and is NOT
            //   covered here (whether a ghost may take NavState is the navigation design's call) — filed with CE-498.
            // ⭐ CE-3026 — NavState is NOT required here: a PathToPoint for infantry (no kinematic NavState — the crowd moves
            //   it) must still reach RequestPath/JoinCrowd below. Every other mode only writes NavState, so it skips without one.
            var query = repo.Query()
                .With<NavigationIntent>()
                .IncludeAll()
                .Build();

            bool locomotion = repo.IsComponentTypeRegistered<Fdp.Toolkit.Motion.Locomotion>();
            // 1. Coarse unmanaged filter
            foreach (var entity in repo.QueryDelta(query, _lastScanTick))
            {
                var lifecycle = repo.GetLifecycleState(entity);
                if (lifecycle != EntityLifecycle.Active && lifecycle != EntityLifecycle.Constructing)
                    continue;

                // ⭐ M7 / CE-2168 (Motion Domains MD-E) — a flyer (or later a boat) is not a ground mover: its domain's model reads the
                //   intent itself (v1: straight legs to the 3-D point) — no ground path request, no NavState
                if (locomotion && repo.HasComponent<Fdp.Toolkit.Motion.Locomotion>(entity)
                    && Fdp.Toolkit.Motion.Locomotions.DomainOf(repo.GetComponentRO<Fdp.Toolkit.Motion.Locomotion>(entity).Class)
                       is Fdp.Toolkit.Motion.MotionDomain.Air or Fdp.Toolkit.Motion.MotionDomain.Naval)
                    continue;

                var intent = repo.GetComponent<NavigationIntent>(entity);

                // 2. Fine-grained filter using the full Entity struct
                if (AppliedIntent(repo, entity) == intent.IntentId) continue;

                bool hasNav = repo.HasComponent<NavState>(entity);
                if (!hasNav && intent.Mode != NavigationMode.PathToPoint) continue;   // nothing to drive (as before)
                var nav = hasNav ? repo.GetComponent<NavState>(entity) : default;

                switch (intent.Mode)
                {
                    case NavigationMode.None:
                        // Explicit cancel/stop intent from cognitive tier.
                        nav.Mode        = KinematicsMode.None;
                        nav.TargetSpeed = 0f;
                        nav.HasArrived  = 0;
                        nav.ReverseAllowed = 0;
                        break;

                    case NavigationMode.DirectPoint:
                        nav.Mode             = KinematicsMode.Direct;
                        nav.FinalDestination = NavigationDestination.Of(repo, entity, intent);   // ⭐ CE-1035 Q0b
                        nav.TargetSpeed      = intent.TargetSpeed;
                        nav.ArrivalRadius    = intent.ArrivalRadius;
                        nav.ReverseAllowed   = intent.ReverseAllowed;
                        nav.HasArrived       = 0;
                        break;

                    case NavigationMode.PathToPoint when hasNav && global::CarKinem.Systems.NavigationExecutionSystem.IsResentMove(repo, entity, in intent, in nav):
                        // ⭐ CE-2304 — a RE-SENT move to the point the path already leads to keeps that path (and the man walking it):
                        //   no stop, no new request. Only its speed and arrival radius follow the new intent. 📌 Live the brain re-sent
                        //   each post move ~2×/s, and every re-send stopped the man to wait for a fresh path — he walked at ~0.4 m/s.
                        nav.TargetSpeed   = intent.TargetSpeed > 0f ? intent.TargetSpeed : nav.TargetSpeed;
                        nav.ArrivalRadius = intent.ArrivalRadius;
                        repo.SetComponent(entity, nav);
                        MarkIntentApplied(repo, entity, intent.IntentId);
                        continue;

                    case NavigationMode.PathToPoint:
                        // ⭐ CE-3026 — wait (no straight-line start) until the path arrives; the request is published
                        //   below once the NavState is stored.
                        // ⭐ CE-3176 — and the wait HOLDS: speed 0. CarKinematicsSystem drives Mode None like Direct whenever it
                        //   has a speed (the legacy idle-with-a-goal meaning examples and tests rely on), so a kept speed drove
                        //   straight at the goal until the path came back. The path response restores the intent's speed.
                        nav.Mode             = KinematicsMode.None;
                        nav.FinalDestination = NavigationDestination.Of(repo, entity, intent);   // ⭐ CE-1035 Q0b
                        nav.TargetSpeed      = 0f;
                        nav.ArrivalRadius    = intent.ArrivalRadius;
                        nav.ReverseAllowed   = intent.ReverseAllowed;
                        nav.TrajectoryId     = 0;
                        nav.HasArrived       = 0;
                        break;

                    case NavigationMode.RoadGraph:
                        nav.Mode             = KinematicsMode.RoadGraph;
                        nav.RoadPhase        = RoadGraphPhase.Approaching;
                        nav.CurrentSegmentId = intent.TargetNodeId;
                        nav.TargetSpeed      = intent.TargetSpeed;
                        nav.HasArrived       = 0;
                        break;

                    case NavigationMode.Manual:
                        // ⭐ CE-3173 — the body follows ManualDrive (CarKinematicsSystem's Manual branch); no destination.
                        nav.Mode        = KinematicsMode.Manual;
                        nav.TargetSpeed = 0f;
                        nav.HasArrived  = 0;
                        break;

                    case NavigationMode.Traffic:
                        // ⭐ AQ93 R-4023 — SUMO drives; the body follows its shadow car (TrafficShadowFollowSystem) to the destination.
                        nav.Mode             = KinematicsMode.Traffic;
                        nav.FinalDestination = NavigationDestination.Of(repo, entity, intent);
                        nav.TargetSpeed      = intent.TargetSpeed;
                        nav.ArrivalRadius    = intent.ArrivalRadius;
                        nav.HasArrived       = 0;
                        break;

                    case NavigationMode.FollowRoute:
                        // ⭐⭐ CE-2171 — the route ENTITY resolves to this node's own compiled trajectory; the intent's TrajectoryId is
                        //   a pool index of the node that wrote it (0 on a split cluster). Not compiled yet ⇒ retried every tick below.
                        int trajectory = ResolveRouteTrajectory(repo, in intent);
                        if (trajectory == 0 && intent.RouteEntityId != 0)
                        {
                            _pendingRoute[entity] = intent.IntentId;
                            continue;
                        }
                        nav.Mode         = KinematicsMode.CustomTrajectory;
                        nav.TrajectoryId = trajectory;
                        nav.TargetSpeed  = intent.TargetSpeed;   // ⭐ CE-2060 — caps the route's speeds (0 = uncapped); was left from the previous move
                        nav.HasArrived   = 0;
                        nav.ProgressS    = StartProgress(repo, entity, in intent, trajectory);   // ⭐ CE-4032
                        break;

                    default:
                        nav.Mode             = KinematicsMode.Direct;
                        nav.FinalDestination = NavigationDestination.Of(repo, entity, intent);   // ⭐ CE-1035 Q0b
                        nav.TargetSpeed      = intent.TargetSpeed;
                        nav.ArrivalRadius    = intent.ArrivalRadius;
                        nav.HasArrived       = 0;
                        break;
                }

                if (hasNav) repo.SetComponent(entity, nav);

                if (intent.Mode == NavigationMode.PathToPoint)
                {
                    RequestPath(repo, entity, in intent);
                    if (!JoinCrowd(repo, entity, in intent))
                        _pendingCrowd[entity] = intent.IntentId;   // crowd not ready yet — retried every tick below
                }
            
                // Cache against the generation-safe handle
                MarkIntentApplied(repo, entity, intent.IntentId);
            }

            _lastScanTick = repo.GlobalVersion;

            // ⭐ CE-2171 — retry a FollowRoute whose route was not compiled on this node yet (the route entity can arrive, and compile,
            //   after the intent does).
            if (_pendingRoute.Count > 0)
            {
                foreach (var (pending, pendingIntentId) in new List<KeyValuePair<Entity, uint>>(_pendingRoute))
                {
                    if (!repo.IsAlive(pending) || !repo.HasComponent<NavigationIntent>(pending) || !repo.HasComponent<NavState>(pending))
                    { _pendingRoute.Remove(pending); continue; }
                    var pendingIntent = repo.GetComponent<NavigationIntent>(pending);
                    if (pendingIntent.IntentId != pendingIntentId || pendingIntent.Mode != NavigationMode.FollowRoute) { _pendingRoute.Remove(pending); continue; }
                    int trajectory = ResolveRouteTrajectory(repo, in pendingIntent);
                    if (trajectory == 0) continue;
                    var nav = repo.GetComponent<NavState>(pending);
                    nav.Mode         = KinematicsMode.CustomTrajectory;
                    nav.TrajectoryId = trajectory;
                    nav.TargetSpeed  = pendingIntent.TargetSpeed;
                    nav.HasArrived   = 0;
                    nav.ProgressS    = StartProgress(repo, pending, in pendingIntent, trajectory);   // ⭐ CE-4032
                    repo.SetComponent(pending, nav);
                    MarkIntentApplied(repo, pending, pendingIntent.IntentId);
                    _pendingRoute.Remove(pending);
                }
            }

            // ⭐ CE-3026 — retry a PathToPoint whose crowd registration was deferred (the crowd provider exists only once a
            //   navmesh is baked). Replaces the old ActionInstanceId non-caching trick (STR-D21 F6) of the removed branch.
            if (_pendingCrowd.Count > 0)
            {
                foreach (var (pending, pendingIntentId) in new List<KeyValuePair<Entity, uint>>(_pendingCrowd))
                {
                    if (!repo.IsAlive(pending) || !repo.HasComponent<NavigationIntent>(pending)) { _pendingCrowd.Remove(pending); continue; }
                    var pendingIntent = repo.GetComponent<NavigationIntent>(pending);
                    if (pendingIntent.IntentId != pendingIntentId || pendingIntent.Mode != NavigationMode.PathToPoint) { _pendingCrowd.Remove(pending); continue; }
                    if (JoinCrowd(repo, pending, in pendingIntent)) _pendingCrowd.Remove(pending);
                }
            }

            // ── Route LocomotionChannel actions into the nav v2 pipeline ──────────────
            // Iterate ALL entities with LocomotionChannel each tick; use the
            // NavigationApplied (on the mover) for fine-grained idempotency since a
            // change to ActionInstanceId does not alter the component mask (QueryDelta
            // would miss the transition).
            if (!repo.IsComponentTypeRegistered<LocomotionChannel>())
                return;

            var chQuery = repo.Query()
                .With<LocomotionChannel>()
                .Build();

            foreach (var entity in chQuery)
            {
                ref var ch = ref repo.GetComponentRW<LocomotionChannel>(entity);

                // Idempotency: skip if this ActionInstanceId was already applied.
                if (AppliedAction(repo, entity) == ch.ActionInstanceId) continue;

                // STR-D21 F6 fix: when crowd registration is deferred (RegisterAgent returns
                // false because the navmesh is not yet baked), we must NOT cache the
                // ActionInstanceId — so the bridge retries on the next tick.
                // This flag is set to false only in the deferred-crowd path below.
                bool cacheActionId = true;

                switch (ch.ActiveAction)
                {
                    // ⛔ CE-3026 — the MoveTo branch is GONE from here. It read the Brain's LocomotionChannel directly, which
                    //   only works when Brain and Muscle share a world (the editor) — so the editor planned and a cluster
                    //   drove straight through buildings. A MoveTo now reaches the vehicle side the same way on every host:
                    //   MoveToExecutor → NavigationIntent{PathToPoint} → (wire on a cluster) → the PathToPoint case above.
                    case NavigationConstants.ActionIdPlanRoute:
                    {
                        var p = Unsafe.ReadUnaligned<PlanRouteParams>(ref ch.Params[0]);

                        var from = repo.HasComponent<SimTransform>(entity)
                            ? repo.GetComponent<SimTransform>(entity).Position
                            : Vector3.Zero;

                        // Carry the Brain-allocated RouteHandle through if the entity
                        // has a NavigationIntent with a pre-allocated handle.
                        int routeHandle = repo.HasComponent<NavigationIntent>(entity)
                            ? repo.GetComponent<NavigationIntent>(entity).RouteHandle
                            : 0;

                        var agentProfile = repo.HasComponent<NavAgentProfile>(entity)
                            ? repo.GetComponent<NavAgentProfile>(entity)
                            : default;

                        long reqId = ((long)entity.Index << 32) | (uint)repo.GlobalVersion;
                        repo.Bus.Publish(new PathfindingRequestEvent
                        {
                            RequestId       = reqId,
                            Start           = from,
                            End             = p.Destination, // real destination Z (Sim Z-up, P3D-302)
                            MobilityProfile = agentProfile.MobilityProfile,
                            BackendForce    = (NavigationBackend)p.BackendForce,
                            RoadUse         = PathRequests.ResolveRoadUse(repo, entity, p.RoadUse),   // CE-3128
                            RouteHandle     = routeHandle,
                            NavLayerMask    = (int)NavLayerSelection.For(repo, entity, (uint)p.LayerMask),
                            MaxCost         = p.MaxCost,
                            TurnRadius      = PathRequests.TurnRadiusOf(repo, entity),   // ⭐ CE-3029 (§5.2b C2) — a planned route is driven too
                        });
                        break;
                    }

                    case NavigationConstants.ActionIdFollowPath:
                    {
                        var p = Unsafe.ReadUnaligned<FollowPathParams>(ref ch.Params[0]);

                        bool found = _trajectoryPool?.TryGetTrajectory(p.RouteHandle, out _) == true;
                        if (!found)
                        {
                            // Handle is not in the trajectory pool — report failure immediately.
                            repo.AddComponent(entity, new NavigationStatus
                            {
                                Result = NavigationResult.FailedInvalidHandle,
                            });
                        }
                        break;
                    }

                    case NavigationConstants.ActionIdFetchPathDetails:
                    {
                        var p = Unsafe.ReadUnaligned<FetchPathDetailsParams>(ref ch.Params[0]);

                        // Publish NavigationPathDetailsResponseEvent so the Brain-side
                        // NavigationPathDetailsUpdateSystem can ingest it this tick.
                        if (_trajectoryPool != null && _trajectoryPool.TryGetTrajectory(p.RouteHandle, out _))
                        {
                            var replanCount = repo.HasComponent<NavigationStatus>(entity)
                                ? (byte)repo.GetComponent<NavigationStatus>(entity).ReplanCount
                                : (byte)0;

                            repo.Bus.Publish(new NavigationPathDetailsResponseEvent
                            {
                                Target        = entity,
                                RouteHandle   = p.RouteHandle,
                                ReplanCount   = replanCount,
                                IsAutoRefresh = 0,
                            });
                        }
                        break;
                    }

                    case NavigationConstants.ActionIdReleasePath:
                    {
                        var p = Unsafe.ReadUnaligned<ReleasePathParams>(ref ch.Params[0]);

                        _trajectoryPool?.RemoveTrajectory(p.RouteHandle);

                        // Reset the corridor muscle component so downstream systems
                        // see a clean state immediately after release.
                        if (repo.IsComponentTypeRegistered<NavigationCorridorMuscle>()
                            && repo.HasComponent<NavigationCorridorMuscle>(entity))
                        {
                            repo.AddComponent(entity, default(NavigationCorridorMuscle));
                        }
                        break;
                    }
                }

                // Only cache the ActionInstanceId when the action was fully processed.
                // If cacheActionId=false (deferred crowd not ready), we skip caching so the
                // bridge retries this action on every subsequent tick.
                if (cacheActionId)
                    MarkActionApplied(repo, entity, ch.ActionInstanceId);
            }
        }
    
        /// <summary>
        /// ⭐ CE-3026 — a <see cref="NavigationMode.PathToPoint"/> is planned on THIS node (the vehicle side), the same way on
        /// every host. Infantry also joins the crowd (<see cref="JoinCrowd"/>). The path solver's answer puts the entity on the
        /// trajectory (<c>EngineBackedPathResponseSystem</c>) or reports <c>FailedUnreachable</c>
        /// (<c>PathfindingResultMaterializationSystem</c>); a node with no solver (Stride) leaves it to its own planner.
        /// </summary>
        private static void RequestPath(EntityRepository repo, Entity entity, in NavigationIntent intent)
        {
            var from = repo.HasComponent<SimTransform>(entity)
                ? repo.GetComponent<SimTransform>(entity).Position
                : Vector3.Zero;
            // ⭐ CE-3128 — built in ONE place with the replan (PathRequests), so the road use reaches the solver on both paths.
            repo.Bus.Publish(PathRequests.FromIntent(repo, entity, in intent, from, ((long)entity.Index << 32) | (uint)repo.GlobalVersion));
        }

        /// <summary>Infantry (no <see cref="VehicleState"/>) on a crowd host joins the crowd, targeted at the intent's
        /// destination. Returns false only when the crowd is not initialised yet (no navmesh baked) — retry next tick.</summary>
        private bool JoinCrowd(EntityRepository repo, Entity entity, in NavigationIntent intent)
        {
            if (_dtCrowd == null || repo.HasComponent<VehicleState>(entity)) return true;
            var from = repo.HasComponent<SimTransform>(entity)
                ? repo.GetComponent<SimTransform>(entity).Position
                : Vector3.Zero;
            var profile = repo.HasComponent<NavAgentProfile>(entity)
                ? repo.GetComponent<NavAgentProfile>(entity)
                : default;
            float radius = profile.AgentRadius > 0f ? profile.AgentRadius : 0.4f;
            float maxSpd = intent.TargetSpeed > 0f ? intent.TargetSpeed : 5f;

            // BATCH-26 / STR-D20: start the agent at its real position, or DtCrowd plans from (0,0,0).
            bool registered = _dtCrowd.RegisterAgent(entity, new CrowdAgentParams
            {
                Radius           = radius,
                Height           = profile.AgentHeight > 0f ? profile.AgentHeight : 1.8f,
                MaxSpeed         = maxSpd,
                MaxAcceleration  = 20f,
                SeparationWeight = 2,
            }, from);

            if (!registered && !_dtCrowd.TryGetAgentSnapshot(entity, out _))
            {
                // Crowd not initialised yet (no navmesh baked) — the caller retries every tick (STR-D21 F6).
                Log.Debug("[BridgeReg] entity #{0} RegisterAgent deferred (crowd not initialized yet).", entity.Index);
                return false;
            }

            // Registered now, or already registered: (re)target it and tag it crowd-managed.
            _dtCrowd.SetAgentTarget(entity, NavigationDestination.Of(repo, entity, intent));   // carries real Z (P3D-302); ⭐ CE-1035 Q0b
            if (!repo.HasComponent<CrowdAgent>(entity))
                repo.AddComponent(entity, default(CrowdAgent));
            Log.Info("[BridgeReg] entity #{0} crowd target: radius={1:F2} maxSpd={2:F1} dest=({3:F1},{4:F1}) intent={5}",
                entity.Index, radius, maxSpd, intent.FinalDestination.X, intent.FinalDestination.Y, intent.IntentId);
            return true;
        }
    }
}
