# Hrot.Network.BDC

**Project path**: `Hrot/Network/Hrot.Network.BDC/Hrot.Network.BDC.csproj`
**Date**: 2026-05-23
**Target framework**: net8.0
**Assembly**: `Hrot.Network.BDC`

---

## README Validation

**Status: Missing**

No `README.md` exists inside the project folder. All documentation is provided by this
architectural document and by the inline XML and block comments within the source files.

---

## Executive Overview

### What BDC Stands For

**BDC** stands for **Battlefield Data Channel**. It is a lightweight, DDS-based network
protocol for the HROT military/combat simulation system. Its primary purpose is to
replicate entity state (position, orientation, velocity, lifecycle) between simulation
nodes using a small, well-defined set of CycloneDDS topics.

### Purpose

BDC serves as a protocol-neutral alternative to the heavier NED (Network Entity
Distribution) protocol. Where NED carries the full HROT simulation data model (perception,
pathfinding, orchestration, time control, map commands, etc.), BDC carries only the minimal
subset required for basic entity presence and spatial state synchronisation. This makes it
suitable for:

- Scenarios with heterogeneous or external simulators that understand only position and
  entity lifecycle.
- Lightweight test environments, demonstrations, or federation gateways where the full
  NED protocol is not required.
- Image-generator (IG) nodes that need to track entity positions but do not participate
  in simulation logic.

BDC is implemented as a concrete realisation of `INetworkFactory` and
`IReplicationModule`, the same protocol-neutral contracts used by NED. Callers (e.g.
`NodeBootstrapper`, `ClusterRunner`) select BDC or NED at configuration time and receive
the same interface — the protocol detail is invisible to higher-level code.

### BDC vs NED

| Capability                        | BDC       | NED       |
|-----------------------------------|-----------|-----------|
| Entity lifecycle (spawn/destroy)  | Yes       | Yes       |
| World position replication        | Yes       | Yes       |
| Mission control commands          | Defined*  | Yes       |
| Pathfinding translators           | Stub      | Yes       |
| Perception translators            | Stub      | Yes       |
| Time control gateway              | Stub      | Yes       |
| IG translators                    | Stub      | Yes       |
| ExCon ingress handlers            | Stub      | Yes       |
| CGF entity lifecycle adapters     | None      | Yes       |
| ID allocator server               | Sequential| DDS-based |

(*) The `BdcMissionControlRequest`/`BdcMissionControlAck` DDS topic definitions exist
in the message layer but the `ICommandGateway` implementation is a null stub. The topic
infrastructure is defined for future use or for external consumers that understand BDC.

---

## Architecture

### Design Principles

1. **Minimal surface** -- Only two DDS topics carry live simulation data per entity:
   `BDC_EntityMaster` (lifecycle) and `BDC_WorldPos` (spatial state). All other
   `INetworkFactory` capabilities are satisfied by null/no-op stubs.

2. **Topic name isolation** -- Every BDC topic is prefixed with `BDC_` to avoid DDS
   partition collisions with NED topics (`EntityMaster`, `WorldPos`, etc.) on a shared
   DDS domain.

3. **Factory pattern** -- `BdcNetworkFactory` is the single entry point. It owns the
   `DdsParticipant` reference and constructs all BDC subsystems on demand via
   `INetworkFactory` interface methods.

4. **Headless support** -- When `DdsParticipant` is null (unit tests, offline runs),
   no DDS writers/readers are created. The `BdcReplicationModule` still registers all
   ECS systems so the module host sees a consistent system graph.

5. **Ordinal namespace** -- `BdcDescriptorType` assigns ordinals starting at 1000
   (`EntityMaster=1000`, `WorldPos=1002`) to avoid collisions with NED ordinals.

6. **DriveFromNetwork** -- The replication module sets `DriveFromNetwork=true` for any
   node whose role does not include `Brain` or `MuscleGround` (i.e. IG, Perception,
   NavigationSolver nodes). Simulation-capable nodes always drive their own entities.

### DDS Topics

| Topic name                   | Type                     | Reliability   | Durability     | History          | Key        |
|------------------------------|--------------------------|---------------|----------------|------------------|------------|
| `BDC_EntityMaster`           | `BdcEntityMaster`        | Reliable      | TransientLocal | KeepLast(1)      | EntityId   |
| `BDC_WorldPos`               | `BdcWorldPos`            | BestEffort    | TransientLocal | KeepLast(1)      | EntityId   |
| `BDC_MissionControlRequest`  | `BdcMissionControlRequest`| Reliable     | Volatile       | KeepAll          | (none)     |
| `BDC_MissionControlAck`      | `BdcMissionControlAck`   | Reliable      | Volatile       | KeepAll          | (none)     |

**Rationale:**
- `BDC_EntityMaster` uses reliable delivery and TransientLocal so that late-joining
  nodes receive the current set of live entities immediately.
- `BDC_WorldPos` uses best-effort delivery because stale position samples are discarded
  anyway; throughput matters more than guaranteed delivery for continuous spatial updates.
- Mission topics use Volatile because they are fire-and-forget commands; historical
  commands must not replay on a node that joins late.

---

## ASCII Block Diagrams

### Diagram 1 -- Layer Stack

```
+------------------------------------------------------------+
|                 Application Layer                          |
|   NodeBootstrapper / ClusterRunner / SimHost / ExCon       |
+-----------------------------+------------------------------+
                              |
                              | INetworkFactory
                              v
+------------------------------------------------------------+
|                  Hrot.Network.BDC                          |
|                                                            |
|  +---------------------+   +---------------------------+  |
|  |  BdcNetworkFactory  |   |   BdcReplicationModule    |  |
|  |  (INetworkFactory)  |-->|   (IReplicationModule)    |  |
|  +---------------------+   +---------------------------+  |
|                                    |                       |
|              +-----------+---------+                       |
|              |                     |                       |
|  +-----------v--------+  +---------v-----------+          |
|  |BdcEntityMaster     |  |  BdcWorldPos        |          |
|  |Translator          |  |  Translator         |          |
|  |(IDescriptor        |  |  (IDescriptor       |          |
|  | Translator)        |  |   Translator)       |          |
|  +----+----------+----+  +---+----------+------+          |
|       |          |           |          |                  |
+------------------------------------------------------------+
        |          |           |          |
        v          ^           v          ^
   DdsWriter   DdsReader  DdsWriter   DdsReader
  [EntityMstr] [EntityMstr][WorldPos] [WorldPos]
        |                     |
        +----------+----------+
                   |
           CycloneDDS.NET
           (DDS Domain)
```

### Diagram 2 -- Entity Lifecycle Flow (Egress from authoritative node)

```
  Authoritative Node (Brain/MuscleGround)
  +------------------------------------------+
  |  ECS World                               |
  |  +----------+   NetworkIdentity          |
  |  |  Entity  |   TkbIdentity              |
  |  +----+-----+   SimTransform             |
  |       |                                  |
  |       | CycloneEgressSystem              |
  |       v                                  |
  |  BdcEntityMasterTranslator.ScanAndPublish|
  |       |  (first time seen)               |
  |       v                                  |
  |  DdsWriter<BdcEntityMaster>.Write()      |
  |       |                                  |
  |  BdcWorldPosTranslator.ScanAndPublish    |
  |       |  (every tick, authority check)   |
  |       v                                  |
  |  DdsWriter<BdcWorldPos>.Write()          |
  +-----|----|---------+--------------------+
        |    |
        |    | DDS Domain (BDC_EntityMaster, BDC_WorldPos)
        |    |
  +-----|----|-----------------------------------------+
  | Remote Node (IG / Perception / other)              |
  |                                                    |
  |  DdsReader<BdcEntityMaster>.Take()                 |
  |    -> GhostCreationSystem.CreateGhost()            |
  |    -> entity added to NetworkEntityMap             |
  |                                                    |
  |  DdsReader<BdcWorldPos>.Take()                     |
  |    -> cmd.SetComponent<SimTransform>(entity, ...)  |
  |    (loopback guard: skip locally-owned entities)   |
  +----------------------------------------------------+
```

### Diagram 3 -- Factory Dependency Graph

```
  BdcNetworkFactory
  +------------------------------------------------+
  |  ctor parameters:                              |
  |    DdsParticipant?         _participant        |
  |    NetworkEntityMap        _entityMap          |
  |    IGeographicTransform    _geoTransform       |
  |    FdpEventBus             _eventBus           |
  |    long                    _localNodeId        |
  |    NodeRole                _role               |
  +----+---------+---------+------------------------+
       |         |         |
       v         v         v
  BdcReplication  Null    Null
  Module          Gateways Senders
       |
       +----------+----------+
       |                     |
       v                     v
  BdcEntityMaster     BdcWorldPos
  Translator          Translator
       |                     |
       v                     v
  DdsWriter/Reader    DdsWriter/Reader
  <BdcEntityMaster>   <BdcWorldPos>
       |                     |
       v                     v
  FdpLog                FdpLog
  GhostCreationSystem   IGeographicTransform
  NetworkEntityMap      NetworkEntityMap
  FdpEventBus           IEntityCommandBuffer
```

### Diagram 4 -- DriveFromNetwork Decision

```
  NodeRole flags
  +---------------------------+
  |  Brain           bit 0    |
  |  MuscleGround    bit 1    |
  |  ImageGenerator  bit 2    |
  |  Perception      bit 3    |
  |  NavigationSolver bit 4   |
  +---------------------------+
           |
           v
  roleHasMuscle = role.HasFlag(MuscleGround)
  roleHasBrain  = role.HasFlag(Brain)
           |
           v
  DriveFromNetwork = !(roleHasMuscle || roleHasBrain)
           |
  +--------+--------+
  |                 |
  v                 v
DriveFromNetwork    DriveFromNetwork
  = false             = true
(Brain, Muscle,     (IG, Perception,
 combined nodes)    solver-only nodes)
  |                   |
  v                   v
Node owns its       Node consumes
own entities;       network-authoritative
no loopback         entity positions
```

---

## Source Structure

### Directory Layout

```
Hrot/Network/Hrot.Network.BDC/
|-- Hrot.Network.BDC.csproj
|-- BdcCommon.cs                      (namespace Hrot.BDC.Common)
|-- BdcDescriptorType.cs              (namespace Hrot.BDC)
|-- BdcEntityMessages.cs              (namespace Hrot.BDC.Messages)
|-- BdcMissionMessages.cs             (namespace Hrot.BDC.Messages)
|-- Factory/
|   +-- BdcNetworkFactory.cs          (namespace Hrot.BDC.Factory)
+-- Replication/
    |-- BdcReplicationModule.cs       (namespace Hrot.BDC.Replication)
    |-- BdcEntityMasterTranslator.cs  (namespace Hrot.BDC.Replication)
    +-- BdcWorldPosTranslator.cs      (namespace Hrot.BDC.Replication)
```

### Namespace Map

| Namespace              | File(s)                                                  |
|------------------------|----------------------------------------------------------|
| `Hrot.BDC.Common`      | `BdcCommon.cs`                                           |
| `Hrot.BDC`             | `BdcDescriptorType.cs`                                   |
| `Hrot.BDC.Messages`    | `BdcEntityMessages.cs`, `BdcMissionMessages.cs`          |
| `Hrot.BDC.Factory`     | `Factory/BdcNetworkFactory.cs`                           |
| `Hrot.BDC.Replication` | `Replication/BdcReplicationModule.cs`, `BdcEntityMasterTranslator.cs`, `BdcWorldPosTranslator.cs` |

### File-by-File Summary

#### `BdcCommon.cs`

Defines the shared primitive structs used across all BDC message types. Decorated with
`[DdsStruct]` and `[DdsIdlFile("bdc-common")]` so the CycloneDDS.NET code generator
emits matching IDL definitions.

| Type              | Kind   | Description                                              |
|-------------------|--------|----------------------------------------------------------|
| `BdcNodeId`       | struct | Unique BDC node identifier: `AppDomainId`, `AppInstanceId` |
| `BdcGeoPoint`     | struct | Geodetic position: `Latitude`, `Longitude`, `Altitude`   |
| `BdcEulerOri`     | struct | Orientation angles in degrees: `Heading`, `Pitch`, `Roll`|
| `BdcAngularVector`| struct | Velocity vector: `Azimuth`, `Elevation`, `Length`        |

#### `BdcDescriptorType.cs`

An enum that defines the ordinal identifiers for BDC descriptor/translator types. Ordinals
start at 1000 to avoid conflicts with NED ordinals.

| Member          | Value | Meaning                                     |
|-----------------|-------|---------------------------------------------|
| `EntityMaster`  | 1000  | Maps to `BdcEntityMasterTranslator`         |
| `WorldPos`      | 1002  | Maps to `BdcWorldPosTranslator`             |

#### `BdcEntityMessages.cs`

Defines the two core DDS topics for entity state synchronisation. Both structs are
`[DdsIdlFile("bdc-entity-msgs")]`.

| Type              | DDS Topic          | Description                                 |
|-------------------|--------------------|---------------------------------------------|
| `BdcEntityMaster` | `BDC_EntityMaster` | Entity lifecycle record (spawn/destroy)     |
| `BdcWorldPos`     | `BDC_WorldPos`     | Merged position + orientation + velocity    |

#### `BdcMissionMessages.cs`

Defines mission control command topics for sending orders from ExCon/Editor to CGF nodes.
Both structs are `[DdsManaged]` and `[DdsIdlFile("bdc-mission-msgs")]`.

| Type                        | DDS Topic                      | Description                            |
|-----------------------------|--------------------------------|----------------------------------------|
| `BdcMissionCommandType`     | (enum)                         | Command variants: ReplaceMission, AbortAll, JumpToTask |
| `BdcMissionControlRequest`  | `BDC_MissionControlRequest`    | ExCon -> CGF mission command           |
| `BdcMissionControlAck`      | `BDC_MissionControlAck`        | CGF -> ExCon acknowledgment            |

#### `Factory/BdcNetworkFactory.cs`

The public entry point of the assembly. Implements `INetworkFactory` (from `Hrot.Core`)
and `IGizmoNetworkFactory` (inherited). Contains the concrete factory plus seven internal
null-implementation stubs for capabilities BDC does not support.

| Type                                   | Kind     | Access   | Description                                    |
|----------------------------------------|----------|----------|------------------------------------------------|
| `BdcNetworkFactory`                    | class    | public   | Main factory; implements `INetworkFactory`     |
| `BdcNullCommandGateway`                | class    | internal | No-op `ICommandGateway`                        |
| `BdcNullExConEgressWriters`            | class    | internal | No-op `IExConEgressWriters`                    |
| `BdcNullTimeControlGateway`            | class    | internal | No-op `ITimeControlGateway`                    |
| `BdcNullSimHostMissionSender`          | class    | internal | No-op `ISimHostMissionSender`                  |
| `BdcNullSimHostAuxiliaryTranslators`   | class    | internal | No-op `ISimHostAuxiliaryTranslators`           |
| `BdcNullSimHostPathfindingTranslators` | class    | internal | No-op `ISimHostPathfindingTranslators`         |
| `BdcNullSimHostPerceptionTranslators`  | class    | internal | No-op `ISimHostPerceptionTranslators`          |

#### `Replication/BdcReplicationModule.cs`

Implements `IReplicationModule` (from `Hrot.Common.Abstractions`). The module configures
and registers the ECS systems that handle DDS ingress and egress during simulation ticks.

| Type                   | Kind  | Access | Description                                               |
|------------------------|-------|--------|-----------------------------------------------------------|
| `BdcReplicationModule` | class | public | Implements `IReplicationModule`; registers translator systems |

#### `Replication/BdcEntityMasterTranslator.cs`

Handles entity lifecycle over DDS. Internal to the assembly.

| Type                        | Kind  | Access   | Description                                         |
|-----------------------------|-------|----------|-----------------------------------------------------|
| `BdcEntityMasterTranslator` | class | internal | `IDescriptorTranslator` for `BDC_EntityMaster` topic |

#### `Replication/BdcWorldPosTranslator.cs`

Handles spatial state replication over DDS. Internal to the assembly.

| Type                    | Kind  | Access   | Description                                          |
|-------------------------|-------|----------|------------------------------------------------------|
| `BdcWorldPosTranslator` | class | internal | `IDescriptorTranslator` for `BDC_WorldPos` topic     |

---

## Public API Reference

### `BdcNodeId` (struct, `Hrot.BDC.Common`)

```
[DdsStruct] [DdsIdlFile("bdc-common")]
public partial struct BdcNodeId
```

| Member          | Type | Description                            |
|-----------------|------|----------------------------------------|
| `AppDomainId`   | int  | DDS domain identifier of the node      |
| `AppInstanceId` | int  | Per-domain instance counter of the node|

---

### `BdcGeoPoint` (struct, `Hrot.BDC.Common`)

```
[DdsStruct] [DdsIdlFile("bdc-common")]
public partial struct BdcGeoPoint
```

| Member      | Type   | Unit    | Description          |
|-------------|--------|---------|----------------------|
| `Latitude`  | double | degrees | North-positive       |
| `Longitude` | double | degrees | East-positive        |
| `Altitude`  | double | metres  | Height above WGS-84  |

---

### `BdcEulerOri` (struct, `Hrot.BDC.Common`)

```
[DdsStruct] [DdsIdlFile("bdc-common")]
public partial struct BdcEulerOri
```

| Member    | Type  | Unit    | Description                          |
|-----------|-------|---------|--------------------------------------|
| `Heading` | float | degrees | Compass bearing (0=North, 90=East)   |
| `Pitch`   | float | degrees | Nose-up positive                     |
| `Roll`    | float | degrees | Right-wing-down positive             |

---

### `BdcAngularVector` (struct, `Hrot.BDC.Common`)

```
[DdsStruct] [DdsIdlFile("bdc-common")]
public partial struct BdcAngularVector
```

| Member      | Type  | Unit  | Description                                 |
|-------------|-------|-------|---------------------------------------------|
| `Azimuth`   | float | deg   | Horizontal bearing of velocity vector       |
| `Elevation` | float | deg   | Vertical angle of velocity vector           |
| `Length`    | float | m/s   | Speed (magnitude of velocity)               |

---

### `BdcDescriptorType` (enum, `Hrot.BDC`)

```
public enum BdcDescriptorType
```

| Member          | Value | Notes                                          |
|-----------------|-------|------------------------------------------------|
| `EntityMaster`  | 1000  | Ordinal base offset avoids NED collisions      |
| `WorldPos`      | 1002  | Gap at 1001 reserved for future use            |

---

### `BdcEntityMaster` (struct, `Hrot.BDC.Messages`)

```
[DdsTopic("BDC_EntityMaster")]
[DdsIdlFile("bdc-entity-msgs")]
[DdsQos(Reliability=Reliable, Durability=TransientLocal, HistoryKind=KeepLast, HistoryDepth=1)]
public partial struct BdcEntityMaster
```

| Member     | Type  | DdsKey | Description                                   |
|------------|-------|--------|-----------------------------------------------|
| `EntityId` | int   | Yes    | Network entity ID; 0 = invalid                |
| `TkbType`  | long  | No     | TKB (entity type catalogue) index             |
| `Diskind`  | byte  | No     | SISO DIS entity kind (1=Platform, 2=Munition) |

---

### `BdcWorldPos` (struct, `Hrot.BDC.Messages`)

```
[DdsTopic("BDC_WorldPos")]
[DdsIdlFile("bdc-entity-msgs")]
[DdsQos(Reliability=BestEffort, Durability=TransientLocal, HistoryKind=KeepLast, HistoryDepth=1)]
public partial struct BdcWorldPos
```

| Member     | Type            | DdsKey | Description                                |
|------------|-----------------|--------|--------------------------------------------|
| `EntityId` | int             | Yes    | Network entity ID matching BdcEntityMaster |
| `Time`     | DateTime        | No     | UTC timestamp of the sample                |
| `Pos`      | BdcGeoPoint     | No     | Geodetic position                          |
| `Ori`      | BdcEulerOri     | No     | Euler orientation                          |
| `Vel`      | BdcAngularVector| No     | Velocity (currently published as zeros)    |

---

### `BdcMissionCommandType` (enum, `Hrot.BDC.Messages`)

```
public enum BdcMissionCommandType : int
```

| Member           | Value | Description                                          |
|------------------|-------|------------------------------------------------------|
| `ReplaceMission` | 0     | Replace the entity's current mission entirely        |
| `AbortAll`       | 1     | Abort all in-progress tasks immediately              |
| `JumpToTask`     | 2     | Skip to a specific task by index/identifier          |

---

### `BdcMissionControlRequest` (struct, `Hrot.BDC.Messages`)

```
[DdsTopic("BDC_MissionControlRequest")]
[DdsIdlFile("bdc-mission-msgs")]
[DdsQos(Reliability=Reliable, Durability=Volatile, HistoryKind=KeepAll)]
[DdsManaged]
public partial struct BdcMissionControlRequest
```

| Member           | Type                   | Description                                           |
|------------------|------------------------|-------------------------------------------------------|
| `RequestId`      | Guid                   | Correlation token; matched by the ACK                 |
| `TargetEntityId` | long                   | Network entity ID of the target CGF entity            |
| `CommandType`    | BdcMissionCommandType  | Type of mission command                               |
| `PayloadJson`    | string                 | JSON parameters; empty string for parameterless commands |

---

### `BdcMissionControlAck` (struct, `Hrot.BDC.Messages`)

```
[DdsTopic("BDC_MissionControlAck")]
[DdsIdlFile("bdc-mission-msgs")]
[DdsQos(Reliability=Reliable, Durability=Volatile, HistoryKind=KeepAll)]
[DdsManaged]
public partial struct BdcMissionControlAck
```

| Member         | Type    | Description                                             |
|----------------|---------|---------------------------------------------------------|
| `RequestId`    | Guid    | Echoes the `RequestId` from the originating request     |
| `ErrorCode`    | int     | 0 = success; non-zero = protocol-specific error code    |
| `ErrorMessage` | string? | Human-readable error description; null on success       |

---

### `BdcNetworkFactory` (class, `Hrot.BDC.Factory`)

```
public sealed class BdcNetworkFactory : INetworkFactory
```

#### Constructor

```csharp
public BdcNetworkFactory(
    DdsParticipant?      participant,
    NetworkEntityMap     entityMap,
    IGeographicTransform geoTransform,
    FdpEventBus          eventBus,
    long                 localNodeId,
    NodeRole             role)
```

All parameters are stored and forwarded when factory methods are called.
Pass `null` for `participant` for headless/unit-test operation.

#### Properties

| Property                     | Type              | Description                                      |
|------------------------------|-------------------|--------------------------------------------------|
| `Participant`                | DdsParticipant?   | The DDS participant or null in headless mode     |
| `WorldPosDescriptorId`       | long              | Returns 0 (BDC does not expose this to callers)  |
| `NavigationStatusDescriptorId` | long            | Returns 0                                        |

#### Key Methods

| Method                                       | Return Type                    | Notes                                              |
|----------------------------------------------|--------------------------------|----------------------------------------------------|
| `CreateReplicationModule()`                  | `IReplicationModule`           | Returns a `BdcReplicationModule`                   |
| `CreateCommandGateway()`                     | `ICommandGateway`              | Returns `BdcNullCommandGateway`                    |
| `CreateExConEgressWriters()`                 | `IExConEgressWriters`          | Returns `BdcNullExConEgressWriters`                |
| `CreateTimeControlGateway()`                 | `ITimeControlGateway`          | Returns `BdcNullTimeControlGateway`                |
| `CreateSimHostMissionSender()`               | `ISimHostMissionSender`        | Returns `BdcNullSimHostMissionSender`              |
| `CreateSimHostAuxiliaryTranslators()`        | `ISimHostAuxiliaryTranslators` | Returns `BdcNullSimHostAuxiliaryTranslators`       |
| `CreateSimHostPathfindingTranslators(...)`   | `ISimHostPathfindingTranslators`| Returns null stub                                 |
| `CreateSimHostPerceptionTranslators(...)`    | `ISimHostPerceptionTranslators`| Returns null stub                                  |
| `CreateSimHostAttributeUpdateSystems()`      | `IReadOnlyList<IEcsModuleSystem>`| Returns empty array                              |
| `CreateIgTranslators()`                      | `IIgTranslators`               | Returns `NullIgTranslators`                        |
| `CreateIgNetworkAdapter(...)`                | `IIgNetworkAdapter`            | Returns `NullIgNetworkAdapter.Instance`            |
| `CreateIgEgressTranslators(...)`             | `IReadOnlyList<IDescriptorTranslator>` | Returns empty array                      |
| `CreateExConIngressHandlers(...)`            | `IEnumerable<IIngressHandler>` | Yields nothing (`yield break`)                     |
| `ConfigureForNode(HrotNodeContext, ...)`      | `INetworkFactory`              | Creates a new factory from `HrotNodeContext`       |
| `ConfigureForNode(DdsParticipant?, int, NodeRole)` | `INetworkFactory`        | Creates a new factory with given participant/role  |
| `CreateCgfEntityLifecycleAdapters()`         | `ICgfEntityLifecycleAdapters?` | Returns null (BDC does not support CGF creation)   |
| `CreateIdAllocatorServer()`                  | `IDisposable`                  | Returns `NullDisposable`                           |
| `CreateIdAllocator(...)`                     | `INetworkIdAllocator`          | Returns `SequentialIdAllocator`                    |
| `CreateMasterTimeTranslators(...)`           | `IMasterTimeTranslators`       | Returns null stub                                  |
| `CreateSlaveOrchestratorTranslators(...)`    | `ISlaveOrchestrationTranslator`| Returns null stub                                  |
| `CreateOrchestrationObserver(...)`           | `IOrchestrationObserver`       | Returns null stub                                  |
| `CreateOrchestratorTranslators(...)`         | `IOrchestrationTranslator`     | Returns `NullOrchestrationTranslator`              |
| `CreateGizmoTranslators(...)`                | `IReadOnlyList<INetworkTranslator>` | Returns empty array                          |
| `CreateGizmoPublisherSystem(...)`            | `IEcsModuleSystem?`            | Returns null                                       |

---

### `BdcReplicationModule` (class, `Hrot.BDC.Replication`)

```
public sealed class BdcReplicationModule : IReplicationModule
```

#### Properties

| Property                  | Type                          | Description                                         |
|---------------------------|-------------------------------|-----------------------------------------------------|
| `Name`                    | string                        | `"BdcReplication"`                                  |
| `Policy`                  | ExecutionPolicy               | `ExecutionPolicy.Synchronous()`                     |
| `GhostCreationSystem`     | GhostCreationSystem           | Shared ghost factory used by entity master translator |
| `DriveFromNetwork`        | bool                          | True when role lacks Brain and MuscleGround flags    |
| `NetworkLifecycleGroup`   | NetworkLifecycleSystemGroup   | Gates ghost promotions during replay playback        |

#### Constructor

```csharp
public BdcReplicationModule(
    DdsParticipant?      participant,
    NodeRole             role,
    NetworkEntityMap     entityMap,
    IGeographicTransform geoTransform,
    FdpEventBus          eventBus,
    long                 localNodeId)
```

Throws `ArgumentNullException` if `entityMap`, `geoTransform`, or `eventBus` are null.

#### `RegisterSystems(ISystemRegistry registry)`

Registers the following ECS systems in order:

1. `GhostCreationSystem`
2. (if participant != null) `CycloneNetworkIngressSystem` with both translators
3. (if participant != null) `CycloneEgressSystem` with both translators
4. (if participant != null) `CycloneNetworkCleanupSystem` with both translators
5. `SmartEgressSystem`
6. `DeadReckoningSyncSystem(_driveFromNetwork)`

#### `Tick(ISimulationView, float)`

No-op. BDC replication is entirely driven by the ECS system pipeline.

---

## Dependencies

### Project References

| Project                          | Path                                               | Purpose                                              |
|----------------------------------|----------------------------------------------------|------------------------------------------------------|
| `Hrot.Core`                      | `Hrot/Engine/Hrot.Core/`                           | `INetworkFactory`, `IReplicationModule`, `NodeRole`, `NetworkEntityMap` |
| `Fdp.Core`                       | `FDP/Engine/Fdp.Core/`                             | `FdpEventBus`, `Entity`, `ISimulationView`, ECS primitives |
| `Fdp.Toolkits`                   | `FDP/Toolkits/Fdp.Toolkits/`                       | `IGeographicTransform`, replication systems, spawning toolkit |
| `Fdp.Network.Cyclone`            | `FDP/Network/Fdp.Network.Cyclone/`                 | `CycloneNetworkIngressSystem`, `CycloneEgressSystem`, `CycloneNetworkCleanupSystem` |

### NuGet Packages

| Package           | Version | Purpose                                                  |
|-------------------|---------|----------------------------------------------------------|
| `CycloneDDS.NET`  | 0.2.2   | DDS runtime: `DdsParticipant`, `DdsWriter<T>`, `DdsReader<T>`, `[DdsTopic]`, `[DdsQos]`, `[DdsStruct]` |

### InternalsVisibleTo

| Assembly                  | Usage                                               |
|---------------------------|-----------------------------------------------------|
| `Hrot.Network.BDC.Tests`  | Unit tests access internal translators and stubs    |

### Compiler Flags

| Flag                | Value  | Reason                                                 |
|---------------------|--------|--------------------------------------------------------|
| `Nullable`          | enable | All nullable warnings treated as errors                |
| `TreatWarningsAsErrors` | true | Zero-warning policy                               |
| `AllowUnsafeBlocks` | true   | Required by CycloneDDS.NET for native pointer access   |

---

## Usage Examples

### Example 1: Creating a BDC Factory in Headless (Unit Test) Mode

```csharp
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Services;
using Hrot.BDC.Factory;
using Hrot.Common;
using NSubstitute;

// Create dependencies
var entityMap    = new NetworkEntityMap();
var geoTransform = Substitute.For<IGeographicTransform>();
var eventBus     = new FdpEventBus();

// Null participant = no DDS; safe for unit tests
var factory = new BdcNetworkFactory(
    participant:  null,
    entityMap:    entityMap,
    geoTransform: geoTransform,
    eventBus:     eventBus,
    localNodeId:  42,
    role:         NodeRole.Brain | NodeRole.MuscleGround);

// The replication module is always created even without a participant
var replication = factory.CreateReplicationModule();
Console.WriteLine(replication.Name);           // "BdcReplication"
Console.WriteLine(replication.DriveFromNetwork); // false (Brain+Muscle role)
```

---

### Example 2: Creating a BDC Factory from a HrotNodeContext (Production)

```csharp
using CycloneDDS.Runtime;
using Hrot.BDC.Factory;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Core.Network;

// nodeContext is produced by HrotNodeBuilder and carries the DDS participant,
// entity map, geo transform, event bus, and node ID for this process.
HrotNodeContext nodeContext = BuildNodeContext();

INetworkFactory baseFactory = new BdcNetworkFactory(
    participant:  null,      // placeholder; ConfigureForNode overwrites it
    entityMap:    nodeContext.EntityMap,
    geoTransform: nodeContext.GeoTransform,
    eventBus:     nodeContext.EventBus,
    localNodeId:  0,
    role:         NodeRole.None);

// ConfigureForNode produces a properly wired factory for the given context
INetworkFactory factory = baseFactory.ConfigureForNode(
    context: nodeContext,
    role:    NodeRole.Brain);

// Now use the factory to construct simulation subsystems
var replicationModule = factory.CreateReplicationModule();
var commandGateway    = factory.CreateCommandGateway();
```

---

### Example 3: Registering BDC Replication Systems in the Module Host

```csharp
using CycloneDDS.Runtime;
using Fdp.ModuleHost;
using Hrot.BDC.Replication;
using Hrot.Common;

// BdcReplicationModule is created by BdcNetworkFactory.CreateReplicationModule()
// but can also be instantiated directly when wiring is explicit.
var replicationModule = new BdcReplicationModule(
    participant:  participant,    // live DDS participant
    role:         NodeRole.ImageGenerator,
    entityMap:    entityMap,
    geoTransform: geoTransform,
    eventBus:     eventBus,
    localNodeId:  7);

// Register all BDC ECS systems onto the module host kernel
var kernel = new ModuleHostKernel();
replicationModule.RegisterSystems(kernel.SystemRegistry);

// After this call the kernel will run on every tick:
//   GhostCreationSystem
//   CycloneNetworkIngressSystem  (polls BDC_EntityMaster, BDC_WorldPos)
//   CycloneEgressSystem          (publishes BDC_EntityMaster, BDC_WorldPos)
//   CycloneNetworkCleanupSystem  (disposes DDS instances for destroyed entities)
//   SmartEgressSystem
//   DeadReckoningSyncSystem(driveFromNetwork=true)
```

---

### Example 4: Sending a BDC Mission Control Request (External Consumer)

```csharp
using CycloneDDS.Runtime;
using CycloneDDS.Schema;
using Hrot.BDC.Messages;

// External system or test tool that speaks BDC natively.
// The BdcNetworkFactory.CreateCommandGateway() returns a null stub,
// so external consumers write the DDS topic directly.

using var participant = new DdsParticipant(domainId: 0);
using var writer = new DdsWriter<BdcMissionControlRequest>(
    participant, "BDC_MissionControlRequest");

var request = new BdcMissionControlRequest
{
    RequestId      = Guid.NewGuid(),
    TargetEntityId = 101,
    CommandType    = BdcMissionCommandType.ReplaceMission,
    PayloadJson    = "{\"MissionId\":\"patrol-alpha\"}",
};
writer.Write(request);

// The CGF node listening on BDC_MissionControlRequest will process this
// and reply on BDC_MissionControlAck with the same RequestId.
```

---

### Example 5: Reading BDC Entity State from an External Observer

```csharp
using CycloneDDS.Runtime;
using Hrot.BDC.Messages;

// External tool (e.g. scenario recorder, test harness) that tracks
// entity positions without being a full HROT simulation node.

using var participant = new DdsParticipant(domainId: 0);
using var masterReader = new DdsReader<BdcEntityMaster>(participant);
using var posReader    = new DdsReader<BdcWorldPos>(participant);

while (running)
{
    using (var loan = masterReader.Take())
    {
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            Console.WriteLine(
                $"Entity {sample.Data.EntityId} TkbType={sample.Data.TkbType}");
        }
    }

    using (var loan = posReader.Take())
    {
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            var p = sample.Data;
            Console.WriteLine(
                $"Entity {p.EntityId}: lat={p.Pos.Latitude:F6} "  +
                $"lon={p.Pos.Longitude:F6} hdg={p.Ori.Heading:F1}");
        }
    }

    await Task.Delay(50);
}
```

---

## Best Practices

### Use the Null Participant for Tests

Always pass `null` for `DdsParticipant` in unit tests. The factory and replication module
handle the null case gracefully: no DDS types are instantiated, no network I/O occurs, and
all ECS systems still register correctly, giving full code coverage of the system graph
without a live DDS daemon.

### Do Not Share DdsParticipant Across Factories

`BdcNetworkFactory` stores the participant reference but does not own it. Pass the same
participant to all factories and translators within one simulation node, but never share a
participant across OS processes.

### Select the Correct NodeRole

`DriveFromNetwork` is computed from `NodeRole` at factory construction time. Use
`Brain | MuscleGround` for a combined single-process simulation node. Use `ImageGenerator`
alone for rendering nodes. Mixing roles incorrectly can cause entities to receive
loopback position updates from their own writers.

### Do Not Use BDC for Full-Featured Deployments

BDC intentionally leaves most `INetworkFactory` methods as no-op stubs. If a deployment
requires mission control, pathfinding distribution, perception, orchestration, or time
control, use `Hrot.Network.NED` (NedNetworkFactory) instead.

### Topic Name Uniqueness

BDC and NED can coexist on the same DDS domain because all BDC topics use the `BDC_`
prefix. However, mixing them on the same simulation node is not supported — each node
uses exactly one `INetworkFactory` implementation.

### Ordinal Offset

Always use the `BdcDescriptorType` enum values when packing/comparing descriptor ordinals.
The 1000-base offset is intentional and must not be changed without a corresponding change
in all ordinal comparison logic in `Fdp.Toolkit.Replication`.

### Velocity Field

The velocity (`Vel`) field in `BdcWorldPos` is currently published as all zeros
(`Azimuth=0, Elevation=0, Length=0`). Consumers must not rely on velocity data from BDC
at this time. Dead-reckoning (`DeadReckoningSyncSystem`) uses position history instead.

### Geographic Coordinate Conversion

`BdcWorldPosTranslator` converts between simulation Cartesian coordinates and WGS-84
geodetic coordinates using `IGeographicTransform`. Always supply a properly configured
transform that matches the simulation's coordinate origin. Using the wrong transform
will silently misplace all entities.

---

## Related Projects

| Project                        | Relationship                                                            |
|--------------------------------|-------------------------------------------------------------------------|
| `Hrot.Network.NED`             | The full-featured counterpart. Implements the same `INetworkFactory` interface with a complete NED protocol stack. |
| `Hrot.Network.BDC.Tests`       | Unit tests for this project. Uses `InternalsVisibleTo` to test internal translators directly. |
| `Hrot.Network.Orchestration`   | Provides orchestration interfaces (`IOrchestrationTranslator`, etc.) that BDC returns null stubs for. |
| `Hrot.Core`                    | Defines `INetworkFactory`, `IReplicationModule`, `NodeRole`, `ICommandGateway`, and all related contracts. |
| `Hrot.Common`                  | Provides `NodeRole` enum, `NetworkEntityMap`, `IGeographicTransform`, `GhostCreationSystem`, and other shared types. |
| `Fdp.Network.Cyclone`          | The DDS ECS systems (`CycloneNetworkIngressSystem`, `CycloneEgressSystem`, `CycloneNetworkCleanupSystem`) that BDC uses for tick-level DDS I/O. |
| `Fdp.Core`                     | FDP engine: `FdpEventBus`, ECS world, `ISimulationView`, component types. |
| `Fdp.Toolkits`                 | Replication toolkit: `SmartEgressSystem`, `DeadReckoningSyncSystem`, `NetworkEntityMap`, `OwnershipExtensions`. |
| `Hrot.Network.NED.Tests`       | Sibling test project; tests for NED are architecturally analogous to BDC tests. |
