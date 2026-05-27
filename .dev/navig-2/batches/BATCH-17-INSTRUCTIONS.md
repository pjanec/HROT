# BATCH-17 Implementation Instructions

## Objective
Implement NAV-P10 integration tests T10 (S9), T11 (S10), T12 (S11), T13 (S12).  
**Target**: ≥ 280 passing tests (274 existing + at least 5 new tests).

## Tasks

| Task ID | Description |
|---------|-------------|
| NAV-P10-T10 | `S9_FlyingAgentRouting` |
| NAV-P10-T11 | `S10_NavalLayerRouting` |
| NAV-P10-T12 | `S11_PlanRouteThenFollowPath` |
| NAV-P10-T13 | `S12_FetchPathDetailsAndCacheInvalidation` |

---

## Files to Modify

### 1. `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs`

Add `MobilityProfile` field to `NavAgentProfile`. Find the struct definition (line ~340) and add one field after `MaxSlopeDeg`:

**Find:**
```csharp
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(NavigationContractsComponentIds.NavAgentProfile)]
    public struct NavAgentProfile
    {
        /// <summary>Bitfield of navmesh layers this agent can traverse. 0xFFFFFFFF = all layers.</summary>
        public uint PreferredLayerMask;

        /// <summary>Physical radius of the agent capsule (metres). Used for corridor clearance checks.</summary>
        public float AgentRadius;

        /// <summary>Physical height of the agent capsule (metres).</summary>
        public float AgentHeight;

        /// <summary>Maximum traversable slope angle in degrees.</summary>
        public float MaxSlopeDeg;
    }
```

**Replace with:**
```csharp
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(NavigationContractsComponentIds.NavAgentProfile)]
    public struct NavAgentProfile
    {
        /// <summary>Bitfield of navmesh layers this agent can traverse. 0xFFFFFFFF = all layers.</summary>
        public uint PreferredLayerMask;

        /// <summary>Physical radius of the agent capsule (metres). Used for corridor clearance checks.</summary>
        public float AgentRadius;

        /// <summary>Physical height of the agent capsule (metres).</summary>
        public float AgentHeight;

        /// <summary>Maximum traversable slope angle in degrees.</summary>
        public float MaxSlopeDeg;

        /// <summary>
        /// Locomotion profile: 0 = Wheeled/Ground (default), 4 = Flying (routes via volumetric provider).
        /// </summary>
        public byte MobilityProfile;
    }
```

---

### 2. `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/NavigationIntentBridgeSystem.cs`

#### 2a. Fix `MobilityProfile` for `ActionIdMoveTo` and `ActionIdPlanRoute`

Both handlers currently hardcode `MobilityProfile = 0`. Change them to read from `NavAgentProfile.MobilityProfile`.

**In the `ActionIdMoveTo` handler, find:**
```csharp
                        repo.Bus.Publish(new PathfindingRequestEvent
                        {
                            RequestId       = reqId,
                            Start           = from,
                            End             = new Vector3(p.Destination.X, p.Destination.Y, 0f),
                            MobilityProfile = 0,
                            BackendForce    = (NavigationBackend)p.BackendForce,
                            RouteHandle     = p.RouteHandle,
                            NavLayerMask    = (int)p.LayerMask,
                        });
```

**Replace with:**
```csharp
                        var agentProfile = repo.HasComponent<NavAgentProfile>(entity)
                            ? repo.GetComponent<NavAgentProfile>(entity)
                            : default;

                        repo.Bus.Publish(new PathfindingRequestEvent
                        {
                            RequestId       = reqId,
                            Start           = from,
                            End             = new Vector3(p.Destination.X, p.Destination.Y, 0f),
                            MobilityProfile = agentProfile.MobilityProfile,
                            BackendForce    = (NavigationBackend)p.BackendForce,
                            RouteHandle     = p.RouteHandle,
                            NavLayerMask    = (int)p.LayerMask,
                        });
```

**In the `ActionIdPlanRoute` handler, find:**
```csharp
                        repo.Bus.Publish(new PathfindingRequestEvent
                        {
                            RequestId       = reqId,
                            Start           = from,
                            End             = new Vector3(p.Destination.X, p.Destination.Y, 0f),
                            MobilityProfile = 0,
                            BackendForce    = (NavigationBackend)p.BackendForce,
                            RouteHandle     = routeHandle,
                            NavLayerMask    = (int)p.LayerMask,
                            MaxCost         = p.MaxCost,
                        });
```

**Replace with:**
```csharp
                        var agentProfile = repo.HasComponent<NavAgentProfile>(entity)
                            ? repo.GetComponent<NavAgentProfile>(entity)
                            : default;

                        repo.Bus.Publish(new PathfindingRequestEvent
                        {
                            RequestId       = reqId,
                            Start           = from,
                            End             = new Vector3(p.Destination.X, p.Destination.Y, 0f),
                            MobilityProfile = agentProfile.MobilityProfile,
                            BackendForce    = (NavigationBackend)p.BackendForce,
                            RouteHandle     = routeHandle,
                            NavLayerMask    = (int)p.LayerMask,
                            MaxCost         = p.MaxCost,
                        });
```

#### 2b. Implement `ActionIdFetchPathDetails`

**Find:**
```csharp
                    case NavigationConstants.ActionIdFetchPathDetails:
                        // Stub: detailed waypoint ingress is owned by a future phase.
                        break;
```

**Replace with:**
```csharp
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
```

---

### 3. `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/NavigationExecutionSystem.cs`

Fix the `NavigationPathDetailsResponseEvent` published during AutoSendPathOnReplan to include `Target` and `ReplanCount`. Currently they are missing (zero-initialized), which causes `NavigationPathDetailsUpdateSystem` to skip the event (`!repo.IsAlive(entity)` check fails for zero entity).

**Find:**
```csharp
                            if ((intent.Flags & (1 << NavigationConstants.FlagBitAutoSendPathOnReplan)) != 0)
                            {
                                repo.Bus.Publish(new NavigationPathDetailsResponseEvent
                                {
                                    RouteHandle   = intent.RouteHandle,
                                    IsAutoRefresh = 1,
                                });
                            }
```

**Replace with:**
```csharp
                            if ((intent.Flags & (1 << NavigationConstants.FlagBitAutoSendPathOnReplan)) != 0)
                            {
                                repo.Bus.Publish(new NavigationPathDetailsResponseEvent
                                {
                                    Target        = entity,
                                    RouteHandle   = intent.RouteHandle,
                                    ReplanCount   = (byte)status.ReplanCount,
                                    IsAutoRefresh = 1,
                                });
                            }
```

---

### 4. `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavTestHarness.cs`

#### 4a. Add `Volumetric`, `BrainRegistry` public properties and `_pathDetailsUpdate` field

**Find** the private field declarations block:
```csharp
        private readonly NavigationIntentBridgeSystem              _bridge;
        private readonly PathfindingSolverSystem                   _solver;
        private readonly PathfindingResultMaterializationSystem    _materialize;
        private readonly CrowdAgentUpdateSystem                    _crowdUpdate;
        private readonly NavigationExecutionSystem                 _navExec;
        private readonly TrajectoryPoolManager                     _pool;
        private readonly OffMeshLinkDetectionSystem                _offMeshDetect;
        private readonly CorridorPreviewSystem                     _corridorPreview;
```

**Replace with:**
```csharp
        private readonly NavigationIntentBridgeSystem              _bridge;
        private readonly PathfindingSolverSystem                   _solver;
        private readonly PathfindingResultMaterializationSystem    _materialize;
        private readonly CrowdAgentUpdateSystem                    _crowdUpdate;
        private readonly NavigationExecutionSystem                 _navExec;
        private readonly TrajectoryPoolManager                     _pool;
        private readonly OffMeshLinkDetectionSystem                _offMeshDetect;
        private readonly CorridorPreviewSystem                     _corridorPreview;
        private readonly NavigationPathDetailsUpdateSystem?        _pathDetailsUpdate;
```

**Find** the existing public properties block:
```csharp
        public EntityRepository          Repo         { get; }
        public CapturedEventLog          EventLog     { get; }
        public FakeNavmeshProvider       Navmesh      { get; }
        public FakeDtCrowdProvider       Crowd        { get; }
        public SharedPathRegistry        PathRegistry { get; }

        public IFakeNavmeshProviderTestApi NavmeshApi => (IFakeNavmeshProviderTestApi)Navmesh;
```

**Replace with:**
```csharp
        public EntityRepository          Repo         { get; }
        public CapturedEventLog          EventLog     { get; }
        public FakeNavmeshProvider       Navmesh      { get; }
        public FakeDtCrowdProvider       Crowd        { get; }
        public SharedPathRegistry        PathRegistry { get; }
        public FakeVolumetricPathProvider Volumetric  { get; }
        public BrainPathRegistry         BrainRegistry { get; }

        public IFakeNavmeshProviderTestApi NavmeshApi => (IFakeNavmeshProviderTestApi)Navmesh;
```

#### 4b. Update the constructor to initialize new fields

**Find** the end of the constructor where the existing properties are assigned (look for the block starting with `Repo = world;`):
```csharp
            Repo         = world;
            EventLog     = new CapturedEventLog();
            Navmesh      = module.Navmesh;
            Crowd        = module.Crowd;
            PathRegistry = module.PathRegistry;
        }
```

**Replace with:**
```csharp
            Repo         = world;
            EventLog     = new CapturedEventLog();
            Navmesh      = module.Navmesh;
            Crowd        = module.Crowd;
            PathRegistry = module.PathRegistry;
            Volumetric   = module.Volumetric;
            BrainRegistry = new BrainPathRegistry();
            _pathDetailsUpdate = new NavigationPathDetailsUpdateSystem(PathRegistry.Muscle, BrainRegistry);
        }
```

#### 4c. Update `Tick()` to run `_pathDetailsUpdate` at two points

The `_pathDetailsUpdate.Execute` must run:
1. **Right after the first `SwapBuffers`** (step 2) — so it can process `NavigationPathDetailsResponseEvent` published by the bridge (which gets lost at step 5's SwapBuffers).
2. **After the final `SwapBuffers`** (step 9) — so it can process `NavigationPathDetailsResponseEvent` published by `NavExec` during replan.

**Find** in `Tick()`:
```csharp
            // 1. Bridge: publishes PathfindingRequestEvent to write buffer.
            _bridge.Execute(Repo, Dt);
            // 2. Swap: PathfindingRequestEvent becomes readable.
            Repo.Bus.SwapBuffers();
            // 3. Solver: reads requests, publishes PathfindingResultEvent via ECB.
            _solver.Execute(Repo, Dt);
```

**Replace with:**
```csharp
            // 1. Bridge: publishes PathfindingRequestEvent (and possibly NavigationPathDetailsResponseEvent) to write buffer.
            _bridge.Execute(Repo, Dt);
            // 2. Swap: bridge events become readable.
            Repo.Bus.SwapBuffers();
            // 2a. PathDetailsUpdate: process NavigationPathDetailsResponseEvent from bridge before next swap.
            _pathDetailsUpdate?.Execute(Repo, Dt);
            // 3. Solver: reads requests, publishes PathfindingResultEvent via ECB.
            _solver.Execute(Repo, Dt);
```

**Find** at the end of `Tick()`:
```csharp
            // 9. Swap: MoveStartedEvent / MoveCompletedEvent become readable.
            Repo.Bus.SwapBuffers();
            // 10. Capture events into the log.
            EventLog.Capture(Repo);
            // 11. Advance frame counter.
            ref var gt = ref Repo.GetSingletonUnmanaged<GlobalTime>();
            gt.FrameNumber++;
```

**Replace with:**
```csharp
            // 9. Swap: MoveStartedEvent / MoveCompletedEvent / NavigationPathDetailsResponseEvent become readable.
            Repo.Bus.SwapBuffers();
            // 10. Capture events into the log.
            EventLog.Capture(Repo);
            // 10a. PathDetailsUpdate: process NavigationPathDetailsResponseEvent from NavExec (AutoSendPathOnReplan).
            _pathDetailsUpdate?.Execute(Repo, Dt);
            // 11. Advance frame counter.
            ref var gt = ref Repo.GetSingletonUnmanaged<GlobalTime>();
            gt.FrameNumber++;
```

#### 4d. Add `SpawnFlying` method

**Find** the `SpawnVehicle` method and add `SpawnFlying` right after it (or after `SpawnInfantry`):

```csharp
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
```

#### 4e. Add `IssuePlanRoute`, `IssueFollowPath`, `IssueFetchPathDetails` methods

Add these methods in the harness class (e.g. after `IssueMoveTo`):

```csharp
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

            // NavigationMode.None: NavExec skips → entity stays still during planning.
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
```

---

## Files to Create

### 5. `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S9_FlyingAgentRoutingTests.cs`

```csharp
using System;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// NAV-P10-T10 (S9). Flying agent with MobilityProfile=4 routes via FakeVolumetricPathProvider.
    /// Proves: bridge passes MobilityProfile=4 from NavAgentProfile, solver invokes volumetric provider.
    /// </summary>
    public sealed class S9_FlyingAgentRoutingTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S9_FlyingAgentRoutingTests()
        {
            // LoadCorridor map: Infantry navmesh, but FakeVolumetricPathProvider ignores navmesh.
            // Default altitude bounds: minAltitude=0, maxAltitude=0 → any Y=0 position is flyable.
            _h = new NavTestHarness(NavTestMaps.LoadCorridor());
        }

        public void Dispose() => _h.Dispose();

        [Fact]
        public void S9_FlyingEntity_RoutesViaVolumetricProvider_AndArrives()
        {
            var e = _h.SpawnFlying(new Vector2(3f, 0f));
            _h.IssueMoveTo(e, new Vector2(28f, 0f));

            // Pump enough for: bridge → solver (volumetric PlanPath) → materialize → crowd → arrival.
            _h.PumpUntil(
                () => _h.EventLog.MoveCompleted.Any(c => c.Target == e),
                maxTicks: 600);

            // The volumetric provider must have been called.
            var stats = ((IFakeVolumetricPathProviderTestApi)_h.Volumetric).GetStats();
            Assert.True(stats.PlanPathCalls > 0,
                $"FakeVolumetricPathProvider.PlanPath should have been called at least once; actual={stats.PlanPathCalls}");

            Assert.Equal(NavigationResult.Arrived,
                _h.EventLog.MoveCompleted.First(c => c.Target == e).Reason);
        }

        [Fact]
        public void S9_GroundEntity_DoesNotInvokeVolumetricProvider()
        {
            // Control: infantry entity (MobilityProfile=0) must NOT invoke volumetric provider.
            var e = _h.SpawnInfantry(new Vector2(3f, 0f));
            _h.IssueMoveTo(e, new Vector2(28f, 0f));

            _h.PumpUntil(
                () => _h.EventLog.MoveCompleted.Any(c => c.Target == e),
                maxTicks: 600);

            var stats = ((IFakeVolumetricPathProviderTestApi)_h.Volumetric).GetStats();
            Assert.Equal(0, stats.PlanPathCalls);

            Assert.Equal(NavigationResult.Arrived,
                _h.EventLog.MoveCompleted.First(c => c.Target == e).Reason);
        }
    }
}
```

---

### 6. `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S10_NavalLayerRoutingTests.cs`

```csharp
using System;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// NAV-P10-T11 (S10). Naval-layer routing: naval entity on water polygons arrives at destination.
    /// Proves: NavLayerMask.Naval routing through FakeNavmeshProvider works end-to-end.
    /// </summary>
    public sealed class S10_NavalLayerRoutingTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S10_NavalLayerRoutingTests()
        {
            _h = new NavTestHarness(NavTestMaps.LoadNaval());
        }

        public void Dispose() => _h.Dispose();

        [Fact]
        public void S10_NavalEntity_RoutesOnWaterLayer_AndArrives()
        {
            // LoadNaval: 3 polygons centred at (5,5), (15,5), (25,5) in XZ plane.
            // Harness positions: Vector2(x,y) → Vector3(x,y,0). PointInPolygon uses X,Z.
            // Vector2(5,5) → Vector3(5,5,0): PointInPolygon(X=5, Z=0) on polygon 0 (X=0..10, Z=0..10) ✓
            var e = _h.SpawnNaval(new Vector2(5f, 5f));
            _h.IssueMoveTo(e, new Vector2(28f, 5f), layerMask: (uint)NavLayerMask.Naval);

            _h.PumpUntil(
                () => _h.EventLog.MoveCompleted.Any(c => c.Target == e),
                maxTicks: 600);

            Assert.Equal(NavigationResult.Arrived,
                _h.EventLog.MoveCompleted.First(c => c.Target == e).Reason);
        }

        [Fact]
        public void S10_InfantryOnNavalMap_FailsUnreachable()
        {
            // Infantry layer does not exist on LoadNaval → FailedUnreachable.
            var e = _h.SpawnInfantry(new Vector2(5f, 5f));
            _h.IssueMoveTo(e, new Vector2(28f, 5f), layerMask: (uint)NavLayerMask.Infantry);

            _h.PumpFor(15);

            var status = _h.Repo.GetComponent<NavigationStatus>(e);
            Assert.Equal(NavigationResult.FailedUnreachable, status.Result);
        }
    }
}
```

---

### 7. `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S11_PlanRouteThenFollowPathTests.cs`

```csharp
using System;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// NAV-P10-T12 (S11). PlanRoute then FollowPath:
    /// 1. IssuePlanRoute → entity stays still; NavigationStatus.Result == PathFound.
    /// 2. IssueFollowPath with the pre-planned route → entity arrives at destination.
    /// Proves: two-phase navigation (plan, then follow) works correctly.
    /// </summary>
    public sealed class S11_PlanRouteThenFollowPathTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S11_PlanRouteThenFollowPathTests()
        {
            _h = new NavTestHarness(NavTestMaps.LoadCorridor());
        }

        public void Dispose() => _h.Dispose();

        [Fact]
        public void S11_PlanRoute_ThenFollowPath_Arrives()
        {
            const int routeHandle = 7;
            var start = new Vector2(3f, 0f);
            var dest  = new Vector2(28f, 0f);

            var e = _h.SpawnInfantry(start);
            _h.IssuePlanRoute(e, dest, routeHandle: routeHandle);

            // Pump until PathFound (solver responds + materialise writes PathFound).
            _h.PumpUntil(
                () => _h.Repo.GetComponent<NavigationStatus>(e).Result == NavigationResult.PathFound,
                maxTicks: 30);

            // Entity must NOT have moved.
            var tf = _h.Repo.GetComponent<SimTransform>(e);
            float distMoved = Vector2.Distance(
                new Vector2(tf.Position.X, tf.Position.Y), start);
            Assert.True(distMoved < 0.5f,
                $"Entity should not move during PlanRoute; moved {distMoved:F2} m.");

            // Now follow the pre-planned path.
            _h.IssueFollowPath(e, routeHandle, dest);

            _h.PumpUntil(
                () => _h.EventLog.MoveCompleted.Any(c => c.Target == e),
                maxTicks: 600);

            Assert.Equal(NavigationResult.Arrived,
                _h.EventLog.MoveCompleted.First(c => c.Target == e).Reason);
        }
    }
}
```

---

### 8. `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S12_FetchPathDetailsAndCacheInvalidationTests.cs`

```csharp
using System;
using System.Linq;
using System.Numerics;
using CarKinem.Systems;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// NAV-P10-T13 (S12). FetchPathDetails populates BrainPathRegistry; cache entry is
    /// invalidated (stale miss) when ReplanCount advances; re-fetching refreshes the cache.
    /// </summary>
    public sealed class S12_FetchPathDetailsAndCacheInvalidationTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S12_FetchPathDetailsAndCacheInvalidationTests()
        {
            _h = new NavTestHarness(NavTestMaps.LoadReplan());
        }

        public void Dispose() => _h.Dispose();

        [Fact]
        public void S12_FetchPathDetails_PopulatesBrainRegistry()
        {
            const int routeHandle = 1;
            _h.NavmeshApi.UnblockPolygon(1);

            var e = _h.SpawnInfantry(new Vector2(3f, 0f));
            _h.IssueMoveTo(e, new Vector2(28f, 0f),
                flags: (byte)(1 << NavigationConstants.FlagBitAllowReplan),
                routeHandle: routeHandle);

            // Wait for path to be found and entity to be following.
            _h.PumpFor(15);

            // Issue FetchPathDetails — bridge processes it next tick and publishes
            // NavigationPathDetailsResponseEvent, then PathDetailsUpdate ingests it.
            _h.IssueFetchPathDetails(e, routeHandle);
            _h.PumpFor(2); // bridge tick + PathDetailsUpdate tick

            // BrainRegistry should now have a fresh entry for replanCount=0.
            var waypointBuf = new NavWaypoint[256];
            var status = _h.Repo.GetComponent<NavigationStatus>(e);
            bool hit = _h.BrainRegistry.TryGetWaypoints(
                e, routeHandle, (byte)status.ReplanCount, waypointBuf.AsSpan(), out int count);

            Assert.True(hit, "BrainRegistry must have a cache entry after FetchPathDetails.");
            Assert.True(count > 0, "Cached path must contain at least one waypoint.");
        }

        [Fact]
        public void S12_CacheInvalidatedOnReplan_ThenRefreshedOnNextFetch()
        {
            const int routeHandle = 2;
            _h.NavmeshApi.UnblockPolygon(1);

            var e = _h.SpawnInfantry(new Vector2(3f, 0f));
            byte flags = (byte)(
                (1 << NavigationConstants.FlagBitAllowReplan) |
                (1 << NavigationConstants.FlagBitAutoSendPathOnReplan));
            _h.IssueMoveTo(e, new Vector2(28f, 0f), flags: flags, routeHandle: routeHandle);

            // Let path be found.
            _h.PumpFor(15);

            // Fetch initial path details.
            _h.IssueFetchPathDetails(e, routeHandle);
            _h.PumpFor(2);

            var statusBefore = _h.Repo.GetComponent<NavigationStatus>(e);
            var waypointBuf = new NavWaypoint[256];
            bool firstHit = _h.BrainRegistry.TryGetWaypoints(
                e, routeHandle, (byte)statusBefore.ReplanCount, waypointBuf.AsSpan(), out _);
            Assert.True(firstHit, "Initial FetchPathDetails should populate BrainRegistry.");

            // Force frustration → replan fires (AutoSendPathOnReplan populates BrainRegistry again).
            _h.NavmeshApi.BlockPolygon(1);
            ((IFakeDtCrowdProviderTestApi)_h.Crowd).OverrideAgentVelocity(e, Vector3.Zero);
            _h.PumpFor(NavigationExecutionSystem.FrustrationTickLimit + 5);

            // PathReplannedEvent should have fired.
            Assert.True(_h.EventLog.PathReplanned.Count > 0, "PathReplannedEvent must fire.");

            var statusAfter = _h.Repo.GetComponent<NavigationStatus>(e);
            Assert.True(statusAfter.ReplanCount > 0, "ReplanCount must be > 0 after replan.");

            // Old cache (replanCount=0) is now stale.
            bool staleMiss = _h.BrainRegistry.TryGetWaypoints(
                e, routeHandle, (byte)statusBefore.ReplanCount, waypointBuf.AsSpan(), out _);
            Assert.False(staleMiss,
                "Old cache entry (stale replanCount) should return false.");

            // BrainRegistry stats should have at least one stale miss.
            var stats = ((IFakeBrainPathRegistryTestApi)_h.BrainRegistry).GetStats();
            Assert.True(stats.StaleMisses > 0, "StaleMisses counter must be > 0.");

            // With AutoSendPathOnReplan, BrainRegistry was auto-refreshed during replan.
            // New cache entry (replanCount=current) should be a hit.
            bool newHit = _h.BrainRegistry.TryGetWaypoints(
                e, routeHandle, (byte)statusAfter.ReplanCount, waypointBuf.AsSpan(), out int newCount);
            Assert.True(newHit, "Auto-refreshed cache entry must be a hit for current replanCount.");
            Assert.True(newCount > 0, "Refreshed cache must contain waypoints.");
        }
    }
}
```

---

## Important Notes

### `using` imports needed in NavTestHarness.cs

The harness already imports many namespaces. Verify that these are present (add if missing):
- `using Fdp.Toolkit.Navigation.Fake;` — for `BrainPathRegistry`, `NavigationPathDetailsUpdateSystem`
- `using Fdp.Toolkit.Navigation.Systems;` — for `NavigationPathDetailsUpdateSystem`

Actually `NavigationPathDetailsUpdateSystem` is in `Fdp.Toolkit.Navigation.Systems` namespace. Check the existing using statements and add if missing.

### `NavigationPathDetailsUpdateSystem` constructor signature

The constructor takes `(IPathRegistry muscleRegistry, BrainPathRegistry brainRegistry)`. `PathRegistry.Muscle` is `IPathRegistry` (the muscle-side registry). In the harness:
```csharp
_pathDetailsUpdate = new NavigationPathDetailsUpdateSystem(PathRegistry.Muscle, BrainRegistry);
```

### S10 infantry control test — positioning

`LoadNaval()` has Naval-layer-only polygons. No Infantry layer. An infantry entity trying to navigate on the Naval map will get `FailedUnreachable`. Start position Vector2(5, 5) → Vector3(5, 5, 0). `FakeNavmeshProvider` looks for Infantry layer → not found → returns FailedUnreachable immediately.

### S12 — AutoSendPathOnReplan BrainRegistry population

With the NavExec fix (step 3 above), `NavigationPathDetailsResponseEvent` now includes `Target = entity` and `ReplanCount = (byte)status.ReplanCount`. When PathDetailsUpdate runs at step 10a (after the final SwapBuffers), it reads this event and calls `BrainRegistry.TryIngestResponse(entity, routeHandle, waypoints, replanCount=1, ...)`.

The test then calls `BrainRegistry.TryGetWaypoints(e, routeHandle, replanCount=1, ...)` and expects a hit.

### S11 — `NavigationStatus.Result == PathFound` timing

After `IssuePlanRoute`:
- Tick N: Bridge processes ActionIdPlanRoute → publishes PathfindingRequestEvent
- Tick N+1: Solver processes request → publishes PathfindingResultEvent (via ECB)
- Tick N+2: Materialize processes result → writes `NavigationStatus.Result = PathFound`

So `PumpFor(3)` is minimum; `PumpUntil(PathFound, maxTicks=30)` is safer.

### Build required namespaces

The test files need these using statements:
```csharp
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Fdp.Toolkit.Navigation.Systems; // if needed
using CarKinem.Systems; // for NavigationExecutionSystem.FrustrationTickLimit
using Fdp.Core; // for NavWaypoint, Entity
```

---

## Build & Test Verification

After implementing:
```
cd D:\Work\IOS-IG-SimHost-FDP-2\FDP
dotnet build FDP.sln -v quiet
dotnet test Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~Navigation" --no-build
```

Expected: ≥ 280 tests passing, 0 errors.

## Reporting

Create `d:\Work\IOS-IG-SimHost-FDP-2\.dev\navig-2\batches\BATCH-17-REPORT.md` with:
- Summary of changes
- Final test count
- Deviations from instructions and why
