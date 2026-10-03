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
    public class NavigationIntentBridgeSystem : IEcsModuleSystem
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        // FIX: Cache by the full Entity struct (Index + Generation) to prevent
        // false-positives when entity indices are recycled by the free-list.
        private readonly Dictionary<Entity, uint> _lastAppliedIntentId = new();

        // Cache for LocomotionChannel action idempotency (keyed by full Entity).
        // IMPORTANT (STR-D21 F6 fix): the ActionInstanceId is only cached when crowd
        // registration succeeds.  If RegisterAgent returns false (crowd not yet initialized)
        // we do NOT update this dict so the bridge retries on the next tick.
        private readonly Dictionary<Entity, uint> _lastAppliedActionInstanceId = new();

        // ⭐ CE-3026 — PathToPoint intents whose crowd registration is waiting for the crowd provider (by IntentId).
        private readonly Dictionary<Entity, uint> _pendingCrowd = new();

        private readonly TrajectoryPoolManager? _trajectoryPool;
        private readonly IDtCrowdProvider? _dtCrowd;

        private uint _lastScanTick;

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

        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(NavigationIntentBridgeSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            // STRICT ARCHITECTURAL BOUNDARY: Detect time-travel (snapshot restore or world clear)
            // and invalidate tracking caches to force a full baseline re-evaluation.
            if (repo.GlobalVersion < _lastScanTick)
            {
                _lastScanTick = 0;
                _lastAppliedIntentId.Clear();
            }

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

            // 1. Coarse unmanaged filter
            foreach (var entity in repo.QueryDelta(query, _lastScanTick))
            {
                var lifecycle = repo.GetLifecycleState(entity);
                if (lifecycle != EntityLifecycle.Active && lifecycle != EntityLifecycle.Constructing)
                    continue;

                var intent = repo.GetComponent<NavigationIntent>(entity);

                // 2. Fine-grained filter using the full Entity struct
                if (_lastAppliedIntentId.TryGetValue(entity, out uint lastId) 
                    && lastId == intent.IntentId)
                {
                    continue;
                }

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
                        nav.FinalDestination = intent.FinalDestination;
                        nav.TargetSpeed      = intent.TargetSpeed;
                        nav.ArrivalRadius    = intent.ArrivalRadius;
                        nav.ReverseAllowed   = intent.ReverseAllowed;
                        nav.HasArrived       = 0;
                        break;

                    case NavigationMode.PathToPoint:
                        // ⭐ CE-3026 — wait (no straight-line start) until the path arrives; the request is published
                        //   below once the NavState is stored. Speed/arrival are kept for the trajectory follower.
                        nav.Mode             = KinematicsMode.None;
                        nav.FinalDestination = intent.FinalDestination;
                        nav.TargetSpeed      = intent.TargetSpeed;
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

                    case NavigationMode.FollowRoute:
                        nav.Mode         = KinematicsMode.CustomTrajectory;
                        nav.TrajectoryId = intent.TrajectoryId;
                        nav.HasArrived   = 0;
                        nav.ProgressS    = 0f; 
                        break;

                    default:
                        nav.Mode             = KinematicsMode.Direct;
                        nav.FinalDestination = intent.FinalDestination;
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
                _lastAppliedIntentId[entity] = intent.IntentId;
            }

            _lastScanTick = repo.GlobalVersion;

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
            // _lastAppliedActionInstanceId dict for fine-grained idempotency since a
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
                if (_lastAppliedActionInstanceId.TryGetValue(entity, out uint lastActionId)
                    && lastActionId == ch.ActionInstanceId)
                {
                    continue;
                }

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
                            RouteHandle     = routeHandle,
                            NavLayerMask    = (int)NavLayerSelection.For(repo, entity, (uint)p.LayerMask),
                            MaxCost         = p.MaxCost,
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
                    _lastAppliedActionInstanceId[entity] = ch.ActionInstanceId;
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
            var agentProfile = repo.HasComponent<NavAgentProfile>(entity)
                ? repo.GetComponent<NavAgentProfile>(entity)
                : default;
            repo.Bus.Publish(new PathfindingRequestEvent
            {
                RequestId       = ((long)entity.Index << 32) | (uint)repo.GlobalVersion,
                Start           = from,
                End             = intent.FinalDestination,   // real destination Z (Sim Z-up, P3D-302)
                MobilityProfile = agentProfile.MobilityProfile,
                BackendForce    = (NavigationBackend)intent.BackendForce,
                RouteHandle     = intent.RouteHandle,
                NavLayerMask    = (int)NavLayerSelection.For(repo, entity, intent.LayerMask),
            });
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
            _dtCrowd.SetAgentTarget(entity, intent.FinalDestination);   // carries real Z (P3D-302)
            if (!repo.HasComponent<CrowdAgent>(entity))
                repo.AddComponent(entity, default(CrowdAgent));
            Log.Info("[BridgeReg] entity #{0} crowd target: radius={1:F2} maxSpd={2:F1} dest=({3:F1},{4:F1}) intent={5}",
                entity.Index, radius, maxSpd, intent.FinalDestination.X, intent.FinalDestination.Y, intent.IntentId);
            return true;
        }
    }
}
