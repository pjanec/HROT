using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using CarKinem.Core;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;

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
        // FIX: Cache by the full Entity struct (Index + Generation) to prevent
        // false-positives when entity indices are recycled by the free-list.
        private readonly Dictionary<Entity, uint> _lastAppliedIntentId = new();

        // Cache for LocomotionChannel action idempotency (keyed by full Entity).
        private readonly Dictionary<Entity, uint> _lastAppliedActionInstanceId = new();

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

            var query = repo.Query()
                .With<NavigationIntent>()
                .With<NavState>()
                .Build();

            // 1. Coarse unmanaged filter
            foreach (var entity in repo.QueryDelta(query, _lastScanTick))
            {
                var intent = repo.GetComponent<NavigationIntent>(entity);

                // 2. Fine-grained filter using the full Entity struct
                if (_lastAppliedIntentId.TryGetValue(entity, out uint lastId) 
                    && lastId == intent.IntentId)
                {
                    continue;
                }

                var nav = repo.GetComponent<NavState>(entity);

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

                repo.SetComponent(entity, nav);
            
                // Cache against the generation-safe handle
                _lastAppliedIntentId[entity] = intent.IntentId;
            }

            _lastScanTick = repo.GlobalVersion;

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

                switch (ch.ActiveAction)
                {
                    case NavigationConstants.ActionIdMoveTo:
                    {
                        var p = Unsafe.ReadUnaligned<MoveToParams>(ref ch.Params[0]);

                        var from = repo.HasComponent<SimTransform>(entity)
                            ? repo.GetComponent<SimTransform>(entity).Position
                            : Vector3.Zero;

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
                            RouteHandle     = p.RouteHandle,
                            NavLayerMask    = (int)p.LayerMask,
                        });

                        // Crowd registration for infantry (entities without VehicleState).
                        if (_dtCrowd != null && !repo.HasComponent<VehicleState>(entity))
                        {
                            var profile = repo.HasComponent<NavAgentProfile>(entity)
                                ? repo.GetComponent<NavAgentProfile>(entity)
                                : default;

                            float radius  = profile.AgentRadius > 0f ? profile.AgentRadius : 0.4f;
                            float maxSpd  = p.Speed > 0f ? p.Speed : 5f;

                            _dtCrowd.RegisterAgent(entity, new CrowdAgentParams
                            {
                                Radius          = radius,
                                Height          = profile.AgentHeight > 0f ? profile.AgentHeight : 1.8f,
                                MaxSpeed        = maxSpd,
                                MaxAcceleration = 20f,
                                SeparationWeight = 2,
                            });

                            // Tag the entity as crowd-managed.
                            if (!repo.HasComponent<CrowdAgent>(entity))
                                repo.AddComponent(entity, default(CrowdAgent));

                            // Set the target in the crowd provider (carry real Z, P3D-302).
                            _dtCrowd.SetAgentTarget(entity, p.Destination);
                        }
                        break;
                    }

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
                            NavLayerMask    = (int)p.LayerMask,
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

                _lastAppliedActionInstanceId[entity] = ch.ActionInstanceId;
            }
        }
    }
}
