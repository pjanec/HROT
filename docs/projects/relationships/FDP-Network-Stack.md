# FDP Network Stack - DDS Communication Architecture

**Date:** 2026-05-23
**Scope:** How `Fdp.Network.Cyclone`, `Fdp.Diagnostics.Network`, `Hrot.Network.NED`,
`Hrot.Network.BDC`, and `Hrot.Network.Orchestration` work together to provide
distributed communication across the HROT simulation cluster.

---

## Table of Contents

1. [DDS Communication Architecture Overview](#1-dds-communication-architecture-overview)
2. [Layer Stack](#2-layer-stack)
3. [The FDP CycloneDDS Adapter (Fdp.Network.Cyclone)](#3-the-fdp-cyclonedds-adapter-fdpnetworkcyclone)
4. [HROT Network Protocol Comparison: NED vs BDC](#4-hrot-network-protocol-comparison-ned-vs-bdc)
5. [NED Protocol Deep Dive](#5-ned-protocol-deep-dive)
6. [BDC Protocol](#6-bdc-protocol)
7. [Orchestration Protocol](#7-orchestration-protocol)
8. [Diagnostics Channel (Fdp.Diagnostics.Network)](#8-diagnostics-channel-fdpdiagnosticsnetwork)
9. [DDS QoS Reference Table](#9-dds-qos-reference-table)
10. [Data Flow Diagrams](#10-data-flow-diagrams)
11. [Integration Examples](#11-integration-examples)
12. [Performance and Reliability Notes](#12-performance-and-reliability-notes)
13. [Links to Individual Project Docs](#13-links-to-individual-project-docs)

---

## 1. DDS Communication Architecture Overview

### What is DDS?

Data Distribution Service (DDS) is an OMG-standardized pub/sub middleware
designed for real-time, data-centric distributed systems. It is well-suited to
simulation because:

- **Decoupled discovery** -- publishers and subscribers find each other
  automatically via the DDS discovery protocol; no broker or rendezvous server
  is required.
- **Data-centric pub/sub** -- the unit of exchange is a typed *topic* keyed on
  application-level identifiers (e.g., `EntityId`). Subscribers always receive
  the latest value for a key, not just raw messages.
- **QoS policies** -- each topic carries fine-grained Quality-of-Service
  settings: reliability (best-effort vs reliable), durability (volatile vs
  transient-local), history depth, etc. This lets high-frequency position
  updates share the same middleware as low-frequency, persistent configuration
  records without compromise.
- **Instance lifecycle** -- DDS tracks whether a keyed topic instance is
  *alive* or *disposed*. The simulation uses this directly for entity
  lifecycle: writing an `EntityMaster` key creates the entity; disposing it
  deletes it.

### The CycloneDDS .NET Binding

The project uses **CycloneDDS.NET** from NuGet. This binding provides:

- `DdsParticipant` -- the domain-level handle; one per process.
- `DdsWriter<T>` / `DdsReader<T>` -- typed writer and reader for a struct `T`
  annotated with `[DdsTopic]` and `[DdsQos]`.
- `[DdsStruct]`, `[DdsKey]`, `[DdsId]`, `[DdsManaged]`, `[DdsUnion]` etc. --
  schema attributes on C# structs that drive IDL code-generation and
  serialization.
- `DdsReader<T>.Take()` -- returns a zero-copy loan of received samples.
- `DdsWriter<T>.DisposeInstance(key)` -- signals lifecycle disposal for a key.

The binding maps C# structs to CDR-encoded wire types compatible with any
OMG-compliant DDS implementation (RTI Connext, OpenDDS, etc.).

### Network Layers at a Glance

```
+----------------------------------------------------------+
| Application Nodes (Hrot.Orchestrator, SimHost, IG, ExCon)|
+----------------------------------------------------------+
| Hrot.Network.Orchestration  (cluster 2PC protocol)       |
+----------------------------------------------------------+
| Hrot.Network.NED / Hrot.Network.BDC (entity protocols)   |
+----------------------------------------------------------+
| Fdp.Network.Cyclone  (FDP DDS adapter, ECS integration)  |
+----------------------------------------------------------+
| Fdp.Diagnostics.Network  (debug/gizmo DDS channel)       |
+----------------------------------------------------------+
| CycloneDDS.NET (NuGet)  (DDS middleware)                 |
+----------------------------------------------------------+
| Network Transport (UDP multicast / unicast)               |
+----------------------------------------------------------+
```

The layers are strict: upper layers depend on lower layers, never the reverse.
NED/BDC depend on `Fdp.Network.Cyclone` for the `CycloneTranslator` base class,
`DdsParticipant`, and `NetworkEntityMap`. Orchestration constructs its own
`DdsWriter<T>` / `DdsReader<T>` directly on the shared `DdsParticipant`.

---

## 2. Layer Stack

```
+===================================+
| Hrot.Network.NED                  | <- Full entity protocol (30+ topics)
| Hrot.Network.BDC                  | <- Lightweight entity protocol (2 topics)
| Hrot.Network.Orchestration        | <- Cluster lifecycle / 2PC
+===================================+
| Fdp.Network.Cyclone               | <- FDP ECS-DDS adapter layer
|   CycloneIngressSystem            |    (input phase: polls all translators)
|   CycloneEgressSystem             |    (export phase: publishes all translators)
|   CycloneTranslator<TDds,TView>   |    (generic translator base class)
|   DdsIdAllocator / Server         |    (distributed ID allocation over DDS)
|   NetworkEntityMap                |    (network-ID -> ECS entity registry)
+===================================+
| Fdp.Diagnostics.Network           | <- Debug primitive / gizmo DDS channel
|   IDdsWriter<T> / IDdsReader<T>   |    (testable thin abstractions)
|   DdsWriterGizmoAdapter<T>        |    (wraps DdsWriter<T>)
|   DdsReaderGizmoAdapter<T>        |    (wraps DdsReader<T>)
+===================================+
| CycloneDDS.NET (NuGet)            | <- DDS middleware binding
|   DdsParticipant                  |
|   DdsWriter<T> / DdsReader<T>     |
|   CDR serialization               |
+===================================+
```

---

## 3. The FDP CycloneDDS Adapter (Fdp.Network.Cyclone)

### 3.1 Purpose

`Fdp.Network.Cyclone` is the bridge between the ECS simulation world
(`Fdp.Core.Entity`, `ISimulationView`, `IEntityCommandBuffer`) and the DDS
wire world. It provides:

1. A **translator abstraction** (`CycloneBaseTranslator`, `CycloneTranslator<TDds,TView>`)
   for mapping ECS components to/from DDS topic structs.
2. **ECS systems** (`CycloneIngressSystem`, `CycloneEgressSystem`) that integrate
   with the FDP module scheduling loop.
3. **Distributed ID allocation** (`DdsIdAllocator`, `DdsIdAllocatorServer`)
   so all nodes agree on globally unique entity IDs without a central DB.
4. **Entity map** (`NetworkEntityMap`) to resolve DDS-wire `EntityId` values
   to local `Entity` handles.
5. A **module entry point** (`CycloneNetworkModule`) that wires all of the above
   into an `IEcsModule`.

### 3.2 IDdsWriter<T> / IDdsReader<T> Pattern

Defined in `Fdp.Diagnostics.Network` (namespace
`Fdp.Toolkit.Diagnostics.Gizmos.Network`), these two interfaces provide the
**testability boundary** for all DDS I/O:

```csharp
public interface IDdsWriter<T>
{
    void Write(T sample);
}

public interface IDdsReader<T>
{
    bool TryRead(out T sample);
}
```

Production code receives these through DI. Unit tests inject capturing stubs.
`DdsWriterGizmoAdapter<T>` and `DdsReaderGizmoAdapter<T>` are the production
implementations that delegate to the real `CycloneDDS.Runtime.DdsWriter<T>`
and `DdsReader<T>` respectively.

### 3.3 The Translator Abstraction

The translator hierarchy is:

```
INetworkTranslator  (interface: TopicName, PollIngress, ScanAndPublish)
  |
  +-- CycloneBaseTranslator  (abstract, TopicName, counters, Direction)
        |
        +-- CycloneTranslator<TDds, TView>  (generic, owns Reader/Writer)
              |
              +-- CycloneNativeEventTranslator  (unsafe / blittable topics)
              +-- CycloneManagedEventTranslator  (managed / heap-allocating)
              +-- MultiInstanceCycloneTranslator  (multi-key-per-entity topics)
```

`CycloneTranslator<TDds, TView>` holds a `DdsReader<TDds>` and a
`DdsWriter<TDds>` directly. Each translator owns its reader/writer pair. The
`CycloneIngressSystem` calls `PollIngress` on every registered translator once
per input phase; `CycloneEgressSystem` calls `ScanAndPublish` once per export
phase.

**Hot-path design decisions:**

- `TDds : unmanaged` enforces blittable struct layouts, enabling
  stack-allocation (`TDds sample = default;`) with zero GC pressure.
- `DisposeInstance` patches the key fields using `UnsafeLayout<TDds>` unsafe
  pointer arithmetic rather than reflection, making instance disposal O(1) and
  allocation-free.
- `using var loan = Reader.Take()` uses CycloneDDS zero-copy loans so incoming
  samples are never copied to the managed heap unless the application explicitly
  accesses them.

### 3.4 Entity State Replication Model (SST)

The FDP adapter uses a **Split-State Table (SST)** model at the wire level:

| DDS Topic            | QoS                           | Purpose                              |
|----------------------|-------------------------------|--------------------------------------|
| `SST_EntityMaster`   | Reliable / TransientLocal / KeepLast-100 | Entity existence, type, owner |
| `SST_EntityState`    | BestEffort / Volatile / KeepLast-1 | High-freq position/velocity   |
| `SST_OwnershipUpdate`| Reliable / Volatile / KeepAll-1 | Descriptor ownership transfer |
| `WeaponStateTopic`   | (per subclass)                | Weapon turret state                  |

The `EntityMaster` topic carries `TkbTypeValue` (blueprint index) so receiving
nodes can look up the template and apply it to the ghost entity during
promotion without any additional round-trip.

`NetworkAppId` (`AppDomainId + AppInstanceId`) uniquely identifies a
participating application instance across the DDS domain.

`NetworkLifecycleState` enumerates the four-phase entity lifecycle:
`Ghost -> Constructing -> Active -> TearDown`. The ghost phase exists
specifically to allow a receiving node to set up ECS components before the
entity is considered active.

### 3.5 Distributed ID Allocation over DDS

All network entity IDs must be globally unique across the entire cluster.
`DdsIdAllocator` implements a chunked allocation protocol over three DDS topics:

| DDS Topic        | QoS                               | Role                                    |
|------------------|-----------------------------------|-----------------------------------------|
| `IdAlloc_Request` | Reliable / Volatile / KeepAll    | Client requests a chunk of N IDs        |
| `IdAlloc_Response`| Reliable / Volatile / KeepAll    | Server responds with `[start, count)`   |
| `IdAlloc_Status`  | Reliable / TransientLocal / KeepLast-1 | Server publishes highest allocated ID |

The client defers its first request until the server's DDS reader is matched
(via `DdsWriter<T>.PublicationMatched`). This prevents the write-before-match
problem where the very first request is silently dropped.

```
Client                                    Server
  |                                          |
  |-- IdAlloc_Request(ClientId, Req_Alloc) ->|
  |<- IdAlloc_Response(ClientId, [100,200)) -|
  |<- IdAlloc_Status(HighestId=200) ---------|
  |                                          |
  |  [local pool: 100 IDs available]         |
  |  [refill when pool < LOW_WATER_MARK=10]  |
```

`DdsIdAllocatorHelper` (in `Hrot.Network.Orchestration`) extends this with a
30-second startup guard: it blocks until `HasPublicationMatch` is true, then
logs a warning at 5 seconds if the orchestrator has not yet appeared.

### 3.6 ECS Integration: CycloneNetworkModule

`CycloneNetworkModule` is the `IEcsModule` entry point. It:

1. Holds the shared `DdsParticipant` and `NodeIdMapper`.
2. Registers serialization providers for `NetworkTransform`, `NetworkVelocity`,
   `NetworkIdentity`, and `TkbIdentity` (component IDs 1001-1004).
3. Accepts custom translators injected by higher-level modules (NED, BDC).
4. Creates `CycloneIngressSystem` (scheduled in `SystemPhase.Input`) and
   `CycloneEgressSystem` (scheduled in `SystemPhase.Export`).
5. Creates `NetworkGatewaySystem` for handling ownership arbitration.

---

## 4. HROT Network Protocol Comparison: NED vs BDC

### 4.1 Feature Comparison Table

| Feature                   | NED (Hrot.Network.NED)                        | BDC (Hrot.Network.BDC)                |
|---------------------------|-----------------------------------------------|---------------------------------------|
| Topic count               | 30+ topics across 8 IDL files                 | 4 topics in 1 IDL file                |
| Entity lifecycle          | `EntityMaster` (full SST)                     | `BDC_EntityMaster` (minimal)          |
| Position/orientation      | `WorldPos` (pos + ori + vel + acc + rotvel)   | `BDC_WorldPos` (pos + ori + vel)      |
| Mission control           | `MissionControlRequest` + `MissionControlAck` | `BDC_MissionControlRequest` + `BDC_MissionControlAck` |
| Descriptor ownership      | Per-descriptor granular ownership (SST rules) | No ownership model                    |
| CQRS navigation           | `NavigationIntent` (Brain) / `NavigationStatus` (Muscle) | Not supported             |
| Damage model              | `EntityDamage`, `EntityHitDamage`             | Not supported                         |
| Weapon interactions       | `WeaponFireRequest`, `WeaponFire`, `MunitionDetonation` | Not supported             |
| Map overlays              | `MapVisualOverlay`, `MapEntitySymbol`, `MapRoute` | Not supported                     |
| Tactical intent           | `TacticalIntentRequest`                       | Not supported                         |
| Entity attributes (ATTR2) | `UpdateEntityAttributeRequest` + typed union  | `PayloadJson` string                  |
| DIS type encoding         | `DisTypeStruct` (7 named fields)              | `Diskind` (byte)                      |
| Sensor / raycast pipeline | `SensorConfig`, `RaycastRequestBatch`, etc.   | Not supported                         |
| EQS area-query pipeline   | `AreaQueryRequestBatch/Response`              | Not supported                         |
| Topic prefix              | No prefix (e.g., `EntityMaster`)              | `BDC_` prefix (e.g., `BDC_EntityMaster`) |
| Primary use case          | Full simulation cluster                       | Lightweight nodes, tooling, testing   |
| Assembly                  | `Hrot.Network.NED.csproj`                     | `Hrot.Network.BDC.csproj`             |

### 4.2 When to Use NED vs BDC

**Use NED when:**
- The node participates as Brain, Muscle, or Image Generator in a full
  simulation cluster.
- The node needs CQRS mission control (`MissionControlRequest`/`Ack`).
- The node needs sensor, weapon, or map overlay data.
- The node needs per-descriptor ownership with `OwnershipUpdate`.
- Interoperability with all other HROT nodes is required.

**Use BDC when:**
- Building a minimal test harness that only needs entity positions.
- Writing a standalone tool that monitors entities without participating in
  full simulation semantics.
- Prototyping a new node type before committing to the full NED schema.
- A lightweight ExCon variant that only sends mission commands without caring
  about SST ownership.

### 4.3 Protocol Selection in ClusterRunner

`NedReplicationModule` performs role-based translator registration at startup:

- `NodeRole.MuscleGround` -- registers shared + kinematic packs; creates
  `GhostCreationSystem` and `SmartEgressSystem`.
- `NodeRole.ImageGenerator` -- registers shared pack + `EntityStatesIngressPack`;
  creates `GhostCreationSystem` and `DeadReckoningSyncSystem` (with
  `driveFromNetwork=true`).
- `NodeRole.Brain` -- registers shared + cognitive packs; creates
  `GhostCreationSystem` and `SmartEgressSystem` for cognitive descriptors.

---

## 5. NED Protocol Deep Dive

### 5.1 IDL Files and Namespace Overview

NED descriptors are split across six IDL files (each maps to one C# file):

| IDL File                | C# File               | Contents                                       |
|-------------------------|-----------------------|------------------------------------------------|
| `hrot-common`           | `Common.cs`           | `NodeId`, `GeoPoint`, `EulerOri`, `AngularVector`, `EulerRate` |
| `hrot-generic-desc`     | `GenericDescriptors.cs` | `EntityMaster`, `EntityInfo`, `DisTypeStruct` |
| `hrot-generic-msgs`     | `GenericMessages.cs`  | `OwnershipUpdate`, `AttributeValueUnion`, `Vec3f/d/4f` |
| `hrot-sim-desc`         | `SimDescriptors.cs`   | `WorldPos`, `EntityDamage`, `NavigationIntent`, `NavigationStatus` |
| `hrot-missions-desc`    | `MissionDescriptors.cs` | `EntityMission`, `MissionPlan`, `MissionTask` |
| `hrot-missions-msgs`    | `MissionMessages.cs`  | `MissionControlRequest`, `MissionControlAck`, `MissionCommandUnion` |
| `hrot-sim-msgs`         | `FireInteractionMessages.cs` | `WeaponFireRequest`, `WeaponFire`, `MunitionDetonation`, `EntityHitDamage` |
| `hrot-tactical-intent`  | `TacticalIntentMessages.cs` | `TacticalIntentRequest`                 |
| `hrot-map-desc`         | `MapDescriptors.cs`   | `MapEntitySymbol`, `MapVisualOverlay`, `MapRoute`, `Waypoint` |
| `hrot-map-msgs`         | `MapMessages.cs`      | Map command messages                           |

### 5.2 All NED DDS Topics with QoS

#### Entity Lifecycle Topics

| Topic Name              | QoS                                     | Direction              | Descriptor ID |
|-------------------------|-----------------------------------------|------------------------|---------------|
| `EntityMaster`          | Reliable / TransientLocal / KeepLast-1  | Any node (owner writes)| 0 (dtEntityMaster) |
| `EntityInfo`            | Reliable / TransientLocal / KeepLast-1  | Owner                  | 1 (dtEntityInfo) |

#### Spatial / Simulation Topics

| Topic Name              | QoS                                     | Direction                         | Descriptor ID |
|-------------------------|-----------------------------------------|-----------------------------------|---------------|
| `WorldPos`              | BestEffort / TransientLocal / KeepLast-1 | Muscle (kinematic owner)          | 2 (dtWorldPos) |
| `EntityDamage`          | Reliable / TransientLocal / KeepLast-1  | Muscle (damage owner)             | 30 (dtEntityDamage) |
| `NavigationIntent`      | Reliable / TransientLocal / KeepLast-1  | Brain (command)                   | 52 (dtNavigationIntent) |
| `NavigationStatus`      | Reliable / TransientLocal / KeepLast-1  | Muscle (status)                   | 53 (dtNavigationStatus) |

#### Sensor / Raycast Pipeline

| Topic Name              | QoS                                     | Direction              | Descriptor ID |
|-------------------------|-----------------------------------------|------------------------|---------------|
| `SensorConfig`          | Reliable / TransientLocal / KeepLast-1  | Brain                  | 60 |
| `RaycastRequestBatch`   | Reliable / Volatile / KeepAll           | Brain -> Muscle        | 61 |
| `SensorTrackState`      | Reliable / TransientLocal / KeepLast-1  | Muscle                 | 62 |
| `RaycastResponseBatch`  | Reliable / Volatile / KeepAll           | Muscle -> Brain        | 63 |
| `PathRequestBatch`      | Reliable / Volatile / KeepAll           | Brain -> Muscle        | 64 |
| `PathResponseBatch`     | Reliable / Volatile / KeepAll           | Muscle -> Brain        | 65 |
| `GroundClampingOverride`| Reliable / TransientLocal / KeepLast-1  | Brain                  | 66 |

#### Weapon / Fire Interaction Pipeline

| Topic Name              | QoS                                     | Direction              | Descriptor ID |
|-------------------------|-----------------------------------------|------------------------|---------------|
| `WeaponFireRequest`     | (inferred Reliable)                     | Brain -> Muscle        | 80 |
| `WeaponFire`            | (inferred Reliable)                     | Muscle -> IG           | 81 |
| `MunitionDetonation`    | (inferred Reliable)                     | Muscle -> IG / DAM     | 82 |
| `EntityHitDamage`       | (inferred Reliable)                     | DAM -> Muscle          | 83 |
| `AudioTargetDetected`   | (inferred Reliable)                     | Muscle -> IG           | 84 |

#### Mission Control Topics

| Topic Name              | QoS                                     | Direction              | Descriptor ID |
|-------------------------|-----------------------------------------|------------------------|---------------|
| `EntityMission`         | Reliable / TransientLocal / KeepLast-1  | Brain (mission owner)  | 51 |
| `MissionControlRequest` | Reliable / Volatile / KeepAll           | ExCon -> CGF           | 90 |
| `MissionControlAck`     | Reliable / Volatile / KeepAll           | CGF -> ExCon           | 91 |

#### Tactical / EQS Topics

| Topic Name              | QoS                                     | Direction              | Descriptor ID |
|-------------------------|-----------------------------------------|------------------------|---------------|
| `TacticalIntentRequest` | (inferred Reliable)                     | Commander Brain -> Subordinate Brain | 92 |
| `AreaQueryRequestBatch` | Reliable / Volatile / KeepAll           | Brain -> Muscle        | 93 |
| `AreaQueryResponseBatch`| Reliable / Volatile / KeepAll           | Muscle -> Brain        | 94 |

#### Ownership / Routing Messages

| Topic Name              | QoS                                     | Direction              | Descriptor ID |
|-------------------------|-----------------------------------------|------------------------|---------------|
| `OwnershipUpdate`       | Reliable / Volatile / KeepLast-1        | Releasing owner -> New owner | 55 |
| `DeferredTakeOwnership` | (inferred Reliable)                     | Brain -> Muscle (pre-genesis routing) | 54 |

#### Map / Visual Topics

| Topic Name              | QoS                                     | Direction              | Descriptor ID |
|-------------------------|-----------------------------------------|------------------------|---------------|
| `MapEntitySymbol`       | Reliable / TransientLocal / KeepLast-1  | ExCon                  | 40 |
| `MapVisualOverlay`      | Reliable / TransientLocal / KeepLast-1  | ExCon                  | 3 (dtMapVisualOverlay) |
| `MapRoute`              | Reliable / TransientLocal / KeepLast-1  | ExCon                  | 4 (dtMapRoute) |

### 5.3 Entity SST (Split-State Table) Model

The NED SST model is the fundamental principle governing entity state:

```
+----------------------------------------------------------+
|  Entity (identified by EntityId)                         |
|  +----------------------------------------------------+  |
|  | EntityMaster  (ALIVE = entity exists)              |  |
|  | EntityInfo    (TKB type, flags, affiliation)       |  |
|  | WorldPos      (position, orientation, velocity)    |  |
|  | EntityDamage  (total damage 0..100)                |  |
|  | EntityMission (current mission plan)               |  |
|  | NavigationIntent (Brain-owned nav command)         |  |
|  | NavigationStatus (Muscle-owned nav feedback)       |  |
|  | ... (more descriptors per EDescriptorType)         |  |
|  +----------------------------------------------------+  |
+----------------------------------------------------------+
```

Rules:
1. An entity exists if and only if its `EntityMaster` instance is ALIVE in DDS.
2. Disposing `EntityMaster` for an `EntityId` removes the entity cluster-wide.
3. Each descriptor is independently owned. Ownership = last successful writer.
4. To change a descriptor owned by another node, send an
   `UpdateEntityDescriptorRequest` via the command gateway.
5. `EntityMaster` does NOT carry an `OwnerId` field. Ownership is determined
   from DDS sample metadata (the participant GUID of the last writer).

### 5.4 CQRS Ownership: Brain vs Muscle

Navigation uses a CQRS pattern where the command (intent) and the status
(acknowledgment) flow on separate topics with different owners:

```
   Brain Node                           Muscle Node
       |                                    |
       |-- NavigationIntent (cmd) --------->|
       |   EntityId, IntentId, Mode,        |
       |   FinalDestination, TargetSpeed    |
       |                                    |
       |<-- NavigationStatus (status) ------|
       |    EntityId, IntentId (echoed),    |
       |    Result, CurrentPos, DistLeft    |
```

- `NavigationIntent` is **Brain-owned** (Brain writes, Muscle reads).
- `NavigationStatus` is **Muscle-owned** (Muscle writes, Brain reads).
- `IntentId` is a monotonically increasing counter echoed by status for
  correlation, preventing stale-response confusion.

The same CQRS pattern applies to mission control:
- `MissionControlRequest` flows from ExCon to CGF (Volatile, KeepAll).
- `MissionControlAck` flows from CGF to ExCon with the new version number.
- Version field enables optimistic locking: `BaseVersion = 0` skips the check.

### 5.5 The Command Gateway Pattern (NedCommandGateway)

`NedCommandGateway` wraps two `DdsCommandClient<TReq, TAck>` instances plus
two fire-and-forget `DdsWriter<T>` instances:

```csharp
// Async request-response (correlated by Guid RequestId):
Task<CreateUpdateDeleteEntityAck> CreateEntityAsync(CreateEntityRequest req, int timeoutMs);
Task<MissionControlAck>           SendMissionControlRequestAsync(MissionControlRequest req, int timeoutMs);

// Fire-and-forget (no ack expected):
void SendUpdateDescriptor(UpdateEntityDescriptorRequest req);
```

`DdsCommandClient<TReq, TAck>` internally creates a writer for the request
topic and a reader for the ack topic, correlates them by `RequestId` (a
`Guid`), and awaits the matching ack within `timeoutMs`. This provides
request/response semantics on top of raw pub/sub DDS.

---

## 6. BDC Protocol

### 6.1 Overview

BDC (Bare-bones DDS Communication) is the minimal viable entity protocol.
It has exactly four topics and no SST complexity.

### 6.2 BDC Topics

| Topic Name                 | QoS                                     | Direction       |
|----------------------------|-----------------------------------------|-----------------|
| `BDC_EntityMaster`         | Reliable / TransientLocal / KeepLast-1  | Publisher       |
| `BDC_WorldPos`             | BestEffort / TransientLocal / KeepLast-1| Kinematic owner |
| `BDC_MissionControlRequest`| Reliable / Volatile / KeepAll           | ExCon -> CGF    |
| `BDC_MissionControlAck`    | Reliable / Volatile / KeepAll           | CGF -> ExCon    |

All BDC topic names are prefixed with `BDC_` to avoid collisions with NED
topics on the same DDS domain. A node can run both NED and BDC translators
on the same `DdsParticipant` without interference.

### 6.3 BDC Topic Structs

`BdcEntityMaster` carries:
- `EntityId` (int, keyed)
- `TkbType` (long) -- TKB blueprint index
- `Diskind` (byte) -- DIS kind only (1=Platform, 2=Munition, etc.)

`BdcWorldPos` carries:
- `EntityId` (int, keyed)
- `Time` (DateTime)
- `Pos` (BdcGeoPoint: lat/lon/alt)
- `Ori` (BdcEulerOri: heading/pitch/roll)
- `Vel` (BdcAngularVector: azimuth/elevation/length)

`BdcMissionControlRequest` carries:
- `RequestId` (Guid, correlation)
- `TargetEntityId` (long)
- `CommandType` (enum: ReplaceMission / AbortAll / JumpToTask)
- `PayloadJson` (string, command-specific JSON)

`BdcMissionControlAck` carries:
- `RequestId` (Guid, echoed)
- `ErrorCode` (int, 0 = success)
- `ErrorMessage` (string?, null on success)

### 6.4 BDC vs NED Wire Compatibility

BDC and NED are **not wire-compatible**. They share the same DDS domain but
use different topic names, different struct layouts, and different namespaces.
A BDC subscriber will never receive NED `EntityMaster` samples and vice versa.
This is intentional: the `BDC_` prefix namespace separation allows mixed
deployments.

---

## 7. Orchestration Protocol

### 7.1 Purpose

`Hrot.Network.Orchestration` provides the cluster-wide lifecycle management
protocol. It implements a 2-Phase-Commit (2PC) pattern over DDS:

1. **ClusterMaster** (Orchestrator process) sends commands to all nodes.
2. Each **ClusterSlave** (SimHost, IG, ExCon) executes its phase and reports
   status back.
3. The ClusterMaster decides to commit or abort the operation.

### 7.2 Orchestration Topics

| Topic Name           | QoS                                  | Direction                 | Purpose                        |
|----------------------|--------------------------------------|---------------------------|--------------------------------|
| `ClusterState`       | Reliable / TransientLocal / KeepLast-1 | Orchestrator -> all      | Authoritative cluster state    |
| `OrchestratorContext`| Reliable / TransientLocal / KeepLast-1 | Orchestrator -> all      | Scenario/exercise context      |
| `AssetInventory`     | Reliable / TransientLocal / KeepLast-1 | Orchestrator -> all      | NAS/local asset lists (5s period) |
| `ClusterOpRequest`   | Reliable / Volatile                   | ExCon -> Orchestrator     | High-level cluster commands    |
| `SysOpStatus`        | Reliable / TransientLocal             | Orchestrator -> all       | Operation result feedback      |
| `NodeOpCommand`      | Reliable / Volatile / KeepAll         | Orchestrator -> per-node  | Per-node 2PC commands          |
| `NodeOpStatus`       | Reliable / Volatile / KeepAll         | Nodes -> Orchestrator     | Per-node 2PC status replies    |
| `NodeHeartbeat`      | BestEffort / TransientLocal / KeepLast-1 | Nodes -> all            | Liveness and health metrics    |

### 7.3 Cluster State Machine

```
         Idle
          |
          |-- LoadingEdit -----> OperatingEdit -----> UnloadingEdit --> Idle
          |-- LoadingPreview --> OperatingPreview --> UnloadingPreview -> Idle
          |-- LoadingLive -----> OperatingLive -----> UnloadingLive --> Idle
          |-- LoadingReplay ---> OperatingReplay ---> UnloadingReplay -> Idle
          |
          +-- Degraded (fault condition, requires manual recovery)
```

`ClusterStateTopic` carries: `CurrentState`, `ExerciseId` (Guid),
`StateStartWallTicks`, `TransactionEpoch`. The `TransactionEpoch` is
incremented on every committed transaction; nodes can detect missed
transactions by comparing epochs.

### 7.4 The Two-Phase Commit Flow

```
ExCon                 Orchestrator                  SimHost / IG
  |                        |                              |
  |-- ClusterOpRequest --> |                              |
  |   {TransitionState,    |                              |
  |    LoadLive payload}   |                              |
  |                        |-- NodeOpCommand(PrepareState) ->
  |                        |   {TargetNodeId, TransactionId} |
  |                        |                    <-- NodeOpStatus(PrepareState, OK) --
  |                        |-- NodeOpCommand(CommitState) -->
  |                        |                    <-- NodeOpStatus(CommitState, OK) --
  |                        |                              |
  |                        | [update ClusterState]        |
  |<-- SysOpStatus(OK) --- |                              |
  |                        |                              |
```

If any node returns a non-zero `StatusCode`, the Orchestrator sends
`NodeOpCommand(AbortTransaction)` to all participating nodes.

`NodeOpCommand.TargetNodeId` is the DDS key; each node applies a
client-side filter (`cmd.TargetNodeId == _nodeId`) so only the addressed
node processes the command.

### 7.5 ClusterOpType Enumeration

16 cluster-level operation types:

| Value | Name              | Description                              |
|-------|-------------------|------------------------------------------|
| 1     | TransitionState   | Move cluster to a new ClusterState       |
| 2     | SaveScenario      | Persist current scenario to disk         |
| 3     | LoadZone          | Load terrain/zone data                   |
| 4     | TakeCheckpoint    | Snapshot current simulation state        |
| 5     | CollectCheckpoint | Gather snapshot chunks from all nodes    |
| 6     | ExportArchive     | Archive exercise to NAS                  |
| 7     | ImportArchive     | Restore exercise from NAS                |
| 8     | ManageEpisode     | Start / stop / replay an episode         |
| 9     | ReplaySeek        | Jump replay to a wall-clock position     |
| 10    | PauseTime         | Freeze simulation time                   |
| 11    | ResumeTime        | Resume simulation time                   |
| 12    | PrefetchScenario  | Pre-load scenario assets across nodes    |
| 13    | CancelOperation   | Cancel an in-progress cluster operation  |
| 14    | StepTime          | Advance simulation by one fixed delta    |
| 15    | SetTimeScale      | Change simulation time scale             |
| 16    | DumpDiagnostics   | Collect diagnostic snapshots             |

### 7.6 NodeOpType Enumeration

28 node-level operation types used in the 2PC protocol:

| Value | Name               | Phase        |
|-------|--------------------|--------------|
| 1     | PrepareState       | Prepare      |
| 2     | CommitState        | Commit       |
| 3     | AbortTransaction   | Rollback     |
| 4     | TakeSnapshot       | Prepare      |
| 5     | RestoreSnapshot    | Commit       |
| 7     | PrepareZone        | Prepare      |
| 8     | CommitZone         | Commit       |
| 9     | PrepareLive        | Prepare      |
| 10    | FinalizeLive       | Commit       |
| 11    | PrepareReplay      | Prepare      |
| 12    | FinalizeReplay     | Commit       |
| 13    | NodeReplaySeek     | Immediate    |
| 14    | UploadChunk        | Data transfer|
| 15    | SerializeLocal     | Data export  |
| 16    | CleanupTempFiles   | Cleanup      |
| 20    | StartEpisode       | Episode mgmt |
| 21    | StopEpisode        | Episode mgmt |
| 22    | ReplayEpisode      | Episode mgmt |
| 23    | ForgetEpisode      | Episode mgmt |
| 24    | LoadEpisodeAssets  | Episode mgmt |
| 25    | PrefetchFiles      | Pre-load     |
| 26    | PrepareEdit        | Prepare      |
| 27    | FinalizeEdit       | Commit       |
| 28    | CollectDiagnostics | Diagnostics  |

### 7.7 Anti-Corruption Layer Translators

`ClusterOpEgressTranslator` is the ACL between the FDP typed intent bus and DDS:

- Consumes `PauseTimeIntent`, `ResumeTimeIntent`, `StepTimeIntent`,
  `SetTimeScaleIntent`, `TransitionStateIntent`, `ManageEpisodeIntent`,
  `ExecuteStorageOpIntent`, `TakeCheckpointIntent`, `SeekReplayIntent`,
  `CancelOperationIntent` from `FdpEventBus`.
- Serializes each to a `ClusterOpRequest` with a new `Guid` `RequestId`.
- Is the **only** class allowed to call `System.Text.Json.JsonSerializer`
  in the ExCon cluster-op egress stack.

`NodeOpMasterTranslator` is the ACL for the Orchestrator side:

- Egress: drains `ExecuteNodeOpIntent` from the bus, serializes payload to
  JSON, and writes `NodeOpCommand` to per-node DDS writers.
- Ingress: reads `NodeOpStatus` from DDS and publishes `NodeOpCompletedEvent`
  on the bus.
- Uses a `Dictionary<int, DdsWriter<NodeOpCommand>>` (one writer per node,
  keyed by node ID) so `NodeOpCommand.TargetNodeId` is set correctly.

`NodeOpSlaveTranslator` is the node-side counterpart that reads `NodeOpCommand`
and writes `NodeOpStatus`.

### 7.8 NodeHeartbeat and Health Monitoring

`NodeHeartbeat` is published by every node every ~1 second:
- `NodeId` (int, DDS key)
- `SubsystemName` (string)
- `LocalClusterState`
- `WallTicksUtc`
- `CpuUsagePercent`, `RamUsedBytes`
- `SimTickAdvancing` (bool)
- `SubsystemsJson` (JSON array of subsystem health objects)

The Orchestrator uses heartbeat liveness to detect node crashes and
transition to `Degraded` state if a required node disappears.

---

## 8. Diagnostics Channel (Fdp.Diagnostics.Network)

### 8.1 Purpose

`Fdp.Diagnostics.Network` provides DDS-backed transport for the GizmoMap
debug primitive system. It does **not** carry simulation entity data; it
carries out-of-band diagnostic visualizations: lines, circles, labels,
and interaction events rendered by the debug overlay.

### 8.2 Assembly Structure

```
Fdp.Diagnostics.Network/
  IDdsReader.cs            <- Testability interface for DDS read
  IDdsWriter.cs            <- Testability interface for DDS write
  DdsGizmoAdapters.cs      <- DdsWriterGizmoAdapter<T>, DdsReaderGizmoAdapter<T>
  TypeForwards.cs          <- global using aliases from GizmoMap.Network
```

### 8.3 GizmoMap Integration

`TypeForwards.cs` re-exports the following types from `GizmoMap.Network`:

| Alias                  | GizmoMap.Network type         | Purpose                           |
|------------------------|-------------------------------|-----------------------------------|
| `DebugPrimitivesBatch` | `GizmoMap.Network.DebugPrimitivesBatch` | Batch of debug draw calls |
| `GizmoInteractionBatch`| `GizmoMap.Network.GizmoInteractionBatch` | Mouse/hover events on gizmos |
| `GizmoInteractionEventKind` | `GizmoMap.Network.GizmoInteractionEventKind` | Click / hover enum |
| `GizmoUiState`         | `GizmoMap.Network.GizmoUiState` | Overlay panel visibility state  |

Production gizmo publisher systems receive `IDdsWriter<DebugPrimitivesBatch>`
through DI. The concrete implementation is `DdsWriterGizmoAdapter<DebugPrimitivesBatch>`
which wraps a `DdsWriter<DebugPrimitivesBatch>` on the shared `DdsParticipant`.

### 8.4 Testability Design

The `IDdsWriter<T>` / `IDdsReader<T>` interfaces act as seams:

```csharp
// Production registration:
services.AddSingleton<IDdsWriter<DebugPrimitivesBatch>>(
    _ => new DdsWriterGizmoAdapter<DebugPrimitivesBatch>(participant));

// Unit test stub:
var stub = new CapturingWriter<DebugPrimitivesBatch>();
// stub.WrittenSamples inspectable in assertions
```

This ensures that gizmo publisher systems never depend on
`CycloneDDS.Runtime` directly and can be exercised without a DDS domain.

### 8.5 Disposal Safety

Both adapters implement `IDisposable` and guard against double-dispose:

```csharp
public void Write(T sample)
{
    if (_disposed) throw new ObjectDisposedException(...);
    _writer.Write(sample);
}
```

---

## 9. DDS QoS Reference Table

All QoS combinations used across the five projects:

| Profile Name         | Reliability  | Durability     | History         | Use Case                                    |
|----------------------|--------------|----------------|-----------------|---------------------------------------------|
| **ReliableTransient**| Reliable     | TransientLocal | KeepLast-1      | Persistent state descriptors (EntityMaster, WorldPos owner) |
| **ReliableTransientDeep** | Reliable | TransientLocal | KeepLast-100   | SST_EntityMaster (FDP layer, 100-deep cache) |
| **BestEffortVolatile**| BestEffort  | Volatile       | KeepLast-1      | High-frequency position updates (WorldPos, SST_EntityState) |
| **ReliableVolatileAll** | Reliable  | Volatile       | KeepAll         | One-shot command messages (IdAlloc, ClusterOpRequest, MissionControlRequest) |
| **ReliableVolatileAll-1** | Reliable | Volatile     | KeepAll-1       | OwnershipUpdate (NED SST)                   |
| **HeartbeatProfile** | BestEffort   | TransientLocal | KeepLast-1      | NodeHeartbeat, liveness data                |
| **ReliableVolatile** | Reliable     | Volatile       | (default)       | ClusterOpRequest, SysOpStatus               |

### QoS Rationale

**TransientLocal durability** is used for any descriptor whose latest value
must be available to a late-joining subscriber (e.g., a newly started IG that
needs the current position of all entities). The DDS data store on each
publisher keeps the history depth of samples; late joiners receive them
automatically on subscription.

**Volatile durability** is used for one-shot commands and event messages that
only make sense to current subscribers. A node that starts after a
`MissionControlRequest` has been sent does not need to receive it.

**BestEffort reliability** is used for high-frequency position updates where
it is acceptable to lose an occasional sample. The next sample will be only
one simulation tick away. This avoids the retransmit overhead of reliable
delivery.

**Reliable** is used for anything that must not be lost: entity creation,
ownership changes, mission commands, orchestration messages.

**KeepAll history** with **Reliable** delivery is used for request/response
protocols (`IdAlloc_Request/Response`, `MissionControlRequest/Ack`,
`NodeOpCommand/Status`). This guarantees that a burst of requests sent before
the remote reader is connected are all delivered when the match is established.

---

## 10. Data Flow Diagrams

### 10.1 Entity Lifecycle Flow (NED / Full Cluster)

```
  Brain Node (SimHost)               Muscle Node                IG Node
       |                                  |                         |
  [spawn entity]                          |                         |
       |-- EntityMaster(id, type) ------> | ........................ | -> GhostCreationSystem
       |                                  |                         |    [create ghost entity]
       |-- WorldPos(id, geo) ----------> | ........................ | -> NetworkEntityMap
       |                                  |                         |    [update transform]
       |-- NavigationIntent(id, cmd) ---> |                         |
       |                                  | [run kinematics]        |
       |                   <-- NavigationStatus(id, result) ------- |
       |                                  |                         |
       |-- DISPOSE EntityMaster(id) -----> | ....................... | -> cleanup ghost
```

### 10.2 Orchestration 2PC Flow

```
  ExCon                     Orchestrator                  SimHost              IG
    |                            |                            |                  |
    |-- ClusterOpRequest ------> |                            |                  |
    |   (TransitionState,        |                            |                  |
    |    LoadLive payload)       |                            |                  |
    |                            |-- NodeOpCommand ---------> |                  |
    |                            |   (PrepareState, txId)     |                  |
    |                            |-- NodeOpCommand --------------------------------> |
    |                            |   (PrepareState, txId)                            |
    |                            |              <-- NodeOpStatus(PrepareState, OK) --|
    |                            |<-- NodeOpStatus(PrepareState, OK) ----------- |
    |                            |                            |                  |
    |                            | [all OK -> commit]         |                  |
    |                            |-- NodeOpCommand ---------> |                  |
    |                            |   (CommitState, txId)      |                  |
    |                            |-- NodeOpCommand --------------------------------> |
    |                            |              <-- NodeOpStatus(CommitState, OK) --|
    |                            |<-- NodeOpStatus(CommitState, OK) ----------- |
    |                            |                            |                  |
    |                            | [publish new ClusterState] |                  |
    |<-- SysOpStatus(OK) ------- |                            |                  |
    |                            |-- ClusterState(OperatingLive) ---------------------> (all)
```

### 10.3 Distributed ID Allocation Flow

```
  New Node (client)                        Orchestrator (server)
       |                                           |
  [DdsIdAllocator ctor]                            |
  [subscribe PublicationMatched]                   |
       |                                           |
       | ... DDS discovery ...                     |
       |                                           |
  [PublicationMatched fired]                       |
  [HandleServerDiscovered()]                       |
       |-- IdAlloc_Request(ClientId, Alloc, 100) ->|
       |                                           | [allocate 100 IDs]
       |<-- IdAlloc_Response(ClientId, [1,101)) ---|
       |<-- IdAlloc_Status(HighestId=101) ---------|
       |                                           |
  [pool: 100 IDs ready]                            |
  [refill when < 10 remain]                        |
       |-- IdAlloc_Request(ClientId, Alloc, 100) ->|
       |<-- IdAlloc_Response(ClientId, [101,201)) --|
```

### 10.4 Weapon Fire Pipeline (NED)

```
  Brain Node                   Muscle Node                  IG Node
       |                            |                           |
  [player fires]                    |                           |
       |-- WeaponFireRequest ------> |                          |
       |   (ShooterEntityId,        |                          |
       |    TargetEntityId,         |                          |
       |    WeaponIndex)            |                          |
       |                    [spawn bullet, physics]             |
       |                            |-- WeaponFire -----------> |
       |                            |   (Shooter, Target, Idx)  |
       |                            |                    [muzzle flash VFX]
       |                            |                           |
       |                    [bullet hits target]                |
       |                            |-- MunitionDetonation ---> |
       |                            |   (Shooter, Hit, pos)     |
       |                            |                    [explosion VFX]
       |                            |                           |
       |                    [damage assessment]                 |
       |                            |-- EntityHitDamage ------> |
       |                            |   (HitEntityId, Damage)   |
       |<-- EntityDamage(id, 0.75) -|                           |
```

---

## 11. Integration Examples

### 11.1 Registering a CycloneTranslator for a New Descriptor

```csharp
// 1. Define the DDS topic struct with QoS attributes.
[DdsTopic("MyCustomState")]
[DdsQos(
    Reliability  = DdsReliability.Reliable,
    Durability   = DdsDurability.TransientLocal,
    HistoryKind  = DdsHistoryKind.KeepLast,
    HistoryDepth = 1)]
public partial struct MyCustomStateTopic
{
    [DdsKey, DdsId(0)] public int EntityId;
    [DdsId(1)] public float Value;
}

// 2. Implement the translator.
public sealed class MyCustomStateTranslator
    : CycloneTranslator<MyCustomStateTopic, MyCustomStateTopic>
{
    public MyCustomStateTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
        : base(participant, "MyCustomState", ordinal: 200, entityMap)
    { }

    public override TranslatorDirection Direction => TranslatorDirection.Bidirectional;

    protected override void Decode(
        in MyCustomStateTopic data,
        IEntityCommandBuffer cmd,
        ISimulationView view)
    {
        if (!EntityMap.TryGet(data.EntityId, out var entity)) return;
        cmd.SetComponent(entity, new MyCustomComponent { Value = data.Value });
    }

    public override void ScanAndPublish(ISimulationView view)
    {
        var query = view.Query()
            .With<MyCustomComponent>()
            .With<NetworkOwned>()
            .Build();

        foreach (var entity in query)
        {
            var comp = view.GetComponent<MyCustomComponent>(entity);
            var netId = view.GetComponent<NetworkIdentity>(entity).NetworkId;
            Publish(new MyCustomStateTopic { EntityId = (int)netId, Value = comp.Value });
        }
    }

    public override void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
}

// 3. Register with CycloneNetworkModule (via NedReplicationModule or directly).
var translator = new MyCustomStateTranslator(participant, entityMap);
cycloneModule.RegisterCustomTranslator(translator);
```

### 11.2 Sending a Mission Command via NedCommandGateway

```csharp
// Create the gateway (once per node, shared via DI).
var gateway = new NedCommandGateway(participant, localNodeId: nodeId);

// Build the request.
var request = new MissionControlRequest
{
    RequestId      = Guid.NewGuid(),
    TargetEntityId = entityNetworkId,
    BaseVersion    = 0, // skip optimistic-lock check
    Payload        = new MissionCommandUnion
    {
        _d = eMissionCommandType.CMD_REPLACE_MISSION,
        FullMissionData = new MissionPlan
        {
            ActiveTaskId = firstTask.TaskId,
            Tasks        = new List<MissionTask> { firstTask }
        }
    }
};

// Send and await ack (default 5s timeout).
MissionControlAck ack = await gateway.SendMissionControlRequestAsync(request);
if (ack.ErrorCode != 0)
{
    FdpLog<MissionSender>.Error(
        "Mission command failed: {0} ({1})", ack.ErrorCode, ack.ErrorMessage);
}
```

### 11.3 Publishing a ClusterOpRequest from ExCon

```csharp
// ClusterOpEgressTranslator is constructed with the FdpEventBus and DdsParticipant.
var translator = new ClusterOpEgressTranslator(eventBus, participant);

// Elsewhere in the UI layer, publish a typed intent:
eventBus.PublishManaged(new PauseTimeIntent());

// translator.Tick() is called once per frame after SwapBuffers:
translator.Tick();
// This drains the intent and writes:
//   ClusterOpRequest { RequestId = Guid.NewGuid(), OperationType = PauseTime, PayloadJson = "" }
```

### 11.4 Reading the Diagnostics DDS Channel

```csharp
// Production wiring (registered in IoC container):
IDdsReader<DebugPrimitivesBatch> reader =
    new DdsReaderGizmoAdapter<DebugPrimitivesBatch>(participant);

// In the IG render loop:
if (reader.TryRead(out DebugPrimitivesBatch batch))
{
    foreach (var primitive in batch.Primitives)
        debugRenderer.Draw(primitive);
}

// Unit test wiring:
var fakeReader = new FakeDdsReader<DebugPrimitivesBatch>();
fakeReader.Enqueue(testBatch);
// inject fakeReader into the system under test
```

### 11.5 Waiting for the ID Allocator Server Before Node Startup

```csharp
// In node startup (e.g., SimHost.Main):
var allocator = new DdsIdAllocator(participant, clientId: "SimHost-Node1");

// Block until Hrot.Orchestrator's allocator server is matched (30s max):
DdsIdAllocatorHelper.EnsureRouting(participant, allocator);

// Now safe to call AllocateId():
long newEntityId = allocator.AllocateId();
```

### 11.6 BDC Entity Lifecycle

```csharp
// Publisher (lightweight node):
using var writer = new DdsWriter<BdcEntityMaster>(participant);
using var posWriter = new DdsWriter<BdcWorldPos>(participant);

// Announce entity:
writer.Write(new BdcEntityMaster
{
    EntityId = 42,
    TkbType  = tkbVehicleIndex,
    Diskind  = 1  // Platform
});

// Publish position:
posWriter.Write(new BdcWorldPos
{
    EntityId = 42,
    Time     = DateTime.UtcNow,
    Pos      = new BdcGeoPoint { Latitude = 50.0, Longitude = 14.5, Altitude = 200.0 },
    Ori      = new BdcEulerOri { Heading = 90.0f, Pitch = 0.0f, Roll = 0.0f },
    Vel      = new BdcAngularVector { Azimuth = 90.0f, Elevation = 0.0f, Length = 15.0f }
});

// Remove entity (DDS dispose):
writer.DisposeInstance(new BdcEntityMaster { EntityId = 42 });
```

---

## 12. Performance and Reliability Notes

### 12.1 Zero-Allocation Hot Paths

The `CycloneTranslator<TDds, TView>` base class is designed for zero GC
pressure on the hot path:

- `TDds : unmanaged` means all DDS topic structs are blittable value types
  that live on the stack during encode/decode.
- `Reader.Take()` returns a zero-copy loan backed by the CycloneDDS internal
  receive buffer. Samples are accessed in-place; no copy to managed heap occurs
  unless the application explicitly does so.
- `Writer.DisposeInstance(keySample)` uses `UnsafeLayout<TDds>` to patch
  only the key fields of a default-initialized struct. The struct is constructed
  on the stack with zero allocation.
- `[DdsManaged]` structs (those containing `string` or `List<T>`) do allocate;
  they are used only for low-frequency command topics, never for per-entity
  high-frequency descriptors.

### 12.2 ID Allocator Chunking

`DdsIdAllocator` requests IDs in chunks of 100 (`CHUNK_SIZE`) and refills
when the local pool falls below 10 (`LOW_WATER_MARK`). This means entity
spawning rarely waits for a network round-trip. In the worst case (empty pool,
no outstanding request), the allocator polls for 3 seconds before throwing.

The server is hosted by `Hrot.Orchestrator`. `DdsIdAllocatorHelper` ensures
no node attempts entity creation before the server is reachable.

### 12.3 Write-Before-Match Protection

`DdsIdAllocator` subscribes to `DdsWriter<IdRequest>.PublicationMatched`
before checking the current match count. This guards against the race where
the server is already matched at construction time but the event subscription
comes too late.

### 12.4 Reliable Initialization Timeout

`CycloneNetworkModule` accepts a `reliableInitTimeoutFrames` parameter.
When positive, `NetworkGatewaySystem` waits this many frames after entity
creation before allowing the entity to become `Active`. This ensures all
`TransientLocal` descriptors have been received by remote subscribers before
the entity is considered fully initialized. The default value of -1 disables
this guard.

### 12.5 Ownership Arbitration

When two nodes attempt to write the same descriptor simultaneously (a race),
DDS delivers both writes. The receiving node sees the last writer as the
owner (per NED SST rules). To avoid conflicts:

1. A node should only write descriptors it owns.
2. Ownership transfer uses `OwnershipUpdate` (NED) or is implicitly handled
   by the current owner stopping publication and the new owner starting.
3. The `DeferredTakeOwnership` mechanism (NED descriptor ID 54) allows the
   Brain to pre-route a descriptor to a Muscle node before the entity is fully
   constructed, preventing the ownership gap during genesis.

### 12.6 Dead Reckoning for IG Nodes

IG nodes set `driveFromNetwork = true` in `NedReplicationModule`. This
activates `DeadReckoningSyncSystem`, which extrapolates entity positions
between received `WorldPos` samples using the velocity and acceleration fields.
This smooths out network jitter on the visual representation.

### 12.7 Diagnostics Channel Isolation

The diagnostics DDS channel (`Fdp.Diagnostics.Network`) runs on the same
`DdsParticipant` as simulation data but uses completely separate topic types.
`DebugPrimitivesBatch` is a high-frequency, best-effort topic; a dropped
debug frame causes no harm to simulation state. This isolation means the
diagnostics channel can be disabled or filtered without affecting any entity
state.

### 12.8 Node Discovery and Degraded State

`NodeHeartbeat` is published by each node every ~1 second with
`BestEffort / TransientLocal` QoS. The Orchestrator monitors heartbeat liveness.
If a required node's heartbeat disappears for longer than the liveness threshold,
the Orchestrator transitions the cluster to `Degraded`. Recovery from `Degraded`
requires a manual operator `TransitionState` command.

`OrchestratorContextTopic` carries `RequiredNodeIdsJson`, a JSON array of
node IDs that must be present before the cluster can transition to `OperatingLive`.
This prevents partial-cluster operation.

### 12.9 Payload JSON Serialization in Orchestration

`ClusterOpEgressTranslator` and `NodeOpMasterTranslator` use
`System.Text.Json.JsonSerializer` for orchestration payloads. This is
restricted to those two classes by convention. All orchestration payload DTOs
use `JsonSerializerOptions` from `OrchestrationJsonOptions.Default`, which
configures snake_case naming, enum-as-string serialization, and null-ignore
policies consistently across the cluster.

---

## 13. Links to Individual Project Docs

| Project                              | Path                                                           |
|--------------------------------------|----------------------------------------------------------------|
| `Fdp.Network.Cyclone`                | [Fdp.Network.Cyclone source](../../../FDP/Network/Fdp.Network.Cyclone/) |
| `Fdp.Diagnostics.Network`            | [Fdp.Diagnostics.Network source](../../../FDP/Diagnostics/Fdp.Diagnostics.Network/) |
| `Hrot.Network.NED`                   | [Hrot.Network.NED source](../../../Hrot/Network/Hrot.Network.NED/) |
| `Hrot.Network.BDC`                   | [Hrot.Network.BDC source](../../../Hrot/Network/Hrot.Network.BDC/) |
| `Hrot.Network.Orchestration`         | [Hrot.Network.Orchestration source](../../../Hrot/Network/Hrot.Network.Orchestration/) |
| FDP Engine README                    | [FDP/Engine/README.md](../../../FDP/Engine/README.md)         |
| AI Dev Guide                         | [docs/AI_DEV_GUIDE.md](../../AI_DEV_GUIDE.md)                 |
| HROT Architecture                    | [docs/HROT architecture.md](../../HROT%20architecture.md)     |
| Hrot Simulation Pipeline             | [docs/projects/relationships/Hrot-Simulation-Pipeline.md](Hrot-Simulation-Pipeline.md) |
