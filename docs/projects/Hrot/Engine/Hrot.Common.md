# Hrot.Common

| Field       | Value                                                                 |
|-------------|-----------------------------------------------------------------------|
| Project     | Hrot.Common                                                           |
| Path        | `Hrot/Engine/Hrot.Common/Hrot.Common.csproj`                         |
| Namespace   | `Hrot.Common` (root), several sub-namespaces                         |
| Framework   | net8.0                                                                |
| Date        | 2026-05-23                                                            |

---

## README Validation

**Status: Missing**

No `README.md` exists in the project folder. This document serves as the canonical
architectural reference.

---

## Executive Overview

`Hrot.Common` is the **shared foundation library** of the HROT simulation engine layer. It
sits directly above `Hrot.Core` in the dependency hierarchy and below every application-level
project (SimHost, IG, NodeComposition, Editor). Its primary responsibilities are:

- **Node bootstrap infrastructure** - `HrotNodeBuilder` and `SharedApplicationBootstrapper`
  eliminate ~300 lines of duplicated bootstrap boilerplate across all node types by providing
  a fluent builder and a strict 7-phase initialization template.
- **Operator interaction pipeline** - A small but complete event-driven pipeline for routing
  context-menu and toolbar actions from the IG terminal to the authoritative simulation node.
  The pipeline is isolated on a dedicated `FdpEventBus` instance to prevent UI noise from
  contaminating the core simulation bus.
- **Debug gizmo library** - Ten gizmo projectors and their settings, shared by both headless
  (SimHost) and rendering (IG) deployments via the DDS gizmo stream transport.
- **Mission control execution** - `MissionControlExecutionSystem` processes `MissionPlan`
  assignments against the ECS world, with retry logic and version tracking.
- **Unit command-hierarchy management** - `UnitHierarchySystem` maintains commander/subordinate
  links in reaction to `CmdAssignSubordinate`, `CmdRemoveSubordinate`, and `DestructionOrder`
  events.
- **Genesis intent DTOs** - Managed transient components that carry inter-entity relationship
  data (passengers, routes, hierarchy links, target memory) through the scenario-load pipeline
  before live ECS components are materialized.
- **Subsystem type constants** - `HrotSubsystemTypes` stable string identifiers used in
  scenario serialization headers.
- **Diagnostics dump handler** - `DiagnosticsDumpClusterOpHandler` implements the node-side
  2PC participant for the `CollectDiagnostics` cluster operation.

The project deliberately avoids any game-engine or rendering dependencies so it can be
referenced by headless SimHost nodes, AI/CGF nodes, and rendering IG nodes alike.

---

## Architecture

### Layered Position

```
+-------------------------------------------------------+
|  Application Layer: SimHost / IG / NodeComposition / Editor |
+-------------------------------------------------------+
                        |
                        v
+-------------------------------------------------------+
|                   Hrot.Common                         |
|  (bootstrap, interaction pipeline, gizmos, missions)  |
+-------------------------------------------------------+
         |                       |
         v                       v
+------------------+   +---------------------------+
|   Hrot.Core      |   |  Hrot.Network.Orchestration |
| (HrotNodeConfig, |   |  (NodeOpSlaveTranslator,    |
|  HrotNodeContext, |   |   DdsIdAllocatorHelper)     |
|  NodeRole, etc.) |   +---------------------------+
+------------------+
         |
         v
+------------------+   +------------------+
|   Fdp.Core       |   |  Fdp.ModuleHost  |
| (EntityRepository|   | (IEcsModule,     |
|  FdpEventBus,    |   |  ModuleHostKernel|
|  ECS components) |   |  ISimulationView)|
+------------------+   +------------------+
         |
         v
+------------------+   +------------------+
|  Fdp.Toolkits    |   | Fdp.Network.     |
| (IEntityState-   |   | Cyclone          |
|  ExtractionSvc,  |   | (DdsIdAllocator, |
|  JsonAesthetic-  |   |  CycloneIngress/ |
|  Formatter)      |   |  EgressSystem)   |
+------------------+   +------------------+
```

### Operator Interaction Pipeline

The pipeline that routes a context-menu click from the IG terminal to an
application-level handler is entirely contained within the `GizmoInteractionModule`
and its constituent systems. All events travel on an isolated `FdpEventBus`
(`_interactionBus`), which is completely separate from the global kernel bus.

```
+-------------------------+      DDS      +----------------------------+
|   IG Terminal           |  -----------> |  CycloneNetworkIngressSystem|
| (GizmoInteractionBatch) |               |  (gizmoIngress)            |
+-------------------------+               +----------------------------+
                                                       |
                                               writes to _interactionBus
                                                       |
                                                       v
                                   +-----------------------------------+
                                   |  _interactionBus.SwapBuffers()    |
                                   +-----------------------------------+
                                                       |
                                                       v
                                   +-----------------------------------+
                                   |  ContextActionIngressSystem       |
                                   |  Reads ContextActionTriggered     |
                                   |  (managed) + GizmoMenuActionEvent |
                                   |  -> Publishes                     |
                                   |     GlobalActionRequestedEvent    |
                                   +-----------------------------------+
                                                       |
                                                       v
                                   +-----------------------------------+
                                   |  GlobalActionDispatchSystem       |
                                   |  Reads GlobalActionRequestedEvent |
                                   |  -> Calls GlobalActionRegistry    |
                                   |     handler(view, target)         |
                                   +-----------------------------------+
                                                       |
                                               application handler
                                                       |
                                                       v
                                   +-----------------------------------+
                                   |  CycloneEgressSystem (optional)   |
                                   |  Sends back GizmoInteractionBatch |
                                   +-----------------------------------+
```

### Node Bootstrap Phases

`SharedApplicationBootstrapper.BootstrapNode` enforces a 7-phase initialization order.
No subclass may reorder or skip phases. The diagram shows which phases are abstract hooks
(subclass implements) vs. base-class logic (B).

```
+----------+  Phase 1: BuildContext (abstract hook)
|          |  Phase 2: RegisterDomainComponents (abstract)
|  Boot-   |  Phase 3: BuildSerializer (abstract)
|  strap   |  Phase 4a: PopulateSystems -> TogglableGroups (abstract + B)
|  Node    |  Phase 4b: GetAdditionalModules (virtual)
|          |  Phase 5: BuildOrchestration (abstract)
|          |  Phase 6a: RegisterSpawningPipeline (abstract)
|          |  Phase 6a+: RegisterModule(NedReplication) (B, NEVER subclass)
|          |  Phase 6b: RegisterNetworkTranslators (abstract)
|          |  Phase 6c: Wire time-sync translators (B, NEVER subclass)
|          |  Phase 6d: RegisterApplicationSystems (virtual)
|          |  Phase 7: Kernel.Initialize() (B, always last)
+----------+
```

### Mission Control Data Flow

```
 DDS (MissionControlRequest)
         |
         v
+-------------------------------+   publishes    +---------------------------+
| MissionControlIngressTranslator|  -----------> |  MissionControlIntent     |
| (in Hrot.Network.NED)         |               |  (managed event on bus)   |
+-------------------------------+               +---------------------------+
                                                            |
                                                            v
                                              +----------------------------+
                                              | MissionControlExecution-   |
                                              | System                     |
                                              |  - Retry queue (10 frames) |
                                              |  - Version tracking        |
                                              |  - Writes MissionPlanQueue |
                                              |  - Publishes MissionControl|
                                              |    AckEvent                |
                                              +----------------------------+
                                                            |
                                                   MissionControlAckEvent
                                                            |
                                                            v
                                              +----------------------------+
                                              | MissionControlAckEgress-   |
                                              | Translator (Hrot.Network)  |
                                              | -> DDS MissionControlAck   |
                                              +----------------------------+
```

---

## Source Structure

All files under `Hrot/Engine/Hrot.Common/`.

### `Components/`

| File                    | Namespace                        | Type                    |
|-------------------------|----------------------------------|-------------------------|
| `ContextAction.cs`      | `Hrot.Map.Common.Components`     | `ContextAction` (class) |
| `GlobalDebugSettings.cs`| `Hrot.Common.Components`         | `GlobalDebugSettings` (struct) |

### `Constants/`

| File                  | Namespace                 | Type                          |
|-----------------------|---------------------------|-------------------------------|
| `GlobalActionIds.cs`  | `Hrot.Common.Constants`   | `GlobalActionIds` (static class) |

### `Diagnostics/`

| File                                   | Namespace                           | Type                                |
|----------------------------------------|-------------------------------------|-------------------------------------|
| `DiagnosticsDumpClusterOpHandler.cs`   | `Hrot.Common.Diagnostics`           | `DiagnosticsDumpClusterOpHandler` (sealed class) |

### `Diagnostics/Gizmos/`

| File                              | Namespace                              | Type                                        |
|-----------------------------------|----------------------------------------|---------------------------------------------|
| `IGizmoControllable.cs`           | `Hrot.Common.Diagnostics.Gizmos`      | `IGizmoControllable` (interface)            |
| `ContextMenuProjectorGizmo.cs`    | `Hrot.Common.Diagnostics.Gizmos`      | `ContextMenuProjectorGizmo` (sealed class)  |
| `EntityRotationGizmo.cs`          | `Hrot.Common.Diagnostics.Gizmos`      | `EntityRotationGizmo` (sealed class)        |
| `EntityRotationGizmoSettings.cs`  | `Hrot.Common.Diagnostics.Gizmos`      | `EntityRotationGizmoSettings` (internal static class) |
| `HealthBarGizmo.cs`               | `Hrot.Common.Diagnostics.Gizmos`      | `HealthBarGizmo` (sealed class)             |
| `HealthBarGizmoSettings.cs`       | `Hrot.Common.Diagnostics.Gizmos`      | `HealthBarGizmoSettings` (static class)     |
| `LayerControlGizmo.cs`            | `Hrot.Common.Diagnostics.Gizmos`      | `LayerControlGizmo` (sealed class)          |
|                                   |                                        | `LayerControlDto` (class)                   |
|                                   |                                        | `OpenLayerEditorEvent` (struct)             |
| `LineOfSightGizmo.cs`             | `Hrot.Common.Diagnostics.Gizmos`      | `LineOfSightGizmo` (sealed class)           |
| `NavigationTargetGizmo.cs`        | `Hrot.Common.Diagnostics.Gizmos`      | `NavigationTargetGizmo` (sealed class)      |
| `SelectionHighlightGizmo.cs`      | `Hrot.Common.Diagnostics.Gizmos`      | `SelectionHighlightGizmo` (sealed class)    |
| `SpatialGridGizmo.cs`             | `Hrot.Common.Diagnostics.Gizmos`      | `SpatialGridGizmo` (sealed class)           |
| `SpatialGridGizmoSettings.cs`     | `Hrot.Common.Diagnostics.Gizmos`      | `SpatialGridGizmoSettings` (internal static class) |
| `VisibilityConeGizmo.cs`          | `Hrot.Common.Diagnostics.Gizmos`      | `VisibilityConeGizmo` (sealed class)        |

### `Events/`

| File                           | Namespace               | Type                                         |
|--------------------------------|-------------------------|----------------------------------------------|
| `ContextMenuEvents.cs`         | `Hrot.Common.Events`    | `ContextActionsUpdate` (sealed class)        |
|                                |                         | `ContextActionTriggered` (sealed class)      |
| `GlobalActionRequestedEvent.cs`| `Hrot.Common.Events`    | `GlobalActionRequestedEvent` (struct)        |
| `IgCommonEvents.cs`            | `Hrot.IG`               | `IgWeaponFireEvent` (struct)                 |

### `Infrastructure/`

| File                           | Namespace                   | Type                                     |
|--------------------------------|-----------------------------|------------------------------------------|
| `HrotNodeBuilder.cs`           | `Hrot.Common.Infrastructure`| `HrotNodeBuilder` (sealed class)         |
| `SharedApplicationBootstrapper.cs` | `Hrot.Common.Infrastructure` | `SharedApplicationBootstrapper` (abstract class) |

### `Interactions/`

| File                          | Namespace                    | Type                                      |
|-------------------------------|------------------------------|-------------------------------------------|
| `GlobalActionRegistry.cs`     | `Hrot.Common.Interactions`   | `GlobalActionRegistry` (sealed class)     |
|                               |                              | `GlobalActionHandler` (delegate)          |
| `GizmoInteractionModule.cs`   | `Hrot.Common.Interactions`   | `GizmoInteractionModule` (sealed class)   |
| `InteractionEventRegistry.cs` | `Hrot.Common.Interactions`   | `InteractionEventRegistry` (static class) |

### `Scenario/`

| File                    | Namespace                  | Type                              |
|-------------------------|----------------------------|-----------------------------------|
| `HrotSubsystemTypes.cs` | `Hrot.Common.Scenario`     | `HrotSubsystemTypes` (static class) |

### `Serializers/`

| File                        | Namespace                   | Types                                                                                                     |
|-----------------------------|-----------------------------|-----------------------------------------------------------------------------------------------------------|
| `GenesisIntentComponents.cs`| `Hrot.Common.Serializers`   | `InitialPassengersIntent`, `InitialVehicleIntent`, `InitialHierarchyIntent`, `InitialRouteIntent`, `TargetEntry`, `InitialTargetsIntent`, `InitialUnitSubordinateIntent` |

### Root

| File                        | Namespace          | Type                                |
|-----------------------------|--------------------|-------------------------------------|
| `GenesisIntentRegistry.cs`  | `Hrot.Map.Common`  | `GenesisIntentRegistry` (static class) |

### `Systems/`

| File                              | Namespace              | Type                                          |
|-----------------------------------|------------------------|-----------------------------------------------|
| `ContextActionIngressSystem.cs`   | `Hrot.Common.Systems`  | `ContextActionIngressSystem` (sealed class)   |
| `GlobalActionDispatchSystem.cs`   | `Hrot.Common.Systems`  | `GlobalActionDispatchSystem` (sealed class)   |
| `MissionControlExecutionSystem.cs`| `Hrot.Common.Systems`  | `MissionControlExecutionSystem` (class)       |
| `UnitHierarchySystem.cs`          | `Hrot.Common.Systems`  | `UnitHierarchySystem` (class)                 |

---

## Public API Reference

### `Hrot.Map.Common.Components.ContextAction`

Immutable record-like class representing a single context-menu action row.

| Member         | Type     | Description                                                           |
|----------------|----------|-----------------------------------------------------------------------|
| `Label`        | `string` | Human-readable label shown in the menu row.                          |
| `ActionName`   | `string` | Internal action identifier. Names prefixed with `"IG_"` are handled locally by the IG. All other names are forwarded to ExCon as a `ContextActionTriggered` managed event. |

---

### `Hrot.Common.Components.GlobalDebugSettings`

Singleton ECS component (blittable struct) that controls global debug behavior across
subsystems. Component ID: `HrotComponentIds.GlobalDebugSettings`. Data policy: `Transient`.

| Member                 | Type     | Description                                                               |
|------------------------|----------|---------------------------------------------------------------------------|
| `ForceAllGizmosVisible`| `bool`   | When `true`, all gizmos are visible regardless of `DebugLayerMask`.       |
| `DebugLayerMask`       | `ushort` | Bitmask for layers 0-15. Bit N set means layer N is visible. Default `0xFFFF` (all on). |
| `MaxGizmoFrameMs`      | `float`  | Max milliseconds per frame for gizmo projection work. `0` = unlimited. Default: `2.0f`. |
| `AutoEnableAiTracing`  | `bool`   | When `true`, genesis pipeline stamps `DebugState` + trace working memory on every AI-enabled entity. |

---

### `Hrot.Common.Constants.GlobalActionIds`

Static class of `const int` fields mapping human-readable action names to stable
numeric IDs. Values must stay in sync with `ContextMenuProjectorGizmo` and
`HandleContextMenuActionById` in IgApplication.

| Constant              | Value | Category                      |
|-----------------------|-------|-------------------------------|
| `MoveHere`            | 1     | Tactical orders               |
| `Engage`              | 2     | Tactical orders               |
| `Stop`                | 3     | Tactical orders               |
| `CenterOnEntity`      | 10    | View / selection              |
| `Select`              | 11    | View / selection              |
| `Properties`          | 12    | View / selection              |
| `Delete`              | 13    | View / selection              |
| `Teleport`            | 14    | View / selection              |
| `Rotate`              | 20    | Gizmo tools                   |
| `Repair`              | 21    | Gizmo tools                   |
| `Reinforce`           | 22    | Gizmo tools                   |
| `Resupply`            | 23    | Gizmo tools                   |
| `Transfer`            | 24    | Gizmo tools                   |
| `EditOverlay`         | 100   | Editor / overlay              |
| `EditRoute`           | 101   | Editor / overlay              |
| `EditPersonalRoute`   | 102   | Editor / overlay              |
| `Measure`             | 200   | Canvas-level tools            |
| `PlaceEntity`         | 201   | Canvas-level tools            |
| `PlaceObstacle`       | 202   | Canvas-level tools            |
| `OpenLayerControl`    | 250   | Layer control                 |
| `ToggleAiTrace`       | 251   | AI diagnostics                |
| `ToggleAiTraceLog`    | 252   | AI diagnostics                |

---

### `Hrot.Common.Events.ContextActionsUpdate`

Managed event sent from ExCon to IG to update the context-menu definition for a
specific network entity. The payload is a pre-serialised JSON string matching the
`ContextMenuItemDto` array schema.

| Member           | Type     | Description                                                       |
|------------------|----------|-------------------------------------------------------------------|
| `EntityNetworkId`| `int`    | Network identity of the entity whose menu is being updated.       |
| `MenuJson`       | `string` | Pre-serialised JSON array of `ContextMenuItemDto` objects. Replaces any previously stored menu definition. |

---

### `Hrot.Common.Events.ContextActionTriggered`

Managed event published when the operator selects a context-menu action.

| Member           | Type     | Description                                                       |
|------------------|----------|-------------------------------------------------------------------|
| `EntityNetworkId`| `int`    | Network identity of the entity on which the action was triggered. |
| `ActionName`     | `string` | Name of the triggered action (typically the integer action ID as a string). |

---

### `Hrot.Common.Events.GlobalActionRequestedEvent`

Unmanaged ECS event (blittable struct) published by `ContextActionIngressSystem`.
Event ID: `8059`. Data policy: `NoRecord`.

| Member     | Type     | Description                                                              |
|------------|----------|--------------------------------------------------------------------------|
| `ActionId` | `int`    | Numeric action identifier (see `GlobalActionIds`).                       |
| `Target`   | `Entity` | Local entity that is the target of the action, or `Entity.Null` for canvas actions. |

---

### `Hrot.IG.IgWeaponFireEvent`

Unmanaged ECS event (blittable struct). Event ID: `6001`. Published by
`WeaponFireIngressTranslator` when a `WeaponFire` DDS message is received; consumed
by the IG visual layer to trigger muzzle-flash effects.

| Member          | Type   | Description                                     |
|-----------------|--------|-------------------------------------------------|
| `ShooterEntityId` | `long` | Network entity ID of the firing entity.       |
| `TargetEntityId`  | `long` | Network entity ID of the intended target.     |
| `WeaponIndex`     | `int`  | Zero-based weapon slot index.                 |

---

### `Hrot.Common.Infrastructure.HrotNodeBuilder`

Fluent, single-use builder for `HrotNodeContext`. Encapsulates the full ECS world
construction sequence.

| Member                                  | Returns             | Description                                                                  |
|-----------------------------------------|---------------------|------------------------------------------------------------------------------|
| `HrotNodeBuilder(HrotNodeConfig)`       | (constructor)       | Creates a builder from the given node configuration.                         |
| `WithRole(string, NodeRole)`            | `HrotNodeBuilder`   | Sets the human-readable subsystem name used in DDS heartbeat publications.   |
| `WithNetworkFactory(INetworkFactory?)`  | `HrotNodeBuilder`   | Supplies the `INetworkFactory` for ID allocator creation.                    |
| `Build()`                               | `HrotNodeContext`   | Executes the full initialization sequence. Throws `InvalidOperationException` if called more than once. |

**Build sequence (internal):**

1. Create `EntityRepository` (ECS world).
2. Create `EventAccumulator` + `ModuleHostKernel`.
3. Create `FdpEventBus`.
4. Create `TimeControllerConfig` (Slave role) + `TimeControllerFactory.Create`.
5. (Non-headless) Create `DdsParticipant` reference, `NetworkEntityMap`, `INetworkIdAllocator` via factory or direct `DdsIdAllocator`.
6. (Headless + external participant) Attach participant without allocator routing.
7. Create `ClusterSlave`, optionally create slave orchestration translator via factory.
8. Create `EntityLifecycleModule` + `GeographicModule`.
9. Return populated `HrotNodeContext`.

---

### `Hrot.Common.Infrastructure.SharedApplicationBootstrapper`

Abstract template-method base class that locks the 7-phase node initialization order.

| Member                                       | Modifier   | Description                                                                         |
|----------------------------------------------|------------|-------------------------------------------------------------------------------------|
| `TimeControl`                                | public property | `ITimeControlGateway?` for forwarding UI time commands. Non-null after `BootstrapNode`. |
| `BootstrapNode(HrotNodeConfig, NodeRole, INetworkFactory?)` | public sealed | Runs the 7-phase pipeline and returns a fully wired `HrotNodeContext`. |
| `BuildContext(HrotNodeConfig, NodeRole, INetworkFactory?)` | protected abstract | Phase 1: Construct `HrotNodeContext`. Must chain `.WithReplication()` before `.Build()`. |
| `RegisterDomainComponents(EntityRepository)` | protected abstract | Phase 2: Register ECS component types.                                              |
| `BuildSerializer(BehaviorRegistry?)`         | protected abstract | Phase 3: Build scenario serializer.                                                 |
| `PopulateSystems(HrotNodeContext, List<IEcsModuleSystem>, List<IEcsModuleSystem>, List<IEcsModuleSystem>)` | protected abstract | Phase 4a: Populate input, sim, and postSim system lists. |
| `BuildOrchestration(HrotNodeContext, TogglableSimulationGroup, TogglablePostSimulationGroup, ScenarioSerializer)` | protected abstract | Phase 5: Build `ClusterSlave`.                  |
| `RegisterSpawningPipeline(HrotNodeContext)` | protected abstract | Phase 6a: Register spawn pipeline.                                                   |
| `RegisterNetworkTranslators(HrotNodeContext, INetworkFactory?)` | protected abstract | Phase 6b: Register DDS translators.                         |
| `GetAdditionalModules()`                     | protected virtual  | Phase 4b: Additional `IEcsModule` instances. Default: empty.                        |
| `GetBehaviorRegistry()`                      | protected virtual  | Returns `BehaviorRegistry?`. Default: `null`.                                       |
| `RegisterApplicationSystems(HrotNodeContext)` | protected virtual | Phase 6d: Register gizmo modules, UI systems, etc. Default: no-op.                  |

---

### `Hrot.Common.Interactions.GlobalActionRegistry`

Composition-root owned registry mapping integer action IDs to handler callbacks.
Immutable during runtime after initial registration.

| Member                                         | Returns | Description                                                                           |
|------------------------------------------------|---------|---------------------------------------------------------------------------------------|
| `Register(int, GlobalActionHandler)`           | `void`  | Registers a handler for an action ID. Throws `InvalidOperationException` on duplicate registration. |
| `TryGetHandler(int, out GlobalActionHandler)`  | `bool`  | Returns `true` and sets `handler` if a handler is registered for the given action ID. |

**Delegate:**

```csharp
public delegate void GlobalActionHandler(ISimulationView view, Entity target);
```

---

### `Hrot.Common.Interactions.GizmoInteractionModule`

`IEcsModule` that encapsulates the gizmo interaction pipeline. Execution policy:
`Synchronous`. Module name: `"GizmoInteraction"`.

| Member                  | Description                                                                                         |
|-------------------------|-----------------------------------------------------------------------------------------------------|
| Constructor             | Accepts `FdpEventBus interactionBus`, optional `contextIngress`, array of `interactionSystems`, optional `gizmoIngress`, optional `gizmoEgress`. Pre-registers unmanaged event types on the bus. |
| `RegisterSystems`       | Intentionally empty; all systems run manually inside `Tick`.                                        |
| `Tick`                  | Executes the 5-step pipeline: ingress -> SwapBuffers -> context ingress -> interaction systems -> egress. |

---

### `Hrot.Common.Interactions.InteractionEventRegistry`

Static helper that bulk-registers all interaction event types on an `FdpEventBus`.

| Member            | Description                                                                              |
|-------------------|------------------------------------------------------------------------------------------|
| `RegisterAll(FdpEventBus)` | Registers all unmanaged gizmo events and managed UI events (see Source Structure). |

Registered events include: `GizmoInteractionStartedEvent`, `GizmoDragUpdateEvent`,
`GizmoInteractionCommitEvent`, `GizmoInteractionCancelEvent`, `GizmoMenuActionEvent`,
`GizmoMouseEvent`, `GizmoKeyEvent`, `GizmoComponentActivatedEvent`,
`GlobalActionRequestedEvent`, `OpenLayerEditorEvent`, `GizmoStructUpdateEvent`,
`TerminalConnectedEvent`, `TerminalDisconnectedEvent`, `ContextActionsUpdate`,
`ContextActionTriggered`.

---

### `Hrot.Common.Scenario.HrotSubsystemTypes`

Static class of `const string` identifiers for scenario serialization.

| Constant   | Value            | Description                                         |
|------------|------------------|-----------------------------------------------------|
| `Scenario` | `"Hrot.Scenario"`| Cross-node, engine-agnostic scenario payload.       |
| `SimHost`  | `"Hrot.SimHost"` | SimHost-authoritative snapshot or scenario payload. |
| `Cgf`      | `"Hrot.CGF"`     | CGF-authoritative snapshot or scenario payload.     |
| `Ig`       | `"Hrot.IG"`      | IG-specific visual configuration payload.           |

---

### `Hrot.Common.Serializers` - Genesis Intent DTOs

All intent types carry `[DataPolicy(DataPolicy.Transient)]` and a `HrotComponentIds`
component ID. They are registered via `GenesisIntentRegistry.RegisterAll`.

#### `InitialPassengersIntent`

| Member              | Type           | Description                                       |
|---------------------|----------------|---------------------------------------------------|
| `PassengerNetworkIds` | `List<long>` | Network IDs of all passenger entities at load time. |

#### `InitialVehicleIntent`

| Member              | Type   | Description                                         |
|---------------------|--------|-----------------------------------------------------|
| `VehicleNetworkId`  | `long` | Network ID of the vehicle this soldier is embarked in. |

#### `InitialHierarchyIntent`

| Member               | Type   | Description                                         |
|----------------------|--------|-----------------------------------------------------|
| `ParentNetworkId`    | `long` | Network ID of parent entity (0 = no parent).        |
| `FirstChildNetworkId`| `long` | Network ID of first-child entity (0 = none).        |
| `NextSiblingNetworkId`| `long`| Network ID of next-sibling entity (0 = none).       |

#### `InitialRouteIntent`

| Member             | Type   | Description                                          |
|--------------------|--------|------------------------------------------------------|
| `RouteNetworkId`   | `long` | Network ID of the personal route entity (0 = none).  |

#### `TargetEntry` (struct)

| Member          | Type    | Description                                              |
|-----------------|---------|----------------------------------------------------------|
| `NetworkId`     | `long`  | Network ID of the perceived target entity.               |
| `PosX`          | `float` | Last known X position (world units).                     |
| `PosY`          | `float` | Last known Y position (world units).                     |
| `Score`         | `float` | Threat score assigned by the perception system.          |
| `LastSeenTick`  | `uint`  | Simulation tick at which this target was last observed.  |
| `Modality`      | `byte`  | Encoded sensor modality that detected this target.       |

#### `InitialTargetsIntent`

| Member    | Type                  | Description                          |
|-----------|-----------------------|--------------------------------------|
| `Entries` | `List<TargetEntry>`   | Target entries at scenario load time. |

#### `InitialUnitSubordinateIntent`

| Member               | Type                   | Description                                           |
|----------------------|------------------------|-------------------------------------------------------|
| `CommanderNetworkId` | `long`                 | Network ID of the commander entity (0 = unassigned).  |
| `Designation`        | `TacticalDesignation`  | Tactical role within the commander's unit.            |

---

### `Hrot.Map.Common.GenesisIntentRegistry`

Static helper for registering genesis intent DTO types on an `EntityRepository`.

| Member                           | Description                                                          |
|----------------------------------|----------------------------------------------------------------------|
| `RegisterAll(EntityRepository)`  | Registers all six intent component types as managed components on the world. |

---

### `Hrot.Common.Systems.ContextActionIngressSystem`

Bridges managed `ContextActionTriggered` events and unmanaged `GizmoMenuActionEvent`
on the isolated interaction bus into typed `GlobalActionRequestedEvent` events.

Phase: `SystemPhase.Input`. Runs before `GlobalActionDispatchSystem`.

| Member       | Description                                                                                  |
|--------------|----------------------------------------------------------------------------------------------|
| Constructor  | Accepts `NetworkEntityMap` and `FdpEventBus interactionBus`.                                 |
| `Execute`    | Reads `ContextActionTriggered` (managed) and `GizmoMenuActionEvent` (unmanaged) from `_interactionBus`; publishes `GlobalActionRequestedEvent` back onto `_interactionBus`. Non-integer `ActionName` values are logged and skipped. |

---

### `Hrot.Common.Systems.GlobalActionDispatchSystem`

Reads `GlobalActionRequestedEvent` from the isolated interaction bus and dispatches to
registered handler callbacks.

Phase: `SystemPhase.Input`.

| Member      | Description                                                                           |
|-------------|---------------------------------------------------------------------------------------|
| Constructor | Accepts `GlobalActionRegistry` and `FdpEventBus interactionBus`.                     |
| `Execute`   | Iterates all `GlobalActionRequestedEvent` events; calls matching handler from registry. |

---

### `Hrot.Common.Systems.MissionControlExecutionSystem`

Pure-ECS execution system for mission control requests. Consumes `MissionControlIntent`
managed events and writes `MissionPlanQueue` + `ActiveMissionPlan` ECS components.

Phase: `SystemPhase.Input`.

| Member       | Description                                                                                     |
|--------------|-------------------------------------------------------------------------------------------------|
| Constructor  | Accepts `NetworkEntityMap`, `BehaviorRegistry`, `TacticalIntentMapperRegistry`.                |
| `Execute`    | Processes retry queue first, then newly-arrived intents. Handles `CMD_REPLACE_MISSION`, `CMD_JUMP_TO_TASK`, `CMD_ABORT_ALL`. Publishes `MissionControlAckEvent` for each resolved intent. |

**Constants:**

| Name                         | Value | Description                                                     |
|------------------------------|-------|-----------------------------------------------------------------|
| `MaxEntityWaitFrames`        | 10    | Frames to retry when target entity is not yet in `NetworkEntityMap`. |
| `EntityMissionDescriptorOrdinal` | 51 | Ordinal for `EntityMission` descriptor (must match `EntityMissionEgressTranslator`). |

---

### `Hrot.Common.Systems.UnitHierarchySystem`

Maintains the ECS commander-subordinate hierarchy by processing hierarchy command events.

Phase: `SystemPhase.Simulation`. Processing order per tick: destruction cascade ->
removal -> assignment.

| Member    | Description                                                                                                              |
|-----------|--------------------------------------------------------------------------------------------------------------------------|
| `Execute` | Processes `DestructionOrder` (cascading release), `CmdRemoveSubordinate`, and `CmdAssignSubordinate` events. Updates `UnitRoster`, `UnitSubordinate`, and `FormationFollower` components. Calls `SmartEgressUtil.MarkDirty` for network replication. |

---

### Diagnostic Gizmos

All gizmo projectors are decorated with `[GizmoProjector(...)]` so that the Roslyn
source generator (`Fdp.Toolkits.Analyzers`) emits a `GizmoRegistrar.g.cs` file that
auto-registers them with `GlobalGizmoManager` at startup.

#### `IGizmoControllable`

Interface exposing a `GizmoExecutionController?` property so
`PerspectiveCoordinatorSystem` can transfer the listener count when the active
perspective changes.

#### `ContextMenuProjectorGizmo`

Projects `ContextMenuBinding` meta-primitives for every networked entity. Selects
one of four pre-serialised JSON menus based on entity state:

- `MenuJsonHealthy` - combat-effective unit (damage < 50%)
- `MenuJsonDegraded` - heavily damaged unit (damage >= 50%)
- `MenuJsonArea` - tactical graphics area overlay (`EditablePolyline` managed component)
- `MenuJsonRoute` - tactical route graphic (`RoutePlan` managed component)

Component requirement: `[GizmoProjector(typeof(NetworkIdentity))]`

#### `EntityRotationGizmo`

Projects an orange heading arrow + compass degree label for every entity with
`SimTransform`. Arrow length is read from `EntityRotationGizmoSettings` (default 30 m).

Component requirement: `[GizmoProjector(typeof(SimTransform))]`

#### `HealthBarGizmo`

Projects an entity badge showing health percentage (green/yellow/red color scale).
Bar dimensions are read from `HealthBarGizmoSettings`.

Component requirement: `[GizmoProjector(typeof(IgHealthState))]`

#### `LayerControlGizmo`

Stateful gizmo that owns layer visibility state for the tactical map. Each frame emits:
- `LayerControlMask` primitive (authoritative 256-bit mask).
- `MainMenuBinding` primitive (`"View > Tactical Map Layers..."`).
- Optionally a `StructInspector` panel when `_isEditing = true`.

Interaction flow: `OpenLayerControl` action -> `OpenLayerEditorEvent` -> panel toggle ->
`GizmoStructUpdateEvent` with JSON -> `_dto` updated -> `_activeLayers` recomputed.

#### `LineOfSightGizmo`

Projects dashed gradient lines from entity to each perceived target in `TargetMemory`.
Fades out targets last seen more than 60 ticks ago.

Component requirement: `[GizmoProjector(typeof(TargetMemory), typeof(SimTransform))]`

#### `NavigationTargetGizmo`

Projects an arrow from entity position to its `NavigationIntent.FinalDestination` when
navigation mode is `DirectPoint` and result is `InProgress`.

Component requirement: `[GizmoProjector(typeof(NavigationIntent), typeof(SimTransform))]`

#### `SelectionHighlightGizmo`

Projects a selection-highlight ring for every entity where `SelectionState.IsSelected`
is `true`. Primary selection: solid green ring (20 px radius, 2 px thick). Secondary:
yellow ring.

Component requirement: `[GizmoProjector(typeof(SelectionState), typeof(SimTransform))]`

#### `SpatialGridGizmo`

Global gizmo (no per-entity anchor) that draws `SpatialHashGrid` tile boundaries and
per-cell entity counts. Toggle via `SpatialGridGizmoSettings.ShowTiles` and
`SpatialGridGizmoSettings.ShowCounts` booleans.

Component requirement: `[GizmoProjector]` (global, no component constraint)

#### `VisibilityConeGizmo`

Projects a semi-transparent cyan cone from entity position representing field-of-view
and vision range sourced from `PerceptionReceptor`. Rendered as two edge lines plus
an 8-segment arc.

Component requirement: `[GizmoProjector(typeof(SimTransform), typeof(PerceptionReceptor))]`

---

### `Hrot.Common.Diagnostics.DiagnosticsDumpClusterOpHandler`

Node-side 2PC participant for the `CollectDiagnostics` cluster operation.

| Member         | Description                                                                                    |
|----------------|------------------------------------------------------------------------------------------------|
| `CanHandle`    | Returns `true` for `NodeOpType.CollectDiagnostics`.                                            |
| `PrepareAsync` | Offloads diagnostic collection to a `LongRunning` background task. Writes entities, architecture, events, and log archive to `LocalTempRoot/dumps/{transactionId:N}/`. Returns a `List<FileManifestEntry>`. |
| `Commit`       | No-op; no ECS mutation required for diagnostic dumps.                                          |
| `Abort`        | Deletes the output directory if `PrepareAsync` had already created it.                         |

Artifacts collected (all conditional on `DiagnosticDumpPayloadDto` flags):

| Flag               | Output file pattern                                       |
|--------------------|-----------------------------------------------------------|
| `DumpEntities`     | `dump_{ts}_entities_{name}_{id}.json`                     |
| `DumpArchitecture` | `dump_{ts}_architecture_{name}_{id}.json`                 |
| `DumpEvents`       | `dump_{ts}_events_{name}_{id}.json`                       |
| `DumpLogs`         | `dump_{ts}_logs_{name}_{id}.log`                          |

---

## Dependencies

### Project References

| Project                       | Purpose                                                                      |
|-------------------------------|------------------------------------------------------------------------------|
| `Hrot.Core`                   | `NodeRole`, `HrotNodeConfig`, `HrotNodeContext`, `ISlaveOrchestrationTranslator` |
| `Hrot.Network.Orchestration`  | `NodeOpSlaveTranslator`, `DdsIdAllocatorHelper`                              |
| `Fdp.Core`                    | `EntityRepository`, `FdpEventBus`, `Entity`, `ModuleHostKernel` and all ECS primitives |
| `Fdp.ModuleHost`              | `IArchitectureDiagnosticsService`, `IEcsModule`, `ISimulationView`, `IEcsModuleSystem` |
| `Fdp.Toolkits`                | `IEntityStateExtractionService`, `JsonAestheticFormatter`                    |
| `Fdp.Toolkits.Analyzers`      | Roslyn source generator - emits `GizmoRegistrar.g.cs` for `[GizmoProjector]` classes |
| `Fdp.Network.Cyclone`         | `DdsIdAllocator`, `CycloneNetworkIngressSystem`, `CycloneEgressSystem`       |
| `StructEdit.Core`             | `IComponentEditService` used by `LayerControlGizmo`                          |
| `StructEdit.Json`             | JSON serialization for StructEdit schemas                                    |

### NuGet Packages

| Package          | Version | Purpose                                               |
|------------------|---------|-------------------------------------------------------|
| `CycloneDDS.NET` | 0.2.2   | `DdsParticipant`, `DdsReader`, `DdsWriter` in `HrotNodeBuilder` |

### `InternalsVisibleTo`

Internal members are exposed to these test and sibling assemblies:

| Assembly              |
|-----------------------|
| `Hrot.SimHost.Tests`  |
| `Hrot.Editor.Tests`   |
| `Hrot.Network`        |
| `Hrot.IG.Tests`       |

---

## JSON Migration Modules

`Hrot.Common` houses all application-layer JSON migration registrations for the HROT engine.
These modules register HROT-owned document types into `Fdp.Core.Serialization.Migrations.MigrationRegistry`
via `HrotMigrationBootstrap`. The generic migration infrastructure lives in
`Fdp.Core.Serialization.Migrations` (see [Fdp.Core.Serialization.Migrations.md](../../FDP/Core/Fdp.Core.Serialization.Migrations.md)).

### File Layout

```
Hrot/Engine/Hrot.Common/Scenario/
|-- HrotDocumentTypes.cs               (doc-type constants for all HROT formats)
+-- Migrations/
    |-- HrotMigrationBootstrap.cs      (role-driven MigrationServices factory)
    |-- PassthroughFormatsModule.cs    (stable-schema passthrough registrations)
    |-- ScenarioMigrationModule.cs     (Hrot.Scenario v1<->v2 migrators)
    |-- BlueprintMigrationModule.cs    (Hrot.Blueprints -- skeleton, v1 only)
    |-- BehaviorTreeMigrationModule.cs (Hrot.BehaviorTree -- passthrough v1)
    |-- TkbMigrationModule.cs          (Hrot.Tkb -- skeleton, v1 only)
    |-- RoadNetworkMigrationModule.cs  (Fdp.RoadNetwork -- passthrough v1)
    |-- Helpers/
    |   |-- CasingPolicy.cs            (PascalCase vs camelCase helpers)
    |   |-- EntityPatch.cs             (per-entity/per-component iteration helpers)
    |   +-- NestedJsonPatch.cs         (BehaviorParams / ExtensionJson nested-JSON helpers)
    +-- Migrators/
        +-- Scenario/
            |-- V1ToV2_EntityInfo_AddTags.cs
            +-- V2ToV1_EntityInfo_RemoveTags.cs
```

Test coverage lives in `Hrot/Engine/Hrot.Common.Tests/`:

```
Hrot/Engine/Hrot.Common.Tests/
|-- Migrations/
|   |-- ModuleRegistrationTests.cs    (all modules register without error)
|   +-- ScenarioPhase2Tests.cs        (scenario read/write round-trip with envelope)
+-- Scenario/
    +-- Migrations/
        |-- Phase2ConventionTests.cs  (all committed fixtures carry valid $meta)
        |-- Phase3MigratorTests.cs    (V1->V2->V1 round-trip, notes, warnings)
        +-- EntityPatchTests.cs       (EntityPatch helper unit tests)
```

### `HrotDocumentTypes` (static class)

Declares all HROT-owned `$meta.docType` constants. The successor to `HrotSubsystemTypes`
for migration registration (the original `HrotSubsystemTypes` is kept for backward
compatibility with non-migration callers).

| Constant | Value | Category |
|---|---|---|
| `Scenario` | `"Hrot.Scenario"` | Versioned, customer-facing |
| `Blueprint` | `"Hrot.Blueprints"` | Versioned, customer-facing |
| `BehaviorTree` | `"Hrot.BehaviorTree"` | Versioned, customer-facing |
| `TkbDefinition` | `"Hrot.Tkb"` | Versioned, customer-facing |
| `StructEdit` | `"Hrot.StructEdit"` | Passthrough |
| `MapInteractionConfig` | `"Hrot.MapInteractionConfig"` | Passthrough |
| `OrchestratorContext` | `"Hrot.OrchestratorContext"` | Passthrough at version 2 (C-4: disk files already at v2) |
| `TestScript` | `"Hrot.TestScript"` | Passthrough |
| `NodeConfiguration` | `"Hrot.NodeConfiguration"` | Passthrough |

### `HrotMigrationBootstrap` (static class)

Role-driven factory for `MigrationServices`. Each host process calls one method during
startup. Each method registers only the formats that host actually loads (M-2 principle:
no unused format registrations).

| Method | Registers | Typical caller |
|---|---|---|
| `BuildSimHostCgf(writerIdentifier)` | Scenario, TKB, RoadNetwork + OrchestratorContext passthrough | SimHost, CGF node startup |
| `BuildIg()` | Scenario, TKB + OrchestratorContext + MapInteractionConfig passthroughs | IG node startup |
| `BuildEditor()` | All customer-facing formats + all passthrough formats | Hrot.Editor bootstrap |
| `BuildClusterRunnerMigrate()` | Same as Editor profile | `Hrot.ClusterRunner --mode migrate` |
| `BuildClusterRunnerCi()` | Scenario, TKB, RoadNetwork (read-only profile) | `Hrot.ClusterRunner --mode ci` |

All overloads call `MigrationBootstrap.BuildForProduction(...)` from `Fdp.Core`.

### `PassthroughFormatsModule` (static class)

Registers all engine-internal HROT document formats as passthrough doc types. These formats
have stable schemas that never need a migration chain; only the `$meta` envelope wraps them.

Registered doc types and their current schema versions:

| Doc type | Version | Note |
|---|---|---|
| `HrotDocumentTypes.StructEdit` | 1 | StructEdit session state |
| `HrotDocumentTypes.MapInteractionConfig` | 1 | ExCon map interaction state |
| `HrotDocumentTypes.OrchestratorContext` | 2 | Disk files already at v2 (correction C-4) |
| `HrotDocumentTypes.TestScript` | 1 | CI/CD test scripts |
| `HrotDocumentTypes.NodeConfiguration` | 1 | Node `config.json` files |

### `ScenarioMigrationModule` (static class)

Registers `Hrot.Scenario` at version 2 with a v1<->v2 migration chain.

| Constant | Value |
|---|---|
| `CurrentVersion` | `2` |

Registered migrators:

| Class | Direction | Schema change |
|---|---|---|
| `V1ToV2_EntityInfo_AddTags` | v1 -> v2 | Adds `Tags: []` to each entity's `EntityInfo` component |
| `V2ToV1_EntityInfo_RemoveTags` | v2 -> v1 | Removes `Tags` from each entity's `EntityInfo` (lossy) |

The down-migration from v2 to v1 is lossy: tag content cannot be recovered from a v1 file.
The `PersistentMigrationAdapter` writes an unknowns journal to capture removed tags.

### Migrator Helper Classes

#### `EntityPatch` (static class)

Scenario-specific helpers for iterating the `$.entities` dictionary and applying
per-component transformations. Handles mixed PascalCase/camelCase entity payloads.

| Method | Description |
|---|---|
| `OnEachEntity(root, action)` | Iterates every entity in `$.entities`. Snapshots keys before iteration. |
| `OnComponent(root, componentName, action)` | Iterates entities that have the named component. |
| `RenameComponent(root, oldName, newName)` | Renames a component key across all entities. Throws if both names coexist on an entity. |
| `RenameField(root, componentName, oldField, newField)` | Renames a field within a component across all entities. |

#### `CasingPolicy` (static class)

Helpers that handle mixed PascalCase/camelCase property access in entity payloads.
`FdpAutoSerializer` uses PascalCase; some custom translators (e.g. `MissionPlanTranslator`)
use camelCase. Migrators that need to locate a field regardless of casing use this class.

#### `NestedJsonPatch` (static class)

Helpers for `BehaviorParams` and `ExtensionJson` fields, which contain stringified JSON
nested inside the document. Methods handle the unescape-transform-re-escape cycle that
migrators touching these fields must perform.

---

## Usage Examples

### Example 1: Building a headless node with HrotNodeBuilder

```csharp
// Composition root of a SimHost node (simplified).
var config = new HrotNodeConfig
{
    NodeId        = 42,
    SubsystemName = "SimHost",
    Headless      = false,
    LocalTempRoot = @"C:\Temp\SimHost",
    ExternalParticipant = ddsParticipant,   // owned by the application shell
};

HrotNodeContext context = new HrotNodeBuilder(config)
    .WithRole("SimHost", NodeRole.MuscleGround)
    .WithNetworkFactory(nedNetworkFactory)
    .Build();

// context.World       -- EntityRepository
// context.Kernel      -- ModuleHostKernel (not yet initialized)
// context.EventBus    -- FdpEventBus
// context.IdAllocator -- INetworkIdAllocator
```

### Example 2: Registering action handlers in GlobalActionRegistry

```csharp
// Composition root wires up handlers once before Kernel.Initialize().
var actionRegistry = new GlobalActionRegistry();

actionRegistry.Register(GlobalActionIds.MoveHere, (view, target) =>
{
    if (target == Entity.Null) return;
    // Publish a MoveIntent managed event on the bus.
    view.Bus.PublishManaged(new MoveIntent { Entity = target, Destination = _pendingMovePos });
});

actionRegistry.Register(GlobalActionIds.Stop, (view, target) =>
{
    if (target == Entity.Null) return;
    view.Bus.PublishManaged(new StopIntent { Entity = target });
});

actionRegistry.Register(GlobalActionIds.CenterOnEntity, (view, target) =>
{
    if (target == Entity.Null) return;
    ref readonly var tf = ref view.GetComponentRO<SimTransform>(target);
    _cameraController.CenterOn(tf.Position);
});

// Pass the registry to the system at construction time.
var dispatchSystem = new GlobalActionDispatchSystem(actionRegistry, interactionBus);
```

### Example 3: Setting up the GizmoInteractionModule for a headless SimHost

```csharp
// The SimHost does not render, but must receive gizmo interaction batches from
// the IG over DDS and route context-menu actions through the same pipeline.
var interactionBus = new FdpEventBus();
InteractionEventRegistry.RegisterAll(interactionBus);

var entityMap = context.EntityMap;

var contextIngress = new ContextActionIngressSystem(entityMap, interactionBus);
var dispatchSystem = new GlobalActionDispatchSystem(actionRegistry, interactionBus);

// gizmoIngress reads DDS GizmoInteractionBatch and writes to interactionBus.
var gizmoIngress = cycloneFactory.CreateGizmoIngressSystem(interactionBus);

var module = new GizmoInteractionModule(
    interactionBus:      interactionBus,
    contextIngress:      contextIngress,
    interactionSystems:  new IEcsModuleSystem[] { dispatchSystem },
    gizmoIngress:        gizmoIngress,
    gizmoEgress:         null          // SimHost does not project gizmos back
);

context.Kernel.RegisterModule(module);
```

### Example 4: Registering genesis intent components and using them at load time

```csharp
// Phase 2 of SharedApplicationBootstrapper:
protected override void RegisterDomainComponents(EntityRepository world)
{
    // Register structural ECS components ...
    world.RegisterComponent<SimTransform>();
    world.RegisterComponent<UnitSubordinate>();
    world.RegisterComponent<UnitRoster>();

    // Register transient intent DTOs used by the genesis pipeline.
    GenesisIntentRegistry.RegisterAll(world);
}

// Later, a translator injects intent data during scenario load:
void InjectPassengerData(EntityRepository world, Entity vehicle, long[] passengerIds)
{
    var intent = new InitialPassengersIntent();
    intent.PassengerNetworkIds.AddRange(passengerIds);
    world.SetManagedComponent(vehicle, intent);
}

// GenesisMaterializationSystem reads and resolves the intent:
void MaterializePassengers(EntityRepository world, Entity vehicle, NetworkEntityMap entityMap)
{
    if (!world.HasManagedComponent<InitialPassengersIntent>(vehicle)) return;

    var intent = world.GetManagedComponent<InitialPassengersIntent>(vehicle);
    foreach (long netId in intent.PassengerNetworkIds)
    {
        if (entityMap.TryGetEntity(netId, out var passenger))
            AddPassenger(world, vehicle, passenger);
    }

    // Remove transient intent after materialization.
    world.SetManagedComponent<InitialPassengersIntent>(vehicle, null!);
}
```

### Example 5: Implementing SharedApplicationBootstrapper for a custom node type

```csharp
public sealed class MyNodeBootstrapper : SharedApplicationBootstrapper
{
    protected override HrotNodeContext BuildContext(
        HrotNodeConfig config, NodeRole role, INetworkFactory? factory)
    {
        // Must call .WithReplication() here because Hrot.Common cannot reference
        // Hrot.Network.NED (would be a circular dependency).
        return new HrotNodeBuilder(config)
            .WithRole("MyNode", role)
            .WithNetworkFactory(factory)
            .Build()
            .WithReplication(role);   // extension method from Hrot.Network.NED
    }

    protected override void RegisterDomainComponents(EntityRepository world)
    {
        world.RegisterComponent<SimTransform>();
        GenesisIntentRegistry.RegisterAll(world);
        // ... domain-specific components
    }

    protected override ScenarioSerializer BuildSerializer(BehaviorRegistry? registry)
        => HrotScenarioSerializerFactory.Create(registry);

    protected override void PopulateSystems(
        HrotNodeContext context,
        List<IEcsModuleSystem> input,
        List<IEcsModuleSystem> sim,
        List<IEcsModuleSystem> postSim)
    {
        sim.Add(new UnitHierarchySystem());
        sim.Add(new MissionControlExecutionSystem(
            context.EntityMap, _behaviorRegistry, _mapperRegistry));
    }

    protected override ClusterSlave BuildOrchestration(
        HrotNodeContext context,
        TogglableSimulationGroup simGroup,
        TogglablePostSimulationGroup postSimGroup,
        ScenarioSerializer serializer)
    {
        return NodeBootstrapper.BuildOrchestration(
            context, simGroup, postSimGroup, serializer,
            lifecycleGroup: context.NedReplication?.NetworkLifecycleGroup);
    }

    protected override void RegisterSpawningPipeline(HrotNodeContext context)
    {
        // Register entity lifecycle, spawning, and sensor feed systems.
    }

    protected override void RegisterNetworkTranslators(
        HrotNodeContext context, INetworkFactory? factory)
    {
        // Register domain DDS translators via factory.
    }
}
```

---

## Best Practices

### 1. Never reference Hrot.Common from Hrot.Core

`Hrot.Core` is a leaf library. The dependency arrow runs upward:
`Hrot.Core` -> (nothing from Hrot.* layer). `Hrot.Common` references `Hrot.Core`.
Any type that `Hrot.Core` needs must be defined in `Hrot.Core` itself.

### 2. Keep GlobalActionIds values stable

`GlobalActionIds` constants are serialised as integer strings in DDS `ContextActionTriggered`
messages and stored in scenario files. Changing a value is a breaking change across all
deployed nodes. Add new IDs; never renumber existing ones.

### 3. Use the isolated interaction bus exclusively for UI events

All gizmo and context-menu events must travel on `_interactionBus` (injected into
`GizmoInteractionModule`), never on the global kernel bus. This prevents UI noise from
appearing in the diagnostic event history or triggering unintended simulation reactions.

### 4. `HrotNodeBuilder.Build` is single-use

The builder throws `InvalidOperationException` on a second `Build()` call. Create a new
builder instance for each node. Do not cache or reuse builders across application restarts.

### 5. Always implement `BuildContext` to call `.WithReplication`

`SharedApplicationBootstrapper` cannot reference `Hrot.Network.NED` due to a circular
dependency. The subclass `BuildContext` hook is the only place where `.WithReplication(role)`
can be called. Failing to do so means `NedReplicationModule` is null and replication is
silently disabled.

### 6. Respect the bootstrap phase order

`SharedApplicationBootstrapper.BootstrapNode` is `sealed`. The 7-phase order is
non-negotiable. In particular:
- `RegisterDomainComponents` (Phase 2) must complete before `BuildSerializer` (Phase 3).
- `RegisterModule(NedReplication)` (Phase 6a+) must be called by the base class, never by a subclass.
- `Kernel.Initialize()` (Phase 7) must always be the final call.

### 7. Use `HrotSubsystemTypes` constants in scenario headers

Do not hard-code subsystem type strings in serializers. Use the constants from
`HrotSubsystemTypes` to ensure load handlers correctly identify their own payload sections.

### 8. Genesis intent DTOs are transient

All `Initial*Intent` types carry `[DataPolicy(DataPolicy.Transient)]`. They must be
removed (set to `null`) by `GenesisMaterializationSystem` after the live components are
created. Leaving them attached wastes memory and can cause unexpected behavior during
snapshot serialization.

### 9. Gizmo projectors must be stateless when possible

Prefer `IStatelessGizmo` over `IEntityStatefulGizmo` for per-entity projectors. Stateless
projectors can be executed in parallel and do not require any per-entity bookkeeping in the
gizmo manager. Use `IEntityStatefulGizmo` only when interaction-FSM state is needed
(e.g., `LayerControlGizmo`).

---

## Related Projects

| Project                     | Relationship                                                                              |
|-----------------------------|-------------------------------------------------------------------------------------------|
| `Hrot.Core`                 | Direct dependency. Provides `HrotNodeConfig`, `HrotNodeContext`, `NodeRole`, ECS component IDs. |
| `Hrot.Network.Orchestration`| Direct dependency. Provides `NodeOpSlaveTranslator`, `DdsIdAllocatorHelper`.              |
| `Hrot.Network.NED`          | Consumer. References `Hrot.Common` for genesis intent types and interaction events. Provides `.WithReplication()`. |
| `Hrot.SimHost`              | Consumer. Implements `SharedApplicationBootstrapper`, uses `HrotNodeBuilder`, `MissionControlExecutionSystem`, `UnitHierarchySystem`. |
| `Hrot.IG`                   | Consumer. Uses gizmos, interaction pipeline, `GlobalActionRegistry`, `GlobalDebugSettings`. |
| `Hrot.NodeComposition`      | Consumer. `StrideNodeBootstrapper` (formerly in `Hrot.StrideMock`) uses `Hrot.Common`'s gizmo library and node infrastructure while composing the real Stride host's systems. |
| `Hrot.Editor`               | Consumer. Uses scenario subsystem types and genesis intent DTOs for scenario editing.     |
| `Fdp.Core`                  | Direct dependency. Provides the entire ECS foundation.                                    |
| `Fdp.ModuleHost`            | Direct dependency. Provides `IEcsModule`, `ModuleHostKernel`, `ISimulationView`.          |
| `Fdp.Toolkits`              | Direct dependency. Provides diagnostics extraction services and gizmo framework.          |
| `Fdp.Toolkits.Analyzers`    | Analyzer-only dependency. Generates `GizmoRegistrar.g.cs` from `[GizmoProjector]` attributes. |
| `Fdp.Network.Cyclone`       | Direct dependency. Provides DDS ID allocator and ingress/egress system infrastructure.    |
