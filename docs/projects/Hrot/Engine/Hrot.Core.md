# Hrot.Core

| Property | Value |
|---|---|
| **Project file** | `Hrot/Engine/Hrot.Core/Hrot.Core.csproj` |
| **Target framework** | net8.0 |
| **Nullable** | enabled |
| **Unsafe blocks** | enabled |
| **Date documented** | 2026-05-23 |

---

## README Validation

**Status: Missing**

No `README.md` exists in `Hrot/Engine/Hrot.Core/` or in the `Hrot/Engine/` parent folder.
No pre-existing documentation was found to diverge from.

---

## Executive Overview

`Hrot.Core` is the **shared kernel** of the HROT military-simulation system.
It sits at the lowest layer of the HROT codebase and is referenced by every
subsystem: SimHost, IG (Image Generator), CGF (Computer-Generated Forces),
Editor, Network adapters, and test projects.

The project defines three orthogonal concerns:

1. **Domain vocabulary** - The entity types, ECS component types, behavior
   contracts, mission-plan structures, and scenario DTOs that all subsystems
   speak.

2. **Network abstraction** - Protocol-neutral interfaces (prefixed `I`) for
   every DDS concern: entity lifecycle, mission-control gateways, IG adapters,
   time-control, orchestration, and replication.  Concrete implementations live
   in `Hrot.Network.NED` or `Hrot.Network.BDC`; Hrot.Core never references
   them.

3. **Shared infrastructure** - `HrotNodeConfig` / `HrotNodeContext` (bootstrap
   carrier types), component registries, serialisation options, the zone
   manager service, and the dead-reckoning ECS system.

### Key Domain Concepts

| Concept | What it is |
|---|---|
| **NodeRole** | Flag enum selecting which simulation modules a process hosts (Brain, MuscleGround, ImageGenerator, Perception, NavigationSolver) |
| **PackRole** | Enum distinguishing DDS Ingress (reader) packs from Egress (writer) packs |
| **TkbEntityTypes** | Integer constants for every spawnable entity class (tanks, infantry, routes, zones, etc.) |
| **MissionPlan / MissionTask** | Domain POCO representing the ordered list of behavior-parameterised tasks assigned to one entity |
| **BehaviorContractAttribute** | Marker + metadata attribute linking a behavior-parameter DTO to its engine ID and valid entity categories |
| **HrotNodeConfig / HrotNodeContext** | Bootstrap carrier types passed between the composition root and subsystem modules |
| **ZoneManagerService** | ACL pivot that translates `ZoneDefinitionDto` into ECS physics-collider entities and a road-network singleton |
| **HrotScenarioEnvelope** | JSON envelope helper that peeks the `Header.SubsystemType` field to route scenario files to the correct loader |

### Architectural Layer

```
+----------------------------------------------------------+
|  HROT Subsystems: SimHost, IG, CGF, Editor, Network      |
+----------------------------------------------------------+
                         |
                  depends on
                         |
                         v
+----------------------------------------------------------+
|                   Hrot.Core  (this project)              |
|  Domain vocab | Network abstractions | Infrastructure    |
+----------------------------------------------------------+
                         |
                  depends on
                         |
                         v
+----------------------------------------------------------+
|       FDP Framework: Fdp.Core, Fdp.Toolkits              |
+----------------------------------------------------------+
```

---

## Architecture

### Design Decisions

**1. No concrete DDS types in Hrot.Core**
CycloneDDS code generation is explicitly disabled (`CycloneDdsDisableCodeGen=true`)
to avoid IDL keyword conflicts arising from the `Hrot.Map.*` namespace containing
the word "map", which is a reserved IDL keyword.  The project imports
`CycloneDDS.NET` only for `DdsParticipant` (stored in `HrotNodeConfig` and
`HrotNodeContext`).  All DDS reads/writes go through the `INetworkFactory`
abstraction; wire types never appear in Hrot.Core.

**2. Multiple logical namespaces in one csproj**
For historical reasons (merger of former `Hrot.Common.csproj` and
`Hrot.Map.Common.csproj`) the project hosts classes in several namespaces:
`Hrot.Common`, `Hrot.Core.Network`, `Hrot.Map.Common`, `Hrot.Map.Definitions`,
`Hrot.IG.Components`, etc.  This avoids circular references while keeping a
single buildable artifact.

**3. InternalsVisibleTo pattern**
The project exposes `internal` members to a named set of test and subsystem
assemblies via `[InternalsVisibleTo]` assembly attributes declared in the
`.csproj` file.  This prevents leaking implementation details to unrelated
assemblies while still enabling white-box unit testing and subsystem-internal
sharing.

**4. Component ID block ownership**
FDP component IDs 0-159 are reserved for the framework toolkit.
Hrot.Core owns IDs 160-186 via `HrotComponentIds`, documented with block
comments that enumerate each ID's purpose and lifecycle intent.

**5. Interface-segregated network factory**
`INetworkFactory` aggregates more than fifteen creation methods, each returning
a narrow, single-concern interface (e.g. `ICommandGateway`, `ITimeControlGateway`,
`IIgNetworkAdapter`).  Callers depend only on the interface they use rather than
the monolithic factory type.

**6. Null-object pattern for headless / test mode**
Every network interface that requires a live DDS participant has a corresponding
`Null*` implementation in `Network/NullOrchestrationImplementations.cs` and
`Network/NullEntityAckSink.cs`.  These allow the composition root to wire
subsystems without conditional `if (participant != null)` guards scattered
throughout domain code.

**7. Component registries as static classes**
`HrotSharedComponentRegistry`, `RouteComponentRegistry`,
`PresentationComponentRegistry`, `MissionComponentRegistry`, and
`ZoneComponentRegistry` are static classes with a single `RegisterAll(EntityRepository)`
method.  This pattern keeps the ECS bootstrap sequence explicit and makes it
trivial to understand which components are available in a given deployment.

**8. BehaviorContractAttribute reflection catalog**
`BehaviorCatalog` builds its per-entity-type behavior lists at type initialisation
time by scanning the assembly for `[BehaviorContract]` attributes.  Adding a new
behavior DTO automatically populates the editor drop-down without any manual
catalog update.

---

## ASCII Block Diagrams

### Diagram 1: Component Registry Bootstrap Flow

```
EntityRepository (world)
        |
        | RegisterAll()
        v
+-----------------------------------+
|  HrotSharedComponentRegistry      |
|  - NetworkIdentity                |
|  - NetworkOwnership               |
|  - SimTransform / SimVelocity     |
|  - Health                         |
|  - TkbIdentity, VisualData        |
|  - Lifecycle events               |
+-----------------------------------+
        |
        | RegisterAll()
        v
+-----------------------------------+
|  RouteComponentRegistry           |
|  - RoutePlan (managed)            |
|  - PersonalRouteRef               |
|  - RouteTrajectoryCache           |
+-----------------------------------+
        |
        | RegisterAll()
        v
+-----------------------------------+
|  MissionComponentRegistry         |
|  - ActiveMissionPlan (managed)    |
|  - MissionControlIntent (event)   |
+-----------------------------------+
        |
        | RegisterAll()
        v
+-----------------------------------+
|  PresentationComponentRegistry    |
|  - EntityInfo                     |
|  - SelectionState                 |
|  - EditablePolyline (managed)     |
|  - IgHealthState                  |
|  - MapOverlayStyle                |
+-----------------------------------+
        |
        | RegisterAll()
        v
+-----------------------------------+
|  ZoneComponentRegistry            |
|  - ZoneMembership (managed)       |
|  - SpawnZoneObstacleCommand       |
|  - UpdateZoneConfigCommand        |
+-----------------------------------+
```

### Diagram 2: Network Factory Abstraction Layer

```
  Subsystem (SimHost, IG, CGF, Editor)
          |
          |  depends on
          v
  +-------------------------------+
  |     INetworkFactory           |
  +-------------------------------+
    |    |    |    |    |    |
    |    |    |    |    |    +--------> IOrchestrationTranslator (master)
    |    |    |    |    +------------> ISlaveOrchestrationTranslator
    |    |    |    +-----------------> ICommandGateway (mission/entity cmds)
    |    |    +----------------------> IIgNetworkAdapter (IG DDS I/O)
    |    +----------------------------> IExConEgressWriters (ExCon egress)
    +---------------------------------> IReplicationModule (state sync)

  Concrete implementations (NOT in Hrot.Core):
  +-------------------------------+    +-------------------------------+
  |  NedNetworkFactory            |    |  BdcNetworkFactory            |
  |  (Hrot.Network.NED)           |    |  (Hrot.Network.BDC)           |
  +-------------------------------+    +-------------------------------+
```

### Diagram 3: Entity Lifecycle Request Pipeline

```
  Network adapter (NED/BDC)
          |
          | Enqueue()
          v
  +----------------------------------+
  | ScenarioEntityCreationRequest    |
  | Source (ConcurrentQueue, 500/tick|
  | cap)                             |
  +----------------------------------+
          |
          | wrapped by
          v
  +----------------------------------+
  | CompositeEntityCreationRequest   |
  | Source (fan-in from N sources)   |
  +----------------------------------+
          |
          | ProcessRequests(handler)
          v
  CreateEntityRequestSystem (CGF)
          |
          | on completion
          v
  +----------------------------------+
  | IEntityAckSink.WriteAck()        |
  | NullEntityAckSink (headless)     |
  | NedEntityAckWriter (live)        |
  +----------------------------------+
```

### Diagram 4: Mission Control Data Flow

```
  ExCon / Editor
      |
      | MissionControlCommand (neutral DTO)
      v
  ICommandGateway.SendMissionControlRequestAsync()
      |
      | (NED wire)
      v
  CGF / Brain node
      |
      | MissionControlIngressTranslator publishes:
      v
  MissionControlIntent (managed bus event)
      |
      | MissionControlExecutionSystem consumes:
      v
  ActiveMissionPlan (ECS managed component on entity)
      |
      | MissionControlAckEvent (bus event)
      v
  MissionControlAckTranslator -> DDS ACK -> ExCon
```

### Diagram 5: Scenario Loading Flow

```
  ClusterOp (load scenario command)
       |
       v
  HrotScenarioLoader.TryLoadScenarioJson(scenarioId)
       |
       | IScenarioStorageProvider.EnumerateScenarioFiles()
       | foreach file: read text, peek header
       v
  HrotScenarioEnvelope.PeekSubsystemType(jsonText)
       |
       | matches "Hrot.Scenario" ?
       v
  HrotScenarioEnvelopeDto (deserialized)
       |     |
       |     +---> Header (SubsystemType, SchemaVersion, TkbName)
       |     +---> Zones  (Dictionary<name, ZoneDefinitionDto>)
       |     +---> Entities (opaque JsonObject -> FDP serializer)
       v
  ZoneManagerService.LoadZones()  +  IScenarioEntityExtractor.Extract()
       |                                        |
       v                                        v
  PhysicsCollider entities             EntityCreationRequest list
  ZoneEnvironmentData singleton        -> CreateEntityRequestSystem
```

---

## Source Structure

### Namespace Overview

| Namespace | Location | Responsibility |
|---|---|---|
| `Hrot.Common` | root, Events/Common | Node roles, events, perspective types |
| `Hrot.Common.Abstractions` | Abstractions/ | Replication module interfaces |
| `Hrot.Common.Infrastructure` | Infrastructure/ | Node config and context POCOs |
| `Hrot.Common.Scenario` | Scenario/Common/ | Scenario envelope helper and loader |
| `Hrot.Common.Events` | Events/Common/ | Bus events (WorldReset, SelectEntity, etc.) |
| `Hrot.Common.Systems` | Systems/Common/ | Dead-reckoning sync and mission helper |
| `Hrot.Core.Diagnostics` | Diagnostics/ | Log archive extraction service |
| `Hrot.Core.Mission` | Mission/ | Mission types, GeoPoint, trigger resolver |
| `Hrot.Core.Network` | Network/ | All protocol-neutral network interfaces |
| `Hrot.Map.Common` | root files, Config/, Services/ | Map config, serialiser options, environment factory, component registries |
| `Hrot.Map.Common.Components` | Components/Map/ | ECS components for routes, zones, styles |
| `Hrot.Map.Common.Config` | Config/ | MapViewConfig, MapLayerBits |
| `Hrot.Map.Common.Dds` | Dds/ | IDdsWriter abstraction and DdsWriterAdapter |
| `Hrot.Map.Common.Events` | Events/Map/ | Route commands, zone commands, shared events |
| `Hrot.Map.Common.Scenario` | Scenario/Map/ | Scenario envelope DTO, zone/obstacle DTOs |
| `Hrot.Map.Common.Services` | Services/ | IZoneManagerService / ZoneManagerService |
| `Hrot.Map.Definitions` | MapDefinitions/ | HrotComponentIds, component ID constants |
| `Hrot.Map.Definitions.Tkb` | MapDefinitions/Tkb/ | TKB templates, entity defs, builder, catalogs |
| `Hrot.Map.Definitions.Behavior` | MapDefinitions/Behavior/ | Behavior IDs, contracts, parameter DTOs |
| `Hrot.Map.Definitions.Behavior.Intents` | MapDefinitions/Behavior/Intents/ | Tactical intent DTOs |
| `Hrot.IG.Components` | Components/Map/, Components/Common/ | IG-specific ECS components (culling, selection, style, etc.) |

### File-Level Inventory

#### Root Files

| File | Type | Responsibility |
|---|---|---|
| `HrotEnvironment.cs` | `static class HrotEnvironment` | Stateless factory for `TkbDatabase`, `WGS84Transform`, and `DdsParticipant`; seeds the Berlin origin by default |
| `HrotSerializerOptions.cs` | `static class HrotSerializerOptions` | Pre-built `JsonSerializerOptions` (camelCase, case-insensitive, indented) for scenario DTO round-trips |
| `HrotSharedComponentRegistry.cs` | `static class HrotSharedComponentRegistry` | Single-call registration of all shared ECS components and events into a world |
| `MapConfig.cs` | `static class MapConfig` + `ContextKeys` | Constants for default map group/instance IDs and UI tool context string literals |
| `NodeRole.cs` | `[Flags] enum NodeRole` | Distributed node role discriminator (Brain, MuscleGround, ImageGenerator, Perception, NavigationSolver) |
| `PackRole.cs` | `enum PackRole` | DDS translator pack direction (Ingress / Egress) |
| `RouteComponentRegistry.cs` | `static class RouteComponentRegistry` | Registers route ECS components and the `CmdAppendPersonalWaypoint` event |
| `PresentationComponentRegistry.cs` | `static class PresentationComponentRegistry` | Registers presentation ECS components and UI events |
| `MissionComponentRegistry.cs` | `static class MissionComponentRegistry` | Registers `ActiveMissionPlan` and `MissionControlIntent` |
| `ZoneComponentRegistry.cs` | `static class ZoneComponentRegistry` | Registers `ZoneMembership`, `SpawnZoneObstacleCommand`, `UpdateZoneConfigCommand` |
| `RouteTkbExtensions.cs` | `static class RouteTkbExtensions` | Attaches `RoutePlan` managed-component factory to the TKB route template (Phase 6 hook) |

#### Infrastructure/

| File | Type | Responsibility |
|---|---|---|
| `HrotNodeConfig.cs` | `sealed class HrotNodeConfig` | Configuration bag for `HrotNodeBuilder`: DDS domain, node ID, subsystem name, temp root, headless flag, participant override |
| `HrotNodeContext.cs` | `sealed record HrotNodeContext` | Immutable snapshot of all infrastructure objects produced by bootstrap (world, kernel, participant, event bus, entity map, cluster slave, etc.) |

#### Abstractions/

| File | Type | Responsibility |
|---|---|---|
| `IReplicationModule.cs` | `interface IReplicationModule` | Protocol-neutral replication subsystem (exposes `GhostCreationSystem`, `DriveFromNetwork`, `NetworkLifecycleGroup`) |
| `INedReplicationModule.cs` | `interface INedReplicationModule` | NED-specific extension of `IReplicationModule`; adds `AfterSeekCallback` for replay seek |

#### Mission/

| File | Type | Responsibility |
|---|---|---|
| `MissionTypes.cs` | enums + classes | `eForceIdentifier`, `eTaskState`, `eMissionCommandType`, `MissionTrigger`, `MissionTask`, `MissionPlan`, `MissionCommandPayload` |
| `GeoPoint.cs` | `struct GeoPoint` + `GeoPointArrayConverter` | Geodetic lat/lon/alt struct with custom JSON array serialisation (G17 precision, invariant culture) |
| `MissionTriggerHelper.cs` | `static class MissionTriggerHelper` | Resolves wire trigger strings to `EcsMissionTrigger` enum + numeric parameter; backward-compatible for legacy `ReachedDestination` |

#### Network/

| File | Type | Responsibility |
|---|---|---|
| `Commands.cs` | DTOs | Protocol-neutral command/event DTOs: `CreateEntityCommand`, `UpdateEntityDescriptorCommand`, `MissionControlCommand`, `MissionCommitResult`, `MapConfigDto`, `MapCommandDto`, `EntityPropertyPatch`, `MapClickEventDto`, `SelectionChangedEventDto`, `EntityLifecycleAckDto`, `MapCommandAckDto`, `ContextActionInvokedDto` |
| `EntityLifecycleInterfaces.cs` | enum + classes + interfaces | `EntityOperationStatus`, `EntityCreationRequest`, `EntityDeletionRequest`, `IEntityCreationRequestSource`, `IEntityDeletionRequestSource`, `IEntityAckSink` |
| `INetworkFactory.cs` | `interface INetworkFactory` | Master factory for all protocol-specific network infrastructure |
| `ICommandGateway.cs` | `interface ICommandGateway` | Async mission-control and entity descriptor commands |
| `IOrchestrationTranslator.cs` | `interface IOrchestrationTranslator` | Per-frame `Tick()` for the orchestrator master DDS transport |
| `ISlaveOrchestrationTranslator.cs` | `interface ISlaveOrchestrationTranslator` | Per-frame `Tick()` for slave orchestration (NodeOp in/out) |
| `IOrchestrationObserver.cs` | `interface IOrchestrationObserver` | Per-frame `Tick()` for observer nodes (ClusterState ingress) |
| `IMasterTimeTranslators.cs` | `interface IMasterTimeTranslators` | Three-phase time-sync bridge: `ScanAndPublish`, `PollIngress`, `PollNtpIngress` |
| `IIgNetworkAdapter.cs` | `interface IIgNetworkAdapter` + null stub | Complete IG DDS I/O (map click, selection, capabilities, map config polling, route creation, gizmo writers) |
| `IIgTranslators.cs` | `interface IIgTranslators` + `NullIgTranslators` | Factory for IG DDS ingress translator list |
| `IExConEgressWriters.cs` | `interface IExConEgressWriters` | Aggregate write surface for ExCon entity lifecycle egress |
| `ITimeControlGateway.cs` | `interface ITimeControlGateway` | Pause/resume/step/scale time-control commands to the Orchestrator |
| `ISimHostMissionSender.cs` | `interface ISimHostMissionSender` | SimHost visualization mission dispatch (navigate to point) |
| `ISimHostAuxiliaryTranslators.cs` | `interface ISimHostAuxiliaryTranslators` | Time-sync, combat, mission-control translators registered on the kernel |
| `ISimHostPathfindingTranslators.cs` | `interface ISimHostPathfindingTranslators` | Pathfinding DDS translators registered on the kernel |
| `ISimHostPerceptionTranslators.cs` | `interface ISimHostPerceptionTranslators` | Perception DDS translators registered on the kernel |
| `ICgfEntityLifecycleAdapters.cs` | `interface ICgfEntityLifecycleAdapters` | CGF entity lifecycle adapters (request source, delete source, ACK sink, ownership strategy, JSON compiler) |
| `IScenarioEntityExtractor.cs` | `interface IScenarioEntityExtractor` | Staging-based entity extraction from scenario JSON into `EntityCreationRequest` list |
| `DescriptorTypeOrdinals.cs` | `static class DescriptorTypeOrdinals` | Numeric ordinal constants for NED descriptor types (`EntityMaster`, `WorldPos`, `EntityMission`, etc.) |
| `CompositeEntityCreationRequestSource.cs` | `sealed class` | Fan-in composite draining N inner `IEntityCreationRequestSource` instances in order |
| `ScenarioEntityCreationRequestSource.cs` | `sealed class` | Thread-safe `ConcurrentQueue` source with per-tick drain cap (default 500) |
| `SequentialIdAllocator.cs` | `sealed class SequentialIdAllocator` | Thread-safe `Interlocked`-based sequential ID allocator for offline/headless mode |
| `NullOrchestrationImplementations.cs` | null stubs | `NullOrchestrationTranslator`, `NullMasterTimeTranslators`, `NullSlaveOrchestrationTranslator`, `NullOrchestrationObserver`, `NullDisposable` |
| `NullEntityAckSink.cs` | `sealed class NullEntityAckSink` | No-op `IEntityAckSink` discarding all ACK writes in offline mode |

#### Orchestration/

| File | Type | Responsibility |
|---|---|---|
| `IOrchestrationTranslator.cs` | `interface IOrchestrationTranslator` | Same-namespace version of the translator interface (namespace `Hrot.Common.Infrastructure`) |

#### Diagnostics/

| File | Type | Responsibility |
|---|---|---|
| `ILogArchiveExtractionService.cs` | `interface ILogArchiveExtractionService` | Contract for async log scanning, severity filtering, and archive writing |
| `LogArchiveExtractionService.cs` | `sealed class LogArchiveExtractionService` | Implementation: discovers NLog `FileTarget` files, filters by severity and age, writes to archive using `ReadOnlySpan<char>` line parsing |

#### Dds/

| File | Type | Responsibility |
|---|---|---|
| `IDdsWriter.cs` | `interface IDdsWriter<T>` | Thin write/tombstone abstraction over `DdsWriter<T>` for testability |
| `DdsWriterAdapter.cs` | `sealed class DdsWriterAdapter<T>` | Live `CycloneDDS.Runtime.DdsWriter<T>` wrapper implementing `IDdsWriter<T>` and `IDisposable` |

#### Config/

| File | Type | Responsibility |
|---|---|---|
| `MapViewConfig.cs` | `sealed class MapViewConfig` | Plain-data layer visibility flags (satellite, ground units, air units, grid) |
| `MapLayerBits.cs` | `static class MapLayerBits` | Bitmask constants for the five standard rendering layers (must match `MapLayerRegistry` in IG) |

#### Services/

| File | Type | Responsibility |
|---|---|---|
| `IZoneManagerService.cs` | `interface IZoneManagerService` | Contract: load zones to ECS (`LoadZones`) and retrieve active zones (`GetActiveZones`) |
| `ZoneManagerService.cs` | `sealed class ZoneManagerService` | Default implementation: spawns `SimTransform`+`PhysicsCollider` entities for obstacles, loads road-network blob into `ZoneEnvironmentData` singleton |

#### Systems/Common/

| File | Type | Responsibility |
|---|---|---|
| `DeadReckoningSyncSystem.cs` | `class DeadReckoningSyncSystem : IEcsModuleSystem` | Post-simulation ECS system: projects ghost positions forward by network velocity, blends `SimTransform` toward projected position with smoothing |
| `MissionControlBehaviorParamsHelper.cs` | `internal static class` | Translates `FollowRoute` behavior params by resolving route entity ID to `TrajectoryId` via ECS query |

#### Events/

| File | Namespace | Type | Responsibility |
|---|---|---|---|
| `Events/Common/WorldResetEvent.cs` | `Hrot.Common.Events` | `struct WorldResetEvent` | Published before world-reset operations (EventId 8101) |
| `Events/Common/SelectEntityCommand.cs` | `Hrot.Common.Events` | `struct SelectEntityCommand` | UI command to select an entity by network ID (EventId 8103) |
| `Events/Common/OpenRenameDialogCommand.cs` | `Hrot.Common.Events` | `struct OpenRenameDialogCommand` | UI command to open rename dialog for an entity (EventId 8102) |
| `Events/Common/TogglePerspectiveEvent.cs` | `Hrot.Common` | `record TogglePerspectiveEvent` | Published when the active UI perspective changes (OldPerspective, NewPerspective) |
| `Events/MissionControlCqrsEvents.cs` | `Hrot.Common.Events` | `MissionControlIntent` + `MissionControlAckEvent` | Managed intent class and unmanaged ACK struct for mission control CQRS flow |
| `Events/Map/SharedEvents.cs` | `Hrot.Map.Common.Events` | `struct FireInteractionEvent` | Fired on combat interactions; positions in FDP world-space metres (EventId 3001) |
| `Events/Map/RouteCommands.cs` | `Hrot.Map.Common.Events` | `struct CmdAppendPersonalWaypoint` | Operator Shift+RightClick command to append waypoint to vehicle's personal route (EventId 3002) |
| `Events/Map/UpdateZoneConfigCommand.cs` | `Hrot.Map.Common.Events` | `sealed class UpdateZoneConfigCommand` | Managed command to reload zone road-network config at runtime |
| `Events/Map/SpawnZoneObstacleCommand.cs` | `Hrot.Map.Common.Events` | `sealed class SpawnZoneObstacleCommand` | Managed command to spawn a zone obstacle entity (ZoneName, Position, Radius) |

#### Scenario/

| File | Namespace | Type | Responsibility |
|---|---|---|---|
| `Scenario/Common/HrotScenarioEnvelope.cs` | `Hrot.Common.Scenario` | `static class HrotScenarioEnvelope` | Peeks `Header.SubsystemType` from scenario JSON without full DOM parse; case-insensitive |
| `Scenario/Common/HrotScenarioLoader.cs` | `Hrot.Common.Scenario` | `sealed class HrotScenarioLoader : IScenarioLoader` | Iterates scenario storage files, peeks subsystem type, returns matching JSON |
| `Scenario/Map/HrotScenarioEnvelopeDto.cs` | `Hrot.Map.Common.Scenario` | `sealed class HrotScenarioEnvelopeDto` | Root DTO: Header + Zones dictionary + opaque `JsonObject` Entities section |
| `Scenario/Map/ScenarioHeaderDto.cs` | `Hrot.Map.Common.Scenario` | `sealed class ScenarioHeaderDto` | SubsystemType, SchemaVersion, TkbName |
| `Scenario/Map/ZoneDefinitionDto.cs` | `Hrot.Map.Common.Scenario` | `sealed class ZoneDefinitionDto` | Road-network path, terrain database ID, and obstacle list for one zone |
| `Scenario/Map/ZoneObstacleDto.cs` | `Hrot.Map.Common.Scenario` | `sealed class ZoneObstacleDto` | Single cylindrical obstacle: X, Y, Radius in zone local-flat-earth metres |

#### MapDefinitions/

| File | Namespace | Type | Responsibility |
|---|---|---|---|
| `MapDefinitions/HrotComponentIds.cs` | `Hrot.Map.Definitions` | `static class HrotComponentIds` | ECS component ID constants 160-186 with documented block allocation |
| `MapDefinitions/TkbEntityTypes.cs` | `Hrot.Map.Common` | `static class TkbEntityTypes` | Integer constants for every TKB entity type (tanks, IFVs, infantry, routes, zones, composites) |
| `MapDefinitions/Tkb/VisualData.cs` | `Hrot.Map.Definitions.Tkb` | `struct VisualData` | Blittable TKB component: MIL-STD-2525 symbol code, 3D model path, colour hex, map shape name |
| `MapDefinitions/Tkb/TkbCompositionDef.cs` | `Hrot.Map.Definitions.Tkb` | `class TkbCompositionDef` | ORBAT composite definition: subordinate slots with TKB type, count, and tactical designation |
| `MapDefinitions/Tkb/SimCombatDef.cs` | `Hrot.Map.Definitions.Tkb` | `class SimCombatDef` | Combat stats: armour (front/side/rear mm RHA), weapon mounts, sensor range, autonomous engagement flag |
| `MapDefinitions/Tkb/SimVehicleDef.cs` | `Hrot.Map.Definitions.Tkb` | `class SimVehicleDef` | Vehicle physics: mass, dimensions, max speed (fwd/rev), acceleration, turn rate, terrain mobility, fuel |
| `MapDefinitions/Tkb/IgVisualDef.cs` | `Hrot.Map.Definitions.Tkb` | `class IgVisualDef` | IG visual definition used during TKB builder calls: symbol code, model path, colour, scale, show-label, layer |
| `MapDefinitions/Tkb/BdcTkbCatalog.cs` | `Hrot.Map.Definitions.Tkb` | `static class NedTkbCatalog` | Registers all entity types (M1 Abrams, Bradley, HMMWV, T-72, infantry, platoons, routes, areas) into a `TkbDatabase` |
| `MapDefinitions/Tkb/BdcTkbBuilder.cs` | `Hrot.Map.Definitions.Tkb` | `class NedTkbBuilder` | Fluent builder for TKB templates; chains `DefineVehicle`, `WithVisual`, `WithPhysics`, `WithCombat`, `WithBehavior`, `WithDisType` |
| `MapDefinitions/Tkb/BehaviorCatalog.cs` | `Hrot.Map.Definitions.Tkb` | `static class BehaviorCatalog` | Reflection-built per-entity-type behavior name lists; `GetValidBehaviors(tkbType)` returns filtered list |
| `MapDefinitions/Behavior/BehaviorIds.cs` | `Hrot.Map.Definitions.Behavior` | `internal static class BehaviorIds` | Integer constants for all behavior parameter DTOs (range 3001-3099 for CGF behaviors) |
| `MapDefinitions/Behavior/BehaviorCategory.cs` | `Hrot.Map.Definitions.Behavior` | `[Flags] enum BehaviorCategory` | Entity category filter bits for behavior contracts (Civilian, MilitaryApc, Infantry, Insurgent, Commander) |
| `MapDefinitions/Behavior/BehaviorContractAttribute.cs` | `Hrot.Map.Definitions.Behavior` | `sealed class BehaviorContractAttribute` | Metadata attribute linking a DTO to its engine behavior ID, wire name, and valid entity categories |
| `MapDefinitions/Behavior/BehaviorTestHelper.cs` | `Hrot.Map.Definitions.Behavior` | `static class BehaviorTestHelper` | Reflection helper for unit tests to retrieve behavior name from a DTO type without hardcoding strings |
| `MapDefinitions/Behavior/MoveToLocationParamsJsonDto.cs` | `Hrot.Map.Definitions.Behavior` | DTO | `MoveToLocation` behavior: lat/lon/speed/arrivalRadius + pickable-geo-point facade |
| `MapDefinitions/Behavior/FollowRouteParamsJsonDto.cs` | DTO | DTO | `FollowRoute` behavior: route entity network ID (long, remappable) |
| `MapDefinitions/Behavior/JoinFormationParamsJsonDto.cs` | DTO | DTO | `JoinFormation` behavior: parameterless; anchors ID and Infantry category |
| `MapDefinitions/Behavior/FireAtTargetParamsJsonDto.cs` | DTO | DTO | `FireAtTarget` behavior: target network ID, max rounds, cooldown seconds |
| `MapDefinitions/Behavior/PlatoonHillAttackParamsJsonDto.cs` | DTO | DTO | `PlatoonHillAttack` commander behavior: firing-line segment, baseline segment, tank spacing, target area |
| `MapDefinitions/Behavior/AmbushParamsJsonDto.cs` | DTO | DTO | `Ambush` behavior: parameterless; Insurgent category |
| `MapDefinitions/Behavior/ConvoyEscortParamsJsonDto.cs` | DTO | DTO | `ConvoyEscort` behavior: parameterless; MilitaryApc category |
| `MapDefinitions/Behavior/InfantryCombatParamsJsonDto.cs` | DTO | DTO | `InfantryCombat` behavior: parameterless; Infantry category |
| `MapDefinitions/Behavior/IdleParamsJsonDto.cs` | DTO | DTO | `Idle` behavior: parameterless; AllMilitary category |
| `MapDefinitions/Behavior/WanderMilitaryParamsJsonDto.cs` | DTO | DTO | `WanderMilitary` behavior: parameterless; MilitaryApc category |
| `MapDefinitions/Behavior/Intents/DefendAreaIntentDto.cs` | DTO | DTO | `DefendArea` tactical intent: center lat/lon, radius in metres |

#### Components/Map/

| File | Namespace | Type | Responsibility |
|---|---|---|---|
| `RoutePlan.cs` | `Hrot.Map.Common.Components` | `sealed class RoutePlan` | Managed ECS component: ordered waypoint list + loop flag + monotonic version stamp; mutation via `Mutate()` callback |
| `RouteWaypoint.cs` | (inside RoutePlan.cs) | `struct RouteWaypoint` | Position in ENU world-space, target speed, optional extension JSON |
| `PersonalRouteRef.cs` | `Hrot.Map.Common.Components` | `struct PersonalRouteRef` | Blittable vehicle->route O(1) reference |
| `RouteTrajectoryCache.cs` | `Hrot.Map.Common.Components` | `struct RouteTrajectoryCache` | Compiled trajectory ID + version stamp; marked `NoSave` |
| `ZoneMembership.cs` | `Hrot.Map.Common.Components` | `sealed class ZoneMembership` | Managed component recording the zone name for an obstacle entity |
| `EditablePolyline.cs` | `Hrot.IG.Components` | `sealed class EditablePolyline` | Managed component storing user-editable vertex list + version counter |
| `MapOverlayStyle.cs` | `Hrot.IG.Components` | `struct MapOverlayStyle` | Blittable fill/border RGBA styling for map visual overlays |
| `CullingState.cs` | `Hrot.IG.Components` | `struct CullingState` | Viewport visibility flag + LOD level (Full/Simplified/IconOnly); `NoSave` |
| `CullingStateConstants.cs` | `Hrot.IG.Components` | `static class` | LOD level constants and zoom thresholds |
| `SelectionState.cs` | `Hrot.IG.Components` | `struct SelectionState` | `IsSelected` + `IsPrimarySelection` flags; `NoSave` |
| `ResolvedStyle.cs` | `Hrot.IG.Components` | `unsafe struct ResolvedStyle` | Cached render state from 3-layer style merge (TKB / network override / user config); fixed-buffer strings for allocation-free hot path |
| `ResolvedStyleConstants.cs` | `Hrot.IG.Components` | `static class` | Buffer sizes, affiliation tint colours (RGBA), damage range constants |
| `IgHealthState.cs` | `Hrot.IG.Components` | `struct IgHealthState` | Damage level 0-100 for IG rendering; `NoSave` |
| `IgSymbolOverride.cs` | `Hrot.IG.Components` | `class IgSymbolOverride` | ExCon-sourced per-entity visual override (style-set, texture, label, history trail flag) |
| `Color32.cs` | `Hrot.IG.Components` | `struct Color32` + `Color32ArrayConverter` | 4-byte RGBA colour with JSON array serialisation |
| `CanvasContextMenuState.cs` | `Hrot.IG.Components` | `sealed class CanvasContextMenuState` | Managed singleton for empty-map-space context menu JSON |

#### Components/Common/

| File | Namespace | Type | Responsibility |
|---|---|---|---|
| `ActivePerspective.cs` | `Hrot.Common` | `sealed class ActivePerspective` | ECS managed singleton tracking the active UI/world-space perspective name |

---

## Public API Reference

### Infrastructure

#### `HrotNodeConfig`
| Member | Description |
|---|---|
| `int DomainId` | CycloneDDS domain ID |
| `int NodeId` | Logical node identifier (heartbeats, entity IDs, log filenames) |
| `string SubsystemName` | Human-readable name published in heartbeats |
| `string LocalTempRoot` | Root directory for scenario staging; defaults to `OrchestrationConstants.DefaultStagingDirectory` |
| `bool Headless` | When true, all DDS initialisation steps are skipped |
| `bool SkipAllocatorRouting` | Skips `DdsIdAllocatorHelper.EnsureRouting` wait even in live mode |
| `DdsParticipant? ExternalParticipant` | Optional caller-supplied participant (bypasses builder's own participant creation) |
| `string LogDirectory` | Directory where this node writes log files |

#### `HrotNodeContext` (record)
| Member | Description |
|---|---|
| `EntityRepository World` | ECS entity repository |
| `ModuleHostKernel Kernel` | Module-host kernel |
| `DdsParticipant? Participant` | Live DDS participant; null in headless mode |
| `FdpEventBus EventBus` | Application event bus |
| `NetworkEntityMap EntityMap` | Shared ghost/egress network entity map |
| `ClusterSlave ClusterSlave` | Cluster slave wired with four reference handlers |
| `ISlaveOrchestrationTranslator? SlaveTranslator` | DDS bridge for NodeOpCommand / NodeOpStatus / NodeHeartbeat; null when headless |
| `IReadOnlyList<IEcsModule> BaseModules` | Infrastructure ECS modules to register before subsystem modules |
| `GhostCreationSystem? GhostCreationSystem` | Ghost creation system; null in headless mode |
| `INetworkIdAllocator? IdAllocator` | DDS ID allocator; null in headless mode |
| `int NodeId` | Local node ID |
| `ITkbDatabase? TkbDb` | TKB database shared by lifecycle module |
| `IGeographicTransform? GeoTransform` | Geodetic coordinate transform |

### Node Role and Pack Role

#### `NodeRole` (Flags enum)
| Value | Description |
|---|---|
| `None` | No role assigned |
| `Brain` | Behavior, mission planning, AI, cognitive dispatch |
| `MuscleGround` | Ground kinematics, navigation execution |
| `ImageGenerator` | Presentation-only IG renderer |
| `Perception` | LOS, broadphase, threat evaluation |
| `NavigationSolver` | On-demand pathfinding solver |

#### `PackRole` (enum)
| Value | Description |
|---|---|
| `Ingress` | DDS reader subscriptions only |
| `Egress` | DDS writer publications only |

### Mission Domain

#### `eForceIdentifier` (enum)
`FORCE_UNKNOWN`, `FORCE_FRIENDLY`, `FORCE_OPPOSING`, `FORCE_NEUTRAL`

#### `eTaskState` (enum)
`TASK_PLANNED`, `TASK_ACTIVE`, `TASK_DONE`, `TASK_FAILED`, `TASK_SKIPPED`

#### `eMissionCommandType` (enum)
`CMD_JUMP_TO_TASK`, `CMD_APPEND_TASK`, `CMD_INSERT_TASK`, `CMD_REPLACE_MISSION`, `CMD_ABORT_ALL`

#### `GeoPoint` (struct)
`double Latitude`, `double Longitude`, `double Altitude`
Serialised as a compact JSON array `[lat, lon, alt]` with G17 precision.

#### `MissionTask`
`Guid TaskId`, `string ExecutingEngine`, `string BehaviorId`, `string BehaviorParams`, `List<MissionTrigger> Triggers`, `eTaskState State`

#### `MissionPlan`
`Guid ActiveTaskId`, `List<MissionTask> Tasks`

#### `MissionTriggerHelper.ResolveTrigger()`
Accepts a trigger list, returns `(EcsMissionTrigger, float)`.
Maps `"ReachedDestination"` -> `BehaviorFinished` for backward wire compatibility.

### Network Interfaces

#### `INetworkFactory`
See network factory section above.  Key creation methods:

| Method | Returns |
|---|---|
| `CreateReplicationModule()` | `IReplicationModule` |
| `CreateCommandGateway()` | `ICommandGateway` |
| `CreateExConEgressWriters()` | `IExConEgressWriters` |
| `CreateTimeControlGateway()` | `ITimeControlGateway` |
| `CreateSimHostMissionSender()` | `ISimHostMissionSender` |
| `CreateSimHostAuxiliaryTranslators()` | `ISimHostAuxiliaryTranslators` |
| `CreateSimHostPathfindingTranslators(pool?)` | `ISimHostPathfindingTranslators` |
| `CreateSimHostPerceptionTranslators(ghost?)` | `ISimHostPerceptionTranslators` |
| `CreateSimHostAttributeUpdateSystems()` | `IReadOnlyList<IEcsModuleSystem>` |
| `CreateIgTranslators()` | `IIgTranslators` |
| `CreateIgNetworkAdapter(participant, nodeId)` | `IIgNetworkAdapter` |
| `CreateIgEgressTranslators(...)` | `IReadOnlyList<IDescriptorTranslator>` |
| `CreateExConIngressHandlers(...)` | `IEnumerable<IIngressHandler>` |
| `CreateOrchestratorTranslators(bus, nodeId)` | `IOrchestrationTranslator` |
| `CreateCgfEntityLifecycleAdapters()` | `ICgfEntityLifecycleAdapters?` |
| `ConfigureForNode(context, role, registry?)` | `INetworkFactory` |
| `ConfigureForNode(participant, nodeId, role)` | `INetworkFactory` |
| `WorldPosDescriptorId` | `long` (protocol ordinal for WorldPos descriptor) |
| `NavigationStatusDescriptorId` | `long` (protocol ordinal for NavigationStatus descriptor) |

#### `ICommandGateway`
| Method | Description |
|---|---|
| `Task<int> CreateEntityAsync(cmd, ct)` | Sends create-entity request; returns assigned entity ID |
| `Task SendUpdateDescriptorAsync(cmd, ct)` | Sends descriptor update |
| `Task<MissionCommitResult> SendMissionControlRequestAsync(cmd, ct)` | Sends mission command, returns commit result |
| `Task SendUpdateAttributeAsync(cmd, ct)` | Sends attribute-level JSON patch |

#### `IEntityCreationRequestSource`
`void ProcessRequests(Action<EntityCreationRequest> handler)` - callback-based drain to avoid per-frame allocations.

#### `IEntityAckSink`
`void WriteAck(Guid requestId, long entityId, EntityOperationStatus status)` - publishes lifecycle ACK.

#### `EntityCreationRequest`
| Property | Description |
|---|---|
| `Guid RequestId` | Unique request tracking ID |
| `int OwnerAppInstanceId` | Requesting node's app instance ID |
| `long TkbType` | TKB entity type code |
| `ulong DisType` | Packed DIS entity type discriminator |
| `string? InitialAttributesJson` | JSON attribute overrides |
| `List<object>? InitialComponents` | Pre-converted ECS components from wire descriptors |
| `long PreAllocatedNetworkId` | When non-zero, bypasses ID allocator |
| `IReadOnlyDictionary<int,(long,IReadOnlyList<object>)>? ChildComponentOverrides` | Per-child network ID and component overrides |

#### `ScenarioEntityCreationRequestSource`
| Member | Description |
|---|---|
| `ctor(int maxRequestsPerTick = 500)` | Configures drain cap |
| `void Enqueue(EntityCreationRequest)` | Thread-safe enqueue from orchestration thread |
| `bool IsEmpty` | Returns true when no requests pending |
| `void ProcessRequests(Action<EntityCreationRequest>)` | ECS-thread drain (capped at `maxRequestsPerTick`) |

#### `SequentialIdAllocator`
`long AllocateId()` - thread-safe `Interlocked.Increment`; `void Reset(long startId = 0)`.

### Component Registries

#### `HrotSharedComponentRegistry.RegisterAll(EntityRepository world)`
Registers: network replication components, geographic components, hierarchy components, shared managed definitions, lifecycle events, combat Health, application-layer events.

#### `RouteComponentRegistry.RegisterAll(EntityRepository world)`
Registers: `RoutePlan`, `PersonalRouteRef`, `RouteTrajectoryCache`, `CmdAppendPersonalWaypoint`.

#### `MissionComponentRegistry.RegisterAll(EntityRepository world)`
Registers: `ActiveMissionPlan`, `MissionControlIntent`.

#### `PresentationComponentRegistry.RegisterAll(EntityRepository world)`
Registers: `EntityInfo`, `SelectionState`, `EditablePolyline`, `MapOverlayStyle`, `IgHealthState`, `TogglePerspectiveEvent`, `WorldResetEvent`, `OpenRenameDialogCommand`, `SelectEntityCommand`.

#### `ZoneComponentRegistry.RegisterAll(EntityRepository world)`
Registers: `ZoneMembership`, `SpawnZoneObstacleCommand`, `UpdateZoneConfigCommand`.

### ECS Components (Selected)

#### `RoutePlan` (managed, ComponentId 168)
| Member | Description |
|---|---|
| `IReadOnlyList<RouteWaypoint> Waypoints` | Read-only waypoint view |
| `bool IsLoop` | Loop-back flag |
| `int Version` | Monotonic version stamp |
| `void Mutate(Action<List<RouteWaypoint>>)` | Increment-safe mutation callback |

#### `CullingState` (struct, NoSave)
`bool IsVisible`, `byte LodLevel` (0=Full, 1=Simplified, 2=IconOnly)

#### `SelectionState` (struct, NoSave)
`bool IsSelected`, `bool IsPrimarySelection`

#### `ResolvedStyle` (unsafe struct, NoSave)
Fixed-buffer texture name (16 B) + label text (32 B), `Color32 Tint`, `ForceId Affiliation`, `float DamageLevel`, `bool ShowTrail`, `bool ShowSensors`.
Total size under 64 bytes. Factory: `ResolvedStyle.CreateDefault()`.

#### `MapOverlayStyle` (struct)
`Color32 FillColor`, `Color32 BorderColor`, channel accessors, `Default` factory.

### Scenario Types

#### `HrotSerializerOptions.HrotJsonOptions`
Pre-built `JsonSerializerOptions`: camelCase naming, case-insensitive, indented, null-omitting.
Built on `FdpJsonOptionsRegistry.Indented`.

#### `HrotEnvironment`
| Method | Description |
|---|---|
| `TkbDatabase CreateTkb()` | Registers all entity types into a new TKB; applies route plan extensions |
| `WGS84Transform CreateGeoTransform()` | Creates transform with Berlin WGS84 origin (52.52 N, 13.405 E) |
| `DdsParticipant CreateParticipant(int domainId)` | Creates CycloneDDS participant for the given domain |

#### `HrotScenarioLoader`
`ctor(IScenarioStorageProvider, string targetSubsystemType)` then `string? TryLoadScenarioJson(string scenarioId)`.

### TKB Definitions

#### `TkbEntityTypes` constants
| Range | Group |
|---|---|
| 100-103 | Ground platforms (M1 Abrams, Bradley, HMMWV, T-72) |
| 200-201 | Infantry (Rifleman, Officer) |
| 301-303 | Composite units (TankPlatoon, InfantrySquad, TankPlatoon_Auto) |
| 501-505 | Civilian / insurgent (Pedestrian, Car, MilitaryApc, InfantrySoldier, Insurgent) |
| 8801-8803 | Tactical graphics (FireLine, Route, Area) |

#### `NedTkbBuilder` fluent API
`DefineVehicle(id, name)` -> `WithVisual(id, cfg)` -> `WithPhysics(id, cfg)` -> `WithCombat(id, cfg)` -> `WithFaction(id, n)` -> `WithBehavior(id)` -> `WithDisType(id, disType)`

#### `BehaviorCatalog.GetValidBehaviors(long tkbType)`
Returns `IReadOnlyList<string>` of behavior name strings valid for the given entity type.
Built at type init from `[BehaviorContract]` reflection.

### Diagnostics

#### `LogArchiveExtractionService`
`ctor(string logDirectory, string subsystemName, int nodeId)`
`Task<int> ExtractLogsAsync(string targetFilePath, int severityThreshold, float maxAgeHours, CancellationToken ct)`

---

## Dependencies

### Project References

| Project | Purpose |
|---|---|
| `FDP/Engine/Fdp.Core/Fdp.Core.csproj` | ECS world (`EntityRepository`), event bus, component attributes, serialisation utilities, geographic module, spawning abstractions |
| `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj` | TKB toolkit, behavior toolkit, replication toolkit, physics, navigation, combat, perception, DER, orchestration, diagnostics gizmos, CarKinem trajectory |

### NuGet Packages

| Package | Version | Purpose |
|---|---|---|
| `CycloneDDS.NET` | 0.2.2 | DDS participant used in `HrotNodeConfig`, `HrotNodeContext`, `HrotEnvironment`, and `DdsWriterAdapter`. Code generation disabled via `CycloneDdsDisableCodeGen=true`. |

### InternalsVisibleTo Assemblies

| Assembly | Reason |
|---|---|
| `Hrot.SimHost.Tests` | White-box unit testing of SimHost internals |
| `Hrot.Editor.Tests` | White-box unit testing of Editor internals |
| `Hrot.Network` | Network adapters need access to internal members |
| `Hrot.IG.Tests` | White-box unit testing of IG internals |
| `Hrot.Map.Common.Tests` | White-box unit testing of map-common internals |
| `Hrot.ClusterRunner.Integration.Tests` | Integration testing |
| `Hrot.Core.Tests` | Core unit tests |
| `Hrot.Common` | Sibling assembly sharing (cross-project internal access) |
| `Hrot.Network.NED` | NED network adapter internals |

---

## Usage Examples

### Example 1: Bootstrapping the ECS World for a SimHost Node

```csharp
// Composition root: called once at process startup before any module is registered.

var world = new EntityRepository();

// 1. Register all shared components in order (Shared must come first).
HrotSharedComponentRegistry.RegisterAll(world);
RouteComponentRegistry.RegisterAll(world);
MissionComponentRegistry.RegisterAll(world);
PresentationComponentRegistry.RegisterAll(world);
ZoneComponentRegistry.RegisterAll(world);

// 2. Create the TKB and node context.
var tkb = HrotEnvironment.CreateTkb();

var config = new HrotNodeConfig
{
    DomainId       = 0,
    NodeId         = 1,
    SubsystemName  = "SimHost",
    LocalTempRoot  = @"C:\FDP_Temp",
    Headless       = false,
};

// 3. Pass config to HrotNodeBuilder (in Hrot.SimHost / Hrot.Common).
// HrotNodeContext ctx = await new HrotNodeBuilder().BuildAsync(config);
// ctx.Kernel.AddModule(new CgfSubsystem(ctx, networkFactory, ...));
```

### Example 2: Creating a Scenario Entity from the Network

```csharp
// Wiring the entity creation pipeline inside a CGF subsystem.

// a. Create the thread-safe queue used by the NED adapter to push requests.
var scenarioSource = new ScenarioEntityCreationRequestSource(maxRequestsPerTick: 500);

// b. After scenario load, the Orchestration thread enqueues requests:
scenarioSource.Enqueue(new EntityCreationRequest
{
    RequestId            = Guid.NewGuid(),
    TkbType              = TkbEntityTypes.Tank_M1Abrams,
    InitialAttributesJson = "{\"name\":\"Alpha-1\",\"forceId\":1}",
    PreAllocatedNetworkId = 1001,
});

// c. During the ECS tick, CreateEntityRequestSystem calls:
scenarioSource.ProcessRequests(request =>
{
    // spawn entity from request.TkbType at request pre-allocated ID, etc.
    Console.WriteLine($"Spawning {request.TkbType} id={request.PreAllocatedNetworkId}");
});

// d. After spawning, send the ACK back to the requester.
IEntityAckSink ackSink = new NullEntityAckSink(); // or live NED writer
ackSink.WriteAck(
    Guid.Empty,
    entityId: 1001,
    EntityOperationStatus.Success);
```

### Example 3: Resolving a Mission Plan through the Event Bus

```csharp
// How an ExCon sends a mission command and a Brain node processes it.

// ExCon side (using ICommandGateway):
ICommandGateway gateway = networkFactory.CreateCommandGateway();

var result = await gateway.SendMissionControlRequestAsync(new MissionControlCommand
{
    EntityId    = 42,
    CommandType = eMissionCommandType.CMD_REPLACE_MISSION,
    Plan = new MissionPlan
    {
        Tasks =
        [
            new MissionTask
            {
                TaskId          = Guid.NewGuid(),
                ExecutingEngine = "BTree",
                BehaviorId      = "MoveToLocation",
                BehaviorParams  = "{\"targetLat\":52.5,\"targetLon\":13.4,\"speed\":15.0,\"arrivalRadius\":10.0}",
                Triggers        = [new MissionTrigger { Type = "BehaviorFinished" }],
            },
        ],
    },
    TaskId      = Guid.NewGuid(),
    BaseVersion = 0,
});

if (!result.Success)
    Console.Error.WriteLine($"Mission commit failed: {result.ErrorMessage}");
```

### Example 4: Loading Zones and Spawning Obstacles from a Scenario File

```csharp
// Deserialise a HROT scenario file and load its zones into the ECS world.

string scenarioJson = File.ReadAllText("my_scenario.json");

var envelope = JsonSerializer.Deserialize<HrotScenarioEnvelopeDto>(
    scenarioJson,
    HrotSerializerOptions.HrotJsonOptions)!;

Console.WriteLine($"Subsystem: {envelope.Header?.SubsystemType}");
Console.WriteLine($"Schema:    {envelope.Header?.SchemaVersion}");

if (envelope.Zones != null)
{
    IZoneManagerService zoneSvc = new ZoneManagerService();
    zoneSvc.LoadZones(world, envelope.Zones);

    // Verify the zones are accessible later:
    var activeZones = zoneSvc.GetActiveZones();
    foreach (var (name, def) in activeZones)
        Console.WriteLine($"Zone '{name}': {def.Obstacles?.Count ?? 0} obstacles");
}
```

### Example 5: Querying Valid Behaviors for an Entity Type

```csharp
// The scenario editor populates behavior drop-downs using BehaviorCatalog.

long tkbType = TkbEntityTypes.Tank_M1Abrams;
IReadOnlyList<string> validBehaviors = BehaviorCatalog.GetValidBehaviors(tkbType);

// Renders as: ["MoveToLocation", "FollowRoute", "JoinFormation", "Idle",
//              "FireAtTarget", "PlatoonHillAttack", "ConvoyEscort",
//              "WanderMilitary", "Ambush", ...]

foreach (string name in validBehaviors)
    Console.WriteLine(name);

// Verify behavior name in a unit test without hardcoding:
string moveToName = BehaviorTestHelper.GetBehaviorName<MoveToLocationParamsJsonDto>();
// moveToName == "MoveToLocation"
```

### Example 6: Dead Reckoning for Ghost Entities

```csharp
// Register DeadReckoningSyncSystem for a combined MuscleGround+IG node.
// Pass driveFromNetwork: false so only Ghost-lifecycle entities are smoothed.

var drSystem = new DeadReckoningSyncSystem(driveFromNetwork: false);
kernel.AddSystem(drSystem);

// For a pure IG node that owns no local entities, use the default ctor:
var drSystemIG = new DeadReckoningSyncSystem(); // driveFromNetwork: true
```

---

## Best Practices

### Do not instantiate concrete DDS writers in Hrot.Core consumers
Always obtain DDS writer access through the `INetworkFactory` / `IDdsWriter<T>` abstractions.
This keeps the domain layer decoupled from any specific DDS transport implementation
and allows unit tests to inject null or stub implementations.

### Register components before creating entities
`HrotSharedComponentRegistry.RegisterAll` must be called before any `EntityRepository`
operation that accesses those component types.  The correct call order is:
Shared -> Route -> Mission -> Presentation -> Zone.

### Use `Mutate()` for all RoutePlan modifications
Never modify `RoutePlan._waypoints` directly through casts or reflection.
The `Mutate(Action<List<RouteWaypoint>>)` pattern guarantees `Version` is incremented,
allowing reactive systems (`RouteTrajectorySyncSystem`) to detect changes without polling.

### Prefer `NullEntityAckSink` over null checks in headless mode
Pass `new NullEntityAckSink()` to systems that require `IEntityAckSink` when running
without a live DDS participant.  This eliminates null-guard branches from the
`CreateEntityRequestSystem` and keeps the ECS pipeline uniform between online and
offline deployments.

### Keep behavior DTO classes lightweight
Behavior parameter DTOs (`MoveToLocationParamsJsonDto`, etc.) are serialised and
deserialised frequently during scenario load.  Avoid adding non-primitive reference
fields or constructor logic.  Use `[JsonPropertyName]` only where the wire name
must differ from the C# property name; otherwise rely on `HrotSerializerOptions`
camelCase policy.

### Do not add new component IDs to HrotComponentIds without allocating from the documented block
IDs 160-186 are currently used.  Document each new ID with a `<summary>` comment
following the existing block-comment style.  Never re-use a retired ID; add a comment
explaining why the old value was retired.

### Use `BehaviorTestHelper.GetBehaviorName<TDto>()` in tests
This avoids string literals for behavior IDs in test code.  If a behavior is renamed
or its `BehaviorContractAttribute` is updated, the test will fail to compile (breaking
change) rather than silently comparing wrong strings at runtime.

### `SequentialIdAllocator` is for offline only
When building a non-headless test that exercises the full ECS spawn path, check
whether an `INetworkIdAllocator` is already available from `HrotNodeContext.IdAllocator`.
`SequentialIdAllocator` guarantees IDs never collide with real DDS-allocated IDs because
the latter use a different allocation domain.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Hrot/Engine/Hrot.Common` | Sister shared library; `Hrot.Core` exposes `internal` members to it via `InternalsVisibleTo`. Contains additional subsystem-shared helpers. |
| `Hrot/Network/Fdp.Network.Cyclone` | Provides the CycloneDDS NED protocol adapter implementing `INetworkFactory` (`NedNetworkFactory`) |
| `Hrot/Subsystems/Hrot.CGF` | Brain/CGF subsystem; consumes `ICgfEntityLifecycleAdapters`, `IEntityCreationRequestSource`, `ICommandGateway`, `MissionPlan` |
| `Hrot/Subsystems/Hrot.SimHost` | SimHost root; consumes `HrotNodeConfig`, `HrotNodeContext`, `INetworkFactory`, `DeadReckoningSyncSystem`, `ZoneManagerService` |
| `Hrot/Editor` | Scenario editor; uses `BehaviorCatalog`, `TkbEntityTypes`, behavior DTOs, `HrotScenarioEnvelopeDto`, `MapViewConfig` |
| `Hrot/IG` | Image generator; uses `IIgNetworkAdapter`, `IIgTranslators`, `CullingState`, `SelectionState`, `ResolvedStyle`, `MapOverlayStyle` |
| `Hrot/Network/Hrot.Network.NED` | NED network adapter; has `InternalsVisibleTo` access to `Hrot.Core` internals |
| `FDP/Engine/Fdp.Core` | Foundation ECS framework; `Hrot.Core` is built directly on top of it |
| `FDP/Toolkits/Fdp.Toolkits` | FDP toolkit aggregate; provides behavior, replication, combat, physics, navigation, perception toolkits |
