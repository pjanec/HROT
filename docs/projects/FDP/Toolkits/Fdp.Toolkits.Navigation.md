# Fdp.Toolkit.Navigation -- Navigation Subsystem v2

**Source folder**: `FDP/Toolkits/Fdp.Toolkits/Navigation/`
**Primary namespace**: `Fdp.Toolkit.Navigation`
**Sub-namespaces**: `Fdp.Toolkit.Navigation.Fake`, `Fdp.Toolkit.Navigation.EngineBacked`,
`Fdp.Toolkit.Navigation.Systems`, `Fdp.Toolkit.Navigation.Executors`,
`Fdp.Toolkit.Navigation.Modules`, `Fdp.Toolkit.Navigation.BTreeNodes`
**Assembly**: `Fdp.Toolkits` (no new production .csproj created -- see NAV-P0-T1)
**Test folder**: `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/`
**Design references**:
- `.dev/navig-2/Navigation_Design_v2_0.md` -- main architecture contract
- `.dev/navig-2/DD-Fake-Nav.md` -- deterministic fake backends
- `.dev/navig-2/DD-EngineBacked-Nav.md` -- engine-backed module (real road network)
- `.dev/navig-2/DD-Tests-Nav.md` -- three-layer test strategy
**Date**: 2026-05-30

---

## Overview

Navigation v2 is the subsystem that translates cognitive movement intentions (a Brain BTree
node issuing `Action_MoveTo`) into physical entity motion (`SimTransform` advancing through
the world over time). It spans:

- **Path planning** -- multi-modal route computation: navmesh A*, road-graph Dijkstra, 3D
  volumetric pather (flying agents).
- **Corridor following** -- tracking progress along the planned segment sequence, frustration
  detection, and Muscle-internal replanning.
- **Local avoidance** -- dtCrowd integration for infantry agents.
- **Off-mesh traversal** -- detecting jump/climb/door/fly links and triggering the animation
  montage seam.
- **Brain-side execution** -- a set of action executors (`MoveToExecutor`, `PlanRouteExecutor`,
  etc.) and a path-data registry for BTree nodes that want the full waypoint list.

The entire production code lives in the existing `Fdp.Toolkits` assembly. No new .csproj files
were created; the `Fdp.Toolkit.Navigation` namespace occupies the `Navigation/` top-level
folder alongside the other eighteen toolkit domains.

**Agent kinds.** Infantry, Wheeled vehicles, Tracked vehicles, Naval surface craft, and
Flying agents. All five are supported at fake-backend fidelity; real DotRecast/dtCrowd
backends are deferred.

**Deferred (separate follow-up docs):**
- DotRecast/real navmesh integration
- dtCrowd real-backend integration
- Formations and squad cohesion
- Flow fields for large groups
- Threat-aware path cost
- Navmesh-patch propagation runtime
- Root-motion authority flip (Animation DD-1 future work)
- Submarine depth control / flying local avoidance

---

## Architecture

### Deployment Topologies

The navigation subsystem supports three deployment topologies. All share identical API
contracts; only the transport used for each wire changes.

| Mode | Brain | Muscle + NavigationSolver | Use |
|---|---|---|---|
| **Default (collocated)** | own process | Muscle hosts `NavigationSolverModule` | Most scenarios. Common case. |
| **Scale-out** | own process | Muscle on one node; `NavigationSolverModule` on a separate node | Heavy solver workload only. |
| **All-in-one** | one process | Same process (Brain + Muscle + Solver) | Editor, headless tests, integration tests. |

DDS transport is used only when Brain and Muscle are in separate processes. In all-in-one mode
every navigation message flows on the local `FdpEventBus` -- no DDS traffic.

| Wire | Default | Scale-out | All-in-one |
|---|---|---|---|
| Brain <-> Muscle (`NavigationIntent` / `NavigationStatus`) | DDS | DDS | local bus |
| Muscle <-> Solver (`PathfindingRequestEvent` / result) | local bus | DDS | local bus |
| Muscle -> Brain (path-details response event) | DDS | DDS | local bus |

### End-to-End Pipeline

The strict separation of concerns: **Brain issues high-level intent and observes a verdict;
Muscle handles every detail of path planning, following, replanning, and animation.**

```
Brain                                  Muscle (collocated NavigationSolverModule)
-----                                  -----------------------------------------------
BTreeNode Action_MoveTo(dest, params)
  MoveToExecutor.OnEnter()
    writes NavigationIntent {
      Mode = DirectPoint,
      FinalDestination = dest,
      RouteHandle = 0,
      IntentId++
    }
    [DDS in default; local bus in all-in-one]
                                       NavigationIntentBridgeSystem
                                         publishes PathfindingRequestEvent
                                         on the local FdpEventBus

                                       PathfindingSolverSystem (10 Hz, background)
                                         multi-modal backend selection
                                         writes waypoints to TrajectoryPoolManager
                                         publishes PathfindingResultEvent

                                       PathfindingResultMaterializationSystem (main thread)
                                         materializes NavigationCorridorMuscle
                                         publishes MoveStartedEvent

                                       CrowdAgentUpdateSystem / NavigationIntentBridgeSystem
                                         drives SimVelocity -> SimTransform

                                       NavigationExecutionSystem
                                         watches frustration, tracks ProgressS,
                                         updates NavigationStatus

                                       OffMeshLinkDetectionSystem
                                         detects jump/climb/door/fly links
                                         triggers animation montage emit

                                       On arrival or failure:
                                         writes NavigationStatus.Result
    [DDS or local bus per topology]
MoveToExecutor.Execute()
  observes NavigationStatus.Result:
    Arrived          -> BTree Success
    FailedBlocked    -> BTree Failure
    FailedUnreachable-> BTree Failure
```

**Key invariant:** Brain never publishes a path request. It writes `NavigationIntent` and
observes `NavigationStatus`. The solver is Muscle's internal tool.

### CQRS Component Roles

| Category | Components | Replication |
|---|---|---|
| Brain-owned command | `NavigationIntent` | Brain -> Muscle (DDS or local) |
| Muscle verdict | `NavigationStatus` | Muscle -> Brain (DDS or local) |
| Opt-in corridor window | `NavigationCorridorPreview` | Muscle -> Brain when `StreamCorridorPreview` flag set |
| Muscle working state | `NavigationCorridorMuscle` | Muscle-internal, not replicated |
| Brain path cache | `NavigationPathDetailsBuffer` | Brain-internal, populated from `NavigationPathDetailsResponseEvent` |

---

## CQRS Components

### `NavigationIntent` (Brain -> Muscle)

~52 B CQRS command component written by executors on the Brain tier. Consumed by
`NavigationIntentBridgeSystem` on the Muscle.

```csharp
[ComponentId(NavigationContractsComponentIds.NavigationIntent)]
struct NavigationIntent
{
    uint          IntentId;          // incremented by executor on each new command
    Vector3       FinalDestination;  // Cartesian metres, Sim Z-up
    float         TargetSpeed;       // m/s
    float         ArrivalRadius;     // metres
    int           RouteHandle;       // 0=fire-and-forget; >0=Brain-assigned handle
    NavigationMode Mode;             // None|DirectPoint|FollowRoute|JoinFormation|RoadGraph
    byte          Flags;             // AllowReplan(bit 0), StreamCorridorPreview(bit 3),
                                     // AutoSendPathOnReplan(bit 4)
    byte          MaxReplans;        // 0 = use NavigationConstants.DefaultMaxReplans
    byte          ReverseAllowed;    // 1 = entity may reverse to reach destination
    ...
}
```

Zero-initialised struct is always idle (`Mode = None`).

### `NavigationStatus` (Muscle -> Brain)

~16 B CQRS status component written by the Muscle tier. Brain BTree nodes poll it.

```csharp
[ComponentId(NavigationContractsComponentIds.NavigationStatus)]
struct NavigationStatus
{
    NavigationResult  Result;       // InProgress|Arrived|FailedBlocked|FailedUnreachable|
                                    // FailedNoLayer|FailedInvalidHandle|PathFound|NoPath
    NavigationPhase   Phase;        // Idle|AwaitingPath|Following|AwaitingTraversal|Completed
    NavigationFailureReason LastFailureReason;
    byte              ReplanCount;  // increments on each Muscle-side silent replan
    int               RouteHandle;  // echoes the intent handle when relevant
    float             EstimatedTimeRemaining; // seconds; 0 when Phase != Following
}
```

### `NavigationCorridorPreview` (Muscle -> Brain, opt-in)

Present only when `StreamCorridorPreview` flag is set in the originating intent. Carries
an 8-waypoint lookahead window. Absent component = zero replication cost.

```csharp
struct NavigationCorridorPreview
{
    // [InlineArray<PreviewWaypoint, 8>] Waypoints
    byte   WaypointCount;        // 0..8; <8 only on final window
    ushort GlobalSegmentStart;   // index in full path of Waypoints[0]
    ushort PreviewVersion;       // bumps on window slide or replan
}
```

### `NavigationCorridorMuscle` (Muscle-internal)

Muscle's working state for the active navigation command. Not replicated. Holds the
`TrajectoryPoolManager` handle that owns the raw waypoint data.

```csharp
struct NavigationCorridorMuscle
{
    int    LocalRouteHandle;       // into TrajectoryPoolManager
    uint   NavmeshVersionAtPlan;
    ushort CurrentSegmentIndex;
    ushort TotalSegmentCount;
    float  TotalDistanceMeters;
    float  ProgressS;              // arc-length progress
    byte   MobilityProfile;
    byte   PrimaryBackend;         // 0=Navmesh, 1=RoadGraph, 2=Spliced, 3=Volumetric
    byte   Flags;                  // StreamCorridorPreview(0), AutoSendPath(1),
                                   // BrainExpressedInterest(2)
}
```

### `NavigationPathDetailsBuffer` (Brain-internal)

Populated by `NavigationPathDetailsUpdateSystem` from a `NavigationPathDetailsResponseEvent`.
Not replicated; lives only on the Brain entity. Backing store for `BrainPathRegistry`.

```csharp
struct NavigationPathDetailsBuffer
{
    int    RouteHandle;
    byte   LastObservedReplanCount;   // stale-detection
    uint   NavmeshVersionAtPlan;
    float  TotalDistanceMeters;
    byte   PrimaryBackend;
    byte   WaypointCount;             // 0..MaxBrainCachedWaypoints (default 64)
    // [InlineArray<NavWaypoint, MaxBrainCachedWaypoints>] Waypoints
}
```

### Key Enums

| Type | Values | Used in |
|---|---|---|
| `NavigationMode` | None, DirectPoint, FollowRoute, JoinFormation, RoadGraph | `NavigationIntent.Mode` |
| `NavigationResult` | InProgress, Arrived, FailedBlocked, FailedUnreachable, PathFound, NoPath, FailedNoLayer, FailedInvalidHandle | `NavigationStatus.Result` |
| `NavigationPhase` | Idle, AwaitingPath, Following, AwaitingTraversal, Completed | `NavigationStatus.Phase` |
| `NavigationBackend` | Auto, NavRoadGraph, Navmesh, Hybrid, Volumetric | `PathfindingRequestEvent.BackendForce` |
| `TraversalKind` | Walk, Jump, Climb, Door, Fly | `NavWaypoint.Traversal` |
| `SurfaceType` | Generic, Road, Terrain, Water, Indoor | `NavWaypoint.Surface` |
| `NavigationFailureReason` | NoFailure, Unreachable, Timeout, InvalidHandle, ProviderError | `NavigationStatus.LastFailureReason` |
| `NavLayerMask` | None, Infantry(1), Vehicle(2), Naval(4), Air(8), All | path requests, provider queries |

---

## Provider Interfaces

All four provider interfaces are registered as ECS singleton managed objects.
`NavigationFakesModule` or `EngineBackedNavigationModule` registers exactly one
implementation of each (mutual exclusion enforced at startup).

### `INavmeshProvider`

Navmesh query interface consumed by EQS, the pathfinding solver, and navigation systems.

```csharp
[ComponentId(GlobalComponentIds.INavmeshProvider)]
public interface INavmeshProvider
{
    bool IsWalkable(Vector3 position, uint layerMask = 0xFFFFFFFF);
    bool ProjectToNavmesh(Vector3 position, out Vector3 snapped, uint layerMask = 0xFFFFFFFF);
    int  SampleNavmeshPoints(Vector3 center, float radius, Span<Vector3> results, uint layerMask = 0xFFFFFFFF);
    bool PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF);
    float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF);
    uint QueryVersion();
    int  PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask = 0xFFFFFFFF);
}
```

`QueryVersion()` returns a monotone counter that increments when the navmesh is rebuilt.
Callers cache path results until the version changes.

All coordinates are 3D world space (Sim Z-up). For 2D flat-earth queries use
`new Vector3(x, 0f, z)` and extract via `.X`/`.Z`.

### `IDtCrowdProvider`

dtCrowd integration interface for local avoidance of infantry agents. State component is
`FakeCrowdAgentState` (per-agent) and `FakeCrowdGlobalState` (singleton) for the fake
implementation.

### `IVolumetricPathProvider`

3D volumetric pathfinder for aerial and sub-surface agents. Shares the `NavTestMap`
no-fly-zone data with `FakeNavmeshProvider` for consistent world representation.

### `IPathRegistry`

Read-only access to stored path data. Implemented by `MusclePathRegistry` (authoritative,
Muscle side), `BrainPathRegistry` (Brain-side cache), and `SharedPathRegistry`
(all-in-one mode, satisfies both roles from a single instance).

```csharp
[ComponentId(GlobalComponentIds.IPathRegistry)]
public interface IPathRegistry
{
    bool IsCached(int routeHandle);
    bool TryGetSummary(int routeHandle, out PathSummary summary);
    bool TryGetWaypoints(int routeHandle, Span<NavWaypoint> dest, out int count);
    bool TryGetWaypointsSlice(int routeHandle, int startSegment, int maxCount,
                              Span<NavWaypoint> dest, out int actualCount);
}

struct PathSummary
{
    int   RouteHandle;
    float TotalDistanceMeters;
    int   WaypointCount;
    uint  NavmeshVersionAtPlan;
    byte  PrimaryBackend;
    byte  Flags;          // bit 0: HasOffMeshLinks
    byte  ReplanCount;
}
```

---

## Key Value Types

### `NavWaypoint` (24 B)

Single point in a planned path, returned by `INavmeshProvider.PlanPath` and stored in the
`TrajectoryPoolManager`.

```csharp
[StructLayout(LayoutKind.Sequential)]
readonly struct NavWaypoint
{
    Vector3      Position;   // world-space metres, Sim Z-up
    TraversalKind Traversal; // how agent traverses edge leading to this point
    SurfaceType   Surface;   // surface type at this waypoint
    // 2 bytes explicit padding
    float         TimeOffset; // seconds from path start; 0 = unknown
    // 4 bytes reserved
}
```

### `NavLayerMask`

Bit-flag enum (`[Flags] uint`) identifying traversable agent layers:
`Infantry = 1`, `Vehicle = 2`, `Naval = 4`, `Air = 8`, `All = 0xFFFFFFFF`.

### `NavigationHandleAllocator`

Thread-safe static allocator for Muscle-private route handles. All Muscle-allocated handles
are `>= MuscleHandleBase` (0x40000000), ensuring no overlap with Brain-allocated handles
which occupy the lower range.

```csharp
public static class NavigationHandleAllocator
{
    public const int MuscleHandleBase = 0x40000000;
    public static int Allocate(); // thread-safe, monotone-increasing
}
```

---

## ECS Systems

All systems live in `Navigation/Systems/` and are annotated with `[UpdateInPhase]`.

| System | Phase | Purpose |
|---|---|---|
| `NavigationIntentBridgeSystem` | Simulation | Translates `NavigationIntent` -> `NavState` and issues `PathfindingRequestEvent`. |
| `PathfindingSolverSystem` | Simulation (10 Hz background) | Resolves `PathfindingRequestEvent`s via road-graph Dijkstra, `INavmeshProvider.PlanPath`, or `IVolumetricPathProvider`. Writes waypoints into `TrajectoryPoolManager` and publishes `PathfindingResultEvent`. |
| `PathfindingResultMaterializationSystem` | Simulation (main thread) | Converts `PathfindingResultEvent`s into `NavigationCorridorMuscle` components and publishes `MoveStartedEvent`. |
| `CrowdAgentUpdateSystem` | PostSimulation | Updates `SimVelocity` for crowd-managed infantry agents via `IDtCrowdProvider`. Runs only on entities tagged `CrowdAgent`. |
| `OffMeshLinkDetectionSystem` | Simulation | Detects when an agent enters an off-mesh link polygon. Suppresses zero-frame false positives. Publishes `OffMeshTraversalStartedEvent` to trigger animation montages. |
| `CorridorPreviewSystem` | PostSimulation | Maintains the 8-waypoint `NavigationCorridorPreview` window for entities that opted in via `FlagBitStreamCorridorPreview`. |
| `NavigationPathDetailsUpdateSystem` | Simulation | (Brain-tier) Receives `NavigationPathDetailsResponseEvent` and materializes waypoints into `NavigationPathDetailsBuffer`. Publishes `NavigationPathDetailsArrivedEvent`. |

### `PathfindingSolverSystem` -- Backend Selection

Backend is selected per-request based on `MobilityProfile` and `BackendForce`:

1. `BackendForce == Navmesh` -- calls `INavmeshProvider.PlanPath`.
2. `BackendForce == Volumetric` (or `MobilityProfile == Flying`) -- calls `IVolumetricPathProvider`.
3. `BackendForce == RoadGraph` (or `Auto` for vehicle profiles) -- runs Dijkstra over `RoadNetworkBlob`.
4. `BackendForce == Hybrid` -- road-graph for macro routing, navmesh for local correction.

**Budget:** At most `PathfindingBatchData.DefaultCapacity` (256) requests processed per
solver tick; excess requests are dropped (oldest-evict ring-buffer semantics).

### `NavigationIntentBridgeSystem` -- Mode Mapping

```
NavigationIntent.Mode          NavState result
--------------------          ----------------
None                ->        Mode=None, TargetSpeed=0 (halt)
DirectPoint         ->        KinematicsMode.Direct; copy FinalDestination, speed, radius
RoadGraph           ->        KinematicsMode.RoadGraph; copy TargetNodeId
FollowRoute         ->        KinematicsMode.CustomTrajectory; copy TrajectoryId
                              (resets ProgressS=0 when IntentId changes)
```

The system is idempotent: it caches the last-applied `IntentId` per entity (keyed by the
full `Entity` struct including generation) and skips processing if the intent has not changed.

---

## Action Executors

Executors live in `Navigation/Executors/` and implement `IActionExecutor<LocomotionChannel>`.
Each is invoked by `LocomotionDispatcherSystem` on the Brain tier.

| Executor | Action ID | Description |
|---|---|---|
| `MoveToExecutor` | `ActionIdMoveTo = 1` | Writes `NavigationIntent` with `Mode=DirectPoint`; polls `NavigationStatus.Result` for arrival/failure verdict. |
| `FleeExecutor` | `ActionIdFlee = 2` | Reads `FleeParams.Threat` entity; periodically replans flee direction. Checks `view.IsAlive(Threat)` before accessing position. |
| `FollowRouteExecutor` | `ActionIdFollowRoute = 3` | Follows a pre-computed `CustomTrajectory` from the pool by `TrajectoryId`. |
| `PlanRouteExecutor` | `ActionIdPlanRoute = 6` | Writes `NavigationIntent` with `Mode=PlanRoute`; returns Success when `NavigationStatus.Result == PathFound`. Handle stashed in blackboard. |
| `FollowPathExecutor` | `ActionIdFollowPath = 7` | Writes `NavigationIntent` with the stashed `RouteHandle`; Muscle looks up cached path and begins following. |
| `FetchPathDetailsExecutor` | `ActionIdFetchPathDetails = 8` | Writes `NavigationIntent` requesting waypoint details. `blocking=true`: Running until `BrainPathRegistry.IsCached(handle)`. |
| `ReleasePathExecutor` | `ActionIdReleasePath = 9` | Writes `NavigationIntent` to release the route handle from `TrajectoryPoolManager`. |
| `JoinFormationExecutor` | `ActionIdJoinFormation = 5` | Joins a formation slot. |

**`ActionIdFollowRoadGraph = 4`** is marked `[Obsolete]`. Use `ActionIdMoveTo` with
`MoveToParams.BackendForce = 2` (RoadGraph) instead (see NAV-P4-T2).

### Action Parameter Structs

All parameter structs are unmanaged, `[StructLayout(Sequential)]`, <= 32 bytes.

| Struct | Size | Key fields |
|---|---|---|
| `MoveToParams` | 32 B | `Vector3 Destination`, `float ArrivalRadius`, `float Speed`, `int RouteHandle`, `uint LayerMask`, `byte ReverseAllowed`, `byte Flags`, `byte MaxReplans`, `byte BackendForce` |
| `FleeParams` | 16 B | `Entity Threat` (full handle, check `IsAlive`), `float SafeDistance`, `float Speed` |
| `FleeState` | 4 B | `uint NextReplanTick` |
| `FollowRouteParams` | 8 B | `int TrajectoryId`, `byte IsLooped` |
| `FollowRoadGraphParams` | 8 B | `int TargetNodeId` (obsolete) |
| `PlanRouteParams` | 32 B | `Vector3 Destination`, `int RouteHandle`, flags |
| `FollowPathParams` | 8 B | `int RouteHandle`, flags |
| `FetchPathDetailsParams` | 8 B | `int RouteHandle`, `byte Blocking` |
| `ReleasePathParams` | 4 B | `int RouteHandle` |

**`Destination`** is `Vector3` (3D Cartesian, Sim Z-up). Altitude is carried for fidelity
but steering remains 2D-projected. Geographic conversion is the egress translator's
responsibility, not the executor's.

---

## Modules

### `NavigationSolverModule`

Wraps `PathfindingSolverSystem` into a self-contained `IEcsModule` installable on any node.

```csharp
public sealed class NavigationSolverModule : IEcsModule
{
    public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(10); // 10 Hz background
    public NavigationSolverModule(RoadNetworkBlob roadNetwork,
                                  TrajectoryPoolManager? trajectoryPool = null,
                                  INavmeshProvider? navmesh = null,
                                  IVolumetricPathProvider? volumetric = null);
    // Registers PathfindingResultMaterializationSystem (main-thread materializer)
    public void RegisterSystems(ISystemRegistry reg);
    // Runs PathfindingSolverSystem on the background thread each tick
    public void Tick(ISimulationView view, float dt);
}
```

### `NavigationFakesModule`

All-in-one module for single-process integration tests. Mutually exclusive with
`EngineBackedNavigationModule` (mutual exclusion enforced by `RegisterProviders`).

```csharp
public sealed class NavigationFakesModule : IEcsModule, IDisposable
{
    // Exposed for test-API access
    public FakeNavmeshProvider          Navmesh      { get; }
    public FakeDtCrowdProvider          Crowd        { get; }
    public FakeVolumetricPathProvider   Volumetric   { get; }
    public SharedPathRegistry           PathRegistry { get; }
    public NavTestMap?                  Map          { get; }

    // Constructors
    public NavigationFakesModule(NavTestMap map);   // from test-map asset
    public NavigationFakesModule();                 // empty / default providers

    // Registers INavmeshProvider, IDtCrowdProvider, IVolumetricPathProvider,
    // IPathRegistry singletons into the repo.
    public void RegisterProviders(EntityRepository repo);
}
```

### `EngineBackedNavigationModule`

Adapter module that wires the new navigation contract to the existing engine road-network
machinery (`RoadNetworkBlob`, `TrajectoryPoolManager`, `CarKinematicsSystem`). Suitable
for demo scenarios with real vehicle movement. Not for unit/integration tests.

```csharp
public sealed class EngineBackedNavigationModule : IEcsModule, IDisposable
{
    public EngineBackedNavigationModule(RoadNetworkBlob roadNetwork,
                                        TrajectoryPoolManager pool);
    public void RegisterSystems(ISystemRegistry reg);   // installs EngineBackedPathResponseSystem
    public void RegisterProviders(EntityRepository repo); // mutual-exclusion guard
}
```

---

## Fake Backends (`Fdp.Toolkit.Navigation.Fake`)

Source folder: `Navigation/Fake/`

The fake backends are deterministic, DotRecast/dtCrowd-free implementations designed to
unblock AI behavior development and integration testing. They share a single `NavTestMap`
data source for consistent world representation across all four fakes.

### `FakeNavmeshProvider`

Implements `INavmeshProvider` using polygon A* over an in-memory `FakeNavLayer[]`.

**Internal data** (per layer):
```
FakeNavLayer:
  NavLayerMask Layer
  NavPolygon[] Polygons    -- immutable after load; IsBlocked togglable via test API
  int[][]      Adjacency   -- polygon-index -> neighbor-polygon-indices
  OffMeshLink[] OffMeshLinks
  uint         Version     -- bumped by test-API patch

NavPolygon: { int Id, Vector3[] Vertices, SurfaceType, bool IsBlocked }
OffMeshLink: { int FromPolygonId, int ToPolygonId, Vector3 StartPos, Vector3 EndPos,
               TraversalKind Kind, float Cost }
```

**Query algorithms** (performance is irrelevant -- dev/test use only):
- `IsWalkable(pos, mask)` -- point-in-polygon test on matching layers
- `ProjectToNavmesh(pos, mask)` -- nearest edge/interior within `maxDist`; Z = interpolated elevation
- `PathExists(a, b, mask)` -- A* over adjacency; off-mesh links count as edges
- `PathCost(a, b, mask)` -- same A*, returns cost or `+inf` if unreachable
- `SampleNavmeshPoints(center, radius, mask)` -- grid-samples within radius, `IsWalkable` filter
- `QueryVersion()` -- max version across matching layers in bounds
- `PlanPath(a, b, mask, output)` -- A* reconstructing polygon-sequence to `NavWaypoint[]` including
  off-mesh entry/exit positions tagged with their `TraversalKind`

**Test API** (`IFakeNavmeshProviderTestApi`):
- `BlockPolygon(layerId, polygonId)` / `UnblockPolygon(...)` -- toggles `IsBlocked`, bumps version
- `PatchNavmesh(layerId, ...)` -- replaces polygons, bumps version (triggers replan in scenario S5)

### `FakeDtCrowdProvider`

Implements `IDtCrowdProvider` using a simple velocity-obstacle tick algorithm. Per-agent
state stored in `FakeCrowdAgentState` (ECS component). Global state in
`FakeCrowdGlobalState` (singleton component).

### `FakeVolumetricPathProvider`

Implements `IVolumetricPathProvider` with a 3D straight-line + obstacle avoidance
algorithm. Uses `NavTestMap` no-fly-zone data for consistency with `FakeNavmeshProvider`.
State: `FakeVolumetricState` (singleton component).

### Path Registries

| Class | Implements | Role |
|---|---|---|
| `MusclePathRegistry` | `IPathRegistry` | Muscle-side authoritative store; per-handle `FakePathPoolEntry` |
| `BrainPathRegistry` | `IPathRegistry` | Brain-side cache; per-handle per-entity `FakeBrainPathCacheEntry` |
| `SharedPathRegistry` | `IPathRegistry` | All-in-one mode: single instance satisfying both Muscle and Brain roles |

`NavigationFakesModule` registers `SharedPathRegistry` for both `IPathRegistry` slots
in all-in-one (integration test) mode.

### `NavTestMap`

JSON+DSL test-world data format. Loaded once at scenario start by `NavTestMapLoader`;
provides ground truth for all three fake providers.

```json
{
  "name": "two-room",
  "layers": [
    {
      "id": "Infantry",
      "polygons": [ { "id": 0, "vertices": [[0,0,0],[10,0,0],[10,10,0],[0,10,0]] }, ... ],
      "off_mesh_links": [ { "from": 0, "to": 1, "kind": "Jump", "cost": 2.0 } ]
    }
  ],
  "no_fly_zones": [...]
}
```

Canonical map names are collected in `NavTestMaps` (static class with string constants).
The `NavTestMapBuilder` DSL builds maps programmatically in test setup code.

---

## Engine-Backed Module (`Fdp.Toolkit.Navigation.EngineBacked`)

Source folder: `Navigation/EngineBacked/`

Four providers that wire the navigation contract to existing engine machinery.
Suitable for demo scenarios; not for unit tests.

| Provider | Backed by | Behavior |
|---|---|---|
| `EngineBackedNavmeshProvider` | Nothing (placeholder) | `IsWalkable=true`, `PlanPath` returns `[start, end]` two waypoints. |
| `EngineBackedDtCrowdProvider` | Nothing (stub) | `RegisterAgent` accepts; no avoidance produced. Entities move via `LinearKinematicsSystem`. |
| `EngineBackedVolumetricPathProvider` | Nothing (placeholder) | Direct-line 3D path, same shape as navmesh placeholder. |
| `EngineBackedPathRegistry` | `TrajectoryPoolManager` | Real: `RouteHandle` = existing `NavState.TrajectoryId`. `TryGetWaypoints` reads from pool's `CustomTrajectory`. |

`EngineBackedPathResponseSystem` intercepts `PathfindingRequestEvent`s with
`BackendForce == RoadGraph` (or `Auto` for vehicle profiles) and routes them to the
existing `PathfindingSolverSystem` (Dijkstra over `RoadNetworkBlob`). This is where real
vehicle paths come from in engine-backed mode.

**What this module intentionally does not provide:**
- Real navmesh obstacle avoidance (humanoids walk through walls)
- Layer differentiation (all `layerMask` queries return identical results)
- Off-mesh links (no jumps, climbs, or doors)
- Version churn (`QueryVersion` returns constant 1; navmesh-patch replans never trigger)

---

## Events

All events are unmanaged, `[StructLayout(Sequential)]`, publishable on `FdpEventBus`.

| Event | EventId | Published by | Consumed by |
|---|---|---|---|
| `PathfindingRequestEvent` | 2032 | `NavigationIntentBridgeSystem` (Muscle) | `PathfindingSolverSystem` |
| `PathfindingResultEvent` | 2033 | `PathfindingSolverSystem` | `PathfindingResultMaterializationSystem`, egress translators |
| `MoveStartedEvent` | 2034 | `PathfindingResultMaterializationSystem` | Executors, egress translators |
| `OffMeshTraversalStartedEvent` | 2035 | `OffMeshLinkDetectionSystem` | Animation systems (montage trigger) |
| `NavigationPathDetailsResponseEvent` | -- | Muscle (on `FetchPathDetails` or auto-refresh) | `NavigationPathDetailsUpdateSystem` (Brain) |
| `NavigationPathDetailsArrivedEvent` | -- | `NavigationPathDetailsUpdateSystem` | Blueprint `WhenNode` subscriptions |
| `PathReplannedEvent` | -- | Muscle (on silent internal replan) | Brain BTree observers |

### `PathfindingRequestEvent` key fields

```csharp
struct PathfindingRequestEvent
{
    long              RequestId;          // (entityIndex << 32) | GlobalVersion
    Vector3           Start, End;         // FDP Cartesian metres
    byte              MobilityProfile;    // 0=Wheeled, 1=Tracked, 2=Infantry, 3=Naval, 4=Flying
    NavigationBackend BackendForce;       // 0=Auto
    int               SourceNodeId;       // originating Brain node
    int               RouteHandle;        // 0=anonymous; solver allocates
    int               NavLayerMask;       // layer filter bitmask
    float             MaxCost;            // 0=unlimited
    int               NavmeshVersionAtRequest;
}
```

---

## Action Constants (`NavigationConstants`)

```csharp
public static class NavigationConstants
{
    const ushort ActionIdMoveTo          = 1;
    const ushort ActionIdFlee            = 2;
    const ushort ActionIdFollowRoute     = 3;
    [Obsolete] const ushort ActionIdFollowRoadGraph = 4;
    const ushort ActionIdJoinFormation   = 5;
    const ushort ActionIdPlanRoute       = 6;
    const ushort ActionIdFollowPath      = 7;
    const ushort ActionIdFetchPathDetails = 8;
    const ushort ActionIdReleasePath     = 9;

    const int   FrustrationTickThreshold  = 120; // ticks (~2 s at 60 Hz)
    const float FrustrationSpeedThreshold = 0.1f; // m/s
    const int   FleeReplanIntervalTicks   = 30;   // ticks (~0.5 s)
    const byte  DefaultMaxReplans         = 3;

    // Flags bit indices
    const byte FlagBitAllowReplan          = 0;
    const byte FlagBitStreamCorridorPreview = 3;
    const byte FlagBitAutoSendPathOnReplan  = 4;
}
```

---

## Test Infrastructure

### Three-Layer Strategy

| Layer | Projects | What it proves |
|---|---|---|
| **1. Unit** | `Fdp.Toolkits.Tests/Navigation/` | Each fake provider in isolation: algorithms, determinism, test-API correctness. |
| **2. System** | `Fdp.Toolkits.Tests/Navigation/` | One Muscle/Brain system against synthetic ECS state. |
| **3. Integration** | `Fdp.Toolkits.Tests/Navigation/Integration/` | Full Brain <-> Muscle <-> Solver pipeline in all-in-one mode, zero DDS. |

**Note:** Per NAV-P0-T1 all test code lives in `Fdp.Toolkits.Tests` (not the originally
designed `Hrot.Navigation.*.Tests` assemblies which were never created).

### `NavTestHarness`

Central test convenience (`Navigation/NavTestHarness.cs`). Provides a single-process
navigation world with exposed fakes for direct manipulation.

```csharp
public sealed class NavTestHarness : IDisposable
{
    public EntityRepository Repo { get; }
    public FakeNavmeshProvider          Navmesh    { get; }
    public FakeDtCrowdProvider          Crowd      { get; }
    public FakeVolumetricPathProvider   Volumetric { get; }
    public IPathRegistry                MusclePaths { get; }
    public IPathRegistry                BrainPaths  { get; }  // == MusclePaths in all-in-one

    // Factory
    public static NavTestHarness LoadMap(string mapName);
    public static NavTestHarness LoadMap(NavTestMap inlineMap);

    // Tick control
    public void Tick(int count = 1);
    public bool PumpUntil(Func<bool> condition, int maxTicks = 600, string failMessage = null);

    // Entity spawning
    public Entity SpawnInfantry(Vector2 pos, NavLayerMask layer = NavLayerMask.Infantry);
    public Entity SpawnVehicle(Vector2 pos, VehicleClass cls = VehicleClass.Wheeled);
    public Entity SpawnNaval(Vector2 pos);
    public Entity SpawnFlying(Vector3 pos);

    // BTree action helpers (write NavigationIntent)
    public void IssueMoveTo(Entity e, Vector2 destination, MoveToFlags flags = 0, int routeHandle = 0);
    public int  IssuePlanRoute(Entity e, Vector2 destination, PlanRouteFlags flags = 0);
    public void IssueFollowPath(Entity e, int routeHandle, FollowPathFlags flags = 0);
    public void IssueFetchPathDetails(Entity e, int routeHandle, bool blocking = true);
    public void IssueReleasePath(Entity e, int routeHandle);
}
```

### Layer-1 Unit Tests

Located in `Fdp.Toolkits.Tests/Navigation/`:

| File | Tests |
|---|---|
| `FakeNavmeshProviderTests.cs` | NAV-P8-T1 |
| `FakeDtCrowdProviderTests.cs` | NAV-P8-T2 |
| `FakeVolumetricPathProviderTests.cs` | NAV-P8-T3 |
| `PathRegistryTests.cs` | NAV-P8-T4 (`MusclePathRegistry`), NAV-P8-T5 (`BrainPathRegistry`), NAV-P8-T6 (`SharedPathRegistry`) |

### Layer-2 System Tests

Located in `Fdp.Toolkits.Tests/Navigation/`:

| File | Tests |
|---|---|
| `OffMeshLinkDetectionSystemTests.cs` | NAV-P9-T1 |
| `CrowdAgentUpdateSystemTests.cs` | NAV-P9-T2 |
| `NavigationIntentBridgeSystemTests.cs` | NAV-P9-T3 |
| `NavigationProgressTrackerSystemTests.cs` | NAV-P9-T4 |
| `ExecutorTests/` | NAV-P9-T5 (MoveTo + new executors) |
| `NavigationPathDetailsUpdateSystemTests.cs` | NAV-P9-T6 |

### Layer-3 Integration Scenarios

Located in `Fdp.Toolkits.Tests/Navigation/Integration/`:

| File | Scenario | What it proves |
|---|---|---|
| `S1_SimpleCorridorTests.cs` | S1 | MoveTo in a single-polygon map; arrival event |
| `S2_LBendFollowTests.cs` | S2 | Multi-waypoint L-bend corridor follow |
| `S2b_LBendCorridorPreviewTests.cs` | S2b | CorridorPreview window slides correctly on L-bend |
| `S3_TwoLayersRoutingTests.cs` | S3 | Two navmesh layers; routing respects layer mask |
| `S4_OffMeshJumpAcrossTests.cs` | S4 | Off-mesh link traversal; montage event emitted |
| `S5_ReplanOnNavmeshPatchTests.cs` | S5 | Navmesh patch bumps version; Muscle replans silently |
| `S5b_ReplanWithAutoRefreshTests.cs` | S5b | AutoSendPathOnReplan: Brain path buffer auto-updated |
| `S6_CrowdAvoidanceTests.cs` | S6 | Two infantry agents avoid each other via crowd provider |
| `S7_FailedUnreachableTests.cs` | S7 | Destination on a blocked polygon; FailedUnreachable result |
| `S8_FrustrationWatchdogTests.cs` | S8 | Agent stuck for FrustrationTickThreshold ticks; FailedBlocked |
| `S9_FlyingAgentRoutingTests.cs` | S9 | Flying agent uses VolumetricPathProvider |
| `S10_NavalLayerRoutingTests.cs` | S10 | Naval agent uses Naval layer mask |
| `S11_PlanRouteThenFollowPathTests.cs` | S11 | PlanRoute returns PathFound; separate FollowPath intent starts motion |
| `S12_FetchPathDetailsAndCacheInvalidationTests.cs` | S12 | FetchPathDetails populates BrainPathRegistry; cache invalidated on replan |

---

## Diagnostics

### `FakeNavigationInspectorWindow`

Four-tab ImGui window registered by `NavigationFakesModule` in non-headless builds
(NAV-P7-T1). Location: `Hrot/Subsystems/Hrot.SimHost/Windows/FakeNavigationInspectorWindow.cs`

Tabs:
1. **Navmesh** -- per-layer polygon list, walkability query, version counter
2. **Crowd** -- per-agent state (position, preferred velocity, actual velocity)
3. **Path Registry** -- all stored handles: waypoint count, total distance, replan count
4. **Entities** -- per-entity `NavigationCorridorMuscle` and `NavigationStatus` state

### `NavigationSnapshotBuilder`

Builds a JSON diagnostic snapshot of the current provider state
(NAV-P7-T2). Used by the inspector window's "Snapshot JSON" export button and by
AAR recording integration.

Top-level JSON keys: `captured_at_tick`, `loaded_map`, `navmesh`, `crowd`, `volumetric`,
`path_registry`.

### Planned-Path Gizmo

`CorridorPreviewSystem` drives a `NavigationTargetGizmo` (located in
`Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/NavigationTargetGizmo.cs`) that draws
the 8-waypoint corridor preview as a line gizmo in the scene view (NAV-P7-T3).

---

## Replan Flow (Muscle-internal)

Brain observes only hard failures; silent replans are invisible to Brain except for the
`ReplanCount` field incrementing in `NavigationStatus`.

```
NavigationExecutionSystem detects frustration
  (SimVelocity.Length < FrustrationSpeedThreshold for FrustrationTickThreshold ticks)
  NavigationStatus.Phase = Stuck (transient)

if ReplanCount < MoveToParams.MaxReplans AND elapsed < ReplanTimeBudget:
  re-publishes PathfindingRequestEvent (same RouteHandle)
  Solver returns new waypoints; TrajectoryPoolManager entry replaced in place
  NavigationStatus.ReplanCount++
  if AutoSendPathOnReplan: fires NavigationPathDetailsResponseEvent(IsAutoRefresh=true)
  fires PathReplannedEvent
  resumes following
else:
  NavigationStatus.Result = FailedBlocked
  Brain BTree observes hard failure for the first time
```

---

## DDS Topology -- `NavigationIntent` / `NavigationStatus`

In the **default** and **scale-out** topologies:

| Translator | Location | Direction |
|---|---|---|
| `NavigationIntentEgressTranslator` | `Hrot.Network.NED` | Brain -> DDS |
| `NavigationIntentIngressTranslator` | `Hrot.Network.NED` | DDS -> Muscle |
| `NavigationStatusEgressTranslator` | `Hrot.Network.NED` | Muscle -> DDS |
| `NavigationStatusIngressTranslator` | `Hrot.Network.NED` | DDS -> Brain |

DDS IDL generated types: `Hrot.NED.Descriptors.NavigationIntent` and
`Hrot.NED.Descriptors.NavigationStatus` (`.g.cs` files in `Hrot.Network.NED/obj/`).

In **all-in-one** mode (editor, integration tests) no translator is registered; intent
and status travel on the local `FdpEventBus` without any DDS serialization.

---

## Relationship to Navigation v1

Navigation v2 extends and renames the earlier ECS components. Key changes:

| v1 | v2 | Notes |
|---|---|---|
| `NavMode` enum | `NavigationMode` enum | Additional values added (same underlying values) |
| `NavResult` enum | `NavigationResult` enum | Additional values added |
| `INavmeshProvider` (in EQS) | `INavmeshProvider` (in Navigation) | Migrated; 3D `Vector3` coords replacing 2D `Vector2`. EQS callers updated. |
| `FollowRoadGraphExecutor` | Removed | Use `MoveToExecutor` with `BackendForce=RoadGraph` |
| No Brain-side registry | `BrainPathRegistry` / `SharedPathRegistry` | New in v2 |
| No fake providers | `FakeNavmeshProvider`, `FakeDtCrowdProvider`, `FakeVolumetricPathProvider` | New in v2 |

---

## Dependencies

**Outgoing (what this domain requires):**

| Dependency | Purpose |
|---|---|
| `Fdp.Core` | `EntityRepository`, `ComponentTypeRegistry`, `FdpEventBus` |
| `Fdp.ModuleHost` | `IEcsModule`, `IEcsModuleSystem`, `ExecutionPolicy` |
| `CarKinem.Core` | `NavState`, `KinematicsMode`, `CarKinematicsSystem` |
| `CarKinem.Road` | `RoadNetworkBlob` for road-graph Dijkstra |
| `CarKinem.Trajectory` | `TrajectoryPoolManager`, `CustomTrajectory` |
| `Fdp.Toolkit.Behavior` | `LocomotionChannel`, `IActionExecutor`, `LocomotionDispatcherSystem` |

**Consumed by (who depends on this domain):**

| Consumer | Uses |
|---|---|
| `Fdp.Toolkit.Spatial.Eqs` | `INavmeshProvider` (reachability scoring, path-cost tests) |
| `Hrot.Network.NED` | `NavigationIntent`, `NavigationStatus` DDS translators |
| `Hrot.SimHost` | `NavigationSolverComponentRegistry`, `FakeNavigationInspectorWindow` |
| `Hrot.Engine.Hrot.Common` | `NavigationTargetGizmo` |
| `Hrot.ClusterRunner.Integration.Tests` | Navigation authority and translator tests |
| `Fdp.Toolkits.Tests` | All three test layers |
