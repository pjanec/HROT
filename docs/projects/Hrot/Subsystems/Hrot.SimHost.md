# Hrot.SimHost

**Project path:** `Hrot/Subsystems/Hrot.SimHost/Hrot.SimHost.csproj`
**Date:** 2026-05-23
**Target framework:** net8.0
**Output type:** Library
**Root namespace:** `Hrot.SimHost`
**Assembly:** `Hrot.SimHost`

---

## README Validation

**Status: Missing**

No `README.md` exists in `Hrot/Subsystems/Hrot.SimHost/`. Documentation authority is this file.

---

## Executive Overview

`Hrot.SimHost` is the authoritative simulation host in the IOS-IG-SimHost distributed
architecture. It owns the "Muscle" half of the Brain/Muscle split-authority model used
throughout HROT:

- **Brain (CGF)** — decision-making, AI behavior trees, mission planning, entity lifecycle.
- **Muscle (SimHost)** — physics, ground kinematics, combat resolution, navigation execution,
  spatial perception.

At runtime SimHost runs as a library that is hosted either:

1. **Standalone graphical mode** (`SimHostApp`) — a Raylib/ImGui window with a 2-D tactical
   map that shows all entities and provides operator panels for spawn, inspection, and scenario
   management.
2. **Headless subsystem mode** (`SimHostSubsystem`) — embedded in `ClusterRunner` alongside IG
   and (optionally) CGF, driven by a background thread with no graphics window.

Both modes share the same kernel initialization path (`SimHostNodeBootstrapper`), the same ECS
world, and the same CycloneDDS network stack. The IG consumes per-entity state from SimHost for
visualization; ExCon/CGF sends commands to SimHost via DDS topics.

### Node Role

SimHost is always started with:

```
NodeRole.MuscleGround | NodeRole.Perception
```

`NodeRole` is a flags enum defined in `Hrot.Common`. The two active flags determine which
simulation modules are installed:

| Flag | Modules activated |
|---|---|
| `MuscleGround` | `CombatModule`, `DamageAssessmentModule`, `GroundKinematicsModule`, navigation bridge systems |
| `Perception` | `CognitiveSpatialModule` (LOS/sensor tracking at 10 Hz on a background thread) |

---

## Architecture

### Brain / Muscle Split

```
+-----------------------------+          +-----------------------------+
|          CGF (Brain)        |          |       SimHost (Muscle)      |
|                             |          |                             |
|  BTree / HSM AI             |          |  Ground Kinematics          |
|  Mission Planning           |          |  Combat Resolution          |
|  Entity Lifecycle           |          |  Physics / Ballistics       |
|  NavigationIntent (write)   |----DDS-->|  NavigationIntent (read)    |
|  MissionControlAck (read)   |<---DDS---|  NavigationStatus (write)   |
|  WeaponFireIntent (write)   |----DDS-->|  FireProcessingSystem       |
+-----------------------------+          +-----------------------------+
            |                                          |
            |  DDS entity state (NED protocol)         |
            +------------------------------------------+
                                 |
                    +------------+------------+
                    |         IG Node         |
                    |  Reads entity state     |
                    |  for visualization      |
                    +-------------------------+
```

### Simulation Tick Pipeline

```
+----------------+   +------------------+   +------------------+   +--------------+
|  Input Phase   |-->| BeforeSync Phase |-->| Simulation Phase |-->| PostSim Phase|
+----------------+   +------------------+   +------------------+   +--------------+
|GenesisMat-     |   |RouteTrajectory-  |   |GroundKinematics- |   |Ballistics-   |
| erialization   |   | SyncSystem       |   | Module           |   | System       |
|System          |   |                  |   |CombatSystems     |   |              |
|AreaQueryResult-|   |                  |   |NavigationExec    |   |              |
| Materialization|   |                  |   |SpatialHash       |   |              |
|FireProcessing  |   |                  |   |FormationFollowing|   |              |
|RaycastSolver   |   |                  |   |                  |   |              |
|HitResolution   |   |                  |   |                  |   |              |
|PersonalRoute-  |   |                  |   |                  |   |              |
| Authoring      |   |                  |   |                  |   |              |
+----------------+   +------------------+   +------------------+   +--------------+

  +----------------------------+
  | Background (SlowBackground)|
  | CognitiveSpatialModule     | -- runs at 10 Hz on a separate thread
  | (SpatialHash, LOS, EQS)    |    against a SoD snapshot
  +----------------------------+
```

### Initialization Phases (SharedApplicationBootstrapper)

SimHost initialization follows the 7-phase `SharedApplicationBootstrapper` contract
orchestrated by `SimHostNodeBootstrapper`:

```
Phase 1: BuildContext
  - HrotNodeBuilder creates HrotNodeContext (DDS participant, NedReplicationModule, etc.)
  - TKB translator list is assembled

Phase 2: RegisterDomainComponents
  - SimHostComponentRegistry.RegisterAll() stamps the ECS schema

Phase 3: PopulateSystems
  - Road network loaded from disk (or default empty blob)
  - SimHostCoreLogicPack instantiated (CombatModule, GroundKinematicsModule, etc.)
  - INetworkFactory.ConfigureForNode() creates attribute-update systems

Phase 4: RegisterModules
  - TogglableInputGroup / SimulationGroup / PostSimGroup created
  - CognitiveSpatialModule installed (10 Hz background)
  - EcsRecordReplayController wired

Phase 5: BuildOrchestration
  - ClusterSlave created with TkbLoadClusterStateHandler
  - HrotScenarioLoadHandler registered for PrepareLive / PrepareEdit
  - CheckpointClusterOpHandler registered

Phase 6: RegisterApplicationSystems
  - ApplicationSystemsRegistrar callback invoked
  - Gizmo modules, diagnostic capture systems installed by SimHostApp

Phase 7: Initialize
  - Kernel.Initialize() called; all modules start
```

### Orchestration State Machine

```
+----------+    PrepareLive     +------------+   OperatingLive    +----------+
|  Idle    |-----------------> | LoadingLive |-----------------> | Live     |
|          | <-----------------| (HrotScenario|                  |          |
+----------+   FinalizeLive    | LoadHandler) |                  +----------+
                                +------------+                        |
                                                                      | TakeSnapshot
                                                                      v
                                                              +----------------+
                                                              | Checkpoint I/O |
                                                              | (background)   |
                                                              +----------------+

  PrepareReplay  -> FinalizeReplay: EcsRecordReplayController installs/tears down
                                    RecordingModule / ReplayModule
```

---

## Source Structure

### Namespace Map

All types live under `Hrot.SimHost` or sub-namespaces.

```
Hrot.SimHost
  SimHostApp                     -- graphical standalone entry point
  SimHostSubsystem               -- ISubsystem adapter for ClusterRunner
  SimHostNodeBootstrapper        -- 7-phase initialization (SharedApplicationBootstrapper)
  NodeBootstrapper               -- orchestration composition root (ClusterSlave builder)
  NodeConfiguration              -- JSON config record (DDS, assets, simulation rate)
  GeodeticOriginConfig           -- WGS-84 flat-earth geodetic reference
  NodeRole                       -- global alias for Hrot.Common.NodeRole (flags enum)
  SimHostCoreLogicPack           -- composite IEcsModule grouping Muscle-tier sub-modules
  SimHostComponentRegistry       -- master ECS schema registration entry point
  SimHostEvents / SimHostEventIds-- stable event ID constants
  SimHostVisualization           -- self-contained graphical visualization layer
  MuscleRoleComponentRegistry    -- Muscle-tier component/event schema
  CombatComponentRegistry        -- combat and perception component/event schema
  CognitiveComponentRegistry     -- cognitive/Brain-tier component/event schema
  KinematicComponentRegistry     -- kinematic/Muscle-tier component/event schema
  HierarchyComponentRegistry     -- commander-subordinate hierarchy schema
  NavigationSolverComponentRegistry -- pathfinding + EQS singletons

Hrot.SimHost.Modules
  SimHostModule                  -- NetworkSpawningSystem host
  CombatModule                   -- FireProcessing, RaycastSolver, HitResolution, Ballistics
  EyesAndMuscleModule            -- async SoD PoC (60 Hz background, Eyes + Muscle)
  CognitiveSpatialModule         -- spatial hash, LOS/vision broadphase, EQS at 10 Hz
  EqsModule                      -- EQS solver at 10 Hz (standalone background)
  SimPresentationModule          -- SimMapRenderSystem registration + IMapCameraProvider
  ActionDispatchModule           -- relocated stub (see Fdp.Toolkit.Behavior.Modules)

Hrot.SimHost.Modules.Orchestration
  EcsRecordReplayController      -- factory + lifecycle for Recording/Replay modules

Hrot.SimHost.Systems
  GenesisMaterializationSystem   -- resolves Intent DTOs -> structural ECS components
  AreaQuerySolverSystem          -- polygon area queries against spatial hash grid
  AreaQueryResultMaterializationSystem -- writes solver results into AreaQueryBatchData
  SimMapRenderSystem             -- draws MapCanvas when perspective == "Sim"
  MissionControlBehaviorParamsHelper -- JSON param rewrite for FollowRoute tasks
  FactionSyncSystem              -- removed (empty tombstone file)
  FactionSyncAdapterSystem       -- removed (empty tombstone file)

Hrot.SimHost.Systems.Routing
  RouteTrajectorySyncSystem      -- syncs RoutePlan -> TrajectoryPoolManager
  PersonalRouteAuthoringSystem   -- processes CmdAppendPersonalWaypoint events

Hrot.SimHost.Orchestration.Handlers
  HrotScenarioLoadHandler        -- HROT scenario deserialization + genesis pipeline
  TkbLoadClusterStateHandler     -- TKB artifact loading from local staging area

Hrot.SimHost.Serializers
  HrotScenarioSerializerFactory  -- builds ScenarioSerializer with all HROT translators
  BrainBlackboardTranslator      -- serializes BrainBlackboard (read-only; no Inject)
  Blackboard1024Translator       -- serializes Blackboard1024
  MissionPlanTranslator          -- serializes MissionPlanQueue
  TargetMemoryTranslator         -- serializes TargetMemory
  PassengerBufferTranslator      -- serializes PassengerBuffer
  VisHierarchyNodeTranslator     -- serializes vis-hierarchy node
  IsEmbarkedTagTranslator        -- serializes IsEmbarkedTag
  PersonalRouteRefTranslator     -- serializes PersonalRouteRef
  UnitSubordinateTranslator      -- serializes UnitSubordinate
  EditablePolylineTranslator     -- serializes EditablePolyline
  BTreeTraceWorkingMemoryTranslator -- serializes BTree trace buffer
  HsmTraceWorkingMemoryTranslator   -- serializes HSM trace buffer

Hrot.SimHost.Gizmos
  SimHostEntityPresentationGizmo -- [GizmoProjector] emits SpatialAnchor + SemanticShape
  EntityRotatorGizmo             -- exclusive-focus gizmo for heading rotation
  GizmoRegistrar                 -- partial class wrapping source-generated RegisterAll()

Hrot.SimHost.Diagnostics
  AiTraceContextMenu             -- publishes PatchDebugStateCommand to toggle AI tracing
  AiDiagnosticsTkbTranslator     -- TKB observer that auto-enables AI trace buffers

Hrot.SimHost.Configuration
  SimHostNetworkConstants        -- LocalNodeId = 1

Hrot.SimHost.UI
  SimHostUIState                 -- shared mutable view-state for panels
  SimHostSimulationControlsPanel -- Play/Pause/Step/TimeScale ImGui panel
  SimHostSpawnPanel              -- vehicle spawn ImGui panel
  SimHostScenarioManager         -- GUI-driven entity spawning and scenario utilities
  SimHostSelectionManager        -- entity selection/hover state tracker
  SimHostInspectorAdapter        -- bridges SelectionManager to IInspectorContext/ISelectionState
  SimHostPanelColors             -- red title-bar theme constants + Push/Pop helpers

Hrot.SimHost.Visualization
  SimHostTrajectoryLayer         -- draws selected entity trajectory + route waypoints
  SimHostRoadLayer               -- draws road-network graph (nodes + segments)

Hrot.SimHost.Windows
  SimHostWindowColor             -- dark red title bar constant
  SimHostControlsWindow          -- ManagedWindow for SimHost controls panel
```

---

## Public API Reference

### Top-Level Types

#### `SimHostApp : FdpApplication`

Graphical standalone entry point. Implements `FdpApplication` lifecycle hooks.

| Member | Kind | Description |
|---|---|---|
| `World` | property | ECS `EntityRepository` (throws before init) |
| `Kernel` | property | `ModuleHostKernel` (throws before init) |
| `WorldOrNull` | property | `EntityRepository?` — null before init completes |
| `EntityMap` | property | `NetworkEntityMap` (throws before init) |
| `Visualization` | property | `SimHostVisualization?` — null in headless mode |
| `GizmoController` | property (internal) | `GizmoExecutionController` |
| `GizmoUiHub` | property (internal) | `GizmoUiStateHub` |
| `ParseRole(string[])` | static method | Parses `--role <value>` CLI arg; default `MuscleGround|Perception` |
| `TestHook_EntityMap` | property (internal) | `NetworkEntityMap` test access |
| `TestHook_BehaviorRegistry` | property (internal) | `BehaviorRegistry` test access |
| `TestHook_NedReplication` | property (internal) | `INedReplicationModule?` test access |

#### `SimHostSubsystem : ISubsystem, IMapCameraProvider, IWindowRegistrar, IGizmoControllable`

Thin adapter that embeds `SimHostApp` for use inside `ClusterRunner`.

| Member | Kind | Description |
|---|---|---|
| `Name` | property | `"SimHost"` |
| `TitleBarColor` | property | Dark red `(0.40, 0.08, 0.08, 1)` |
| `World` | property | `EntityRepository?` — null before init |
| `GetCameraView()` | method | Delegates to inner `MapCamera` |
| `ApplyCameraView(MapCameraView)` | method | Delegates to inner `MapCamera` |
| `GetMapCamera()` | method | `MapCamera?` (backward-compat) |
| `GizmoController` | property | `GizmoExecutionController?` |
| `TestHook_EntityMap` | property (internal) | `NetworkEntityMap` |
| `TestHook_BehaviorRegistry` | property (internal) | `BehaviorRegistry` |
| `App` | property (internal) | `SimHostApp` (throws before init) |

Constructors:

```csharp
SimHostSubsystem()
SimHostSubsystem(INetworkFactory networkFactory)
```

#### `SimHostNodeBootstrapper : SharedApplicationBootstrapper`

Concrete bootstrapper that drives the 7-phase initialization.

| Member | Kind | Description |
|---|---|---|
| `CoreLogicPack` | property | `SimHostCoreLogicPack?` (valid after BootstrapNode) |
| `SlaveTranslator` | property | `ISlaveOrchestrationTranslator?` (valid after BootstrapNode) |
| `CheckpointWorker` | property | `CheckpointIOWorker?` (valid after BootstrapNode) |
| `PhysicsModule` | property | `PhysicsToolkitModule?` (valid after BootstrapNode) |
| `PerceptionModule` | property | `CognitiveSpatialModule?` (valid after BootstrapNode) |
| `BehaviorRegistry` | property | `BehaviorRegistry?` (valid after BootstrapNode) |
| `RoadNetwork` | property | `RoadNetworkBlob?` (valid after BootstrapNode) |
| `ApplicationSystemsRegistrar` | property | `Action<HrotNodeContext>?` callback for Phase 6 |

Constructor:

```csharp
SimHostNodeBootstrapper(
    INetworkFactory? networkFactory,
    NodeRole role,
    string localTempRoot,
    IDiagnosticEventHistoryService? eventHistoryService,
    HrotNodeConfig hrotConfig,
    string? roadNetworkBlobPath = null,
    float simulationRateHz = 20.0f)
```

#### `NodeBootstrapper`

Orchestration composition root — builds `ClusterSlave` with role-appropriate handlers.

| Member | Kind | Description |
|---|---|---|
| `SlaveTranslator` | property | `ISlaveOrchestrationTranslator?` (set after BuildOrchestration) |
| `RecordReplayController` | property | `EcsRecordReplayController?` (set after BuildOrchestration) |
| `BuildOrchestration(...)` | method | Creates `ClusterSlave` and wires handlers |

#### `NodeConfiguration` (record)

JSON-serializable deployment configuration.

| Property | Type | Default | Description |
|---|---|---|---|
| `CycloneDdsConfigPath` | string | `""` | Path to CycloneDDS XML config |
| `DdsDomainId` | uint | 42 | DDS domain ID |
| `RoadNetworkBlobPath` | string | `""` | Road network blob file path |
| `BehaviorRegistryPath` | string | `""` | Behavior registry JSON path |
| `EntityTemplatePath` | string | `""` | Entity template database path |
| `SimulationRateHz` | int | 60 | Simulation loop rate |
| `GeodeticOrigin` | `GeodeticOriginConfig` | Tel Aviv | WGS-84 flat-earth origin |
| `LocalTempRoot` | string | `C:\FDP_Temp` | Staging area root for checkpoints/scenarios |

Static methods: `LoadFrom(string path)`, `Parse(string json)`, `ApplyEnvironment()`.

#### `GeodeticOriginConfig` (record)

| Property | Type | Default |
|---|---|---|
| `Latitude` | double | 32.0853 |
| `Longitude` | double | 34.7818 |
| `Altitude` | double | 10.0 |

---

### Module Types

#### `SimHostCoreLogicPack : IEcsModule`

| Member | Description |
|---|---|
| `Name` | `"SimHostCoreLogicPack"` |
| `Policy` | `ExecutionPolicy.Synchronous()` |
| `TrajectoryPool` | `TrajectoryPoolManager` (forwarded from `GroundKinematicsModule`) |
| `FormationTemplates` | `FormationTemplateManager` (forwarded from `GroundKinematicsModule`) |
| `InputSystems` | `IReadOnlyList<IEcsModuleSystem>` — systems for `TogglableInputGroup` |
| `SimulationSystems` | `IReadOnlyList<IEcsModuleSystem>` — systems for `TogglableSimulationGroup` |
| `PostSimulationSystems` | `IReadOnlyList<IEcsModuleSystem>` — systems for `TogglablePostSimulationGroup` |

Constructor:

```csharp
SimHostCoreLogicPack(
    NetworkEntityMap entityMap,
    RoadNetworkBlob roadNetwork = default,
    TrajectoryPoolManager? trajectoryPool = null,
    FormationTemplateManager? formationTemplateManager = null)
```

#### `CombatModule`

| Member | Description |
|---|---|
| `InputSystems` | `FireProcessingSystem`, `RaycastSolverSystem`, `HitResolutionSystem` |
| `PostSimulationSystems` | `BallisticsSystem` |

#### `CognitiveSpatialModule : IEcsModule, IDisposable`

Background perception module running at 10 Hz against a SoD snapshot.

| Member | Description |
|---|---|
| `Name` | `"CognitiveSpatial"` |
| `Policy` | `ExecutionPolicy.SlowBackground(10)` |
| `ScopedBus` | `FdpEventBus` — scoped bus for `LosCheckRequestEvent`, `TargetVisibleEvent`, `SensorTrackStateEvent` |

Constructor:

```csharp
CognitiveSpatialModule(
    EntityRepository liveWorld,
    Func<ISimulationView, Entity, float>? colliderRadiusReader = null)
```

#### `EqsModule : IEcsModule`

EQS solver at 10 Hz on a background thread.

| Member | Description |
|---|---|
| `Name` | `"Eqs"` |
| `Policy` | `ExecutionPolicy.SlowBackground(10)` |

#### `EyesAndMuscleModule : IEcsModule`

Async SoD proof-of-concept module at 60 Hz.

| Member | Description |
|---|---|
| `Name` | `"EyesAndMuscle"` |
| `Policy` | `ExecutionPolicy.SlowBackground(60)` |
| `EyesTicks` | Count of all Tick calls |
| `MuscleTicks` | Count of Tick calls when `MuscleGround` is active |
| `LastTickThreadId` | Thread ID of the last Tick call |

Constructor: `EyesAndMuscleModule(NodeRole role)`

#### `SimHostModule : IEcsModule`

Hosts `NetworkSpawningSystem`.

| Member | Description |
|---|---|
| `Name` | `"SimHost"` |
| `Policy` | `ExecutionPolicy.Synchronous()` |

Constructor: `SimHostModule(NetworkSpawningSystem spawnSystem)`

#### `SimPresentationModule : IMapCameraProvider`

| Member | Description |
|---|---|
| `RenderSystem` | `SimMapRenderSystem` |
| `GetCameraView()` | Returns `MapCameraView?` from the canvas camera |
| `ApplyCameraView(MapCameraView)` | Applies a view to the canvas camera |
| `RegisterSystems(ISystemRegistry)` | Registers `SimMapRenderSystem` |

---

### Orchestration Types

#### `EcsRecordReplayController : IClusterOpHandler, IRecordReplayController`

| Member | Description |
|---|---|
| `ActiveRecordingModule` | `RecordingModule?` installed by `PrepareRecordingAsync` |
| `ActiveReplayModule` | `ReplayModule?` installed by `PrepareReplayAsync` |
| `IsReplayActive` | bool |
| `ActiveMaxNetworkId` | long |
| `ActiveRecordingStartWallTicks` | long |
| `ActiveReplayDurationSeconds` | float |

Constructor:

```csharp
EcsRecordReplayController(
    ModuleHostKernel kernel,
    int nodeId,
    EntityRepository repo,
    Action? afterSeek = null)
```

#### `HrotScenarioLoadHandler : ITickableClusterStateHandler`

| Member | Description |
|---|---|
| `PrepareCallCountForTest` | int — integration-test assertion counter |

Handles `PrepareLive` / `OperatingLive` transitions via genesis pipeline.

Constructor:

```csharp
HrotScenarioLoadHandler(
    ScenarioSerializer serializer,
    IScenarioLoader scenarioLoader,
    IZoneManagerService zoneService,
    IScenarioEntityExtractor extractor,
    ScenarioEntityCreationRequestSource source,
    INetworkIdAllocator idAllocator,
    EntityRepository? world = null,
    IRecordReplayController? controller = null,
    string storageDirectory = @"C:\FDP_Temp")
```

#### `TkbLoadClusterStateHandler : IClusterStateHandler`

Loads TKB ZIP artifacts from local staging before scenario deserialization.

| Member | Description |
|---|---|
| `CanHandle(NodeOpType)` | Returns true for `PrepareLive` and `PrepareEdit` |

---

### System Types

#### `GenesisMaterializationSystem : IEcsModuleSystem`

Phase: `SystemPhase.Input`

Resolves Intent-DTO managed components to structural ECS components once all
referenced entities are alive in `NetworkEntityMap`. Handles:
- `InitialPassengersIntent` -> `PassengerBuffer`
- `InitialVehicleIntent` -> `IsEmbarkedTag`
- Hierarchy intents, route intents, target intents, unit-subordinate intents

Constructor: `GenesisMaterializationSystem(NetworkEntityMap entityMap)`

#### `AreaQuerySolverSystem : IEcsModuleSystem`

Phase: `SystemPhase.Simulation`

Resolves `AreaQueryRequestEvent`s against the spatial hash grid and polygon areas.
Runs at 11 Hz inside `CognitiveSpatialModule` on a background thread.

Constructors:
```csharp
AreaQuerySolverSystem()                                          // reads grid from singleton
AreaQuerySolverSystem(SpatialHashGrid grid, EntityRepository liveWorld)
```

#### `AreaQueryResultMaterializationSystem : IEcsModuleSystem`

Phase: `SystemPhase.Input`

Materializes `AreaQueryResultEvent`s into `AreaQueryBatchData` ring buffer. Main-thread only.

#### `SimMapRenderSystem : IEcsModuleSystem`

Phase: `SystemPhase.Export`

Calls `MapCanvas.Draw()` when the active `ActivePerspective.Name` is `"Sim"`.

| Member | Description |
|---|---|
| `DrawCallCount` | int — for unit-test assertions |

Constructor: `SimMapRenderSystem(MapCanvas? canvas = null)`

#### `RouteTrajectorySyncSystem : IEcsModuleSystem`

Phase: `SystemPhase.BeforeSync`

Syncs `RoutePlan` version changes to `TrajectoryPoolManager`. Frees pool entries
when entities are destroyed (`DestructionOrder` events).

Constructor: `RouteTrajectorySyncSystem(TrajectoryPoolManager pool)`

#### `PersonalRouteAuthoringSystem : IEcsModuleSystem`

Phase: `SystemPhase.Input`

Processes `CmdAppendPersonalWaypoint` events. Creates or mutates child route entities
and defers `CmdFollowTrajectory` by one frame so `RouteTrajectorySyncSystem` can
compile the trajectory first.

---

### Component Registries

#### `SimHostComponentRegistry`

Static. `RegisterAll(EntityRepository)` — delegates to all sub-registries below and
additionally registers formation/spawn commands and diagnostic events.

#### `CombatComponentRegistry`

Registers: `PerceptionReceptor`, `TargetMemory`, `SensorContactList`, `WeaponState`,
`EntityInfo`, `BallisticProjectile`, `PhysicsCollider`, and all combat/perception events.

#### `CognitiveComponentRegistry`

Registers: `BehaviorState`, `SimTier`, locomotion/weapon/interaction channels,
`ActorCapabilityState`, BTree/HSM brain components, `MissionPlanQueue`,
`PassengerBuffer`, `IsEmbarkedTag`, `NavigationIntent`, AI trace buffers, and
cognitive command events.

#### `KinematicComponentRegistry`

Registers: `VehicleState`, `VehicleParams`, `NavState`, formation components,
`NavigationStatus`, `FrustrationTicks`, `UnitSubordinate`, `UnitRoster`.

#### `MuscleRoleComponentRegistry`

Registers: `KinematicComponentRegistry` + `NavigationIntent` + `WeaponFireNotification`
+ `DetonationNotification`.

#### `CombatComponentRegistry`

Registers combat and perception schema (see above).

#### `HierarchyComponentRegistry`

Registers `UnitRoster`, `UnitSubordinate`, and hierarchy command events.

#### `NavigationSolverComponentRegistry`

Initializes `PathfindingBatchData` and `AreaQueryBatchData` singletons with
`NativeArray` backing. Registers pathfinding and EQS request/result events.

---

### UI / Visualization Types

#### `SimHostVisualization : IDisposable`

Self-contained graphical layer. Lifecycle: `Initialize`, `Update`, `DrawWorld`, `DrawUI`, `Dispose`.

| Member | Description |
|---|---|
| `Selection` | `SimHostSelectionManager?` |
| `GetMapCamera()` | `MapCamera?` |

#### `SimHostSelectionManager`

Multi-entity selection tracker with primary-entity concept.

| Member | Description |
|---|---|
| `SelectedEntities` | `IReadOnlyCollection<Entity>` |
| `PrimarySelected` / `SelectedEntity` | `Entity?` |
| `HoveredEntity` | `Entity?` |
| `Count` | int |
| `SelectionChanged` | `event Action?` |
| `Set(Entity)` | Replaces selection |
| `Add(Entity)` | Adds to selection |
| `Clear()` | Clears all |
| `SetMultiple(IEnumerable<Entity>)` | Bulk selection |
| `Remove(Entity)` | Removes one |
| `Contains(Entity)` | bool |

#### `SimHostInspectorAdapter : IInspectorContext, ISelectionState`

Bridges `SimHostSelectionManager` to FDP framework inspector interfaces.

#### `SimHostScenarioManager`

GUI-driven scenario utilities — entity spawning via `SpawnEntityCommand` on the event bus.

Constructor:
```csharp
SimHostScenarioManager(
    EntityRepository repo,
    RoadNetworkBlob road,
    TrajectoryPoolManager traj,
    FormationTemplateManager formations,
    IEventBus? spawnBus = null,
    INetworkIdAllocator? idAllocator = null,
    int localNodeId = 0)
```

#### `SimHostTrajectoryLayer : IMapLayer`

Renders trajectory path and route waypoints for the currently selected entity.

Test hooks: `TestHook_SkipRaylibCalls`, `TestHook_LineDrawCount`, `TestHook_CircleDrawCount`.

#### `SimHostRoadLayer : IMapLayer`

Renders road-network nodes and segments using Raylib.

#### `SimHostPanelColors`

| Member | Value | Description |
|---|---|---|
| `TitleBg` | `(0.40, 0.08, 0.08, 1)` | Dark red (unfocused) |
| `TitleBgActive` | `(0.56, 0.12, 0.12, 1)` | Bright red (focused) |
| `Push()` | | Pushes both colors onto ImGui stack |
| `Pop()` | | Pops both colors |

#### `SimHostEventIds`

| Constant | Value | Description |
|---|---|---|
| `TogglePerspective` | 6001 | Request perspective switch (IG <-> Sim) |
| `MissionControlAck` | 6002 | Mission command acknowledgment |

---

### Gizmo Types

#### `SimHostEntityPresentationGizmo : IStatelessGizmo`

`[GizmoProjector(typeof(SimTransform), typeof(NetworkIdentity))]`

Emits `SpatialAnchor` + `SemanticShape` for every entity with `SimTransform` +
`NetworkIdentity`. Used by the IG gizmo renderer for 3-D entity display.

#### `EntityRotatorGizmo : IEntityStatefulGizmo`

Exclusive-focus interactive gizmo for rotating a `SimTransform`'s heading.

| Member | Description |
|---|---|
| `RequiresExclusiveFocus` | `true` |
| `WantsRawInput` | `true` |
| `IsFocused` | bool |
| `SetFocus(bool)` | Focus state setter |
| `UpdateAndDraw(float, IDebugDrawBuilder)` | Draws yellow heading arrow |
| `OnDragUpdate(Vector3)` | Recomputes heading from cursor position |
| `OnMouseEvent(...)` | Left-release commits; right-press cancels |

Constructor:
```csharp
EntityRotatorGizmo(ISimulationView view, Entity entity, Action onRemove)
```

#### `GizmoRegistrar` (partial, static)

`Register(GizmoRegistry, StatelessGizmoRegistry, GizmoSettingsRegistry)` — calls
source-generated `RegisterAll()` with all gizmos in the assembly.

---

### Diagnostic Types

#### `AiTraceContextMenu` (internal static)

`PublishToggle(ISimulationView, Entity, BehaviorDebugFlags)` — flips a single
`BehaviorDebugFlags` bit via a `PatchDebugStateCommand` managed event. Uses JSON
patch with `nameof`-derived property names for compile-time safety.

#### `AiDiagnosticsTkbTranslator : ITkbEntityTranslator`

TKB observer translator that auto-stamps `BTreeTraceWorkingMemory1024` or
`HsmTraceWorkingMemory1024` + `DebugState.EnableTraceBuffer` during entity genesis
when `GlobalDebugSettings.AutoEnableAiTracing` is set.

`GetConsumedDescriptors()` returns empty — does not claim any descriptor type.

---

### Configuration

#### `SimHostNetworkConstants`

| Constant | Value | Description |
|---|---|---|
| `LocalNodeId` | 1 | Owner node ID for spawned entities |

---

### Serializers

#### `HrotScenarioSerializerFactory`

`Build(BehaviorRegistry)` — creates a `ScenarioSerializer` using
`ScenarioSerializerBuilder(HrotSubsystemTypes.Scenario)` with all HROT translators
registered (see table below).

| Translator | Component | Notes |
|---|---|---|
| `MissionPlanTranslator` | `MissionPlanQueue` | Requires `BehaviorRegistry` |
| `TargetMemoryTranslator` | `TargetMemory` | |
| `PassengerBufferTranslator` | `PassengerBuffer` | |
| `VisHierarchyNodeTranslator` | vis-hierarchy node | |
| `IsEmbarkedTagTranslator` | `IsEmbarkedTag` | |
| `PersonalRouteRefTranslator` | `PersonalRouteRef` | |
| `UnitSubordinateTranslator` | `UnitSubordinate` | |
| `EditablePolylineTranslator` | `EditablePolyline` | |
| `BrainBlackboardTranslator` | `BrainBlackboard` | Serialize-only (Inject is no-op) |
| `Blackboard1024Translator` | `Blackboard1024` | |
| `BTreeTraceWorkingMemoryTranslator` | `BTreeTraceWorkingMemory1024` | |
| `HsmTraceWorkingMemoryTranslator` | `HsmTraceWorkingMemory1024` | |

---

## Dependencies

### Project References

| Project | Path | Purpose |
|---|---|---|
| `Hrot.Common` | `Hrot/Engine/Hrot.Common` | Data model, `NodeRole`, `HrotNodeConfig`, shared schema |
| `Fdp.Core` | `FDP/Engine/Fdp.Core` | ECS kernel, `EntityRepository`, `FdpEventBus`, core interfaces |
| `Fdp.Presentation` | `FDP/Engine/Fdp.Presentation` | `FdpApplication`, panels, adapters, window manager |
| `Hrot.Presentation` | `Hrot/Engine/Hrot.Presentation` | HROT-specific windows, facades |
| `Hrot.Network.NED` | `Hrot/Network/Hrot.Network.NED` | NED protocol, `EntityAttributeSchema`, network translators |
| `Fdp.Toolkits.Analyzers` | `FDP/Toolkits/Fdp.Toolkits.Analyzers` | `[GizmoProjector]` source generator (Analyzer only, no output assembly) |

### NuGet Packages

| Package | Version | Purpose |
|---|---|---|
| `Raylib-cs` | 7.0.2 | 2-D/3-D rendering, window management |
| `rlImGui-cs` | 3.2.0 | ImGui integration for Raylib |

### Indirect Dependencies (via Fdp.Core / Hrot.Common)

The project transitively pulls in all FDP toolkits consumed at runtime:

- `Fdp.ModuleHost` — `ModuleHostKernel`, module scheduling, `TogglableSimulationGroup`
- `Fdp.Toolkit.Behavior` — `BehaviorRegistry`, `BrainBTreeState`, `BrainHsm128`
- `Fdp.Toolkit.Combat` — `FireProcessingSystem`, `RaycastSolverSystem`, `HitResolutionSystem`, `BallisticsSystem`
- `Fdp.Toolkit.Navigation` — `NavigationIntent`, `NavigationStatus`, `NavigationIntentBridgeSystem`
- `Fdp.Toolkit.Physics` — `PhysicsToolkitModule`, `BallisticProjectile`, `PhysicsCollider`
- `Fdp.Toolkit.Replication` — `NetworkEntityMap`, `NetworkSpawningSystem`, DDS replication
- `Fdp.Toolkit.Scenario` — `ScenarioSerializer`, `ScenarioSerializerBuilder`
- `Fdp.Toolkit.Tkb` — `ITkbDatabase`, `TkbDatabase`
- `Fdp.Toolkit.Vis2D` — `MapCanvas`, `MapCamera`, `IMapLayer`
- `Fdp.Toolkit.Orchestration` — `ClusterSlave`, `IClusterOpHandler`, `ClusterSlave`
- `Fdp.Toolkit.Diagnostics.Gizmos` — `GizmoRegistry`, `StatelessGizmoRegistry`, `DataDrivenGizmoSystem`
- `Fdp.Network.Cyclone` — CycloneDDS transport modules and services
- `CarKinem.*` — `RoadNetworkBlob`, `GroundKinematicsModule`, `TrajectoryPoolManager`, `FormationTemplateManager`

### InternalsVisibleTo

| Assembly | Purpose |
|---|---|
| `Hrot.SimHost.Tests` | Unit tests |
| `Hrot.SimHost.Integration.Tests` | Integration tests |
| `Hrot.ClusterRunner.Tests` | Cluster runner tests |
| `Hrot.ClusterRunner.Integration.Tests` | Cluster runner integration tests |

---

## Usage Examples

### Example 1 — Headless SimHost in a test

Instantiate and run SimHost without a graphics window. This is the pattern used by
integration tests to start the full simulation kernel on a background thread.

```csharp
using Hrot.SimHost;
using Hrot.Common.Infrastructure;
using Fdp.Toolkit.Time;

// 1. Build configuration
var config = new NodeConfiguration
{
    DdsDomainId      = 42,
    SimulationRateHz = 20,
    LocalTempRoot    = @"C:\FDP_Temp",
};

// 2. Create and initialize the subsystem (headless)
var subsystem = new SimHostSubsystem();
subsystem.Initialize(headless: true, nodeId: 1, config: config);

// 3. Start the background simulation loop
subsystem.Start();

// 4. Access the ECS world
EntityRepository? world = subsystem.World;
if (world != null)
{
    // Query all entities with vehicle state
    var query = world.Query()
        .With<CarKinem.Core.VehicleState>()
        .Build();

    foreach (var entity in query)
    {
        ref readonly var vs = ref world.GetComponentRO<CarKinem.Core.VehicleState>(entity);
        Console.WriteLine($"Vehicle speed: {vs.Speed}");
    }
}

// 5. Stop
subsystem.Stop();
```

### Example 2 — Bootstrapping the simulation core programmatically

Use `SimHostNodeBootstrapper` directly to compose the kernel topology
when the composition root needs fine-grained control (e.g. integration test harnesses).

```csharp
using Hrot.SimHost;
using Hrot.Common;
using Hrot.Common.Infrastructure;

var hrotConfig = new HrotNodeConfig
{
    SubsystemName = "SimHost",
    NodeId        = 1,
};

var bootstrapper = new SimHostNodeBootstrapper(
    networkFactory:   null,               // no DDS in this test
    role:             NodeRole.MuscleGround | NodeRole.Perception,
    localTempRoot:    @"C:\FDP_Temp",
    eventHistoryService: null,
    hrotConfig:       hrotConfig,
    simulationRateHz: 20.0f);

// Optional: register extra systems before Initialize
bootstrapper.ApplicationSystemsRegistrar = ctx =>
{
    // Register additional diagnostics systems here
};

// Run 7-phase initialization
bootstrapper.BootstrapNode(hrotConfig, bootstrapper.GetBehaviorRegistry());

// CoreLogicPack is now valid
SimHostCoreLogicPack pack = bootstrapper.CoreLogicPack!;
Console.WriteLine($"Road network segments: {pack.TrajectoryPool != null}");
```

### Example 3 — Spawning a vehicle via event bus

Vehicles are spawned by publishing `SpawnEntityCommand` onto the ECS event bus.
`NetworkSpawningSystem` picks up the command and creates the entity with full
network component set.

```csharp
using Hrot.SimHost.UI;
using Fdp.Toolkit.NetworkSpawning.Events;
using System.Numerics;

// Given an initialized SimHostSubsystem
EntityRepository repo = subsystem.World!;
INetworkIdAllocator idAllocator = /* ... */;

var manager = new SimHostScenarioManager(
    repo:        repo,
    road:        bootstrapper.RoadNetwork ?? default,
    traj:        bootstrapper.CoreLogicPack!.TrajectoryPool,
    formations:  bootstrapper.CoreLogicPack!.FormationTemplates,
    idAllocator: idAllocator,
    localNodeId: Hrot.SimHost.Configuration.SimHostNetworkConstants.LocalNodeId);

// Spawn a single personal car at map origin
manager.SpawnVehicle(
    vehicleClass: CarKinem.Core.VehicleClass.PersonalCar,
    position:     new Vector2(100f, 200f));
```

### Example 4 — Toggling AI trace from a context menu

Toggle BTree trace logging on a selected entity via the `AiTraceContextMenu` helper.
This publishes a JSON patch that is processed by `PatchDebugStateSystem` on the next tick.

```csharp
using Hrot.SimHost.Diagnostics;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Core;

// view must be EntityRepository (SimHost ECS views always are)
Entity selectedEntity = selection.PrimarySelected ?? Entity.Null;

// Toggle the EnableTraceBuffer flag (flip current value)
AiTraceContextMenu.PublishToggle(
    view:   repo,
    target: selectedEntity,
    flag:   BehaviorDebugFlags.EnableTraceBuffer);
```

### Example 5 -- Scenario load flow (orchestration)

The scenario loading flow is driven by the `ClusterSlave` state machine. During
`PrepareLive`, `TkbLoadClusterStateHandler` runs first, then `HrotScenarioLoadHandler`
extracts entity creation requests and enqueues them for the genesis pipeline.

```csharp
// This is handled automatically by NodeBootstrapper.BuildOrchestration.
// The sequence during a PrepareLive transition is:

// 1. TkbLoadClusterStateHandler.PrepareAsync:
//    - Reads TkbName from locally staged scenario header
//    - Loads matching TKB ZIP from {LocalTempRoot}/TKB/{TkbName}.zip
//    - Populates ITkbDatabase

// 2. HrotScenarioLoadHandler.PrepareAsync:
//    - Deserializes scenario JSON using ScenarioSerializer
//    - Extracts EntityCreationRequest list via IScenarioEntityExtractor

// 3. HrotScenarioLoadHandler.Commit:
//    - Enqueues requests into ScenarioEntityCreationRequestSource
//    - Applies zone definitions synchronously

// 4. HrotScenarioLoadHandler.TickAsync (ITickableClusterStateHandler):
//    - Polls each frame: source drained? all Constructing entities promoted?
//    - all GenesisMaterializationSystem intents removed?
//    - Only then allows transition to OperatingLive
```

---

## Best Practices

### 1. Always use `NodeRole.MuscleGround | NodeRole.Perception` for production

SimHost is the Muscle tier. Never assign `NodeRole.Brain` to a SimHost instance;
that role belongs exclusively to CGF. Using the wrong role will cause missing
module registrations and undefined behavior in the Brain/Muscle data handshake.

### 2. Register components before calling any ECS queries

`SimHostComponentRegistry.RegisterAll(world)` must be called before any query is
executed. The ordering within `RegisterAll` matters: call `HrotSharedComponentRegistry`
first, then the role-specific sub-registries. Do not duplicate registrations across
registries — components registered by `HrotSharedComponentRegistry` (e.g. `SimTransform`,
health) must not appear again in `CombatComponentRegistry` or `KinematicComponentRegistry`.

### 3. Spawn entities through the event bus, not directly

All entity creation must go through `SpawnEntityCommand` on the event bus processed
by `NetworkSpawningSystem`. Direct calls to `EntityRepository.Create()` bypass
network ID allocation, NED replication, and TKB blueprint application. The only
exception is replay / checkpoint restoration handled by `EcsRecordReplayController`.

### 4. Navigation is CQRS

The Brain (CGF) writes `NavigationIntent` to command movement. The Muscle (SimHost)
reads `NavigationIntent` and executes it, writing back `NavigationStatus`.
Never write `NavigationStatus` from the Brain tier or `NavigationIntent` from the
Muscle tier's own logic systems.

### 5. Background modules use SoD snapshots

`CognitiveSpatialModule` and `EqsModule` run on background threads against a
Separation-of-Duties (SoD) snapshot, not the live world. Do not hold references to
live ECS data across SoD tick boundaries. Results are published back to the main thread
via `IEntityCommandBuffer.PublishEvent`.

### 6. Scenario serializers are for persistence, not runtime state

`BrainBlackboardTranslator.Inject` is intentionally a no-op because `BrainBlackboard`
is transient execution state (`DataPolicy.NoSave`). Do not attempt to restore
blackboard state from a scenario file. Add `DataPolicy.NoSave` annotations to any
new translator that serializes transient runtime state.

### 7. Config file over code for deployment changes

Use `NodeConfiguration` loaded from `config.json` to change DDS domain, road network
path, simulation rate, and geodetic origin. Do not hard-code these values in C#. The
`ApplyEnvironment()` method will set `CYCLONEDDS_URI` automatically from
`CycloneDdsConfigPath` unless the environment variable is already set (external override wins).

### 8. TKB loading precedes scenario deserialization

`TkbLoadClusterStateHandler` is registered before `HrotScenarioLoadHandler` in
`NodeBootstrapper.BuildOrchestration`. Do not reorder these. The TKB must be loaded
before entity blueprints can be applied during the genesis pipeline.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Hrot.Common` | Shared data model, `NodeRole`, `HrotNodeConfig`, component schema base |
| `Hrot.IG` | Image Generator — consumes entity state from SimHost over DDS for visualization |
| `Hrot.CGF` | Computer-Generated Forces — the Brain tier; sends `NavigationIntent`, `WeaponFireIntent`, mission plans |
| `Hrot.Orchestrator` | Cluster orchestration master; sends `NodeOpCommand` to `ClusterSlave` in SimHost |
| `Hrot.ClusterRunner` | Multi-subsystem host process that embeds `SimHostSubsystem`, `IgSubsystem`, and `CgfSubsystem` |
| `Hrot.Network.NED` | NED protocol definitions and network translators used by SimHost's replication module |
| `Hrot.Presentation` | Shared HROT UI windows and facades (used by `SimHostVisualization`) |
| `Fdp.Core` | ECS kernel, event bus, scheduling abstractions |
| `Fdp.ModuleHost` | `ModuleHostKernel`, `TogglableSimulationGroup`, diagnostic panels |
| `Fdp.Toolkit.Behavior` | `BehaviorRegistry`, BTree/HSM runtime, `ActionDispatchModule` |
| `Fdp.Toolkit.Combat` | Fire processing, raycast, hit resolution, ballistics |
| `Fdp.Toolkit.CarKinem` | Ground kinematics, road network, trajectory pool, formation |
| `Fdp.Toolkit.Orchestration` | `ClusterSlave`, `IClusterOpHandler`, state-handler contracts |
| `Fdp.Toolkit.Replication` | Network entity map, NED replication module, spawning system |
| `Fdp.Toolkit.Scenario` | Scenario serializer builder and translator contracts |
| `Fdp.Toolkit.Vis2D` | `MapCanvas`, `IMapLayer`, `MapCamera` |
| `Fdp.Toolkit.Diagnostics.Gizmos` | Gizmo registry, stateless gizmo contracts, data-driven gizmo system |
| `Hrot.SimHost.Tests` | Unit tests for SimHost (has `InternalsVisibleTo` access) |
| `Hrot.SimHost.Integration.Tests` | Integration tests including full cluster lifecycle tests |
