# Hrot.CGF

**Project Path:** `Hrot/Subsystems/Hrot.CGF/Hrot.CGF.csproj`
**Assembly:** `Hrot.CGF`
**Root Namespace:** `Hrot.CGF`
**Target Framework:** net8.0
**Date:** 2026-05-23

---

## README Validation

**Status: Missing**

No `README.md` exists in `Hrot/Subsystems/Hrot.CGF/`. This document serves as the authoritative
architectural reference for the project.

---

## Executive Overview

### What is CGF?

CGF stands for **Computer Generated Forces** -- a standard military simulation term for entities
whose behaviour is driven by automated AI rather than human operators. In the HROT distributed
simulation architecture, the CGF node is the **Brain-tier** participant: it owns all
cognitive state (behavior trees, hierarchical state machines, mission plans, threat memory)
for every simulated vehicle, while the **Muscle-tier** nodes (SimHost instances) own the
kinematics (position, velocity, physics) and network replication state.

The split Authority model follows the FDP (Framework for Distributed Processing) pattern:

- **Brain (CGF, node 400 by default):** `BehaviorState`, `BrainBlackboard`, `BrainBTreeState`,
  `MissionPlanQueue`, `ActiveMissionPlan`, `TargetMemory`.
- **Muscle (SimHost):** `SimTransform`, `NetworkTransform`, `WorldPos`, `NavigationStatus`,
  `NavigationIntent`, `PhysicsState`.

### Role in the Distributed Simulation

The CGF subsystem participates in the cluster as a **ClusterSlave** node coordinated by the
orchestrator (ExCon / ClusterMaster). It:

1. Receives and processes `NodeOpCommand` orchestration messages over DDS (CycloneDDS).
2. Loads scenario and episode entities into the ECS world via the genesis pipeline.
3. Executes Brain-tier AI logic (mission adapter, behavior tree tick, threat evaluation,
   route context, unit hierarchy, combat dispatch) every simulation frame.
4. Participates in distributed lockstep time synchronisation via `SlaveSyncController`.
5. Allocates network entity IDs and acts as the **default processor** for broadcast
   `CreateEntityRequest` messages from ExCon.
6. Participates (at the ACK level) in recording and replay cluster handshakes.

---

## Architecture

### Layered Architecture Overview

The subsystem is structured into four architectural layers:

```
+========================================================+
|              Composition Root / Runner                 |
|  CgfSubsystem (ISubsystem) or CgfApplication         |
+========================================================+
           |                          |
           v                          v
+---------------------+    +---------------------+
|   Orchestration     |    |   ECS Simulation    |
|   Control Plane     |    |   Data Plane        |
|   (ClusterSlave)    |    |   (ModuleHostKernel)|
+---------------------+    +---------------------+
           |                          |
           v                          v
+---------------------+    +---------------------+
|  Load Handlers      |    |  CgfLogicPack       |
|  CgfScenario/       |    |  (Brain-tier AI)    |
|  CgfEpisode         |    |                     |
+---------------------+    +---------------------+
           |                          |
           v                          v
+---------------------+    +---------------------+
|  Genesis Pipeline   |    |  ECS World          |
|  CreateEntity/      |    |  (EntityRepository) |
|  DeleteEntity       |    |                     |
+---------------------+    +---------------------+
```

### Control Plane vs. Data Plane

The CGF node maintains two distinct execution planes with separate event buses:

```
+-------------------+   DDS ingress    +-------------------+
|  SlaveTranslator  |----------------->|  _orchestrationBus|
|  (DDS -> Bus)     |                  |  (Control Plane)  |
+-------------------+                  +-------------------+
                                                |
                                                v
                                       +-------------------+
                                       |  ClusterSlave     |
                                       |  .Tick()          |
                                       +-------------------+
                                                |
                                      Enqueue entity requests
                                                |
                                                v
+-------------------+   DDS ingress    +-------------------+
|  TimeTranslators  |----------------->|  _eventBus        |
|  (Lockstep/Time)  |                  |  (Data Plane)     |
+-------------------+                  +-------------------+
                                                |
                                                v
                                       +-------------------+
                                       |  ModuleHostKernel |
                                       |  .Update()        |
                                       +-------------------+
```

### Brain-tier AI Execution Pipeline

Each simulation frame, the CGF Brain executes systems in this fixed order:

```
Input Phase:
+-----------------------------+
| MissionControlExecutionSystem|  <-- reads network mission commands
+-----------------------------+
| MissionControlModule        |  <-- behavior ingress (assign/clear events)
|  (BehaviorIngressSystem)    |
+-----------------------------+
| CognitiveRuntimeModule      |  <-- BTree/HSM tick engine
|  (InputSystems)             |
+-----------------------------+

Simulation Phase:
+-----------------------------+
| MissionAdapterSystem        |  <-- MissionPlanQueue -> AssignTacticalIntentEvent
+-----------------------------+
| TacticalIntentResolutionSystem| <-- IntentId -> AssignBehaviorEvent (mapper or passthrough)
+-----------------------------+
| MissionControlModule        |  <-- behavior state machine
|  (SimulationSystems)        |
+-----------------------------+
| HealthApplicationSystem     |  <-- apply incoming damage
+-----------------------------+
| ActiveSensorTracksUpdateSystem| <-- update sensor track visibility
+-----------------------------+
| CgfThreatEvaluationSystem   |  <-- threat score boost/decay per frame
+-----------------------------+
| CognitiveRuntimeModule      |  <-- BTree execution, channel writes
|  (SimulationSystems)        |
+-----------------------------+
| ActionDispatchModule        |  <-- locomotion + weapon executors
|  (SimulationSystems)        |
+-----------------------------+
| RouteContextSystem          |  <-- waypoint ExtensionJson -> BrainBlackboard
+-----------------------------+
| UnitHierarchySystem         |  <-- unit hierarchy maintenance
+-----------------------------+
```

### Scenario Load Pipeline

When the cluster transitions through `PrepareLive -> Commit -> PrepareState(OperatingLive)`:

```
ClusterSlave                  CgfScenarioLoadHandler
     |                                |
     |--- PrepareLive intent -------->|
     |                                | 1. Wait for prefetch (poll with retry)
     |                                | 2. StagingEntityExtractor.Extract()
     |                                |    a. Register all component types in staging repo
     |                                |    b. Deserialize JSON into staging EntityRepository
     |                                |    c. Pass 1: allocate network IDs
     |                                |    d. Pass 2: extract EntityCreationRequests
     |<-- Prepare complete -----------|
     |
     |--- PrepareState(OperatingLive)|
     |                                | Hold cluster until genesis complete:
     |                                |   - ScenarioEntityCreationRequestSource.IsEmpty
     |                                |   - No Constructing entities remain
     |                                |   - No transient Intent DTO components present
     |<-- Task completed, transition--|
     |
     |--- Commit ------------------>  |
                                      | Enqueue EntityCreationRequests into source
                                      | -> CreateEntityRequestSystem processes each tick
                                      |    (max 500 per tick to stay within frame budget)
```

---

## Source Structure

### Namespace and File Map

```
Hrot.CGF/
  CgfApplication.cs          -- Standalone node (headless / test / ClusterRunner)
  CgfSubsystem.cs            -- Runner-hosted subsystem (Raylib + ImGui UI)
  CgfLogicPack.cs            -- Brain-tier AI module composite
  CgfComponentRegistry.cs    -- ECS component registration bootstrap

  Configuration/
    CgfBehaviorIds.cs        -- Compile-time constants for behavior IDs (3001-3099)
    CgfBehaviorSetup.cs      -- Dynamic load of Hrot.AI.Behaviors.dll; remapper factory

  Components/
    MissionAdapterState.cs   -- Transient per-entity phase-tracking component

  Systems/
    CreateEntityRequestSystem.cs       -- Genesis: entity creation from requests
    DeleteEntityRequestSystem.cs       -- Genesis: entity deletion from requests
    EntityRequestFinalizationSystem.cs -- Two-ACK lifecycle tracker (PostSimulation)
    CgfThreatEvaluationSystem.cs       -- Brain-tier wrapper for ThreatEvaluationSystem
    MissionAdapterSystem.cs            -- MissionPlanQueue -> AssignTacticalIntentEvent
    TacticalIntentResolutionSystem.cs  -- IntentId -> AssignBehaviorEvent (mapper/passthrough)

    Routing/
      RouteContextSystem.cs            -- Waypoint ExtensionJson -> BrainBlackboard
      BlackboardOffsets.cs             -- Named offsets for route-context blackboard slots

  Orchestration/
    StagingEntityExtractor.cs          -- Two-pass entity extractor (JSON -> EntityCreationRequest)

    Handlers/
      CgfScenarioLoadHandler.cs        -- Scenario PrepareLive/Commit/PrepareState handler
      CgfEpisodeLoadHandler.cs         -- Episode StartEpisode/StopEpisode handler

  Modules/
    Orchestration/
      CgfRecordReplayController.cs     -- Phase-3 skeleton record/replay controller
      Handlers/
        FailLoudRecordReplayStub.cs    -- Fail-loud stub for unsupported R/R operations

  Gizmos/
    CgfEntityPresentationGizmo.cs      -- [GizmoProjector] visual gizmo for CGF entities
```

### Class Summary

| Class | Namespace | Kind | Role |
|---|---|---|---|
| `CgfApplication` | `Hrot.CGF` | `sealed class` | Standalone CGF node (headless path) |
| `CgfSubsystem` | `Hrot.CGF` | `sealed class` | Runner-hosted CGF subsystem with UI |
| `CgfLogicPack` | `Hrot.CGF` | `sealed class` | Composite Brain-tier AI module |
| `CgfComponentRegistry` | `Hrot.CGF` | `static class` | ECS component registration |
| `CgfBehaviorIds` | `Hrot.CGF.Configuration` | `static class` | Behavior ID constants |
| `CgfBehaviorSetup` | `Hrot.CGF.Configuration` | `static class` | AI assembly dynamic loader |
| `MissionAdapterState` | `Hrot.CGF.Components` | `struct` | Transient phase-change tracking |
| `CreateEntityRequestSystem` | `Hrot.CGF.Systems` | `class` | Network entity creation handler |
| `DeleteEntityRequestSystem` | `Hrot.CGF.Systems` | `class` | Network entity deletion handler |
| `EntityRequestFinalizationSystem` | `Hrot.CGF.Systems` | `class` | Phase-2 ACK dispatcher |
| `CgfThreatEvaluationSystem` | `Hrot.CGF.Systems` | `sealed class` | Brain threat model adapter |
| `MissionAdapterSystem` | `Hrot.CGF.Systems` | `class` | Mission plan phase dispatcher |
| `TacticalIntentResolutionSystem` | `Hrot.CGF.Systems` | `sealed class` | Intent-to-behavior translator |
| `RouteContextSystem` | `Hrot.CGF.Systems.Routing` | `sealed class` | Route waypoint JSON -> blackboard |
| `BlackboardOffsets` | `Hrot.CGF.Systems.Routing` | `static class` | Blackboard slot constants |
| `StagingEntityExtractor` | `Hrot.CGF.Orchestration` | `sealed class` | Two-pass scenario extractor |
| `CgfScenarioLoadHandler` | `Hrot.CGF.Orchestration.Handlers` | `sealed class` | Scenario load cluster handler |
| `CgfEpisodeLoadHandler` | `Hrot.CGF.Orchestration.Handlers` | `sealed class` | Episode load cluster handler |
| `CgfRecordReplayController` | `Hrot.CGF.Modules.Orchestration` | `sealed class` | R/R lifecycle skeleton |
| `FailLoudRecordReplayStub` | `Hrot.CGF.Modules.Orchestration.Handlers` | `sealed class` | Unsupported-op sentinel |
| `CgfEntityPresentationGizmo` | `Hrot.CGF.Gizmos` | `sealed class` | Entity position/shape gizmo |

---

## Public API Reference

### `CgfApplication`

Entry point for the headless / standalone CGF node. Used by `Hrot.ClusterRunner` and in
unit / integration tests without a Raylib window.

```csharp
public sealed class CgfApplication : IDisposable
```

**Constructor:**
```csharp
public CgfApplication(
    int domainId = 0,
    int nodeId = 400,
    DdsParticipant? participant = null,
    ScenarioSerializer? scenarioSerializer = null,
    string localTempRoot = OrchestrationConstants.DefaultStagingDirectory,
    INetworkFactory? networkFactory = null,
    CgfLogicPack? logicPack = null)
```

| Parameter | Description |
|---|---|
| `domainId` | DDS domain ID for all topics |
| `nodeId` | Node ID published in `NodeHeartbeat.NodeId`; defaults to 400 |
| `participant` | Optional pre-built DDS participant (composition root rule) |
| `scenarioSerializer` | When provided, wires CGF-authoritative scenario/episode handlers |
| `localTempRoot` | Staging directory root for pre-fetched scenario files |
| `networkFactory` | Protocol factory for Brain-role network translators |
| `logicPack` | Pre-built `CgfLogicPack`; when provided, togglable groups are registered |

**Members:**
```csharp
// Install a module before first Tick (throws if called after first Tick)
public void Install(IEcsModule module)

// Advance one application frame
public void Tick()

// Exposes ClusterSlave for test assertions
public ClusterSlave ClusterSlave { get; }

// Module names registered via Install()
public IReadOnlyList<string> InstalledModuleNames { get; }

// Internal: in-memory scenario entity creation source
internal ScenarioEntityCreationRequestSource ScenarioEntityCreationSource { get; }

public void Dispose()
```

---

### `CgfSubsystem`

Runner-hosted subsystem with optional Raylib/ImGui visualization. Implements `ISubsystem`
from `Fdp.Toolkit.Runner`.

```csharp
public sealed class CgfSubsystem
    : ISubsystem,
      Fdp.Toolkit.Runner.IMapCameraProvider,
      IWindowRegistrar,
      Hrot.Common.Diagnostics.Gizmos.IGizmoControllable
```

**Constructors:**
```csharp
public CgfSubsystem()
public CgfSubsystem(Hrot.Core.Network.INetworkFactory networkFactory)
```

**Key public members:**
```csharp
// ISubsystem
public string Name { get; }           // "CGF"
public System.Numerics.Vector4 TitleBarColor { get; }  // golden color
public void Initialize(SubsystemConfig config)
public void Update(float deltaTime)
public void DrawWorld()
public void DrawUI()

// IMapCameraProvider
public MapCameraView? GetCameraView()
public void ApplyCameraView(MapCameraView view)
public MapCamera? GetMapCamera()

// IWindowRegistrar
public void RegisterWindows(WindowManager windowManager)

// IGizmoControllable
GizmoExecutionController? GizmoController { get; }  // explicit interface

// Internal test hooks
internal NetworkEntityMap? GhostEntityMap { get; }
internal EntityRepository? World { get; }
internal BehaviorRegistry? TestHook_BehaviorRegistry { get; }
internal long TestHook_SpawnEntityWithSplitAuthority(long tkbType, int muscleNodeId)
internal GizmoExecutionController CgfGizmoController { get; }
```

---

### `CgfLogicPack`

Composite `IEcsModule` grouping the three Brain-tier modules and standalone CGF systems.

```csharp
public sealed class CgfLogicPack : IEcsModule
```

**Constructor:**
```csharp
public CgfLogicPack(
    BehaviorRegistry                    behaviorRegistry,
    NetworkEntityMap                    entityMap,
    ScenarioEntityCreationRequestSource scenarioSource,
    TacticalIntentMapperRegistry        mapperRegistry,
    VehicleAPI?                         vehicleApi = null)
```

**Members:**
```csharp
public string Name { get; }                // "CgfLogicPack"
public ExecutionPolicy Policy { get; }     // ExecutionPolicy.Synchronous()
public IReadOnlyList<IEcsModuleSystem> InputSystems { get; }
public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }
public void RegisterSystems(ISystemRegistry registry)  // no-op
public void Tick(ISimulationView view, float deltaTime) // no-op
internal ScenarioEntityCreationRequestSource ScenarioSource { get; }
```

**Contained sub-modules (Brain role):**

| Order | Module | Phase | Role |
|---|---|---|---|
| 1 | `MissionControlModule` | Input + Simulation | Behavior ingress, mission direction |
| 2 | `CognitiveRuntimeModule` | Input + Simulation | BTree/HSM tick, channel arbitration |
| 3 | `ActionDispatchModule` | Simulation | Locomotion + weapon executors |

**Standalone systems included:**
- `MissionControlExecutionSystem` (Input)
- `MissionAdapterSystem` (Simulation)
- `TacticalIntentResolutionSystem` (Simulation)
- `HealthApplicationSystem` (Simulation)
- `ActiveSensorTracksUpdateSystem` (Simulation)
- `CgfThreatEvaluationSystem` (Simulation)
- `RouteContextSystem` (Simulation)
- `UnitHierarchySystem` (Simulation)

---

### `CgfComponentRegistry`

```csharp
public static class CgfComponentRegistry
{
    public static void RegisterAll(EntityRepository world)
}
```

Calls into all sub-registries to bootstrap the ECS world with every component type the CGF
node reads or writes. Sub-registries invoked:

- `HrotSharedComponentRegistry.RegisterAll`
- `CognitiveComponentRegistry.RegisterAll`
- `HierarchyComponentRegistry.RegisterAll`
- `KinematicComponentRegistry.RegisterAll`
- `CombatComponentRegistry.RegisterAll`
- `PresentationComponentRegistry.RegisterAll`
- `RouteComponentRegistry.RegisterAll`
- `MissionComponentRegistry.RegisterAll`
- `ZoneComponentRegistry.RegisterAll`
- `NavigationSolverComponentRegistry.RegisterAll`
- `GenesisIntentRegistry.RegisterAll`
- Direct: `ActiveSensorTracks`, `MapDisplayComponent`, `MissionAdapterState`,
  `DamageAssessedEvent`, `WeaponFireIntent`, `SensorTrackStateEvent`

---

### `CgfBehaviorIds`

```csharp
public static class CgfBehaviorIds
```

Stable compile-time integer constants. Range: 3001-3099.

| Constant | Value | Behavior | Type |
|---|---|---|---|
| `MoveTo_BT` | 3001 | `"MoveToLocation"` | BTree |
| `FollowRoute_BT` | 3002 | `"FollowRoute"` | BTree |
| `JoinFormation_BT` | 3003 | `"JoinFormation"` | BTree |
| `Idle_HSM` | 3010 | `"Idle"` | HSM |
| `WanderMilitary_BT` | 3011 | `"WanderMilitary"` | BTree |
| `FireAtTarget_BT` | 3012 | `"FireAtTarget"` | BTree |

---

### `CgfBehaviorSetup`

```csharp
public static class CgfBehaviorSetup
{
    // Dynamically loads Hrot.AI.Behaviors.dll and populates registry via reflection
    public static void LoadFromAiAssembly(
        BehaviorRegistry registry,
        IGeographicTransform? geoTransform,
        NetworkEntityMap entityMap)

    // Creates ScenarioBehaviorRemapper with auto-registered DTO types
    public static ScenarioBehaviorRemapper CreateBehaviorRemapper()
}
```

`LoadFromAiAssembly` loads the AI assembly into a non-collectible `AssemblyLoadContext`
named `"AiBehaviors.Startup"`, invokes `AiBehaviorFactory.BuildRegistrationAction` via
reflection, and applies the returned action to the registry. This design means `Hrot.CGF`
has **no compile-time dependency** on `Hrot.AI.Behaviors`, enabling hot-reload in the editor.

---

### `MissionAdapterState`

```csharp
[ComponentId(129)]
[DataPolicy(DataPolicy.Transient)]
public struct MissionAdapterState
{
    public byte LastPhase;      // last evaluated mission phase index
    public uint LastPlanVersion; // hash of (BehaviorId XOR BehaviorParams) for re-commit detection
}
```

Transient component added lazily by `MissionAdapterSystem` on the entity's first tick. Never
serialised to scenario disk; new entities start without it, triggering immediate phase detection.

---

### `CreateEntityRequestSystem`

```csharp
[UpdateInPhase(SystemPhase.Input)]
public class CreateEntityRequestSystem : IEcsModuleSystem
{
    public const int MaxRequestsPerTick = 500;

    public CreateEntityRequestSystem(
        IEntityCreationRequestSource        requestSource,
        IEntityAckSink                      ackSink,
        ITkbDatabase                        tkbDb,
        INetworkIdAllocator                 idAllocator,
        int                                 localNodeId,
        JsonAttributeCompiler?              jsonAttributeCompiler = null,
        EntityRequestFinalizationSystem?    finalizationSystem    = null,
        bool                                isDefaultProcessor    = false,
        IOwnershipDistributionStrategy?     ownershipStrategy     = null)

    public int PendingQueueCount { get; }
    public void Execute(ISimulationView view, float deltaTime)
}
```

The CGF node always constructs this with `isDefaultProcessor: true`, making it the cluster's
single authoritative genesis processor for broadcast `CreateEntityRequest` messages
(where `OwnerAppInstanceId == 0`). All SimHost (Muscle) nodes use `isDefaultProcessor: false`.

**Two-ACK lifecycle:**

1. Phase-1 `InProgress` ACK: sent immediately on ingress (unblocks ExCon client).
2. Phase-2 `Success`/`EntityNotFound` ACK: sent by `EntityRequestFinalizationSystem` once
   the entity reaches `EntityLifecycle.Active` in the `NetworkEntityMap`.

---

### `DeleteEntityRequestSystem`

```csharp
[UpdateInPhase(SystemPhase.Input)]
public class DeleteEntityRequestSystem : IEcsModuleSystem
{
    public DeleteEntityRequestSystem(
        IEntityDeletionRequestSource requestSource,
        IEntityAckSink               ackSink,
        NetworkEntityMap             entityMap,
        EntityRequestFinalizationSystem finalizationSystem,
        int                          localNodeId = 0)

    public void Execute(ISimulationView view, float deltaTime)
}
```

Validates entity existence in `NetworkEntityMap`, sends Phase-1 ACK, registers for Phase-2
tracking, then publishes `DestroyEntityCommand` to trigger ELM teardown.

---

### `EntityRequestFinalizationSystem`

```csharp
[UpdateInPhase(SystemPhase.PostSimulation)]
public class EntityRequestFinalizationSystem : IEcsModuleSystem
{
    public EntityRequestFinalizationSystem(IEntityAckSink ackSink, NetworkEntityMap entityMap)
    internal void Track(long networkId, Guid requestId, RequestKind kind)
    public void Execute(ISimulationView view, float deltaTime)
}
```

Runs in `PostSimulation`, after all spawning and teardown is complete. Iterates tracked requests
and dispatches Phase-2 ACK when the lifecycle condition is satisfied.

---

### `CgfThreatEvaluationSystem`

```csharp
[UpdateInPhase(SystemPhase.Simulation)]
public sealed class CgfThreatEvaluationSystem : IEcsModuleSystem
```

Thin adapter that delegates to `ThreatEvaluationSystem` (from `Fdp.Toolkit.Perception`).
Runs immediately before `CognitiveRuntimeModule` in the simulation phase so that B-Trees
always evaluate against freshly updated threat scores. Reads `ActiveSensorTracks`, boosts
`TargetMemory`, and applies continuous score decay.

---

### `MissionAdapterSystem`

```csharp
[UpdateInPhase(SystemPhase.Simulation)]
public class MissionAdapterSystem : IEcsModuleSystem
```

Iterates all entities with `MissionPlanQueue + BehaviorState`. For each, it:

1. Lazily adds `MissionAdapterState` if absent (`LastPhase = byte.MaxValue`).
2. Detects phase exhaustion: publishes `ClearBehaviorEvent` once, caches result.
3. Detects phase change or plan re-commit (via `LastPlanVersion` hash): publishes
   `AssignTacticalIntentEvent` with the task's `BehaviorName` and `BehaviorParams` JSON.

Does NOT mutate `BehaviorState` or `BrainBlackboard` directly; delegates to
`TacticalIntentResolutionSystem` -> `BehaviorIngressSystem` (next frame).

---

### `TacticalIntentResolutionSystem`

```csharp
[UpdateInPhase(SystemPhase.Simulation)]
public sealed class TacticalIntentResolutionSystem : IEcsModuleSystem
{
    public TacticalIntentResolutionSystem(
        TacticalIntentMapperRegistry mapperRegistry,
        BehaviorRegistry behaviorRegistry)

    public void Execute(ISimulationView view, float deltaTime)
}
```

Reads all `AssignTacticalIntentEvent`s from the managed bus read buffer. For each event:

1. Authority gate: skip if `!repo.HasAuthority<BehaviorState>(entity)`.
2. Try mapper: `TacticalIntentMapperRegistry.TryGetMapper(intentId)` -> `mapper.TryMap(...)`.
3. Fallback: treat `IntentId` as a direct behavior name, log a warning if not found in registry.
4. Publish `AssignBehaviorEvent` (consumed by `BehaviorIngressSystem` next Input phase).

---

### `RouteContextSystem`

```csharp
[UpdateInPhase(SystemPhase.Simulation)]
public sealed class RouteContextSystem : IEcsModuleSystem
{
    public float TickIntervalSeconds { get; set; }  // default 0.5f
    public void Execute(ISimulationView view, float deltaTime)
}
```

Throttled to 0.5 s intervals. For each entity in `FollowRoute` mode with a `BrainBlackboard`:

1. Resolves the active `RoutePlan` (personal route takes priority over shared route).
2. Calculates the current waypoint segment index from `NavigationStatus.ProgressS`.
3. Parses `RouteWaypoint.ExtensionJson` and writes recognised keys into `BrainBlackboard`:
   - `"dangerLevel"` -> `BrainBlackboard.ExpectedThreatLevel` (clamped to byte range).

---

### `StagingEntityExtractor`

```csharp
public sealed class StagingEntityExtractor : IScenarioEntityExtractor
{
    public IReadOnlyList<EntityCreationRequest> Extract(
        ScenarioSerializer serializer,
        string json,
        INetworkIdAllocator idAllocator,
        Guid? episodeId = null,
        ScenarioBehaviorRemapper? behaviorRemapper = null)

    // Internal test hook
    internal Action? StagingRepositoryDisposedCallback { get; set; }
}
```

**Two-pass extraction algorithm:**

- **Pass 1 (ID allocation):** iterate all entities with `NetworkIdentity`; pre-allocate new
  network IDs; record old-to-new mapping for behavior param JSON patching.
- **Pass 2 (extraction):** classify each entity as *root* (no `PartMetadata`) or *structural
  child* (has `PartMetadata`). Root entities produce `EntityCreationRequest` objects with the
  exclusion mask applied; child entity components are added to the parent request's
  `ChildComponentOverrides` dictionary.

**Static exclusion mask** (components stripped from all requests):
`NetworkIdentity`, `NetworkAuthority`, `DescriptorOwnership`, `TkbIdentity`,
`GhostStateTracker`, `NetworkOwnership`, `PendingNetworkAck`.

The staging `EntityRepository` is always disposed after extraction, even on exception. The
staging repo is pre-populated with all globally registered component types via reflection on
`RegisterUnmanagedComponent<T>` / `RegisterManagedComponentInternal<T>`.

---

### `CgfScenarioLoadHandler`

```csharp
public sealed class CgfScenarioLoadHandler : ITickableClusterStateHandler
{
    public CgfScenarioLoadHandler(
        ScenarioSerializer serializer,
        IScenarioLoader scenarioLoader,
        StagingEntityExtractor extractor,
        ScenarioEntityCreationRequestSource source,
        INetworkIdAllocator idAllocator,
        EntityRepository? world = null,
        ScenarioBehaviorRemapper? remapper = null,
        IRecordReplayController? controller = null,
        string storageDirectory = @"C:\FDP_Temp")

    public bool CanHandle(NodeOpType operation)
    public bool CanHandle(ExecuteNodeOpIntent intent)
    public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
    public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
    public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo)
    public void DrainDeferredAcks()  // ITickableClusterStateHandler
}
```

Claims `PrepareLive` (cold live scenarios) and `PrepareState(OperatingLive)`. The
`PrepareState(OperatingLive)` intercept holds the cluster in `LoadingLive` via a
`TaskCompletionSource` until `DrainDeferredAcks()` confirms that the genesis pipeline is
fully resolved (source empty, no constructing entities, no transient Intent DTO components).

---

### `CgfEpisodeLoadHandler`

```csharp
public sealed class CgfEpisodeLoadHandler : IClusterStateHandler
{
    public CgfEpisodeLoadHandler(
        ScenarioSerializer serializer,
        IScenarioLoader scenarioLoader,
        StagingEntityExtractor extractor,
        ScenarioEntityCreationRequestSource source,
        INetworkIdAllocator idAllocator,
        EntityRepository world,
        ScenarioBehaviorRemapper? remapper = null)

    public bool CanHandle(NodeOpType operation)  // StartEpisode, StopEpisode
    public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
    public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
    public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo)
}
```

On `StartEpisode`: extracts episode entities (with `EpisodeTag` appended) and enqueues into
the shared `ScenarioEntityCreationRequestSource`. On `StopEpisode`: collects network IDs of
all entities tagged with the episode ID, then publishes `DestroyEntityCommand` per entity
at Commit time to trigger ELM teardown.

---

### `CgfRecordReplayController`

```csharp
public sealed class CgfRecordReplayController : IRecordReplayController
{
    public Task PrepareRecordingAsync(Guid exerciseId, string storageDirectory)
    public Task FinalizeRecordingAsync(long maxNetworkId = 0)
    public Task PrepareReplayAsync(Guid exerciseId, string storageDirectory)
    public Task<GlobalTime> SeekToTimeAsync(long targetWallClockTicks)
    public void ProcessPlaybackTick(GlobalTime currentTime)
    public Task TeardownReplayAsync()
    public bool IsReplayActive { get; }
    public long ActiveMaxNetworkId { get; }         // always 0
    public float ActiveReplayDurationSeconds { get; } // always 0
    public long ActiveRecordingStartWallTicks { get; }// always 0
    public GlobalTime GetCurrentReplayTime()
}
```

Phase-3 skeleton: all lifecycle methods return `Task.CompletedTask` and log at `Info` level.
`IsReplayActive` is tracked correctly so `ReferenceLiveLoadHandler` / `ReferenceReplayLoadHandler`
can gate the Live-from-Replay branch (CGF1-S0305).

---

### `FailLoudRecordReplayStub`

```csharp
public sealed class FailLoudRecordReplayStub : IClusterOpHandler
{
    public FailLoudRecordReplayStub(string nodeName = "CGF")
    public bool CanHandle(NodeOpType op)   // FinalizeLive, PrepareReplay, FinalizeReplay
    public Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct)
    public void Commit(NodeOpCommand cmd, EntityRepository? repo)
    public void Abort(NodeOpCommand cmd, EntityRepository? repo)
}
```

`PrepareAsync` logs an `Error` message identifying the operation as unsupported on the CGF
node. Does not throw; the `ClusterSlave` dispatch loop continues. Must be removed when CGF
acquires a recordable kernel.

**`CanHandle` scope (BATCH-19 fix):** `NodeOpType.PrepareLive` is intentionally excluded
from `CanHandle`. Prior to BATCH-19 the stub returned `true` for `PrepareLive`,
intercepting all scenario-load commands and preventing `ScenarioLoadClusterStateHandler`
from running. After the fix, `CanHandle` returns `true` only for the three unsupported
recording/replay operations: `FinalizeLive`, `PrepareReplay`, and `FinalizeReplay`.

**Deferred replacement:** Once CGF hosts a recordable `ModuleHostKernel` (Phase 3+ brain
kernel), this stub must be removed and replaced with a real implementation using the
shared orchestration handlers (`ReferenceLiveLoadHandler`, `ReferenceReplayLoadHandler`,
`ReferenceCheckpointHandler`). Until then, `FinalizeLive`, `PrepareReplay`, and
`FinalizeReplay` are explicitly unsupported on the CGF node and log an `Error` on
receipt.

---

### `CgfEntityPresentationGizmo`

```csharp
[GizmoProjector(typeof(SimTransform), typeof(NetworkIdentity))]
public sealed class CgfEntityPresentationGizmo : IStatelessGizmo
{
    public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
}
```

Auto-registered by `GizmoRegistrar.RegisterAll` at subsystem initialization. Emits a
`SpatialAnchor` + `SemanticShape` pair for each CGF entity. Prefers `NetworkTransform`
(when `LastRotation != default`) over `SimTransform` for position/rotation, matching the
logic of `CgfDebugVisualizerAdapter`.

---

## Dependencies

### Project References

| Project | Purpose |
|---|---|
| `Hrot.Network.Orchestration` | Orchestration DDS types; `IClusterStateHandler` |
| `Hrot.Common` | Shared component registries, `HrotNodeBuilder`, `HrotNodeConfig`, `HrotEnvironment` |
| `Fdp.Core` | `FdpLog<T>`, `FdpEventBus`, `EntityRepository`, `Entity`, component infrastructure |
| `Fdp.Presentation` | `EntityInspectorPanel`, `EventBrowserPanel`, FDP presentation utilities |
| `Hrot.Presentation` | `ClusterControlWindow`, `BrainBlackboardRenderer`, `BTreeVisualizerRenderer` |
| `Fbt.Compiler` | Fluent BTree builder (`BTreeBuilder<TBlackboard, TContext>`) |
| `Fdp.Toolkits.Analyzers` | Source generators for `[GizmoProjector]`, `[BTreeDefinition]`, `[SharedAiAction]` (analyzer-only, no assembly ref) |
| `Hrot.SimHost` | `NodeBootstrapper`, `EcsRecordReplayController`, `GenesisMaterializationSystem`, scenario serializer factory |
| `Hrot.AI.Behaviors` | Behavior tree and HSM definitions (loaded dynamically at runtime; no compile-time ref enforced at build, but transitively present) |

### InternalsVisibleTo

The assembly grants `internal` access to:

- `Hrot.CGF.Tests`
- `Hrot.ClusterRunner`
- `Hrot.SimHost.Tests`
- `Hrot.SimHost.Integration.Tests`
- `Hrot.ClusterRunner.Tests`
- `Hrot.ClusterRunner.Integration.Tests`

### NuGet / External Packages

| Package | Usage |
|---|---|
| CycloneDDS.Runtime | DDS participant, topic readers/writers |
| NLog | Structured logging (`LogManager.GetLogger("AI.Behavior.TacticalIntent")`) |
| Raylib_cs | Rendering (non-headless path in `CgfSubsystem`) |
| ImGuiNET | Debug UI panels (non-headless path in `CgfSubsystem`) |
| System.Text.Json | Route waypoint `ExtensionJson` parsing in `RouteContextSystem` |

---

## Usage Examples

### Example 1: Standalone Headless CGF Node (ClusterRunner / test)

```csharp
// Build the scenario serializer once (includes behavior registry).
var behaviorRegistry = new BehaviorRegistry();
var scenarioSerializer = HrotScenarioSerializerFactory.Build(behaviorRegistry);

// Create the CGF application.  No DDS participant is passed -- the
// application creates one internally on domain 0.
using var app = new CgfApplication(
    domainId:           0,
    nodeId:             400,
    scenarioSerializer: scenarioSerializer,
    localTempRoot:      @"C:\FDP_Temp\CGF");

// Build and install the Brain-tier logic pack.
var entityMap      = new NetworkEntityMap();
var scenarioSource = app.ScenarioEntityCreationSource;
var mapperRegistry = new TacticalIntentMapperRegistry();
mapperRegistry.Register(new DefendAreaMapper());
mapperRegistry.Register(new HullDownAttackMapper());
var logicPack = new CgfLogicPack(behaviorRegistry, entityMap, scenarioSource, mapperRegistry);
app.Install(logicPack);

// Run at 60 Hz until cancelled.
var cts = new CancellationTokenSource();
var timer = new System.Timers.Timer(1000.0 / 60.0);
timer.Elapsed += (_, _) =>
{
    if (!cts.IsCancellationRequested)
        app.Tick();
};
timer.Start();

// Wait for shutdown signal.
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => { });
timer.Stop();
```

### Example 2: Registering Tactical Intent Mappers

Tactical intent mappers translate generic mission intent IDs into concrete behavior
assignments. They are registered before `CgfLogicPack` is constructed.

```csharp
var mapperRegistry = new TacticalIntentMapperRegistry();

// Built-in mappers supplied by CgfSubsystem:
mapperRegistry.Register(new DefendAreaMapper());
mapperRegistry.Register(new HullDownAttackMapper());

// Custom scenario-specific mapper:
mapperRegistry.Register(new UrbanPatrolMapper());

var logicPack = new CgfLogicPack(
    behaviorRegistry, entityMap, scenarioSource, mapperRegistry);

// When the cluster issues:
//   AssignTacticalIntentEvent { Entity = tank, IntentId = "DefendArea", JsonParams = "{...}" }
//
// TacticalIntentResolutionSystem will:
//   1. Look up "DefendArea" in mapperRegistry (found: DefendAreaMapper)
//   2. Call DefendAreaMapper.TryMap(entity, repo, jsonParams, out behaviorEvent)
//   3. Publish the returned AssignBehaviorEvent (e.g. BehaviorName = "AimAndFire")
//
// If "UrbanPatrol" is used and no mapper exists, the IntentId is passed through
// directly as the behavior name (fallback path).
```

### Example 3: Loading a Scenario via Cluster Orchestration (integration test pattern)

```csharp
// Simulate the cluster transition from Idle -> LoadingLive -> OperatingLive.

var cgfApp = new CgfApplication(
    domainId:           0,
    nodeId:             400,
    scenarioSerializer: scenarioSerializer);

// The ClusterSlave has registered CgfScenarioLoadHandler internally.
// Simulate PrepareLive intent (as ExCon would send):
var prepareLiveIntent = new ExecuteNodeOpIntent
{
    Operation     = NodeOpType.PrepareLive,
    TransactionId = Guid.NewGuid(),
    DomainPayload = new EditLoadHandlerPayload
    {
        ScenarioId  = "hill-attack",
        ExerciseId  = Guid.NewGuid(),
    },
};

// Prepare runs asynchronously (fetches + extracts scenario JSON).
await cgfApp.ClusterSlave.HandleAsync(prepareLiveIntent, CancellationToken.None);

// Commit enqueues EntityCreationRequests into the scenario source.
cgfApp.ClusterSlave.Commit(prepareLiveIntent, world: null);

// Tick the kernel until the genesis pipeline drains.
for (int i = 0; i < 100; i++)
    cgfApp.Tick();

// PrepareState(OperatingLive) -- held until all entities reach Active.
var prepareStateIntent = new ExecuteNodeOpIntent
{
    Operation     = NodeOpType.PrepareState,
    TransactionId = Guid.NewGuid(),
    DomainPayload = new EditLoadHandlerPayload
    {
        TargetState = ClusterState.OperatingLive,
    },
};
await cgfApp.ClusterSlave.HandleAsync(prepareStateIntent, CancellationToken.None);
```

### Example 4: Spawning an Entity with Split Authority (integration test)

```csharp
// CgfSubsystem provides a test hook to spawn an entity and publish
// a DeferredTakeOwnership routing table assigning WorldPos to a Muscle node.

var cgfSubsystem = new CgfSubsystem(networkFactory);
cgfSubsystem.Initialize(new SubsystemConfig { NodeId = 400, DomainId = 0 });

// Spawn entity: CGF owns cognitive state, SimHost node 300 owns WorldPos.
long networkId = cgfSubsystem.TestHook_SpawnEntityWithSplitAuthority(
    tkbType:      TkbTypes.T72Tank,
    muscleNodeId: 300);

// The network ID can now be asserted in subsequent tick cycles.
Assert.True(networkId > 0);
```

### Example 5: Assigning a Behavior via Mission Plan

Behavior assignment flows from a structured mission plan through the adapter pipeline:

```csharp
// 1. Create a mission plan with two phases.
var plan = new ActiveMissionPlan
{
    Plan = new MissionPlan
    {
        Tasks = new List<MissionTask>
        {
            new() { BehaviorName = "MoveToLocation",
                    BehaviorParams = @"{""X"": 500, ""Y"": 300, ""Speed"": 15}" },
            new() { BehaviorName = "FireAtTarget",
                    BehaviorParams = @"{""targetNetworkId"": 42, ""maxRounds"": 10}" },
        }
    }
};
repo.SetManagedComponent(entity, plan);

// 2. Add the MissionPlanQueue component (controls which phase is active).
repo.SetComponent(entity, new MissionPlanQueue { CurrentPhase = 0, PhaseCount = 2 });

// 3. On the next simulation tick, MissionAdapterSystem detects Phase 0 is new:
//      Publishes: AssignTacticalIntentEvent { IntentId = "MoveToLocation", JsonParams = "..." }
//
// 4. TacticalIntentResolutionSystem receives it:
//      No mapper found for "MoveToLocation" (pass-through path).
//      Publishes: AssignBehaviorEvent { BehaviorName = "MoveToLocation", JsonParams = "..." }
//
// 5. BehaviorIngressSystem (next Input phase) assigns the behavior to BehaviorState.
//    CognitiveRuntimeModule begins ticking the MoveToLocation BTree.
```

---

## Best Practices

### 1. Always Use the Composition Root for DDS Participants

`CgfApplication` and `CgfSubsystem` both accept an optional `DdsParticipant` from outside.
Only the outermost executable (the composition root) should create `DdsParticipant`. In
`CgfSubsystem.Initialize`, the participant is created from `_networkFactory?.Participant`
before falling back to `HrotEnvironment.CreateParticipant`. This ensures a single
participant per node process.

### 2. Install All Modules Before the First Tick

`CgfApplication.Install` throws `InvalidOperationException` if called after the first
`Tick()`. The kernel is lazily initialized on the first tick. Register all `IEcsModule`
instances (including `CgfLogicPack`) before entering the update loop.

### 3. Never Mutate Cognitive State Directly in MissionAdapterSystem

`MissionAdapterSystem` publishes `AssignTacticalIntentEvent` rather than calling
`BehaviorState` or `BrainBlackboard` setters directly. This indirect dispatch prevents
double-apply bugs (which previously wiped behavior working memory such as `RoundsFired`).
Respect this contract in any new mission phase handlers.

### 4. Respect the IsDefaultProcessor Contract

Exactly one node in the cluster must construct `CreateEntityRequestSystem` with
`isDefaultProcessor: true`. CGF (Brain) is always this node. Muscle (SimHost) nodes use
`isDefaultProcessor: false`. Violating this produces duplicate network IDs and race
conditions in the genesis pipeline.

### 5. Handler Registration Order Is Significant

In both `CgfApplication` and `CgfSubsystem`, the handler registration order on
`ClusterSlave` is not arbitrary:

1. `ReferenceReplayLoadHandler` -- must be first (gates Live-from-Replay branch)
2. `CgfScenarioLoadHandler` -- before `ReferenceLiveLoadHandler` (claims PrepareLive)
3. `CgfEpisodeLoadHandler` -- before `ReferenceLiveLoadHandler`
4. `ReferenceLiveLoadHandler` -- claims only `FinalizeLive` and fallback `PrepareLive`
5. Utility handlers (`ReferencePrefetchHandler`, `ReferenceArchiveHandler`,
   `ReferencePreviewHandler`) -- order among themselves does not matter

Inserting a handler in the wrong position can cause the wrong handler to claim an
operation, silently skipping entity load or incorrectly routing Live-from-Replay.

### 6. Behavior ID Constants Must Never Change

Constants in `CgfBehaviorIds` are written to scenario files and persisted in exercise
recordings. Once published, a constant's numeric value must never change; add new constants
at new IDs rather than reusing retired ones.

### 7. Gizmo Execution Is Disabled by Default on CGF

The CGF node is headless-first. The gizmo execution group is created with `Enabled = false`
and enabled only when a remote viewer connects (ref-counted gate via
`GizmoExecutionController`). Do not enable the group unconditionally -- this would waste
CPU rendering primitives that no viewer is consuming.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Hrot.SimHost` | **Muscle-tier** counterpart. Owns kinematics (SimTransform, WorldPos). Provides `EcsRecordReplayController`, `GenesisMaterializationSystem`, scenario serializer factory. |
| `Hrot.AI.Behaviors` | Dynamically loaded AI assembly. Provides all BTree/HSM behavior definitions, action delegates, and interpreter constructors. Hot-reloadable in the editor. |
| `Hrot.Common` | Shared infrastructure: `HrotNodeBuilder`, `HrotNodeConfig`, `HrotEnvironment`, shared component registries, `NetworkEntityMap`. |
| `Hrot.Network.Orchestration` | DDS topic types for cluster orchestration (`NodeOpCommand`, `NodeOpStatus`, `NodeHeartbeat`). |
| `Hrot.ClusterRunner` | Process host that instantiates `CgfApplication` (or `CgfSubsystem`) as the headless Brain node in a distributed cluster. |
| `Hrot.CGF.Tests` | Unit tests for `CgfApplication`, genesis pipeline, `TacticalIntentResolutionSystem`, `MissionAdapterSystem`, etc. |
| `Hrot.SimHost.Integration.Tests` | Integration tests exercising the full Brain+Muscle distributed pair via `CgfSubsystem` test hooks. |
| `Fdp.Core` | FDP ECS core: `EntityRepository`, `FdpEventBus`, component infrastructure, `FdpLog<T>`. |
| `Fdp.Toolkit.Behavior` | Behavior registry, `BehaviorState`, `BrainBlackboard`, `BrainBTreeState`, `MissionControlModule`, `CognitiveRuntimeModule`, `ActionDispatchModule`. |
| `Fdp.Toolkit.Orchestration` | `ClusterSlave`, reference load handlers, `ScenarioEntityCreationRequestSource`. |
| `Fbt.Compiler` | Fluent BTree compiler used to build BTree definitions registered in `BehaviorRegistry`. |
| `Hrot.Editor` | Scenario editor that exercises `StagingEntityExtractor` and hot-reloads `Hrot.AI.Behaviors` independently of the CGF node. |

---

## Known Limitations / Deferred Items

### 1. Brain-Side Record/Replay -- Phase 3 Gap

`FailLoudRecordReplayStub` is still registered on `CgfApplication` for
`FinalizeLive`, `PrepareReplay`, and `FinalizeReplay`. These three cluster operations log
an `Error` and return without performing any action, because the CGF node does not yet
host a recordable `ModuleHostKernel`.

The consequence is that HSMs running on the CGF (Brain) node cannot participate in
recording or replay sessions. The stub is an explicit fail-loud placeholder; it must be
removed and replaced with `ReferenceLiveLoadHandler`, `ReferenceReplayLoadHandler`, and
`ReferenceCheckpointHandler` (matching the SimHost architecture) once the Phase 3+ brain
kernel is introduced.

`NodeOpType.PrepareLive` is correctly excluded from the stub's `CanHandle` (fixed in
BATCH-19) so that normal scenario loads route to `ScenarioLoadClusterStateHandler` as
expected.
