using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using CarKinem.Core;
using CarKinem.Road;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Fdp.Toolkit.Navigation.Systems;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// All-in-one test harness that wires the full navigation pipeline
    /// (Bridge -> Solver -> Materialize -> CrowdUpdate -> NavExec) into a single
    /// object.  Tests call <see cref="SpawnInfantry"/>, <see cref="IssueMoveTo"/>,
    /// and <see cref="PumpFor"/> / <see cref="PumpUntil"/> to drive the simulation.
    /// </summary>
    public sealed class NavTestHarness : IDisposable
    {
        private const float Dt = 1f / 60f;

        private readonly NavigationIntentBridgeSystem              _bridge;
        private readonly PathfindingSolverSystem                   _solver;
        private readonly PathfindingResultMaterializationSystem    _materialize;
        private readonly CrowdAgentUpdateSystem                    _crowdUpdate;
        private readonly NavigationExecutionSystem                 _navExec;
        private readonly TrajectoryPoolManager                     _pool;
        private readonly OffMeshLinkDetectionSystem                _offMeshDetect;
        private readonly CorridorPreviewSystem                     _corridorPreview;
        private readonly NavigationPathDetailsUpdateSystem?        _pathDetailsUpdate;

        private uint _actionInstanceCounter;

        public EntityRepository          Repo         { get; }
        public CapturedEventLog          EventLog     { get; }
        public FakeNavmeshProvider       Navmesh      { get; }
        public FakeDtCrowdProvider       Crowd        { get; }
        public SharedPathRegistry        PathRegistry { get; }
        public FakeVolumetricPathProvider Volumetric  { get; }
        public BrainPathRegistry         BrainRegistry { get; }

        public IFakeNavmeshProviderTestApi NavmeshApi => (IFakeNavmeshProviderTestApi)Navmesh;

        public NavTestHarness(NavTestMap? map = null)
        {
            var world = NavigationTestWorldFactory.Create();
            // PathfindingResultEvent is not registered by the factory; add it here.
            world.RegisterEvent<PathfindingResultEvent>();

            var batch = new PathfindingBatchData
            {
                Results = new NativeArray<PathResult>(PathfindingBatchData.DefaultCapacity, Allocator.Persistent),
            };
            world.SetSingleton(batch);

            var module = map != null ? new NavigationFakesModule(map) : new NavigationFakesModule();
            module.RegisterProviders(world);

            // VehicleState component needed for SpawnVehicle and to prevent crowd registration.
            world.RegisterComponent<VehicleState>();

            // Events needed by off-mesh traversal tests.
            world.RegisterEvent<OffMeshTraversalStartedEvent>();

            _pool            = new TrajectoryPoolManager();
            _bridge          = new NavigationIntentBridgeSystem(_pool, module.Crowd);
            _solver          = new PathfindingSolverSystem(default(RoadNetworkBlob), _pool, module.Navmesh, module.Volumetric);
            _materialize     = new PathfindingResultMaterializationSystem();
            _crowdUpdate     = new CrowdAgentUpdateSystem(module.Crowd);
            _navExec         = new NavigationExecutionSystem();
            _offMeshDetect   = new OffMeshLinkDetectionSystem(module.PathRegistry, module.Crowd);
            _corridorPreview = new CorridorPreviewSystem(module.PathRegistry);

            Repo         = world;
            EventLog     = new CapturedEventLog();
            Navmesh      = module.Navmesh;
            Crowd        = module.Crowd;
            PathRegistry = module.PathRegistry;
            Volumetric   = module.Volumetric;
            BrainRegistry = new BrainPathRegistry();
            _pathDetailsUpdate = new NavigationPathDetailsUpdateSystem(PathRegistry.Muscle, BrainRegistry);
        }

        /// <summary>
        /// Spawns an infantry entity at the given XY position (Z = 0).
        /// Does NOT add NavState — arrival is checked via Cartesian XY distance in NavExec.
        /// </summary>
        public Entity SpawnInfantry(Vector2 pos)
        {
            var entity = Repo.CreateEntity();
            Repo.AddComponent(entity, new SimTransform { Position = new Vector3(pos.X, pos.Y, 0f) });
            Repo.AddComponent(entity, new SimVelocity());
            Repo.AddComponent(entity, new NavigationIntent());
            Repo.AddComponent(entity, new NavigationStatus());
            Repo.AddComponent(entity, new FrustrationTicks());
            Repo.AddComponent(entity, new LocomotionChannel());
            Repo.AddComponent(entity, new NavAgentProfile { AgentRadius = 0.4f, AgentHeight = 1.8f });
            Repo.AddComponent(entity, new CrowdAgent());
            return entity;
        }

        /// <summary>
        /// Issues a MoveTo command to the entity.
        /// Pre-sets status.IntentId so NavExec does not reset NavigationStatus on the same
        /// tick that PathfindingResultMaterializationSystem writes FailedUnreachable.
        /// </summary>
        public unsafe void IssueMoveTo(Entity e, Vector2 destination, byte flags = 0, int routeHandle = 0,
            uint layerMask = (uint)NavLayerMask.Infantry)
        {
            uint instanceId = ++_actionInstanceCounter;

            ref var ch = ref Repo.GetComponentRW<LocomotionChannel>(e);
            ch.ActiveAction     = NavigationConstants.ActionIdMoveTo;
            ch.ActionInstanceId = instanceId;
            var p = new MoveToParams
            {
                Destination   = destination,
                ArrivalRadius = 1.5f,
                Speed         = 5.0f,
                Flags         = flags,
                RouteHandle   = routeHandle,
                LayerMask     = layerMask,
            };
            Unsafe.WriteUnaligned(ref ch.Params[0], p);

            ref var intent = ref Repo.GetComponentRW<NavigationIntent>(e);
            intent.Mode             = NavigationMode.DirectPoint;
            intent.FinalDestination = destination;
            intent.IntentId         = instanceId;
            intent.ArrivalRadius    = 1.5f;
            intent.TargetSpeed      = 5.0f;
            intent.Flags            = flags;
            intent.RouteHandle      = routeHandle;

            // Pre-set status.IntentId so NavigationExecutionSystem sees no mismatch
            // on the same tick that PathfindingResultMaterializationSystem writes
            // FailedUnreachable.  Without this, NavExec would overwrite the result.
            ref var status = ref Repo.GetComponentRW<NavigationStatus>(e);
            status.IntentId = instanceId;
            status.Result   = NavigationResult.InProgress;
        }

        /// <summary>
        /// Issues a PlanRoute command. Entity stays in-place (NavigationMode.None) while the
        /// path is found. After status.Result == PathFound the caller can issue FollowPath.
        /// Bridge reads NavigationIntent.RouteHandle as the handle for the planned path.
        /// </summary>
        public unsafe void IssuePlanRoute(Entity e, Vector2 destination, int routeHandle = 0,
            uint layerMask = (uint)NavLayerMask.Infantry)
        {
            uint instanceId = ++_actionInstanceCounter;

            ref var ch = ref Repo.GetComponentRW<LocomotionChannel>(e);
            ch.ActiveAction     = NavigationConstants.ActionIdPlanRoute;
            ch.ActionInstanceId = instanceId;
            var p = new PlanRouteParams
            {
                Destination   = destination,
                ArrivalRadius = 1.5f,
                Speed         = 5.0f,
                LayerMask     = layerMask,
            };
            Unsafe.WriteUnaligned(ref ch.Params[0], p);

            // NavigationMode.None: NavExec skips -> entity stays still during planning.
            // Bridge reads intent.RouteHandle when processing ActionIdPlanRoute.
            ref var intent = ref Repo.GetComponentRW<NavigationIntent>(e);
            intent.Mode        = NavigationMode.None;
            intent.IntentId    = instanceId;
            intent.RouteHandle = routeHandle;
            intent.TargetSpeed = 5.0f;

            ref var status = ref Repo.GetComponentRW<NavigationStatus>(e);
            status.IntentId = instanceId;
            status.Result   = NavigationResult.InProgress;
        }

        /// <summary>
        /// Issues a FollowPath command. Sets NavigationMode.DirectPoint directly (bypassing the
        /// buggy FollowPathExecutor which sets Mode=None) and registers the entity with the crowd
        /// so it gets driven to the destination.
        /// </summary>
        public unsafe void IssueFollowPath(Entity e, int routeHandle, Vector2 destination)
        {
            uint instanceId = ++_actionInstanceCounter;

            ref var ch = ref Repo.GetComponentRW<LocomotionChannel>(e);
            ch.ActiveAction     = NavigationConstants.ActionIdFollowPath;
            ch.ActionInstanceId = instanceId;
            var p = new FollowPathParams
            {
                RouteHandle   = routeHandle,
                Speed         = 5.0f,
                ArrivalRadius = 1.5f,
            };
            Unsafe.WriteUnaligned(ref ch.Params[0], p);

            // Set DirectPoint directly: bypasses the FollowPathExecutor bug (Mode=None).
            ref var intent = ref Repo.GetComponentRW<NavigationIntent>(e);
            intent.Mode             = NavigationMode.DirectPoint;
            intent.FinalDestination = destination;
            intent.IntentId         = instanceId;
            intent.ArrivalRadius    = 1.5f;
            intent.TargetSpeed      = 5.0f;
            intent.RouteHandle      = routeHandle;

            ref var status = ref Repo.GetComponentRW<NavigationStatus>(e);
            status.IntentId = instanceId;
            status.Result   = NavigationResult.InProgress;

            // Register entity with crowd so CrowdAgentUpdateSystem drives it.
            var profile = Repo.HasComponent<NavAgentProfile>(e)
                ? Repo.GetComponent<NavAgentProfile>(e)
                : default;
            float radius = profile.AgentRadius > 0f ? profile.AgentRadius : 0.4f;
            float height = profile.AgentHeight > 0f ? profile.AgentHeight : 1.8f;
            Crowd.RegisterAgent(e, new CrowdAgentParams
            {
                Radius           = radius,
                Height           = height,
                MaxSpeed         = 5.0f,
                MaxAcceleration  = 20f,
                SeparationWeight = 2,
            });
            Crowd.SetAgentTarget(e, new Vector3(destination.X, destination.Y, 0f));
        }

        /// <summary>
        /// Issues a FetchPathDetails command. The bridge processes this on the NEXT tick:
        /// publishes NavigationPathDetailsResponseEvent, then PathDetailsUpdate ingests it
        /// into BrainRegistry (within the same tick after the bridge's SwapBuffers).
        /// Call PumpFor(1) after this to complete the ingestion.
        /// </summary>
        public unsafe void IssueFetchPathDetails(Entity e, int routeHandle)
        {
            uint instanceId = ++_actionInstanceCounter;

            ref var ch = ref Repo.GetComponentRW<LocomotionChannel>(e);
            ch.ActiveAction     = NavigationConstants.ActionIdFetchPathDetails;
            ch.ActionInstanceId = instanceId;
            var p = new FetchPathDetailsParams
            {
                RouteHandle = routeHandle,
            };
            Unsafe.WriteUnaligned(ref ch.Params[0], p);
        }

        /// <summary>
        /// Advances the simulation by one tick, running the full pipeline.
        /// Tick order:
        ///   Bridge -> SwapBuffers -> Solver -> FlushCommandBuffers -> SwapBuffers
        ///   -> Materialize -> CrowdUpdate -> NavExec -> SwapBuffers -> EventLog.Capture
        /// </summary>
        public void Tick()
        {
            // 1. Bridge: publishes PathfindingRequestEvent (and possibly NavigationPathDetailsResponseEvent) to write buffer.
            _bridge.Execute(Repo, Dt);
            // 2. Swap: bridge events become readable.
            Repo.Bus.SwapBuffers();
            // 2a. PathDetailsUpdate: process NavigationPathDetailsResponseEvent from bridge before next swap.
            _pathDetailsUpdate?.Execute(Repo, Dt);
            // 3. Solver: reads requests, publishes PathfindingResultEvent via ECB.
            _solver.Execute(Repo, Dt);
            // 4. Flush ECBs: PathfindingResultEvent moves to write buffer.
            Repo.FlushCommandBuffers();
            // 5. Swap: PathfindingResultEvent becomes readable.
            Repo.Bus.SwapBuffers();
            // 6. Materialize: reads results, updates corridor/status, publishes MoveStartedEvent.
            _materialize.Execute(Repo, Dt);
            // 6b. Off-mesh detection (must be BEFORE CrowdUpdate to suppress velocity this tick).
            _offMeshDetect.Execute(Repo, Dt);
            // 7. CrowdUpdate: integrates agent positions.
            _crowdUpdate.Execute(Repo, Dt);
            // 7a. Sync solver trajectories into PathRegistry so CorridorPreviewSystem can read
            //     waypoints.  The solver stores paths in TrajectoryPoolManager; PathRegistry.Muscle
            //     is populated here for any entity whose RouteHandle exists in the pool.
            SyncSolverTrajectoriesIntoPathRegistry();
            // 7b. Corridor preview (opt-in 8-waypoint window).
            _corridorPreview.Execute(Repo, Dt);
            // 8. NavExec: checks arrival, publishes MoveCompletedEvent on Arrived.
            _navExec.Execute(Repo, Dt);
            // 9. Swap: MoveStartedEvent / MoveCompletedEvent / NavigationPathDetailsResponseEvent become readable.
            Repo.Bus.SwapBuffers();
            // 10. Capture events into the log.
            EventLog.Capture(Repo);
            // 10a. PathDetailsUpdate: process NavigationPathDetailsResponseEvent from NavExec (AutoSendPathOnReplan).
            _pathDetailsUpdate?.Execute(Repo, Dt);
            // 11. Advance frame counter.
            ref var gt = ref Repo.GetSingletonUnmanaged<GlobalTime>();
            gt.FrameNumber++;
        }

        /// <summary>
        /// Spawns a flying entity (MobilityProfile = 4) at the given XY position.
        /// Has CrowdAgent so the crowd provider drives it to the destination after path planning.
        /// Bridge will route via FakeVolumetricPathProvider when MobilityProfile = 4.
        /// </summary>
        public Entity SpawnFlying(Vector2 pos)
        {
            var entity = Repo.CreateEntity();
            Repo.AddComponent(entity, new SimTransform { Position = new Vector3(pos.X, pos.Y, 0f) });
            Repo.AddComponent(entity, new SimVelocity());
            Repo.AddComponent(entity, new NavigationIntent());
            Repo.AddComponent(entity, new NavigationStatus());
            Repo.AddComponent(entity, new FrustrationTicks());
            Repo.AddComponent(entity, new LocomotionChannel());
            Repo.AddComponent(entity, new NavAgentProfile { AgentRadius = 0.4f, AgentHeight = 1.8f, MobilityProfile = 4 });
            Repo.AddComponent(entity, new CrowdAgent());
            return entity;
        }

        /// <summary>
        /// Spawns a naval entity at the given XY position.
        /// Has CrowdAgent. PreferredLayerMask = Naval.
        /// </summary>
        public Entity SpawnNaval(Vector2 pos)
        {
            var entity = Repo.CreateEntity();
            Repo.AddComponent(entity, new SimTransform { Position = new Vector3(pos.X, pos.Y, 0f) });
            Repo.AddComponent(entity, new SimVelocity());
            Repo.AddComponent(entity, new NavigationIntent());
            Repo.AddComponent(entity, new NavigationStatus());
            Repo.AddComponent(entity, new FrustrationTicks());
            Repo.AddComponent(entity, new LocomotionChannel());
            Repo.AddComponent(entity, new NavAgentProfile
            {
                AgentRadius          = 0.4f,
                AgentHeight          = 1.8f,
                PreferredLayerMask   = (uint)NavLayerMask.Naval,
            });
            Repo.AddComponent(entity, new CrowdAgent());
            return entity;
        }

        /// <summary>
        /// Spawns a vehicle entity at the given XY position (Z = 0).
        /// Does NOT add CrowdAgent -- VehicleState suppresses crowd registration in the bridge.
        /// </summary>
        public Entity SpawnVehicle(Vector2 pos)
        {
            var entity = Repo.CreateEntity();
            Repo.AddComponent(entity, new SimTransform { Position = new Vector3(pos.X, pos.Y, 0f) });
            Repo.AddComponent(entity, new SimVelocity());
            Repo.AddComponent(entity, new NavigationIntent());
            Repo.AddComponent(entity, new NavigationStatus());
            Repo.AddComponent(entity, new FrustrationTicks());
            Repo.AddComponent(entity, new LocomotionChannel());
            Repo.AddComponent(entity, new NavAgentProfile { AgentRadius = 1.2f, AgentHeight = 2.5f });
            Repo.AddComponent(entity, new VehicleState());
            return entity;
        }

        /// <summary>
        /// Mirrors solver-produced trajectories from TrajectoryPoolManager into
        /// PathRegistry.Muscle so that CorridorPreviewSystem.TryGetWaypointsSlice succeeds.
        /// Called once per tick, between CrowdUpdate and CorridorPreviewSystem.
        /// </summary>
        private void SyncSolverTrajectoriesIntoPathRegistry()
        {
            var muscleQuery = Repo.Query().With<NavigationCorridorMuscle>().Build();
            foreach (var entity in muscleQuery)
            {
                var muscle = Repo.GetComponent<NavigationCorridorMuscle>(entity);
                if (muscle.RouteHandle == 0) continue;
                if (!_pool.TryGetTrajectory(muscle.RouteHandle, out var traj)) continue;
                var wps = new NavWaypoint[traj.Waypoints.Length];
                for (int i = 0; i < traj.Waypoints.Length; i++)
                    wps[i] = new NavWaypoint
                    {
                        Position  = new Vector3(traj.Waypoints[i].Position.X, 0f, traj.Waypoints[i].Position.Y),
                        Traversal = TraversalKind.Walk,
                    };
                PathRegistry.Muscle.RegisterOrReplace(muscle.RouteHandle, wps, 0f, 0, 0, 0);
            }
        }

        public void PumpFor(int ticks)
        {
            for (int i = 0; i < ticks; i++)
                Tick();
        }

        /// <summary>
        /// Runs ticks until <paramref name="condition"/> returns true or
        /// <paramref name="maxTicks"/> is exhausted, in which case the test fails.
        /// </summary>
        public void PumpUntil(Func<bool> condition, int maxTicks = 600)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                Tick();
                if (condition()) return;
            }
            Assert.Fail($"PumpUntil: condition not met after {maxTicks} ticks.");
        }

        public void Dispose()
        {
            if (Repo.HasSingleton<PathfindingBatchData>())
            {
                ref var b = ref Repo.GetSingletonUnmanaged<PathfindingBatchData>();
                if (b.Results.IsCreated)
                    b.Results.Dispose();
            }
            _pool.Dispose();
        }
    }

    /// <summary>
    /// Accumulates navigation lifecycle events emitted during a harness tick.
    /// Call <see cref="Capture"/> once per tick (after the final SwapBuffers).
    /// </summary>
    public sealed class CapturedEventLog
    {
        private readonly List<MoveStartedEvent>                      _started              = new();
        private readonly List<MoveCompletedEvent>                    _completed            = new();
        private readonly List<PathReplannedEvent>                    _replanned            = new();
        private readonly List<MoveBlockedEvent>                      _blocked              = new();
        private readonly List<OffMeshTraversalStartedEvent>          _offMeshStarted       = new();
        private readonly List<NavigationPathDetailsResponseEvent>    _pathDetailsResponses = new();

        public void Capture(EntityRepository repo)
        {
            var view = (ISimulationView)repo;

            foreach (ref readonly var e in view.ReadEvents<MoveStartedEvent>())
                _started.Add(e);
            foreach (ref readonly var e in view.ReadEvents<MoveCompletedEvent>())
                _completed.Add(e);
            foreach (ref readonly var e in view.ReadEvents<PathReplannedEvent>())
                _replanned.Add(e);
            foreach (ref readonly var e in view.ReadEvents<MoveBlockedEvent>())
                _blocked.Add(e);
            foreach (ref readonly var e in view.ReadEvents<OffMeshTraversalStartedEvent>())
                _offMeshStarted.Add(e);
            foreach (ref readonly var e in view.ReadEvents<NavigationPathDetailsResponseEvent>())
                _pathDetailsResponses.Add(e);
        }

        public bool HasMoveCompleted(Entity entity)
            => _completed.Exists(e => e.Target == entity);

        public MoveCompletedEvent GetMoveCompleted(Entity entity)
        {
            int idx = _completed.FindIndex(e => e.Target == entity);
            if (idx < 0)
                throw new InvalidOperationException($"No MoveCompletedEvent for entity {entity}.");
            return _completed[idx];
        }

        public void Clear()
        {
            _started.Clear();
            _completed.Clear();
            _replanned.Clear();
            _blocked.Clear();
            _offMeshStarted.Clear();
            _pathDetailsResponses.Clear();
        }

        public IReadOnlyList<MoveStartedEvent>                       MoveStarted          => _started;
        public IReadOnlyList<MoveCompletedEvent>                     MoveCompleted        => _completed;
        public IReadOnlyList<PathReplannedEvent>                     PathReplanned        => _replanned;
        public IReadOnlyList<MoveBlockedEvent>                       MoveBlocked          => _blocked;
        public IReadOnlyList<OffMeshTraversalStartedEvent>           OffMeshStarted       => _offMeshStarted;
        public IReadOnlyList<NavigationPathDetailsResponseEvent>     PathDetailsResponses => _pathDetailsResponses;

        public bool HasOffMeshTraversalStarted()
            => _offMeshStarted.Count > 0;

        public OffMeshTraversalStartedEvent GetFirstOffMeshTraversalStarted()
            => _offMeshStarted.Count > 0
                ? _offMeshStarted[0]
                : throw new InvalidOperationException("No OffMeshTraversalStartedEvent captured.");
    }
}
