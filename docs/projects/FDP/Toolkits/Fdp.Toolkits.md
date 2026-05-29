# Fdp.Toolkits

**Project file**: `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj`
**Root namespace**: `Fdp.Toolkit`
**Target framework**: net8.0
**Date**: 2026-05-23

---

## README Validation

**Status: Missing** -- No `README.md` exists in the project folder. This document serves as the
primary architectural reference.

---

## Executive Overview

`Fdp.Toolkits` is the principal simulation-toolkit library of the FDP framework. It is the widest
single assembly in the FDP codebase: a deliberately consolidated monolith that replaces what were
previously many smaller per-domain toolkit projects (Behavior, CarKinem, Combat, Replication, etc.).
Consolidation was done to simplify dependency graphs and avoid circular cross-assembly references
between subsystems that are tightly coupled at runtime.

The library is layered on top of:

- **`Fdp.Core`** -- low-level ECS kernel (`EntityRepository`, `ComponentTypeRegistry`, event bus).
- **`Fdp.ModuleHost`** -- module lifecycle abstraction (`IEcsModule`, `IEcsModuleSystem`,
  `ExecutionPolicy`, `ISystemRegistry`).

It provides nineteen functional domains (top-level folders), each self-contained but sharing the
ECS primitives from the layers below:

| Domain | Purpose |
|---|---|
| `Behavior` | AI brain: BTree / HSM dispatch, mission control, cognitive runtime |
| `Blueprints` | Compiled entity-logic scripts: tick, event handlers, blackboard state |
| `CarKinem` | Ground-vehicle kinematics: bicycle model, pure-pursuit, formation, road graph |
| `Combat` | Ballistics, damage, weapon fire pipeline |
| `Commands` | DDS-level command client for external operator tooling |
| `DER` | Distributed Entity Repository -- network presence bookkeeping |
| `Diagnostics` | In-sim gizmo overlay system + entity-state extraction |
| `Geographic` | WGS-84 transforms, geodetic smoothing, terrain-query pipeline |
| `Lifecycle` | Distributed construction/destruction acknowledgement protocol |
| `Navigation` | Navigation v2: multi-modal path planning, corridor following, fake/engine-backed providers, BTree action nodes -- see [Fdp.Toolkits.Navigation.md](Fdp.Toolkits.Navigation.md) |
| `NetworkSpawning` | Entity creation/deletion driven by network messages |
| `Orchestration` | Cluster state machine: slave, transition planner, reference handlers |
| `Perception` | Sensor simulation: spatial broadphase, LOS, audio, threat evaluation |
| `Physics` | Raycast batching, hit resolution, 2-D intersection math |
| `Replay` | Recording and playback of simulation episodes |
| `ReplayBrowser` | Offline search, diff, and JSON export of recordings |
| `Runner` | Subsystem lifecycle host: ISubsystem, SubsystemOrchestrator |
| `Scenario` | JSON save/load of EntityRepository snapshots |
| `Serialization` | JSON formatting utilities |
| `Spatial` | Area-query batch helpers; **EQS** (Environment Query System v1.3) -- entity, positional, and path-aware standing queries |
| `Time` | Distributed time synchronisation: master/slave lockstep, NTP-style sync |
| `Utility` | **Utility AI** decision-scoring system -- consideration curves, candidate rankers, posture selector, group assignment; consumed by BTree / HSM / Blueprint |
| `Tkb` | Transient Knowledge Base: entity template database, JSON parser, VFS |
| `Vis2D` | 2-D map camera view utilities |

---

## Architecture

### Layering Model

```
+-----------------------------------------------------------------------+
|                         Applications / Subsystems                     |
|        (Hrot.IG, Hrot.SimHost, Fdp.Examples.*, etc.)                 |
+-----------------------------------------------------------------------+
              |                           |
              v                           v
+-------------------------------+   +------------------------------+
|        Fdp.Toolkits           |   |   Fdp.Toolkit.DER (ext.)     |
|  (this assembly)              |   |   Fdp.Toolkit.Behavior.Tests  |
|                               |   |   FDP.Toolkit.DER.Tests, etc  |
|  +-------------------------+  |   +------------------------------+
|  | Domain Modules (19)     |  |
|  | Behavior, Blueprints,   |  |
|  | CarKinem, Combat, ...   |  |
|  +-------------------------+  |
+-------+---------------+-------+
        |               |
        v               v
+---------------+  +------------------+
|  Fdp.Core     |  | Fdp.ModuleHost   |
|  (ECS kernel) |  | (module lifecycle|
+---------------+  +------------------+
        |
        v
+-----------------------------+
| External deps               |
| FastBTree, FastHSM,         |
| CycloneDDS.NET, StructEdit  |
+-----------------------------+
```

### Domain-Internal Structure Pattern

Every domain follows a consistent internal layout:

```
Domain/
  Components/    -- ECS component structs (unmanaged or blittable)
  Events/        -- Event types published to FdpEventBus
  Systems/       -- IEcsModuleSystem implementations (Execute per frame)
  Modules/       -- IEcsModule compositions (register sets of systems)
  Executors/     -- Behavior action executors (IActionExecutor)
  Translators/   -- TKB -> ECS component mappers (ITkbEntityTranslator)
  BTreeNodes/    -- FastBTree action/condition node implementations
```

### ECS System Execution Pipeline

Systems are ordered within each module and annotated with `[UpdateInPhase]`:

```
+------------------+     +------------------+     +------------------+
|  SystemPhase     |     |  SystemPhase      |     |  SystemPhase     |
|  BeforeSync      |---->|  Simulation       |---->|  PostSimulation  |
|  (Replication    |     |  (AI, Combat,     |     |  (CarKinem,      |
|   ingress)       |     |   Perception)     |     |   Vis2D)         |
+------------------+     +------------------+     +------------------+
         |                         |                        |
         v                         v                        v
+------------------+     +------------------+     +------------------+
|  GhostCreation   |     |  BTreeTickSystem |     | CarKinematics    |
|  OwnershipIngress|     |  HsmTickSystem   |     | SpatialHash      |
|  NetworkGateway  |     |  ChannelArb.     |     | FormationTarget  |
+------------------+     +------------------+     +------------------+
```

### Distributed Cluster Model

```
+---------------------------+          +---------------------------+
|   Master Node             |          |   Slave Node (N)          |
|                           |          |                           |
|  ClusterMaster (Hrot)     |--[DDS]-->|  ClusterSlave             |
|  TransitionPlanner        |          |  IClusterStateHandler[]   |
|  TransitionGraphBuilder   |<--[DDS]--|  ReferenceXxxHandler      |
|  OrchestrationEventReg.   |          |  (ScenarioLoad, Archive,  |
|                           |          |   Checkpoint, etc.)       |
+---------------------------+          +---------------------------+
         |                                        |
         v                                        v
+------------------+                   +------------------+
| MasterSyncCtrl   |---[FrameOrder]--->| SlaveSyncCtrl    |
| (TimeNetworkMod) |<--[FrameAck]------| (TimeNetworkMod) |
+------------------+                   +------------------+
```

---

## Source Structure

### Namespace: `Fdp.Toolkit.Behavior`

**File**: `Behavior/BehaviorRegistry.cs`
- `BehaviorDefinition` -- immutable descriptor for a named AI behaviour (BTree or HSM)
- `BehaviorRegistry` -- maps behavior names/hashes to `BehaviorDefinition` entries
- `ParseParamsDelegate` -- unsafe delegate: JSON -> behavior blackboard inline memory
- `BlueprintBTreeActionDelegate` -- unsafe delegate for BTree action thunks
- `BlueprintBTreeConditionDelegate` -- unsafe delegate for BTree condition thunks

**File**: `Behavior/BTreeContext.cs`
- `BTreeContext` -- stack-allocated context struct passed through all BTree tick calls

**File**: `Behavior/BehaviorIds.cs` / `BehaviorConstants.cs`
- Well-known behavior tier constants (`BrainTierBTree`, `BrainTierHsm`)
- Stable integer behavior IDs

**File**: `Behavior/AiHotReloadCoordinator.cs`
- `AiHotReloadCoordinator` -- orchestrates hot-reload of behavior assets at runtime

**File**: `Behavior/ScenarioBehaviorRemapper.cs`
- `ScenarioBehaviorRemapper` -- remaps behavior GUIDs when loading saved scenarios

**File**: `Behavior/BehaviorParamRemapperCompiler.cs`
- `BehaviorParamRemapperCompiler` -- compiles parameter-remap rules for behavior assignment

#### Components (`Behavior/Components/`)
- `BehaviorState` -- active behavior name, brain tier, instance ID
- `BrainBlackboard` -- inline byte array carrying behavior parameters
- `BrainBTreeState` -- FastBTree working state struct (per-entity BTree execution cursor)
- `BrainHsmState` -- FastHSM working state struct
- `ChannelComponents` -- locomotion, weapon, interaction channel structs
- `MissionComponents` -- mission plan components
- `DomainMissionPlan` -- serialised mission plan attached to an entity

#### Events (`Behavior/Events/`)
- `AssignBehaviorEvent` / `AssignBehaviorHashEvent` -- assign a behavior by name or hash
- `ClearBehaviorEvent` -- terminate current behavior
- `BehaviorFinishedEvent` -- terminal BTree/HSM result notification
- `AssignTacticalIntentEvent` -- higher-level mission intent assignment
- `CognitiveInterruptEvent` / `CognitiveInterruptType` -- priority cognitive interrupt
- `EmbarkEntityCommand` / `DisembarkEntityCommand` -- boarding/unboarding commands

#### Diagnostics (`Behavior/Diagnostics/`)
- `BehaviorDebugFlags` -- per-entity debug flag component
- `BTreeTraceRecord` / `BTreeTraceWorkingMemory1024` -- ring-buffer BTree trace storage
- `HsmTraceWorkingMemory1024` -- ring-buffer HSM trace storage
- `DebugState` / `DebugStatePatchCompiler` / `DebugStatePatchSystem`
- `IBehaviorTraceLogEmitter` -- abstract trace sink interface
- `TraceBufferLifecycleSystem` -- allocates/frees trace buffers on entity lifecycle events

#### Executors (`Behavior/Executors/`)
- `IActionExecutor` -- interface for behavior action implementations
- `EmbarkExecutor` / `EjectPassengersExecutor` / `OpenDoorExecutor`

#### Systems (`Behavior/Systems/`)
- `BTreeTickSystem` -- steps FastBTree for all BTree-tier entities; zero alloc per tick
- `HsmTickSystem` -- steps FastHSM for all HSM-tier entities
- `BehaviorIngressSystem` -- processes `AssignBehaviorEvent` and `AssignBehaviorHashEvent`
- `ChannelArbitrationSystem` -- resolves competing locomotion/weapon channel requests
- `CognitiveInterruptSystem` -- handles priority interrupts; replaces active behavior
- `CognitiveCleanupSystem` -- resets brain state on behavior termination
- `DispatcherSystemBase` -- base class for action-dispatch systems
- `LocomotionDispatcherSystem` / `WeaponDispatcherSystem` / `InteractionDispatcherSystem`
- `MissionDirectorSystem` -- converts mission plans to behavior assignments

#### Modules (`Behavior/Modules/`)
- `CognitiveRuntimeModule` -- registers all tick, arbitration, and interrupt systems
- `ActionDispatchModule` -- registers all dispatcher systems
- `BehaviorDiagnosticsModule` -- registers trace-buffer lifecycle systems
- `MissionControlModule` -- registers `MissionDirectorSystem`

---

### Namespace: `Fdp.Toolkit.Blueprints`

**File**: `Blueprints/BlueprintDefinition.cs`
- `BlueprintDefinition` (sealed record) -- immutable compiled blueprint: name, kind, state size,
  tick/init delegates, event handlers, field descriptors

**File**: `Blueprints/BlueprintRegistry.cs`
- `BlueprintRegistry` -- atomic staging+commit snapshot store; lock-free reads via reference swap

**File**: `Blueprints/BlueprintIdHash.cs`
- `BlueprintIdHash` -- stable 32-bit hash computation for blueprint names

**File**: `Blueprints/BlueprintDispatchKind.cs`
- `BlueprintDispatchKind` -- enum: `Library`, `Instance`, `AiPrimitive`

**File**: `Blueprints/BlueprintDelegates.cs`
- `InitDefaultDelegate` / `TickDelegate` / `EventHandlerDelegate` -- function pointer signatures

**File**: `Blueprints/BlueprintStateView.cs`
- `BlueprintStateView` -- typed accessor over the raw blackboard byte array for a given blueprint

**File**: `Blueprints/BlueprintLatentCursor.cs`
- `BlueprintLatentCursor` -- tracks async operation position within a blueprint tick

**File**: `Blueprints/BlueprintFieldDescriptor.cs`
- `BlueprintFieldDescriptor` -- field name, byte offset, and CLR type for inspector/debugger

**File**: `Blueprints/BlackboardTier.cs`
- `BlackboardTier` -- enum selecting which fixed-size blackboard component to use

**File**: `Blueprints/CompilerMode.cs`
- `CompilerMode` -- enum: `Strict`, `Permissive` for blueprint compilation

#### Partitioning (`Blueprints/Partitioning/`)
- `BlueprintBlackboardHeader` -- first 16 bytes of a blackboard component: blueprint ID, tick counter
- `BlueprintBlackboardPartitions` -- layout of variable vs. fixed partitions within the blackboard
- `BlueprintFreeBlockHeader` -- free-list node for partial-blackboard compaction
- `BlueprintSlotEntry` -- slot descriptor inside a partitioned blackboard

#### Components (`Blueprints/Components/`)
- `BlueprintBlackboard1024` -- fixed-size 1024-byte blackboard component
- `BlueprintBlackboard4096` -- fixed-size 4096-byte blackboard component
- `BlueprintBlackboard16384` -- fixed-size 16384-byte blackboard component

#### Catalogs (`Blueprints/Catalogs/`)
- `ChannelCommandCatalog` -- registry of channel-command type IDs exposed to blueprints
- `EngineEventCatalog` -- registry of engine event types accessible to blueprints
- `WaitPrimitiveCatalog` -- registry of latent wait primitive implementations

#### Systems (`Blueprints/Systems/`)
- `BlueprintTickSystem` -- calls `Tick` delegate for every entity with a live blueprint
- `BlueprintMaintenanceSystem` -- handles hot-reload, garbage-collects stale blackboards
- `IReloadLogSink` -- interface for hot-reload diagnostic output

#### Attributes (`Blueprints/Attributes/`)
- `BlueprintRegistrarAttribute` -- marks a class as a blueprint registrar (source-gen target)
- `BlueprintExposedEventAttribute` -- marks an event type as accessible from blueprint scripts
- `BlueprintExposedChannelCommandAttribute` -- marks a channel command as accessible from blueprints

---

### Namespace: `CarKinem` / `CarKinem.*`

**File**: `CarKinem/Core/VehicleState.cs` / `VehicleParams.cs`
- `VehicleState` -- position, heading, speed, steering angle (ECS component)
- `VehicleParams` -- physics parameters: wheelbase, max speed, max steer angle
- `VehicleClass` -- enum: `Wheeled`, `Tracked`, `Legged`
- `NavState` -- pathfinding state machine for a single vehicle
- `NavigationEnums` -- `NavMode`, `NavResult` enumerations

**File**: `CarKinem/Controllers/`
- `BicycleModel` -- kinematic bicycle model: computes delta position/heading per timestep
- `PurePursuitController` -- geometric path tracker; outputs curvature command
- `SpeedController` -- PID-based longitudinal speed controller

**File**: `CarKinem/Formation/`
- `FormationController` -- manages formation slot assignments for a unit leader
- `FormationTemplate` / `FormationTemplateManager` -- data-driven formation layouts
- `FormationFollower` -- follower offset maintenance logic
- `FormationSlot` / `FormationParams` / `FormationTarget` -- formation data types
- `FormationEnums` -- `FormationMode`, `FormationState`

**File**: `CarKinem/Road/`
- `RoadNetworkBlob` -- unmanaged memory layout for loaded road graph
- `RoadNetworkBuilder` -- constructs `RoadNetworkBlob` from `RoadNetworkJson`
- `RoadGraphNavigator` -- A\* / Dijkstra route planner over the road graph
- `RoadNetworkLoader` -- VFS-based loader; reads JSON and builds blob
- `RoadNode` / `RoadSegment` -- road graph topology primitives

**File**: `CarKinem/Spatial/`
- `SpatialHashGrid` -- native-memory spatial hash grid for broadphase neighbour queries
- `SpatialGridData` -- ECS singleton component wrapping the grid

**File**: `CarKinem/Avoidance/`
- `RVOAvoidance` -- Reciprocal Velocity Obstacle local avoidance

**File**: `CarKinem/Trajectory/`
- `CustomTrajectory` -- spline/waypoint trajectory representation
- `TrajectoryWaypoint` -- single waypoint: position, speed, heading
- `TrajectoryInterpolation` -- continuous interpolation along a trajectory
- `TrajectoryPoolManager` -- pool for reusable trajectory objects

**File**: `CarKinem/Systems/`
- `CarKinematicsSystem` -- main vehicle physics; runs `BicycleModel`+`PurePursuit` in parallel
- `SpatialHashSystem` -- rebuilds the spatial grid each frame from entity positions
- `FormationTargetSystem` -- computes per-follower target transforms from formation template
- `NavigationExecutionSystem` -- advances `NavState` FSM; emits position setpoints
- `VehicleCommandSystem` -- processes velocity/heading commands into `NavState`
- `LinearKinematicsSystem` -- simple linear (non-bicycle) kinematics for non-vehicle entities

**File**: `CarKinem/Modules/GroundKinematicsModule.cs`
- `GroundKinematicsModule` -- `IEcsModule` registering all CarKinem systems

---

### Namespace: `Fdp.Toolkit.Combat`

**File**: `Combat/Components/`
- `CombatComponents` -- aggregated combat component registrations
- `Health` -- current/max hit points component

**File**: `Combat/Events/`
- `WeaponFireEvents` -- `RequestFireEvent`, `WeaponFiredEvent`
- `DetonationEvents` -- `ProjectileDetonatedEvent`
- `CombatEvents` -- general combat lifecycle events

**File**: `Combat/Systems/`
- `FireProcessingSystem` -- resolves `RequestFireEvent` into projectile creation
- `BallisticsSystem` -- integrates projectile trajectories (linear / gravity-affected)
- `HitResolutionSystem` (see Physics) -- geometry hit check
- `DamageCalculationSystem` -- computes raw damage from ballistic properties
- `DamageSystem` -- applies damage events to `Health` components
- `HealthApplicationSystem` -- clamps health, fires `EntityKilledEvent` on zero

**File**: `Combat/Executors/`
- `AimAndFireExecutor` -- behavior executor: aims weapon and triggers `RequestFireEvent`
- `AimAndFireParams` -- parameter struct passed to `AimAndFireExecutor`

**File**: `Combat/Translators/CombatTkbTranslator.cs`
- `CombatTkbTranslator` -- maps TKB `WeaponSuite` and `AmmoWeaponBallistics` descriptors to ECS

**File**: `Combat/Modules/DamageAssessmentModule.cs`
- `DamageAssessmentModule` -- `IEcsModule` registering ballistics, damage, and health systems

---

### Namespace: `Fdp.Toolkit.DER`

**File**: `DER/IDerEntity.cs` / `DerEntity.cs`
- `IDerEntity` -- interface: entity ID, TKB type, mutable attribute dictionary
- `DerEntity` -- thread-safe `IDerEntity` implementation

**File**: `DER/IDerRepo.cs` / `DerRepo.cs`
- `IDerRepo` -- interface: CRUD plus `EntityCreated`/`EntityDeleted` events
- `DerRepo` -- `ConcurrentDictionary`-backed implementation; raises events on mutations

**File**: `DER/DdsIngressHandlers.cs`
- DDS subscriber callbacks that forward network entity update messages into `IDerRepo`

---

### Namespace: `Fdp.Toolkit.Diagnostics`

**File**: `Diagnostics/Gizmos/GizmoRegistry.cs`
- `GizmoRegistry` -- startup-time registry mapping component bitmasks to gizmo definitions

**File**: `Diagnostics/Gizmos/IGizmoDefinition.cs`
- `IGizmoDefinition` -- required component list + render method contract

**File**: `Diagnostics/Gizmos/IStatelessGizmo.cs` / `IStatefulGizmo.cs`
- `IStatelessGizmo` -- gizmo with no per-entity state (overlay lines, labels)
- `IStatefulGizmo` -- gizmo that allocates per-entity state objects
- `IGlobalStatelessGizmo` -- gizmo rendering a world-global overlay (no entity filter)

**File**: `Diagnostics/Gizmos/StatelessGizmoRegistry.cs`
- `StatelessGizmoRegistry` -- list of `IGlobalStatelessGizmo` instances

**File**: `Diagnostics/Gizmos/GizmoExecutionController.cs`
- `GizmoExecutionController` -- per-entity enable/disable, visibility policy, undo/redo

**File**: `Diagnostics/Gizmos/Hub/`
- `GizmoUiStateHub` -- in-process shared state between sim thread and UI thread
- `LocalGizmoUiStateTransport` -- passes gizmo UI state updates without network transport

**File**: `Diagnostics/Gizmos/Settings/`
- `GizmoSettingsRegistry` -- per-gizmo settings catalog (typed `GizmoSettingValue` entries)
- `GizmoSettingsPersistence` -- JSON round-trip for gizmo settings
- `GizmoSettingValue` / `SettingScope` / `GizmoSettingChangedEvent`

**File**: `Diagnostics/Gizmos/Systems/`
- `DataDrivenGizmoSystem` -- iterates entities; matches component bitmasks; dispatches to gizmos
- `GlobalGizmoManager` -- runs `IGlobalStatelessGizmo` instances each frame
- `StatelessGizmoSystem` -- renders stateless entity-bound gizmos
- `BehaviorGizmoManagerSystem` / `BehaviorGizmoRegistry` / `IBehaviorGizmoFactory`
- `GizmoSettingsPublisherSystem` -- publishes settings changes via DDS (uses StructEdit)
- `DebugPrimitivesBatchPublisherSystem` -- batches debug draw primitives before publish

**File**: `Diagnostics/Gizmos/Modules/`
- `GizmoNetworkTransportModule` -- `IEcsModule` wiring DDS gizmo protocol
- `DdsGizmoUiStatePublisher` -- DDS publisher for `IGizmoUiStatePublisher`
- `LocalTerminalModule` -- in-process terminal without DDS
- `GizmoCapabilitiesTracker` -- tracks which gizmo capabilities a connected tool supports
- `IGizmoNetworkFactory` -- factory abstraction for creating DDS gizmo transport objects

**File**: `Diagnostics/Gizmos/UndoRedo/`
- `GizmoUndoStack` -- circular undo/redo buffer for gizmo interactions
- `IGizmoUndoRecord` -- serialisable undo record interface

**File**: `Diagnostics/EntityStateExtractionService.cs`
- `EntityStateExtractionService` -- serialises a single entity's ECS components to JSON
- `IEntityStateExtractionService` -- public interface consumed by gizmo tooling

**File**: `Diagnostics/DtoDiagnosticMapper.cs`
- `DtoDiagnosticMapper` -- reflection-based mapper from ECS components to diagnostic DTOs

**File**: `Diagnostics/DiagnosticGuidResolver.cs`
- `DiagnosticGuidResolver` -- maps entity GUIDs to human-readable names using TKB

---

### Namespace: `Fdp.Modules.Geographic`

**File**: `Geographic/GeographicModule.cs`
- `GeographicModule` -- `IEcsModule` wiring geodetic smoothing and coordinate transform

**File**: `Geographic/IGeographicTransform.cs`
- `IGeographicTransform` -- converts (lat, lon, alt) to local Cartesian and vice versa

**File**: `Geographic/ITerrainProvider.cs`
- `ITerrainProvider` -- asynchronous terrain height query interface

**File**: `Geographic/Transforms/WGS84Transform.cs`
- `WGS84Transform` -- WGS-84 ellipsoid math: geodetic <-> ECEF <-> ENU conversion

**File**: `Geographic/Components/`
- `Position` -- ECS 3-D local-frame position component
- `PositionGeodetic` -- WGS-84 geodetic position component
- `Velocity` -- velocity vector component
- `TerrainClampBaseline` -- per-entity jump-rejection baseline: holds `LastValidIgAltitude` and
  `IgAltitudeBaselineEstablished`; the former `GroundClampingState` visual-offset fields were
  removed by the 3D Cognitive Spatial Awareness promotion (P3D-101) -- terrain altitude is now
  authoritative on `SimTransform.Position.Z`
- `TerrainQueryBatchData` -- singleton holding pending terrain height queries

**File**: `Geographic/Systems/`
- `CoordinateTransformSystem` -- converts `PositionGeodetic` -> local `Position` each frame
- `GeodeticSmoothingSystem` -- low-pass smoothing of raw geodetic input
- `SimTransformBridgeSystem` -- writes local `Position` into `SimTransform` for ECS queries
- `TerrainQueryInitializationSystem` / `TerrainQuerySubmitSystem` /
  `TerrainQuerySolverSystem` -- three of the four async terrain query phases
- `TerrainQueryResolutionSystem` -- final phase; writes the accepted `HitZ` into
  `SimTransform.Position.Z` (authoritative altitude, P3D-102); jump-rejection filter
  suppresses pops at geometry seams; first hit always accepted to seed baseline state

---

### Namespace: `Fdp.Toolkit.Lifecycle`

**File**: `Lifecycle/EntityLifecycleModule.cs`
- `EntityLifecycleModule` -- distributed construction/destruction acknowledgement coordinator;
  holds pending construction/destruction maps; drives `BlueprintApplicationSystem` and
  `LifecycleSystem`

**File**: `Lifecycle/Events/LifecycleEvents.cs`
- `ConstructionAck` / `DestructionAck` -- per-module acknowledgement events
- `ConstructionOrder` / `DestructionOrder` -- authoritative lifecycle commands from master

**File**: `Lifecycle/Systems/`
- `BlueprintApplicationSystem` -- when a new ghost entity is promoted, runs all
  `ITkbEntityTranslator` instances to materialise ECS components from TKB template data
- `LifecycleSystem` -- tracks acknowledgement count; transitions entity lifecycle state

---

### Namespace: `Fdp.Toolkit.Navigation`

> Full documentation: [Fdp.Toolkits.Navigation.md](Fdp.Toolkits.Navigation.md)

**File**: `Navigation/NavigationComponents.cs`
- `NavigationIntent` -- CQRS command component (Brain -> Muscle): mode, destination, route handle, flags
- `NavigationStatus` -- CQRS status component (Muscle -> Brain): result, phase, replan count, ETA
- `NavigationCorridorMuscle` -- Muscle-internal working state (not replicated): handle, segment index, progress
- `NavigationCorridorPreview` -- opt-in 8-waypoint corridor window replicated to Brain
- `NavigationPathDetailsBuffer` -- Brain-internal full-waypoint cache populated from path-details events
- `NavigationMode`, `NavigationResult`, `NavigationPhase`, `NavigationBackend`,
  `TraversalKind`, `SurfaceType`, `NavigationFailureReason` -- navigation enums

**File**: `Navigation/NavigationActions.cs`
- `MoveToParams`, `FleeParams`, `FleeState`, `FollowRouteParams`,
  `PlanRouteParams`, `FollowPathParams`, `FetchPathDetailsParams`, `ReleasePathParams`
  -- action parameter structs (unmanaged, <= 32 B)

**File**: `Navigation/NavigationConstants.cs`
- `NavigationConstants` -- action IDs (1-9), frustration thresholds, default replan limit, flag bit indices

**File**: `Navigation/NavWaypoint.cs`
- `NavWaypoint` (24 B) -- single planned-path point: `Vector3 Position`, `TraversalKind`, `SurfaceType`, `TimeOffset`

**File**: `Navigation/NavLayerMask.cs`
- `NavLayerMask` -- `[Flags] uint`: Infantry(1), Vehicle(2), Naval(4), Air(8), All

**File**: `Navigation/NavigationHandleAllocator.cs`
- `NavigationHandleAllocator` -- thread-safe Muscle-private handle allocator; handles >= `MuscleHandleBase`(0x40000000)

**File**: `Navigation/INavmeshProvider.cs`
- `INavmeshProvider` -- `IsWalkable`, `ProjectToNavmesh`, `SampleNavmeshPoints`, `PathExists`, `PathCost`, `QueryVersion`, `PlanPath`

**File**: `Navigation/IDtCrowdProvider.cs`, `Navigation/IVolumetricPathProvider.cs`
- `IDtCrowdProvider` -- dtCrowd integration interface for local infantry avoidance
- `IVolumetricPathProvider` -- 3D volumetric pather interface for flying/sub-surface agents

**File**: `Navigation/IPathRegistry.cs`
- `IPathRegistry` -- `IsCached`, `TryGetSummary`, `TryGetWaypoints`, `TryGetWaypointsSlice`
- `PathSummary` -- lightweight path entry summary (handle, distance, waypoint count, backend, replan count)

**File**: `Navigation/PathfindingEvents.cs`
- `PathfindingRequestEvent` (EventId 2032) -- Brain -> Solver request
- `PathfindingResultEvent` (EventId 2033) -- Solver -> Materializer result
- `MoveStartedEvent` (EventId 2034) -- emitted when corridor-following begins
- `OffMeshTraversalStartedEvent` (EventId 2035) -- emitted by `OffMeshLinkDetectionSystem`

**File**: `Navigation/Executors/`
- `MoveToExecutor` (ActionId 1) -- writes `NavigationIntent`; polls `NavigationStatus` for verdict
- `FleeExecutor` (ActionId 2) -- periodically replans flee direction from a threat entity
- `FollowRouteExecutor` (ActionId 3) -- follows a pre-planned `CustomTrajectory`
- `JoinFormationExecutor` (ActionId 5) -- joins a formation slot
- `PlanRouteExecutor` (ActionId 6) -- plans a path; returns Success when `PathFound`
- `FollowPathExecutor` (ActionId 7) -- follows a previously planned path by route handle
- `FetchPathDetailsExecutor` (ActionId 8) -- populates `NavigationPathDetailsBuffer`; optional blocking wait
- `ReleasePathExecutor` (ActionId 9) -- releases route handle from `TrajectoryPoolManager`

**File**: `Navigation/Systems/`
- `NavigationIntentBridgeSystem` -- translates `NavigationIntent` -> `NavState`; issues `PathfindingRequestEvent`
- `PathfindingSolverSystem` -- multi-modal Dijkstra/A*/volumetric solver at 10 Hz on background thread
- `PathfindingResultMaterializationSystem` -- main-thread materializer of `PathfindingResultEvent`
- `CrowdAgentUpdateSystem` -- `SimVelocity` update for crowd-managed infantry agents
- `OffMeshLinkDetectionSystem` -- off-mesh link detection with zero-frame suppression
- `CorridorPreviewSystem` -- maintains 8-waypoint `NavigationCorridorPreview` window
- `NavigationPathDetailsUpdateSystem` -- (Brain-tier) materializes path-detail events into `NavigationPathDetailsBuffer`

**File**: `Navigation/Modules/NavigationSolverModule.cs`
- `NavigationSolverModule` -- `IEcsModule` wrapping `PathfindingSolverSystem` at 10 Hz background

**File**: `Navigation/Fake/NavigationFakesModule.cs`
- `NavigationFakesModule` -- all-in-one module for integration tests; exposes `FakeNavmeshProvider`,
  `FakeDtCrowdProvider`, `FakeVolumetricPathProvider`, `SharedPathRegistry`

**File**: `Navigation/EngineBacked/EngineBackedNavigationModule.cs`
- `EngineBackedNavigationModule` -- adapter module wiring new contract to existing `RoadNetworkBlob` /
  `TrajectoryPoolManager` machinery for demo scenarios

**File**: `Navigation/Fake/`
- `FakeNavmeshProvider` -- polygon A* over in-memory `FakeNavLayer[]`; test API for `BlockPolygon`/`PatchNavmesh`
- `FakeDtCrowdProvider` -- velocity-obstacle crowd tick; per-agent `FakeCrowdAgentState`
- `FakeVolumetricPathProvider` -- 3D direct-line + obstacle avoidance
- `MusclePathRegistry`, `BrainPathRegistry`, `SharedPathRegistry` -- `IPathRegistry` implementations
- `NavTestMap`, `NavTestMapBuilder`, `NavTestMapLoader`, `NavTestMaps` -- test-world data format and helpers

**File**: `Navigation/BTreeNodes/`
- `Action_PlanRoute` -- FastBTree action node issuing a `PlanRoute` intent
- `PathfindingActionNode` -- base class for navigation action nodes

---

### Namespace: `Fdp.Toolkit.NetworkSpawning`

**File**: `NetworkSpawning/Events/`
- `SpawnEntityCommand` -- inbound network command to create a new entity
- `DestroyEntityCommand` -- inbound network command to destroy an entity
- `UpdateEntityCommand` -- inbound network command to update entity attributes
- `DeferredTakeOwnershipCommand` -- deferred authority transfer request

**File**: `NetworkSpawning/Systems/NetworkSpawningSystem.cs`
- `NetworkSpawningSystem` -- processes spawn/destroy/update commands from the event bus

**File**: `NetworkSpawning/EntityComponentReflector.cs`
- `EntityComponentReflector` -- reflection helper mapping component type names to ECS IDs

**File**: `NetworkSpawning/Abstractions/INetworkIdAllocator.cs`
- `INetworkIdAllocator` -- interface for allocating unique network entity IDs

---

### Namespace: `Fdp.Toolkit.Orchestration`

**File**: `Orchestration/ClusterSlave.cs`
- `ClusterSlave` -- generic cluster FSM slave: heartbeat publisher, intent dispatcher,
  async prepare/commit protocol, transaction deduplication

**File**: `Orchestration/TransitionPlanner.cs`
- `TransitionPlanner` -- BFS-based shortest-path planner over `ITransitionGraph`

**File**: `Orchestration/TransitionGraphBuilder.cs`
- `TransitionGraphBuilder` -- fluent builder for directed transition graphs

**File**: `Orchestration/ITransitionGraph.cs`
- `ITransitionGraph` -- directed graph abstraction for state-transition queries

**File**: `Orchestration/IClusterStateHandler.cs` / `ITickableClusterStateHandler.cs`
- `IClusterStateHandler` -- `PrepareAsync` + `Commit` two-phase handler contract
- `ITickableClusterStateHandler` -- adds `Tick` for handlers that need per-frame work

**File**: `Orchestration/Enums/`
- `ClusterState` -- enum listing cluster operational states
- `ClusterOpType` / `NodeOpType` -- operation type enumerations for cluster commands

**File**: `Orchestration/Events/`
- `ClusterCqrsEvents` -- CQRS event types: `ExecuteNodeOpIntent`, `NodeOpCompletedEvent`
- `ClusterOpIntents` -- cluster-level operation intents (load, archive, checkpoint, etc.)

**File**: `Orchestration/Handlers/`
- `ReferenceScenarioLoadHandler` -- loads a scenario file into the sim
- `ReferenceEpisodeLoadHandler` -- loads a specific episode from a recording
- `ReferenceReplayLoadHandler` -- transitions cluster to replay mode
- `ReferenceLiveLoadHandler` -- returns cluster to live simulation
- `ReferenceArchiveHandler` -- archives the current recording
- `ReferenceCheckpointHandler` -- writes a simulation checkpoint
- `ReferenceEditLoadHandler` -- loads the sim for entity editing
- `ReferencePrefetchHandler` -- prefetches assets before a load command
- `ReferencePreviewHandler` -- loads a lightweight preview snapshot

**File**: `Orchestration/ReferenceScenarioLoader.cs`
- `ReferenceScenarioLoader` -- shared load logic used by multiple reference handlers

**File**: `Orchestration/IScenarioLoader.cs` / `IScenarioStorageProvider.cs`
- `IScenarioLoader` -- abstraction for loading scenario data
- `IScenarioStorageProvider` -- abstraction for scenario file storage (disk, cloud, etc.)

**File**: `Orchestration/LocalDiskStorageProvider.cs`
- `LocalDiskStorageProvider` -- `IScenarioStorageProvider` implementation using the local filesystem

---

### Namespace: `Fdp.Toolkit.Perception`

**File**: `Perception/Components/`
- `PerceptionReceptor` -- sensor configuration: vision range, FOV angle, sensor modalities
- `PerceptionOutput` -- set of currently tracked entities
- `TargetMemory` -- fixed-size unsafe struct holding up to `MaxTrackedTargets` perceived threats;
  stores entity IDs, last-known X/Y/Z world-space positions (Z = altitude, Sim Z-up; 3D
  Cognitive Spatial Awareness promotion P3D-206), threat scores, last-seen ticks, and modality
  bitmasks; sorted descending by threat score; mutated via `AddOrUpdateTarget`
- `SensorModality` -- flags enum: `Visual`, `Acoustic`, `Radar`, `Thermal`

**File**: `Perception/Events/PerceptionEvents.cs`
- `LosCheckRequestEvent` -- request for line-of-sight computation
- `LosCheckResultEvent` -- result of LOS computation
- `NewContactDetectedEvent` / `ContactLostEvent` -- sensor track lifecycle events

**File**: `Perception/Systems/`
- `VisionBroadphaseSystem` -- spatial-hash broadphase per observer; emits `LosCheckRequestEvent`
- `LosRequestBatchingSystem` -- collects LOS requests; batches before expensive solver
- `ActiveSensorTracksUpdateSystem` -- merges LOS results into `PerceptionOutput`
- `SensorTrackDebounceSystem` -- debounces flickering contact detection/loss events
- `ThreatEvaluationSystem` -- ranks contacts by threat level; tags top-N as threats; passes
  real `SimTransform.Position.Z` into `AddOrUpdateTarget` (P3D-206) so `TargetMemory`
  contacts carry 3D world positions
- `AudioPerceptionSystem` -- sound-based contact detection (range + terrain occlusion estimate)
- `LocalGridBuilderSystem` -- populates per-perception-module spatial grid from entity positions

**File**: `Perception/Modules/AutonomousPerceptionModule.cs`
- `AutonomousPerceptionModule` -- `IEcsModule` registering all perception systems

---

### Namespace: `Fdp.Toolkit.Physics`

**File**: `Physics/Components/PhysicsComponents.cs`
- `RaycastRequest` -- input component for a pending raycast
- `RaycastResult` -- output component for a completed raycast

**File**: `Physics/Events/`
- `PhysicsEvents` -- general physics event types
- `RaycastEvents` -- `RaycastRequestEvent`, `RaycastResultEvent`

**File**: `Physics/Math/Intersection2D.cs`
- `Intersection2D` -- static 2-D line segment intersection helpers

**File**: `Physics/Systems/`
- `RaycastSolverSystem` -- resolves pending raycasts against registered geometry
- `RaycastResultMaterializationSystem` -- writes results back to requesting entities
- `HitResolutionSystem` -- tests projectile paths against entity bounding volumes
- `LinearKinematicsSystem` -- projectile position integration (constant-velocity)

**File**: `Physics/BTreeNodes/`
- `Action_QueryRaycast` -- FastBTree action node: fires a raycast and waits for result
- `PhysicsQueryActionNode` -- base class for physics-query BTree nodes

**File**: `Physics/Modules/PhysicsQueryModule.cs`
- `PhysicsQueryModule` -- `IEcsModule` for raycast batch pipeline

---

### Namespace: `Fdp.Toolkit.Replay`

**File**: `Replay/RecordingModule.cs`
- `RecordingModule` -- `IEcsModule` driving `AsyncRecorder` (LZ4-compressed binary frames)

**File**: `Replay/EpisodeRecorderModule.cs`
- `EpisodeRecorderModule` -- per-episode recorder running alongside the global recorder

**File**: `Replay/ReplayModule.cs`
- `ReplayModule` -- `IEcsModule` driving the frame-by-frame playback engine

**File**: `Replay/RecorderTickSystem.cs` / `PlaybackTickSystem.cs`
- Systems wrapping the async recorder/player; advance frame cursors

**File**: `Replay/RecordingConfiguration.cs`
- `RecordingConfiguration` -- exercise ID, file path, entity filter, compression settings

**File**: `Replay/EpisodeReplayTag.cs`
- `EpisodeReplayTag` -- component marking entities as belonging to a specific episode replay

---

### Namespace: `Fdp.Toolkit.ReplayBrowser`

**File**: `ReplayBrowser/ReplayBrowserContext.cs`
- `ReplayBrowserContext` -- in-memory state for the replay browser UI session

**File**: `ReplayBrowser/RecordingExportService.cs`
- `RecordingExportService` -- reads recording frames and exports to JSON / CSV

**File**: `ReplayBrowser/Search/`
- `RecordingSearchService` -- full timeline scan to find frames matching predicates
- `EventScannerCompiler` / `PredicateCompiler` -- compile `SearchPredicateDto` to delegates
- `PropertyEvaluator` -- evaluates property path expressions against entity snapshots
- `TargetEntityFilter` / `SearchPredicateDto` -- search query model

**File**: `ReplayBrowser/Diff/`
- `ComponentDiffService` -- computes field-level diff between two entity snapshots
- `DiffNode` -- diff tree node (added, removed, changed value)

---

### Namespace: `Fdp.Toolkit.Replication`

**File**: `Replication/ReplicationLogicModule.cs`
- `ReplicationLogicModule` -- `IEcsModule` composing all replication systems

**File**: `Replication/Components/`
- `NetworkIdentity` -- network (DIS) entity ID component
- `NetworkAuthority` -- authority tier: `Owner`, `Ghost`, `Shared`
- `NetworkTransform` / `NetworkVelocity` -- last-received network state cache
- `GhostStateTracker` -- ghost age (first-seen frame) for timeout detection
- `BinaryGhostStore` -- raw binary state from last received network packet
- `DescriptorOwnership` / `PendingAuthorityGrants` -- ownership negotiation components
- `EgressPublicationState` -- tracks egress dirty-bits per component
- `TkbIdentity` -- TKB type ID carried by the entity for ghost promotion
- `ChildMap` / `PartMetadata` -- sub-entity hierarchy support

**File**: `Replication/Systems/`
- `GhostCreationSystem` -- creates bare ghost entities from inbound network IDs
- `GhostPromotionSystem` -- promotes ghost to full entity using TKB + translators
- `GhostTimeoutSystem` -- removes stale ghosts after configurable timeout
- `OwnershipIngressSystem` -- processes inbound ownership-claim messages
- `OwnershipEgressSystem` -- publishes outbound ownership state when dirty
- `SmartEgressSystem` -- delta-encodes outbound component state (skip unchanged)
- `SubEntityCleanupSystem` -- destroys sub-entities when parent is destroyed
- `DisposalMonitoringSystem` -- detects unexpected entity disposal; logs diagnostics
- `IdAllocationMonitorSystem` -- watches ID allocator and reports conflicts
- `NetworkGatewaySystem` -- entry-point for all inbound DDS entity state packets

**File**: `Replication/Patching/`
- `BinaryInterpreter` / `BinaryInterpreterBuilder` -- builds a function to read a binary packet and apply it to ECS
- `EcsPatchContext` / `BinaryPatchContext` / `ListPatchContext` -- context types for patch application
- `JsonAttributeCompiler` / `JsonToRecordCompiler` -- JSON -> ECS attribute patch compilers
- `AttributeCompilerBuilder` / `AttributeIds` / `AttributeValueKind`
- `IAttributeRecordEmitter` / `IBinaryAttributeInstaller` / `IEntityPatchContext`

**File**: `Replication/Services/`
- `NetworkEntityMap` -- bidirectional map: network ID <-> ECS entity
- `DescriptorOwnershipMap` -- tracks which node owns each TKB descriptor
- `BlockIdManager` -- allocates contiguous blocks of network entity IDs

**File**: `Replication/Extensions/`
- `AuthorityExtensions` -- extension methods for querying entity authority state
- `OwnershipExtensions` -- extension methods for ownership state queries

---

### Namespace: `Fdp.Toolkit.Spatial.Eqs`

> Full reference: [Fdp.Toolkits.Spatial.Eqs.md](Fdp.Toolkits.Spatial.Eqs.md)

**Folder**: `Spatial/Eqs/`
**Companion namespace**: `FDP.Eqs` (for `EqsSensorHandle`)
**DDS topics namespace**: `Fdp.Toolkit.Spatial.Eqs.Topics`

Environment Query System v1.3 -- standing AI spatial queries (entity, positional, path-aware)
over the Brain/Muscle boundary.

#### Core ECS components

- `EqsSensor` -- Brain-authored standing query config; replicated to Muscle via DDS
- `EqsCognitiveBuffer` -- Brain-side result cache; top-K inline array; read by BTree nodes
- `EqsResult` -- 32-byte ranked candidate (entity or positional; includes `PositionZ`, `FlagsMeaningful`)
- `EqsPublishPolicy` -- `AlwaysPush` | `TopChanged` | `ScoreDelta`
- `SensorEvalState` -- Muscle-side per-sensor cross-tick state machine component
- `EqsSolverGlobalState` -- Muscle-side singleton: accurate-raycast budget tracking

#### Result pool and events

- `EqsResultPool` -- Muscle singleton: 16384-entry native ring buffer (1024 sensors x 16 TopK)
- `EqsResultEvent` -- 28-byte unmanaged event with pool handle (Muscle event bus)
- `EqsResultUpdateEvent` -- managed DDS-bridged event (Brain event bus)

#### Query template system

- `EqsQueryTemplate` -- compiled template: generator + four test phases + `StructureHash`
- `IEqsGenerator` / `IEqsTest` -- zero-allocation candidate generation and testing interfaces
- `EqsTestPhase` -- `FilterCheap` | `FilterExpensive` | `ScoreCheap` | `ScoreExpensive`
- `IEqsTemplateRegistry` -- solver lookup by `BlueprintId`
- `EqsTemplateAttribute` -- marks a class for the Roslyn source generator
- `IEqsTemplateBuilder` / `EqsTemplateBuilder` -- no-op builder type for generated `Build()` overloads

#### Generators

- `EntitiesInRadiusGenerator` -- entity-shaped; queries spatial hash grid
- `CoverPointsGenerator` -- positional; queries `ICoverProvider`
- `NavmeshSamplesGenerator` -- positional; samples `INavmeshProvider`

#### Tests

- `FactionFilterTest` / `CheapLineOfSightTest` -- `FilterCheap`
- `NavmeshReachableTest` / `AccurateLineOfSightTest` -- `FilterExpensive` / `ScoreExpensive`
- `DistanceScoreTest` -- `ScoreCheap`
- `PathCostScoreTest` -- `ScoreExpensive`

#### Service interfaces

- `ICoverProvider` / `ManualCoverProvider` -- cover database
- `CoverPoint` -- 28-byte cover node struct (position X/Y/Z, direction, quality, stance)
- `ILosService` / `BlockedLosService` -- cheap LOS service (stub: always blocked)

#### DDS topics (`Topics/` subfolder)

- `EqsSensorConfigTopic` -- Brain -> Muscle; compound key `(ParentNetworkId, LocalChildIndex)`
- `EqsResultEntry` / `EqsResultTopic` -- Muscle -> Brain; `[DdsManaged] List<EqsResultEntry>`

#### Handle type

- `EqsSensorHandle` (namespace `FDP.Eqs`) -- typed `Entity` wrapper for child sensor entities

---

### Namespace: `Fdp.Toolkit.Runner`

**File**: `Runner/ISubsystem.cs`
- `ISubsystem` -- interface for simulation subsystems: `Initialize`, `Update`, `DrawWorld`,
  `DrawUI`, `Shutdown`, `TitleBarColor`, `Name`

**File**: `Runner/SubsystemOrchestrator.cs`
- `SubsystemOrchestrator` -- manages full lifecycle of injected `ISubsystem` list; fixed-timestep
  loop; headless mode (skips rendering); per-frame console action queue

**File**: `Runner/RunnerOptions.cs`
- `RunnerOptions` -- headless flag, domain ID, node ID, deterministic mode, fixed delta

**File**: `Runner/RunnerConfiguration.cs`
- `RunnerConfiguration` -- JSON-deserialisable top-level configuration for a runner process

**File**: `Runner/SubsystemConfig.cs`
- `SubsystemConfig` -- per-subsystem init configuration (window ownership, headless)

**File**: `Runner/IMapCameraProvider.cs`
- `IMapCameraProvider` -- optional interface for subsystems that own a 2-D map view

**File**: `Runner/Testing/`
- `HeadlessTestExecutor` -- runs a `TestScript` against a headless subsystem stack
- `TestScript` -- sequence of timed actions to execute during a test run
- `TestMetricsCollector` -- collects per-frame numeric metrics during headless test
- `TestReport` -- test result summary written to disk at test completion
- `ITestActionHandler` -- interface for individual test step actions

---

### Namespace: `Fdp.Toolkit.Scenario`

**File**: `Scenario/ScenarioSerializer.cs`
- `ScenarioSerializer` -- save/load `EntityRepository` to/from JSON; two-pass load
  (create entities, then resolve GUIDs, then inject components)

**File**: `Scenario/ScenarioSerializerBuilder.cs`
- `ScenarioSerializerBuilder` -- fluent builder; registers auto-serializer and custom translators

**File**: `Scenario/FdpAutoSerializer.cs`
- `FdpAutoSerializer` -- reflection-based fallback serializer for components not covered by translators

**File**: `Scenario/IEntityScenarioTranslator.cs`
- `IEntityScenarioTranslator` -- custom translator: `CanTranslate`, `Extract`, `Inject`

**File**: `Scenario/IGuidResolver.cs`
- `IGuidResolver` -- maps entity GUIDs to `Entity` handles during deserialisation

**File**: `Scenario/ScenarioHeader.cs`
- `ScenarioHeader` -- `SubsystemType` and `SchemaVersion` metadata block

**File**: `Scenario/ScenarioJsonConverters.cs`
- Custom `System.Text.Json` converters for simulation-specific types

**File**: `Scenario/ScenarioIgnoreAttribute.cs` / `ScenarioIgnoreTag.cs`
- Attribute/tag to exclude components from serialisation

---

### Namespace: `Fdp.Toolkit.Time`

**File**: `Time/Controllers/`
- `MasterSyncController` -- NTP-like master time coordinator; publishes `TimeSyncResponse`
- `SlaveSyncController` -- slave time synchroniser; adjusts local clock from responses
- `SteppingTimeController` -- manual frame-by-frame time controller for editor/replay
- `TimeConfig` / `TimeControllerConfig` -- configuration for time controllers
- `TimeControllerFactory` -- creates correct controller based on node role

**File**: `Time/Translators/`
- `MasterLockstepTranslator` -- DDS bridge: `AdvanceFrameIntent` -> `FrameOrder` (egress) +
  `FrameAck` -> `FrameStepCompletedEvent` (ingress). Master side.
- `SlaveLockstepTranslator` -- DDS bridge: `FrameOrder` -> `AdvanceFrameIntent` (ingress) +
  `FrameStepCompletedEvent` -> `FrameAck` (egress). Slave side.
- `MasterTimeSyncTranslator` / `SlaveTimeSyncTranslator` -- DDS bridges for NTP-style sync

**File**: `Time/TimeNetworkModule.cs`
- `TimeNetworkModule` (static) -- factory helper creating translator instances for composition roots

**File**: `Time/HighResUtcClock.cs`
- `HighResUtcClock` -- high-resolution UTC clock wrapper using `Stopwatch` for sub-millisecond precision

**File**: `Time/Messages/TimeMessages.cs`
- CycloneDDS IDL-generated wire DTOs: `FrameOrder`, `FrameAck`, `TimeSyncRequest`,
  `TimeSyncResponse`, `SwitchTimeModeWireDto`

---

### Namespace: `Fdp.Toolkit.Tkb`

**File**: `Tkb/TkbDatabase.cs`
- `TkbDatabase` -- `ITkbDatabase` implementation; dual index by name and by `TkbType` (long)

**File**: `Tkb/TkbDeserializer.cs`
- `TkbDeserializer` -- parses TKB entity JSON files; dispatches to registered parser thunks

**File**: `Tkb/TkbDescriptorRegistry.cs`
- `TkbDescriptorRegistry` -- static dictionary of hierarchical-name -> parser thunks;
  populated at startup by source-generated `[ModuleInitializer]` code

**File**: `Tkb/TkbFormatException.cs`
- `TkbFormatException` -- thrown when a TKB entity file is structurally invalid

**File**: `Tkb/Vfs/`
- `ITkbStorageStrategy` -- abstraction over how TKB files are stored and enumerated
- `RawDirectoryTkbProvider` -- reads TKB JSON from a flat directory tree
- `ZipTkbProvider` -- reads TKB JSON from a ZIP archive
- `TkbUnifiedLoader` -- tries providers in priority order; loads all found entity files
- `TkbEntityFile` -- in-memory record of a single TKB JSON file's content and metadata

**File**: `Tkb/Attributes/`
- `TkbDescriptorAttribute` -- marks a record as a TKB descriptor with a hierarchical name
- `ModelRefAttribute` -- marks a field as a reference to a visual model asset
- `WeaponRefAttribute` / `AmmoRefAttribute` -- cross-reference attributes within TKB entries

**File**: `Tkb/Domain/`
- `TkbMasterDto` -- mandatory master descriptor: `CustomName`, `DisType`
- `CombatPlatformDefDto` -- combat platform capabilities
- `WeaponSuiteDto` / `WeaponCapabilitiesDto` / `AmmoWeaponBallisticsDto` -- weapon definitions
- `SensorCapabilitiesDto` -- sensor suite definition
- `VehicleParametersDto` -- ground vehicle physics parameters
- `BehaviorProfileDto` -- default behavior and parameter overrides
- `UnitCompositionDto` -- parent unit composition (sub-entities)
- `VisualDefinitionDto` -- visual model reference and LOD settings

---

## Public API Reference

### Key interfaces

```csharp
// Entity template database
public interface ITkbDatabase
{
    void                          Register(TkbTemplate template);
    TkbTemplate                   GetByType(long tkbType);
    bool                          TryGetByType(long tkbType, out TkbTemplate template);
    TkbTemplate                   GetByName(string name);
    bool                          TryGetByName(string name, out TkbTemplate template);
    IEnumerable<TkbTemplate>      GetAll();
    IEnumerable<TkbTemplate>      GetEntitiesByCategory(string categoryPath);
    string?                       ActiveTkbName { get; set; }
    void                          Clear();
}

// Subsystem lifecycle contract
public interface ISubsystem
{
    string   Name          { get; }
    Vector4  TitleBarColor { get; }
    void     Initialize(SubsystemConfig config);
    void     Update(float deltaTime);
    void     DrawWorld();
    void     DrawUI();
    void     Shutdown();
}

// Cluster state handler two-phase commit
public interface IClusterStateHandler
{
    Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct);
    void          Commit(object? prepareResult, ExecuteNodeOpIntent intent);
}

// Scenario translation contract
public interface IEntityScenarioTranslator
{
    bool   CanTranslate(BitMask256 componentMask);
    void   Extract(Entity entity, ISimulationView view, JsonObject target);
    void   Inject(Entity entity, EntityRepository repo, JsonObject source,
                  IGuidResolver guidResolver);
}

// Gizmo definition contract
public interface IGizmoDefinition
{
    IReadOnlyList<Type> RequiredComponents { get; }
    void                Render(Entity entity, ISimulationView view,
                               GizmoExecutionController controller);
}
```

### Key concrete types

```csharp
// Entity template -- created by TkbDeserializer, stored in TkbDatabase
public class TkbTemplate
{
    public string Name        { get; }
    public long   TkbType     { get; }
    public string CategoryPath{ get; }
    // Domain descriptors attached dynamically by TkbDescriptorRegistry thunks
}

// Immutable blueprint runtime definition
public sealed record BlueprintDefinition
{
    public required string               Name          { get; init; }
    public required BlueprintDispatchKind Kind          { get; init; }
    public required ulong                StructureHash { get; init; }
    public required int                  StateSize     { get; init; }
    public InitDefaultDelegate?          InitDefault   { get; init; }
    public TickDelegate?                 Tick          { get; init; }
    public IReadOnlyDictionary<string, EventHandlerDelegate> EventHandlers { get; init; }
    public Type?                         StateClrType  { get; init; }
    public IReadOnlyDictionary<string, BlueprintFieldDescriptor> StateFields { get; init; }
    public Guid                          AssetId       { get; init; }
}

// Cluster state machine slave
public sealed class ClusterSlave : IDisposable
{
    public ClusterSlave(int nodeId, string subsystemName, FdpEventBus? eventBus = null);
    public void RegisterHandler(IClusterStateHandler handler);
    public void Tick();
    public void Dispose();
}

// Transition path planner (BFS)
public sealed class TransitionPlanner
{
    public TransitionPlanner(ITransitionGraph graph);
    public IReadOnlyList<int> CalculateShortestPath(int fromStateId, int toStateId);
}

// Transition graph builder (fluent)
public sealed class TransitionGraphBuilder
{
    public TransitionGraphBuilder AddState(int stateId, string debugName = "");
    public TransitionGraphBuilder AddTransition(int fromStateId, int toStateId);
    public ITransitionGraph       Build();
}

// Subsystem orchestrator
public class SubsystemOrchestrator
{
    public SubsystemOrchestrator(IEnumerable<ISubsystem> subsystems,
                                 RunnerOptions? options = null);
    public void Initialize();
    public void Run();
    public void Stop();
}

// Ghost entity creation
public class GhostCreationSystem : IEcsModuleSystem
{
    public bool   BypassLifecycle { get; set; }
    public Entity CreateGhost(EntityRepository repo, long networkId, uint tick = 0);
}

// DER concurrent entity repository
public class DerRepo : IDerRepo
{
    public event Action<IDerEntity>? EntityCreated;
    public event Action<IDerEntity>? EntityDeleted;
    public IDerEntity  CreateEntity(int entityId, long tkbType);
    public void        DeleteEntity(int entityId);
    public IDerEntity? GetEntity(int entityId);
    public IEnumerable<IDerEntity> GetAllEntities();
}
```

---

## Dependencies

### Project References

| Reference | Purpose |
|---|---|
| `Fdp.ModuleHost` | `IEcsModule`, `IEcsModuleSystem`, `ExecutionPolicy`, `ISystemRegistry`, `ISimulationView` |
| `Fdp.Diagnostics.Contracts` | Phase-1 primitive diagnostic protocol types |
| `Fdp.Diagnostics.Network` | DDS schema types for gizmo network protocol |
| `GizmoMap.Contracts` | ECS-free gizmo interaction interfaces (`IGizmoInteractionHandler`) |
| `Fbt.Kernel` | FastBTree behavior tree runtime |
| `Fhsm.Kernel` | FastHSM hierarchical state machine runtime |
| `Fdp.Toolkits.Analyzers` | Roslyn analyzer: enforces `BlackboardMemoryLayout` size constraints at compile time (analyzer only, not linked) |
| `StructEdit.Core` / `StructEdit.Json` | Runtime struct inspection and JSON round-trip for `GizmoSettingsPublisherSystem` |

### NuGet Packages

| Package | Version | Usage |
|---|---|---|
| `CycloneDDS.NET` | 0.2.2 | DDS publish/subscribe for replication, time sync, gizmo network |
| `CommandLineParser` | 2.9.1 | CLI argument parsing in runner processes |
| `Microsoft.Extensions.Logging` | 8.0.0 | Structured logging abstractions |
| `Newtonsoft.Json` | 13.0.3 | Legacy JSON support (scenario serialization compat path) |
| `NLog` | 5.2.8 | Concrete logging implementation for runner processes |

### InternalsVisibleTo

The assembly grants internal access to:
`Fdp.Toolkits.Tests`, `FDP.Toolkit.Behavior.Tests`, `FDP.Toolkit.Orchestration.Tests`,
`FDP.Toolkit.Replication.Tests`, `Hrot.IG.Tests`, `Fdp.Presentation.Tests`, `Hrot.Blueprints.Tests`

---

## Usage Examples

### Example 1 -- Bootstrapping a simulation subsystem with TKB and Replication

```csharp
// 1. Load TKB from disk
var tkbDatabase = new TkbDatabase();
var loader = new TkbUnifiedLoader(new RawDirectoryTkbProvider("Data/Tkb"));
loader.LoadAll(tkbDatabase);

// 2. Build entity lifecycle module
var lifecycleModule = new EntityLifecycleModule(
    tkb: tkbDatabase,
    participatingModuleIds: new[] { SimHostNodeId, IgNodeId },
    timeoutFrames: 300,
    localNodeId: SimHostNodeId,
    translators: myTranslators);

// 3. Build replication module
var networkEntityMap = new NetworkEntityMap();
var replicationModule = new ReplicationLogicModule(
    entityMap: networkEntityMap,
    tkbDatabase: tkbDatabase,
    lifecycleModule: lifecycleModule,
    translators: myTranslators);

// 4. Register modules with the module host
moduleHost.AddModule(lifecycleModule);
moduleHost.AddModule(replicationModule);
moduleHost.AddModule(new GroundKinematicsModule());
moduleHost.AddModule(new CognitiveRuntimeModule(behaviorRegistry));
```

### Example 2 -- Configuring the cluster orchestration state machine

```csharp
// 1. Build a state-transition graph for the cluster
var graph = new TransitionGraphBuilder()
    .AddTransition((int)ClusterState.Idle,           (int)ClusterState.Loading)
    .AddTransition((int)ClusterState.Loading,        (int)ClusterState.RunningLive)
    .AddTransition((int)ClusterState.RunningLive,    (int)ClusterState.Idle)
    .AddTransition((int)ClusterState.RunningLive,    (int)ClusterState.RunningReplay)
    .AddTransition((int)ClusterState.RunningReplay,  (int)ClusterState.RunningLive)
    .Build();

// 2. Find the path to transition from Idle to RunningLive
var planner = new TransitionPlanner(graph);
var path = planner.CalculateShortestPath(
    (int)ClusterState.Idle,
    (int)ClusterState.RunningLive);
// path == [ (int)ClusterState.Loading, (int)ClusterState.RunningLive ]

// 3. Create a ClusterSlave and register handlers
var eventBus = new FdpEventBus();
var slave = new ClusterSlave(nodeId: 1, subsystemName: "SimHost", eventBus: eventBus);
slave.RegisterHandler(new ReferenceScenarioLoadHandler(scenarioLoader, storageProvider));
slave.RegisterHandler(new ReferenceLiveLoadHandler());

// 4. Tick the slave each frame to process pending intents
slave.Tick();
```

### Example 3 -- Assigning an AI behavior to an entity

```csharp
// During startup -- register a BTree behavior definition
var registry = new BehaviorRegistry();
registry.Register(new BehaviorDefinition
{
    Name        = "Patrol",
    BrainTier   = BehaviorConstants.BrainTierBTree,
    BTreeInterpreter = BuildPatrolBTree(),
    ParseParams = (json, ptr) => ParsePatrolParams(json, ptr),
    ParamsDtoType = typeof(PatrolParams)
});

// At runtime -- assign behavior via event bus
repo.Bus.Publish(new AssignBehaviorEvent
{
    Entity = soldierEntity,
    BehaviorName = "Patrol",
    ParametersJson = @"{ ""RouteId"": 42 }"
});

// The BehaviorIngressSystem (next frame) picks up the event,
// looks up "Patrol" in BehaviorRegistry, writes ParseParams into
// BrainBlackboard, and sets BehaviorState.BrainTier = BrainTierBTree.
// The BTreeTickSystem then steps the interpreter every frame.
```

### Example 4 -- Saving and loading a scenario snapshot

```csharp
// Build a serializer for "SimHost" subsystem
var serializer = new ScenarioSerializerBuilder("SimHost")
    .AddTranslator(new VehicleScenarioTranslator())
    .AddTranslator(new CombatScenarioTranslator())
    .Build();

// Save
string json = serializer.Serialize(repo);
File.WriteAllText("scenario.json", json);

// Load (two-pass: entities created first, then components injected)
var loadedRepo = new EntityRepository();
// ... register components ...
serializer.Deserialize(loadedRepo, File.ReadAllText("scenario.json"));
```

### Example 5 -- Registering a diagnostic gizmo

```csharp
// Define a gizmo that draws a health bar above combat entities
public class HealthBarGizmo : IGizmoDefinition
{
    public IReadOnlyList<Type> RequiredComponents
        => new[] { typeof(Health), typeof(SimTransform) };

    public void Render(Entity entity, ISimulationView view,
                       GizmoExecutionController controller)
    {
        ref readonly var health = ref view.GetComponentRO<Health>(entity);
        ref readonly var tf     = ref view.GetComponentRO<SimTransform>(entity);
        float fraction = (float)health.Current / health.Max;
        DrawProgressBar(tf.Position, fraction);
    }
}

// At startup -- register before first tick
var gizmoRegistry = new GizmoRegistry();
gizmoRegistry.Register(new HealthBarGizmo());
// DataDrivenGizmoSystem automatically queries entities matching the component bitmask
// and calls Render each frame.
```

---

## Best Practices

### 1. Module composition over direct system registration

Always wrap related systems in an `IEcsModule`. The `RegisterSystems` method is the
correct place to express ordering via array position or `[UpdateAfter]`. Never
register systems directly with the kernel from application code -- this bypasses
module-level dependency injection.

### 2. Zero-allocation hot paths

Systems in `SystemPhase.Simulation` (BTree tick, formation, spatial hash) run every
frame on potentially thousands of entities. Follow existing patterns:
- Use `stackalloc` for small per-frame buffers (see `VisionBroadphaseSystem`).
- Avoid `new` inside `Execute`; pre-allocate in constructor or use pools.
- Keep `Execute` methods free of LINQ; use query builder + `foreach`.

### 3. Snapshot-on-Demand (SoD) discipline for async systems

Systems with `[UpdateInPhase(SystemPhase.Manual)]` run on a background thread against
a read-only snapshot. They must:
- Only call `view.GetComponentRO<T>` -- never `GetComponentRW`.
- Write results exclusively via `view.GetCommandBuffer().PublishEvent(...)`.
- Never capture or store the `view` reference beyond a single `Execute` call.

### 4. TKB descriptors via `[TkbDescriptor]` attribute

Domain-specific TKB data must be declared as `record` types annotated with
`[TkbDescriptor("HierarchicalName")]`. The source generator in `Fdp.Toolkits.Analyzers`
emits a `[ModuleInitializer]` that registers the parser thunk with `TkbDescriptorRegistry`
at startup. Do not call `TkbDescriptorRegistry.RegisterParser` manually.

### 5. Gizmo registration must precede first ECS tick

`GizmoRegistry.Register` calls `ComponentTypeRegistry.GetId` to pre-compute bitmasks.
All component types must be registered with the `EntityRepository` before any gizmos
are registered. Register gizmos in the composition root, after `repo.RegisterComponent<T>()`
calls and before `moduleHost.Start()`.

### 6. Cluster handlers follow two-phase commit

`IClusterStateHandler.PrepareAsync` may run async work (disk I/O, DDS handshake). It
must be idempotent with respect to the same `TransactionId`. `Commit` is called on the
main simulation thread once `PrepareAsync` completes. Long-running prepares must respect
the `CancellationToken`.

### 7. Network authority before mutation

All systems that write position or behavior state must guard with an authority check:
```csharp
if (!repo.IsOwned(entity)) continue;
```
Ghost entities (remote nodes own them) must not have their authoritative components
modified locally. Use `AuthorityExtensions.IsOwner(entity, view)` for readable guard
expressions.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fdp.Core` | Direct dependency -- ECS kernel, entity repository, event bus |
| `Fdp.ModuleHost` | Direct dependency -- module lifecycle, system registry, execution policy |
| `Fdp.Toolkits.Analyzers` | Analyzer dependency -- compile-time blackboard size checking |
| `Fdp.Toolkits.Tests` | Test project for this assembly |
| `FDP.Toolkit.Behavior.Tests` | Behavior-subsystem integration tests |
| `FDP.Toolkit.Orchestration.Tests` | Orchestration-subsystem integration tests |
| `FDP.Toolkit.Replication.Tests` | Replication-subsystem integration tests |
| `FDP.Toolkit.DER` | Higher-level DER toolkit built on top of Fdp.Toolkits DER subsystem |
| `FDP.Toolkit.DER.Tests` | Tests for FDP.Toolkit.DER |
| `Fdp.Engine` (Toolkits) | Standalone engine integrating multiple Fdp.Toolkits modules |
| `Fdp.Engine.Tests` | Tests for Fdp.Engine |
| `Fdp.Presentation` | UI layer consuming gizmo and diagnostic APIs from this assembly |
| `Fdp.Presentation.Tests` | Tests for Fdp.Presentation |
| `Fdp.Diagnostics.Contracts` | Primitive diagnostic protocol types consumed here |
| `Fdp.Diagnostics.Network` | DDS schema types for gizmo protocol |
| `GizmoMap.Contracts` | Gizmo interaction interface contracts |
| `Fbt.Kernel` (FastBTree) | BTree behavior runtime |
| `Fhsm.Kernel` (FastHSM) | HSM behavior runtime |
| `StructEdit.Core` / `StructEdit.Json` | Struct inspector used by gizmo settings publisher |
| `Hrot.*` (Hrot layer) | Consumer of almost every domain in this assembly |
| `Fdp.Examples.*` | Example applications demonstrating Fdp.Toolkits usage |
