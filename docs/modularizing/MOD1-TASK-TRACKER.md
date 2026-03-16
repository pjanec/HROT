# MOD1 Task Tracker — Modularising SimHost

**Reference:** See [MOD1-TASK-DETAIL.md](./MOD1-TASK-DETAIL.md) for detailed task descriptions  
**Design:** See [MOD1-DESIGN.md](./MOD1-DESIGN.md) for architecture and phase goals  
**Debt tracker:** See [MOD1-DEBT-TRACKER.md](./MOD1-DEBT-TRACKER.md)

---

## Phase 1 — CQRS Navigation Contract + Authority Bug Fixes

**Goal:** Introduce the engine-agnostic `NavigationIntent`/`NavigationStatus` ECS components (in **`FDP.Toolkit.Navigation`**; Cartesian `Vector2` destination; toolkit ID block 20–49) and matching DDS descriptors (in `Bagira.BDC.SSTD`). Apply the **dual-enum pattern**: engine-side `NavigationMode`/`NavigationResult` in `FDP.Toolkit.Navigation`; DDS wire `ENavigationMode`/`ENavigationResult` in `Bagira.BDC.SSTD`. Fix legacy `PrimaryOwnerId` authority guard bugs that break split-authority deployments. `MoveToExecutor` writes raw Cartesian coordinates — geo conversion is the translator’s responsibility.

- [x] **MOD1-P1T1** Define `NavigationIntent` and `NavigationStatus` ECS components + DDS descriptors [details](./MOD1-TASK-DETAIL.md#mod1-p1t1--define-navigationintent-and-navigationstatus-ecs-components--dds-descriptors) *(contracts in `Fdp.Kernel`, IDs 67/68)*
- [x] **MOD1-P1T2** Refactor `MoveToExecutor` to CQRS Pattern [details](./MOD1-TASK-DETAIL.md#mod1-p1t2--refactor-movetoexecutor-to-cqrs-pattern)
- [x] **MOD1-P1T3** Fix authority guard bugs in Geographic Systems [details](./MOD1-TASK-DETAIL.md#mod1-p1t3--fix-authority-guard-bugs-in-geographic-systems)
- [x] **MOD1-P1T4** Add navigation fulfillment logic to `CarKinematicsSystem` [details](./MOD1-TASK-DETAIL.md#mod1-p1t4--add-navigation-fulfillment-logic-to-carkinematicssystem) *(as standalone `NavigationExecutionSystem`)*

---

## Phase 2 — Brain & Muscle Module Decomposition

**Goal:** Break the monolithic `SimulationLogicModule` into five focused `IModule` implementations. Cognitive modules (`MissionControlModule`, `CognitiveRuntimeModule`, `ActionDispatchModule`) land in **`FDP.Toolkit.Behavior`**; `GroundKinematicsModule` lands in **`FDP.Toolkit.CarKinem`**; `CombatModule` stays in `Bagira.SimHost.Modules` (Bagira weapon domain). See §2.5.

- [x] **MOD1-P2T1** Create `MissionControlModule` [details](./MOD1-TASK-DETAIL.md#mod1-p2t1--create-missioncontrolmodule)
- [x] **MOD1-P2T2** Create `CognitiveRuntimeModule` [details](./MOD1-TASK-DETAIL.md#mod1-p2t2--create-cognitiveruntimemodule)
- [x] **MOD1-P2T3** Create `ActionDispatchModule` [details](./MOD1-TASK-DETAIL.md#mod1-p2t3--create-actiondispatchmodule)
- [x] **MOD1-P2T4** Create `GroundKinematicsModule` [details](./MOD1-TASK-DETAIL.md#mod1-p2t4--create-groundkinematicsmodule)
- [x] **MOD1-P2T5** Refactor `SimulationLogicModule` as delegation facade [details](./MOD1-TASK-DETAIL.md#mod1-p2t5--refactor-simulationlogicmodule-as-delegation-facade)

---

## Phase 3 — Network Translator Packs + Node Bootstrapper

**Goal:** Replace the God-Class initialisation in `SimHostApp.OnLoad` with declarative `NodeRole`-based composition via `NodeBootstrapper`. Deliver **full concrete translator implementations** for `NavigationIntent`/`NavigationStatus`, DDS discovery config files, command-line role selection, and entity lifecycle coordination — making Brain/Muscle/Perception/NavigationSolver separate-process deployment a first-class outcome of MOD1.

- [x] **MOD1-P3T1** Create domain-specific translator packs [details](./MOD1-TASK-DETAIL.md#mod1-p3t1--create-domain-specific-translator-packs)
- [x] **MOD1-P3T2** Create domain-specific component registries [details](./MOD1-TASK-DETAIL.md#mod1-p3t2--create-domain-specific-component-registries)
- [x] **MOD1-P3T3** Create `NodeRole` and `NodeBootstrapper` [details](./MOD1-TASK-DETAIL.md#mod1-p3t3--create-noderole-and-nodebootstrapper)
- [x] **MOD1-P3T4** Implement concrete navigation translator classes (`NavigationIntent` + `NavigationStatus` egress/ingress) [details](./MOD1-TASK-DETAIL.md#mod1-p3t4--implement-concrete-navigation-translator-classes)
- [x] **MOD1-P3T5** DDS discovery config + entry-point role selection (`--role` flag, `NodeConfiguration`, XML config files) [details](./MOD1-TASK-DETAIL.md#mod1-p3t5--dds-discovery-config--entry-point-role-selection)

> **Entity lifecycle across process boundaries** is handled by the existing `EntityMasterEgressTranslator` / `EntityMasterIngressTranslator` + `GhostCreationSystem` from `FDP.Toolkit.Replication` — no extra task required.

---

## Phase 4 — Presentation Module Split + Dynamic Perspective Switching

**Goal:** Wrap the IG and SimHost 2-D map presentations in formal `IModule` implementations with dynamic runtime switching in all-in-one deployments.

- [x] **MOD1-P4T1** Create `IgPresentationModule` and `SimPresentationModule` [details](./MOD1-TASK-DETAIL.md#mod1-p4t1--create-igpresentationmodule-and-simpresentationmodule)
- [x] **MOD1-P4T2** `ActivePerspective` singleton + `PerspectiveCoordinatorSystem` [details](./MOD1-TASK-DETAIL.md#mod1-p4t2--activeperspective-singleton--perspectivecoordinatorsystem)

---

## Phase 5 — Component ID Registry Split

**Goal:** Move all Bagira-specific component ID constants out of `Fdp.Kernel.GlobalComponentIds` into a single `BagiraComponentIds` class in `Bagira.Map.Definitions`. Two registries only: FDP owns `GlobalComponentIds`; Bagira owns `BagiraComponentIds`.

- [x] **MOD1-P5T1** Create `BagiraComponentIds` in `Bagira.Map.Definitions`; migrate all Bagira-owned `[ComponentId]` usages [details](./MOD1-TASK-DETAIL.md#mod1-p5t1--create-bagiracomponentids-in-bagiramapdefinitions)

---

## Phase 6 — Distributed Perception & Pathfinding Modules

**Goal:** Modularise the perception pipeline and pathfinding. `AutonomousPerceptionModule`, `PhysicsQueryModule`, `SensorModality`, and per-modality receptor components land in **`FDP.Toolkit.Perception`**; `NavigationSolverModule`, `PathfindingBatchData`, and `PathfindingSolverSystem` land in **`FDP.Toolkit.Navigation`**. Translator packs (Bagira DDS schema) stay in `Bagira.SimHost.Network`. Wire `BTreeContext` stubs to real singletons.

- [x] **MOD1-P6T1** Add `SensorModality` bitmask to `TargetMemory` + per-modality receptor components [details](./MOD1-TASK-DETAIL.md#mod1-p6t1--add-sensormodality-bitmask-to-targetmemory--per-modality-receptor-components)
- [x] **MOD1-P6T2** Add DDS descriptors for perception & pathfinding [details](./MOD1-TASK-DETAIL.md#mod1-p6t2--add-dds-descriptors-for-perception--pathfinding)
- [x] **MOD1-P6T3** Add `PathfindingBatchData` ECS singleton [details](./MOD1-TASK-DETAIL.md#mod1-p6t3--add-pathfindingbatchdata-ecs-singleton)
- [x] **MOD1-P6T4** Wire `BTreeContext.RequestRaycast` / `GetRaycastResult` to `RaycastBatchData` [details](./MOD1-TASK-DETAIL.md#mod1-p6t4--wire-btreecontextrequestraycast--getraycastresult-to-raycastbatchdata)
- [x] **MOD1-P6T5** Wire `BTreeContext.RequestPath` / `GetPathResult` to `PathfindingBatchData` [details](./MOD1-TASK-DETAIL.md#mod1-p6t5--wire-btreecontextrequestpath--getpathresult-to-pathfindingbatchdata)
- [x] **MOD1-P6T6** Create `AutonomousPerceptionModule` and `PhysicsQueryModule` [details](./MOD1-TASK-DETAIL.md#mod1-p6t6--create-autonomousperceptionmodule-and-physicsquerymodule)
- [x] **MOD1-P6T7** Create `NavigationSolverModule` [details](./MOD1-TASK-DETAIL.md#mod1-p6t7--create-navigationsolvermodule)
- [x] **MOD1-P6T8** Create perception & pathfinding translator packs [details](./MOD1-TASK-DETAIL.md#mod1-p6t8--create-perception--pathfinding-translator-packs)

---

## Phase 7 — IG Ground Clamping Module

**Goal:** Solve the Heterogeneous Terrain Correlation problem. Generic terrain-query types (`TerrainQueryBatchData`, systems, `ITerrainProvider`, `EClampingMode`, `GroundClampingConfig/State`) land in **`FDP.Toolkit.Geographic`**. `IgGroundClampingModule` (IG-specific wiring) stays in `Bagira.IG`. DDS contract (`GroundClampingOverride`) stays in `Bagira.BDC.SSTD`.

- [x] **MOD1-P7T1** `GroundClampingOverride` DDS descriptor + `EClampingMode` enum [details](./MOD1-TASK-DETAIL.md#mod1-p7t1--groundclampingoverride-dds-descriptor--eclampingmode-enum)
- [x] **MOD1-P7T2** ECS components: `GroundClampingConfig`, `GroundClampingState`, `TerrainQueryBatchData` [details](./MOD1-TASK-DETAIL.md#mod1-p7t2--ecs-components-groundclampingconfig-groundclampingstate-terrainquerybatchdata)
- [x] **MOD1-P7T3** `ITerrainProvider` interface + `GroundClampingOverrideTranslator` [details](./MOD1-TASK-DETAIL.md#mod1-p7t3--iterrainprovider-interface--groundclampingoverridetranslator)
- [x] **MOD1-P7T4** Three-phase execution systems (`Submit`, `Solver`, `Resolution`, `Initialization`) [details](./MOD1-TASK-DETAIL.md#mod1-p7t4--three-phase-execution-systems)
- [x] **MOD1-P7T5** `IgGroundClampingModule` + `TransformSyncSystem` Z-offset application [details](./MOD1-TASK-DETAIL.md#mod1-p7t5--iggroundclampingmodule--transformsyncsystem-z-offset-application)

---

## Phase 8 — Recording/Replay Module Architecture

**Goal:** `RecordingModule`, `ReplayModule`, `StoryRecorderModule`, `RecordingConfiguration`, `StoryTag`, `StoryReplayTag` land in **`FDP.Toolkit.Replay`** (generic; purely ECS memory). `EcsRecordReplayController` stays in `Bagira.SimHost.Modules.Orchestration` (Bagira `IDsmHandler` / DSM binding). Achieves zero-cost idle path, concurrent per-story I/O isolation, and ACID-safe `Dispose()`.

- [x] **MOD1-P8T1** `RecordingConfiguration` + `EcsRecordReplayController` skeleton [details](./MOD1-TASK-DETAIL.md#mod1-p8t1--recordingconfiguration--ecsrecordreplaycontroller-skeleton)
- [x] **MOD1-P8T2** `RecordingModule` + `RecorderSystem.EntityFilter` extension [details](./MOD1-TASK-DETAIL.md#mod1-p8t2--recordingmodule--recordersystementityfilter-extension)
- [x] **MOD1-P8T3** `StoryRecorderModule` + `StoryTag` / `StoryReplayTag` components [details](./MOD1-TASK-DETAIL.md#mod1-p8t3--storyrecordermodule--storytag--storyreplaytag-components)
- [x] **MOD1-P8T4** `ReplayModule` [details](./MOD1-TASK-DETAIL.md#mod1-p8t4--replaymodule)
- [x] **MOD1-P8T5** `NodeBootstrapper` integration + `DrillSlave` registration [details](./MOD1-TASK-DETAIL.md#mod1-p8t5--nodebootstrapper-integration--drillslave-registration)

---

## Phase 9 — `FDP.Framework.Runner` — Generic Application Lifecycle Toolkit

**Goal:** Extract application orchestration infrastructure (`ISubsystem`, `SubsystemOrchestrator`, `WaitingRoomCoordinator`, `HeadlessTestExecutor`, test models, and generic handlers) into a new **`FDP.Framework.Runner`** toolkit. Remove all three Bagira coupling points from `SubsystemOrchestrator` (hardcoded construction, hardcoded UI colours, hardcoded menu buttons). `Bagira.Runner` becomes a pure composition root that wires concrete subsystems and domain-specific test handlers into the generic framework.

- [x] **MOD1-P9T1** Create `FDP.Framework.Runner` project + extract `ISubsystem` / `IMapCameraProvider` (add `TitleBarColor`) [details](./MOD1-TASK-DETAIL.md#mod1-p9t1--create-fdpframeworkrunner-project--extract-isubsystem--imapcameraprovider)
- [x] **MOD1-P9T2** Refactor `SubsystemOrchestrator` into `FDP.Framework.Runner` (remove `BuildSubsystems`, hardcoded colours, hardcoded menu) [details](./MOD1-TASK-DETAIL.md#mod1-p9t2--refactor-subsystemorchestrator-into-fdpframeworkrunner)
- [x] **MOD1-P9T3** Extract `WaitingRoomCoordinator` and `RunnerConfiguration` base into `FDP.Framework.Runner` [details](./MOD1-TASK-DETAIL.md#mod1-p9t3--extract-waitingroomcoordinator-and-runnerconfiguration-into-fdpframeworkrunner)
- [x] **MOD1-P9T4** Extract `HeadlessTestExecutor` core + generic action handlers into `FDP.Framework.Runner` [details](./MOD1-TASK-DETAIL.md#mod1-p9t4--extract-headlesstestexecutor-core--generic-action-handlers-into-fdpframeworkrunner)
- [x] **MOD1-P9T5** Refactor `Bagira.Runner` as pure composition root [details](./MOD1-TASK-DETAIL.md#mod1-p9t5--refactor-bagirarunner-as-pure-composition-root)


