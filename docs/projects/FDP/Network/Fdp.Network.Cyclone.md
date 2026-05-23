# Fdp.Network.Cyclone

| Item | Value |
|------|-------|
| Project | `Fdp.Network.Cyclone` |
| Path | `FDP/Network/Fdp.Network.Cyclone/` |
| Date | 2026-05-23 |
| Target Framework | net8.0 |

---

## README Validation

**Status: Missing**

No `README.md` is present in the project folder
(`FDP/Network/Fdp.Network.Cyclone/`). This document serves as the primary
reference until a README is created.

---

## Executive Overview

`Fdp.Network.Cyclone` is the **CycloneDDS networking transport layer** for the
FDP simulation framework. It bridges the FDP entity-component-system (ECS)
world to the publish-subscribe messaging layer provided by Eclipse CycloneDDS
(via the `CycloneDDS.NET` NuGet package).

### What is DDS?

Data Distribution Service (DDS) is an OMG standard for real-time,
data-centric publish/subscribe communication. Key concepts:

- **DDS Domain**: A logical communication channel. Participants in the same
  domain can discover and exchange data with each other.
- **DDS Participant**: A node in the domain. Each simulation process creates
  one participant.
- **Topic**: A named, typed data channel (e.g., `SST_EntityState`). Publishers
  write to topics; subscribers read from them.
- **DataWriter (`DdsWriter<T>`)**: Publishes typed samples to a topic.
- **DataReader (`DdsReader<T>`)**: Subscribes to a topic and receives samples.
- **QoS Policies**: Quality-of-service settings that govern reliability,
  persistence, and history of data. See the Topics section for the policies
  used here.
- **Instance**: DDS topics with a `[DdsKey]` field support multiple
  independent keyed instances per topic (analogous to multiple rows in a
  table). Disposing an instance notifies all readers that the key no longer
  exists.

### What This Library Does

1. **Owns all DDS boilerplate.** Application code never touches `DdsReader` or
   `DdsWriter` directly; it creates translators and registers them with the
   module.
2. **Translates between DDS structs and ECS components/events.** Each
   translator owns a reader/writer pair and implements the mapping.
3. **Drives the network tick.** Three ECS systems (`CycloneIngressSystem`,
   `CycloneEgressSystem`, `CycloneNetworkCleanupSystem`) are registered at
   the appropriate simulation phases and process data every frame.
4. **Allocates globally unique network IDs.** A distributed client/server
   ID allocator (`DdsIdAllocator` / `DdsIdAllocatorServer`) ensures that
   entity IDs are unique across all participants without a central authority.
5. **Maps node and type identities.** `NodeIdMapper` and `TypeIdMapper` keep
   DDS-specific opaque identifiers out of the core simulation code.

---

## Architecture

### DDS Participant Model

```
+---------------------------------------------------------------+
|                   FDP Simulation Process                      |
|                                                               |
|  +---------------------+   +--------------------------+      |
|  | CycloneNetworkModule |   | DdsIdAllocatorServer     |      |
|  |  (IEcsModule)       |   |  (optional, one per sim) |      |
|  +---------+-----------+   +----------+---------------+      |
|            |                          |                       |
|            v                          v                       |
|  +-------------------+     +----------------------+          |
|  | DdsParticipant    |<----| DdsParticipant (same)|          |
|  | (shared ref)      |     +----------------------+          |
|  +--------+----------+                                       |
|           |                                                   |
|    +------+--------+                                         |
|    |               |                                         |
|    v               v                                         |
| DdsWriter<T>   DdsReader<T>      (one pair per translator)   |
+---------------------------------------------------------------+
              |               |
              | CycloneDDS.NET |
              | (UDP multicast)|
              v               v
+---------------------------------------------------------------+
|                   DDS Domain (Network)                        |
|                                                               |
|   SST_EntityState   SST_EntityMaster   SST_OwnershipUpdate    |
|   IdAlloc_Request   IdAlloc_Response   IdAlloc_Status         |
+---------------------------------------------------------------+
```

### Ingress / Egress Data Flow (Per Frame)

```
  ECS Simulation Frame
  +-----------------------------------------------------------------+
  |                                                                 |
  |  SystemPhase.Input                                              |
  |  +---------------------------+                                  |
  |  | CycloneIngressSystem      |                                  |
  |  |  for each translator:     |                                  |
  |  |    reader.Take()          |  <--- DDS network (UDP)         |
  |  |    foreach sample:        |                                  |
  |  |      translator.Decode()  |                                  |
  |  |      cmd.SetComponent()   |  ---> ECS CommandBuffer         |
  |  +---------------------------+                                  |
  |                                                                 |
  |  ... (game logic systems run) ...                               |
  |                                                                 |
  |  SystemPhase.Export                                             |
  |  +---------------------------+                                  |
  |  | CycloneEgressSystem       |                                  |
  |  |  ProcessForcePublish()    |                                  |
  |  |  for each translator:     |                                  |
  |  |    view.Query().With<T>() |  <--- ECS World                 |
  |  |    translator.Publish()   |  ---> DDS network (UDP)         |
  |  +---------------------------+                                  |
  |                                                                 |
  |  +---------------------------+                                  |
  |  | CycloneNetworkCleanupSys  |  (registered manually by app)   |
  |  |  ReadEvents<Destruction>  |  <--- ECS lifecycle events      |
  |  |  translator.Dispose(id)   |  ---> DDS dispose instance      |
  |  +---------------------------+                                  |
  |                                                                 |
  +-----------------------------------------------------------------+
```

### Translator Class Hierarchy

```
  INetworkTranslator (Fdp.Interfaces)
        |
        +-- CycloneBaseTranslator  (abstract, non-generic)
                 |   TopicName, ReceivedSampleCount, SentSampleCount
                 |   abstract PollIngress(), ScanAndPublish()
                 |
                 +-- CycloneTranslator<TDds, TView>  (abstract, unsafe, generic)
                 |        DdsReader<TDds>, DdsWriter<TDds>
                 |        NetworkEntityMap, DescriptorOrdinal
                 |        Implements PollIngress() via loan.Take()
                 |        abstract Decode(), abstract ScanAndPublish()
                 |        virtual  Dispose(networkEntityId)
                 |
                 +-- CycloneNativeEventTranslator<TEcs, TDds>  (abstract)
                 |        TEcs : unmanaged  (zero-alloc hot path)
                 |        abstract TryDecode(), abstract TryEncode()
                 |
                 +-- CycloneManagedEventTranslator<TEcs, TDds>  (abstract)
                          TEcs : class  (uses IEventBus)
                          abstract TryDecode(), abstract TryEncode()

  IDescriptorTranslator (Fdp.Interfaces) - also implemented by:
        MultiInstanceCycloneTranslator<T>  (sealed, non-hierarchy)
              Routes samples to child entities by InstanceId
              Uses ChildMap managed component for lookup
        BlitEventTranslator<T>  (utility, not registered as INetworkTranslator)
              1:1 memory copy for pure-data events
```

### ID Allocation Protocol

```
  Client (DdsIdAllocator)            Server (DdsIdAllocatorServer)
  --------------------------------   --------------------------------
  constructor()
    subscribe PublicationMatched
    wait for server discovery
          <--- Reliable DDS discovery --->
  HandleServerDiscovered()
    RequestChunk(100)
    Write(IdRequest{Req_Alloc,100}) --->  HandleRequest()
                                          HandleAlloc(): allocate 100 IDs
                                  <---   Write(IdResponse{Resp_Alloc,start,100})
  ProcessResponses()
    _availableIds.Enqueue(x100)
  AllocateId() -> dequeue next ID
```

---

## Source Structure

All types live under the root namespace `Fdp.Network.Cyclone`.

### Namespace `Fdp.Network.Cyclone.Components`

| File | Type | Description |
|------|------|-------------|
| `Components/NetworkOrientation.cs` | `struct NetworkOrientation` | ECS component holding a `Quaternion` orientation. Tagged with `[ComponentId(GlobalComponentIds.NetworkOrientation)]`. |

### Namespace `Fdp.Network.Cyclone.Modules`

| File | Type | Description |
|------|------|-------------|
| `Modules/CycloneNetworkModule.cs` | `class CycloneNetworkModule` | Root `IEcsModule`. Constructs and registers all systems, serialization providers, and the gateway system. Also contains the inner class `CycloneNetworkIngressSystem` (a thin shim used when the module is wired without the full `CycloneIngressSystem`). |

### Namespace `Fdp.Network.Cyclone.Providers`

| File | Type | Description |
|------|------|-------------|
| `Providers/CycloneSerializationProvider.cs` | `class CycloneSerializationProvider<T>` | `ISerializationProvider` for unmanaged structs. Uses `Unsafe.WriteUnaligned` / `Unsafe.ReadUnaligned`. |
| `Providers/ManagedSerializationProvider.cs` | `class ManagedSerializationProvider<T>` | `ISerializationProvider` for managed classes. Uses `FdpAutoSerializer` and `BinaryWriter`/`BinaryReader`. |

### Namespace `Fdp.Network.Cyclone.Services`

| File | Type | Description |
|------|------|-------------|
| `Services/DdsIdAllocator.cs` | `class DdsIdAllocator` | ID allocator client. Waits for server discovery (`ManualResetEventSlim`), requests chunks of 100 IDs, maintains a local queue. |
| `Services/DdsIdAllocatorServer.cs` | `class DdsIdAllocatorServer` | ID allocator server. Processes `IdRequest` samples and writes `IdResponse` samples. Handles Alloc, Reset, and GetStatus. |
| `Services/NetworkEntityMap.cs` | `class NetworkEntityMap` | Thread-safe `long -> Entity` lookup. Used by translators to find the ECS entity for an incoming network ID. |
| `Services/NodeIdMapper.cs` | `class NodeIdMapper` | Maps `NetworkAppId` (DDS, external) <-> `int` (core, internal). Local node is always ID 1. New remote nodes get IDs 2, 3, 4... |
| `Services/TypeIdMapper.cs` | `class TypeIdMapper` | Maps `ulong` DIS type values (DDS) <-> `int` TypeId (core). Arrival-order dependent; see determinism warning in source. |

### Namespace `Fdp.Network.Cyclone.Systems`

| File | Type | Description |
|------|------|-------------|
| `Systems/CycloneIngressSystem.cs` | `class CycloneIngressSystem` | `[UpdateInPhase(SystemPhase.Input)]`. Polls all translators. Records per-translator `SystemProfileData`. |
| `Systems/CycloneEgressSystem.cs` | `class CycloneEgressSystem` | `[UpdateInPhase(SystemPhase.Export)]`. Scans entities and publishes. Also processes `ForceNetworkPublish` component one-shots. |
| `Systems/CycloneNetworkCleanupSystem.cs` | `class CycloneNetworkCleanupSystem` | `[UpdateInPhase(SystemPhase.Export)]`. Tracks owned entities, listens for `DestructionOrder` events, calls `translator.Dispose(netId)`. **Not** auto-registered by the module; the application registers it manually. |

### Namespace `Fdp.Network.Cyclone.Topics`

| File | Type | Kind | DDS Topic Name | QoS Summary |
|------|------|------|----------------|-------------|
| `Topics/CommonTypes.cs` | `struct NetworkAppId` | DDS struct | - | Equatable; AppDomainId + AppInstanceId |
| `Topics/CommonTypes.cs` | `enum NetworkAffiliation` | enum | - | Neutral/Friend/Hostile/Unknown |
| `Topics/CommonTypes.cs` | `enum NetworkLifecycleState` | enum | - | Ghost/Constructing/Active/TearDown |
| `Topics/EntityStateTopic.cs` | `struct EntityStateTopic` | DDS topic | `SST_EntityState` | BestEffort, Volatile, KeepLast(1) |
| `Topics/EntityMasterTopic.cs` | `struct EntityMasterTopic` | DDS topic | `SST_EntityMaster` | Reliable, TransientLocal, KeepLast(100) |
| `Topics/OwnershipUpdate.cs` | `struct OwnershipUpdate` | DDS topic | `SST_OwnershipUpdate` | Reliable, Volatile, KeepAll |
| `Topics/IdAllocTopics.cs` | `enum EIdRequestType` | enum | - | Req_Alloc / Req_Reset / Req_GetStatus |
| `Topics/IdAllocTopics.cs` | `enum EIdResponseType` | enum | - | Resp_Alloc / Resp_Reset / Resp_Status |
| `Topics/IdAllocTopics.cs` | `struct IdRequest` | DDS topic | `IdAlloc_Request` | Reliable, Volatile, KeepAll |
| `Topics/IdAllocTopics.cs` | `struct IdResponse` | DDS topic | `IdAlloc_Response` | Reliable, Volatile, KeepAll |
| `Topics/IdAllocTopics.cs` | `struct IdStatus` | DDS topic | `IdAlloc_Status` | Reliable, TransientLocal, KeepLast(1) |
| `Topics/WeaponStateTopic.cs` | `struct WeaponStateTopic` | DDS topic | - | EntityId + InstanceId keyed |
| `Topics/WeaponStateDescriptor.cs` | `class WeaponStateDescriptor` | class | - | ECS-side descriptor; AzimuthAngle, ElevationAngle, AmmoCount, Status |
| `Topics/WeaponStateDescriptor.cs` | `enum WeaponStatus` | enum | - | Ready/Firing/Reloading/Jammed/Disabled |
| `Topics/EntityLifecycleStatusDescriptor.cs` | `class EntityLifecycleStatusDescriptor` | class | - | EntityId, NodeId, State, Timestamp; used in reliable init mode |

### Namespace `Fdp.Network.Cyclone.Translators`

| File | Type | Description |
|------|------|-------------|
| `Translators/CycloneBaseTranslator.cs` | `abstract class CycloneBaseTranslator` | Non-generic base. Holds TopicName, sample counters, abstract Direction. |
| `Translators/CycloneTranslator.cs` | `abstract class CycloneTranslator<TDds, TView>` | Generic typed base. Owns `DdsReader<TDds>`, `DdsWriter<TDds>`. Implements PollIngress via `Reader.Take()`. Abstract `Decode()` and `ScanAndPublish()`. |
| `Translators/MultiInstanceCycloneTranslator.cs` | `class MultiInstanceCycloneTranslator<T>` | Multi-keyed translator (EntityId + InstanceId). Routes samples to root or child entities via `ChildMap`. |
| `Translators/CycloneNativeEventTranslator.cs` | `abstract class CycloneNativeEventTranslator<TEcs, TDds>` | Zero-allocation translator for unmanaged events. Decode -> `cmd.PublishEvent()`, Encode -> `view.ReadEvents<T>()`. |
| `Translators/CycloneManagedEventTranslator.cs` | `abstract class CycloneManagedEventTranslator<TEcs, TDds>` | Translator for class events. Uses `IEventBus.PublishManaged()` on ingress and `view.ReadManagedEvents<T>()` on egress. |
| `Translators/BlitEventTranslator.cs` | `class BlitEventTranslator<T>` | Pure data blit: zero transformation, direct copy between DDS and ECS event bus. Does not inherit `CycloneBaseTranslator`. |

---

## Public API Reference

### CycloneNetworkModule

```csharp
public class CycloneNetworkModule : IEcsModule
```

**Constructor**

```csharp
public CycloneNetworkModule(
    DdsParticipant participant,
    NodeIdMapper nodeMapper,
    INetworkIdAllocator idAllocator,
    INetworkTopology topology,
    EntityLifecycleModule elm,
    ISerializationRegistry? serializationRegistry = null,
    IEnumerable<IDescriptorTranslator>? customTranslators = null,
    NetworkEntityMap? sharedEntityMap = null,
    int reliableInitTimeoutFrames = -1)
```

| Parameter | Purpose |
|-----------|---------|
| `participant` | Shared `DdsParticipant`; must not be null |
| `nodeMapper` | Maps remote `NetworkAppId` values to compact node IDs |
| `idAllocator` | Provides globally unique network IDs for newly spawned entities |
| `topology` | Describes the multi-node simulation topology |
| `elm` | Entity lifecycle module; supplies lifecycle events |
| `serializationRegistry` | If provided, registers serialization providers for built-in component types (`NetworkTransform`, `NetworkVelocity`, `NetworkIdentity`, `TkbIdentity`) |
| `customTranslators` | Application-supplied translators (e.g., domain-specific state descriptors) |
| `sharedEntityMap` | Supply an existing map when multiple modules share entity lookups |
| `reliableInitTimeoutFrames` | Frames before the gateway gives up waiting for peer acknowledgement; -1 disables timeout |

**Members**

| Member | Type | Description |
|--------|------|-------------|
| `Name` | `string` | `"CycloneNetwork"` |
| `Policy` | `ExecutionPolicy` | `ExecutionPolicy.Synchronous()` |
| `RegisterSystems(registry)` | void | Registers `CycloneNetworkIngressSystem`, `CycloneEgressSystem`, and the gateway system |
| `Tick(view, deltaTime)` | void | Empty; all work is done in systems |

---

### CycloneBaseTranslator

```csharp
public abstract class CycloneBaseTranslator : INetworkTranslator
```

| Member | Description |
|--------|-------------|
| `string TopicName { get; }` | DDS topic name set at construction |
| `long ReceivedSampleCount { get; protected set; }` | Total valid samples received |
| `long SentSampleCount { get; protected set; }` | Total samples published |
| `abstract TranslatorDirection Direction { get; }` | Ingress / Egress / Bidirectional |
| `abstract void PollIngress(cmd, view)` | Pull from DDS into ECS |
| `abstract void ScanAndPublish(view)` | Push from ECS into DDS |

---

### CycloneTranslator\<TDds, TView\>

```csharp
public abstract unsafe class CycloneTranslator<TDds, TView> : CycloneBaseTranslator, IDescriptorTranslator
    where TDds : unmanaged
    where TView : struct
```

| Member | Description |
|--------|-------------|
| `DdsReader<TDds> Reader` | `protected`; may be null in unit-test mode |
| `DdsWriter<TDds> Writer` | `protected`; may be null in unit-test mode |
| `NetworkEntityMap EntityMap` | `protected`; entity lookup |
| `long DescriptorOrdinal` | Unique ordinal used by the ownership system |
| `virtual void Dispose(long networkEntityId)` | Disposes DDS instance 0 for the given entity |
| `protected void DisposeInstance(long entityId, long instanceId)` | Patches key fields and calls `Writer.DisposeInstance()` |
| `protected abstract void Decode(in TDds data, cmd, view)` | Application-supplied ingress mapping |
| `protected virtual void Publish(in TDds sample)` | Calls `Writer.Write()` and increments `SentSampleCount`; virtual for test overriding |
| `abstract void ApplyToEntity(entity, data, repo)` | IDescriptorTranslator; apply descriptor to existing entity |

---

### MultiInstanceCycloneTranslator\<T\>

```csharp
public unsafe class MultiInstanceCycloneTranslator<T> : IDescriptorTranslator
    where T : unmanaged
```

Requires that `T` passes `MultiInstanceLayout<T>.IsValid` (i.e., has `EntityId`
and `InstanceId` fields with the expected layout).

| Member | Description |
|--------|-------------|
| `string TopicName` | Set at construction |
| `long DescriptorOrdinal` | Set at construction |
| `TranslatorDirection Direction` | `Bidirectional` |
| `void PollIngress(cmd, view)` | Routes sample to root (instId==0) or child (instId>0) via ChildMap |
| `void ScanAndPublish(view)` | Queries all entities with component `T`; checks authority via `OwnershipExtensions.PackKey()` |
| `void Dispose(long networkEntityId)` | Disposes DDS instance 0 for the entity |

---

### CycloneIngressSystem

```csharp
[UpdateInPhase(SystemPhase.Input)]
public class CycloneIngressSystem : IEcsModuleSystem
```

| Member | Description |
|--------|-------------|
| `IReadOnlyList<INetworkTranslator> Translators` | Registered translators |
| `SystemProfileData? GetTranslatorProfileData(translator)` | Per-translator timing data |
| `void Execute(view, deltaTime)` | Calls `translator.PollIngress(cmd, view)` for each translator and records elapsed time |

---

### CycloneEgressSystem

```csharp
[UpdateInPhase(SystemPhase.Export)]
public class CycloneEgressSystem : IEcsModuleSystem
```

| Member | Description |
|--------|-------------|
| `IReadOnlyList<INetworkTranslator> Translators` | Registered translators |
| `SystemProfileData? GetTranslatorProfileData(translator)` | Per-translator timing data |
| `void Execute(view, deltaTime)` | `ProcessForcePublish(view)` then `translator.ScanAndPublish(view)` for each translator |

---

### CycloneNetworkCleanupSystem

```csharp
[UpdateInPhase(SystemPhase.Export)]
public class CycloneNetworkCleanupSystem : IEcsModuleSystem
```

| Member | Description |
|--------|-------------|
| `IReadOnlyList<IDescriptorTranslator> Translators` | Registered translators |
| `SystemProfileData? GetTranslatorProfileData(translator)` | Per-translator timing data |
| `void Execute(view, dt)` | Tracks owned entities (by `NetworkIdentity` + `NetworkOwnership.HasAuthority`), calls `translator.Dispose(netId)` on `DestructionOrder` events |

---

### DdsIdAllocator

```csharp
public class DdsIdAllocator : INetworkIdAllocator
```

| Member | Description |
|--------|-------------|
| `static TimeSpan DiscoveryTimeout` | `3 seconds`; timeout waiting for server match |
| `bool HasPublicationMatch` | True once the server reader is matched |
| `long AllocateId()` | Blocks until server is discovered, then dequeues from pre-fetched pool. Throws if pool exhausted or server not found. |

**Internal constants:**

| Constant | Value | Description |
|----------|-------|-------------|
| `CHUNK_SIZE` | 100 | IDs requested per batch |
| `LOW_WATER_MARK` | 10 | Refill threshold |
| `MAX_POLL_ATTEMPTS` | 600 | Spin limit (~3 seconds at 5ms sleep) |

---

### DdsIdAllocatorServer

```csharp
public class DdsIdAllocatorServer : IDisposable
```

| Member | Description |
|--------|-------------|
| `void ProcessRequests()` | Drains the `IdAlloc_Request` topic and handles each request |
| `void Dispose()` | Disposes all DDS readers and writers |

---

### NetworkEntityMap

```csharp
public class NetworkEntityMap
```

| Member | Description |
|--------|-------------|
| `void Register(long networkId, Entity entity)` | Inserts or updates mapping |
| `void Unregister(long networkId)` | Removes mapping |
| `bool TryGet(long networkId, out Entity entity)` | Thread-safe lookup |
| `void Clear()` | Clears all entries |

---

### NodeIdMapper

```csharp
public class NodeIdMapper
```

| Member | Description |
|--------|-------------|
| `int LocalNodeId` | Always 1 |
| `int GetOrRegisterInternalId(NetworkAppId externalId)` | Lazy-registers and returns compact ID |
| `NetworkAppId GetExternalId(int internalId)` | Reverse lookup; throws `ArgumentException` if not found |
| `bool HasInternalId(int internalId)` | Existence check |

---

### TypeIdMapper

```csharp
public class TypeIdMapper
```

| Member | Description |
|--------|-------------|
| `int GetCoreTypeId(ulong disType)` | Lazy-registers DIS type and returns stable int ID |
| `ulong GetDISType(int coreTypeId)` | Reverse lookup; throws `ArgumentException` if not found |
| `bool HasCoreTypeId(int coreTypeId)` | Existence check |

> **Warning:** TypeId assignment depends on packet arrival order. This means
> IDs may differ between sessions (e.g., live versus replay). Pre-register
> known types at startup if determinism is required.

---

### CycloneSerializationProvider\<T\>

```csharp
public class CycloneSerializationProvider<T> : ISerializationProvider where T : unmanaged
```

| Member | Description |
|--------|-------------|
| `int GetSize(object descriptor)` | Returns `Unsafe.SizeOf<T>()` |
| `void Encode(object descriptor, Span<byte> buffer)` | `Unsafe.WriteUnaligned` to buffer |
| `void Apply(entity, buffer, cmd)` | `Unsafe.ReadUnaligned` then `cmd.SetComponent()` |

---

### ManagedSerializationProvider\<T\>

```csharp
public class ManagedSerializationProvider<T> : ISerializationProvider where T : class, new()
```

| Member | Description |
|--------|-------------|
| `int GetSize(object descriptor)` | Serializes to a temporary `MemoryStream` to measure size |
| `void Encode(object descriptor, Span<byte> buffer)` | `FdpAutoSerializer.Serialize()` into buffer |
| `void Apply(entity, buffer, cmd)` | `FdpAutoSerializer.Deserialize<T>()` then `cmd.SetManagedComponent()` |

---

## DDS Topic Reference

### QoS Matrix

| Topic | DDS Name | Reliability | Durability | History |
|-------|----------|-------------|------------|---------|
| `EntityStateTopic` | `SST_EntityState` | BestEffort | Volatile | KeepLast(1) |
| `EntityMasterTopic` | `SST_EntityMaster` | Reliable | TransientLocal | KeepLast(100) |
| `OwnershipUpdate` | `SST_OwnershipUpdate` | Reliable | Volatile | KeepAll |
| `IdRequest` | `IdAlloc_Request` | Reliable | Volatile | KeepAll |
| `IdResponse` | `IdAlloc_Response` | Reliable | Volatile | KeepAll |
| `IdStatus` | `IdAlloc_Status` | Reliable | TransientLocal | KeepLast(1) |

### QoS Design Rationale

- **`SST_EntityState` (BestEffort, Volatile):** Position and velocity are sent
  every frame. Losing a sample causes at most one stale frame on the remote
  side. Using reliable QoS here would cause head-of-line blocking and waste
  bandwidth retransmitting data that is immediately superseded.

- **`SST_EntityMaster` (Reliable, TransientLocal):** Entity ownership and type
  information must never be lost. `TransientLocal` durability means late-joining
  nodes receive the latest known state for every keyed instance.

- **`SST_OwnershipUpdate` (Reliable, Volatile, KeepAll):** Ownership changes
  are rare but must be delivered in order and without loss. `KeepAll` ensures no
  change is dropped even when a burst arrives.

- **ID Allocation topics (Reliable, Volatile, KeepAll):** The client/server
  protocol must not lose requests or responses. `Volatile` is sufficient because
  the client re-requests on timeout anyway.

### EntityStateTopic Fields

| Field | Type | DDS ID | Description |
|-------|------|--------|-------------|
| `EntityId` | `long` | 0 | DDS key; unique entity identifier |
| `PositionX/Y/Z` | `double` | 1-3 | World-space position |
| `VelocityX/Y/Z` | `float` | 4-6 | Linear velocity |
| `OrientationX/Y/Z/W` | `float` | 7-10 | Quaternion orientation |
| `Timestamp` | `long` | 11 | Sample timestamp |

### EntityMasterTopic Fields

| Field | Type | DDS ID | Description |
|-------|------|--------|-------------|
| `EntityId` | `long` | 0 | DDS key; unique entity identifier |
| `OwnerId` | `NetworkAppId` | 1 | Owner application (domain + instance) |
| `DisTypeValue` | `ulong` | 2 | DIS entity type as packed value |
| `Flags` | `int` | 3 | Entity metadata flags |
| `TkbTypeValue` | `long` | 4 | TKB blueprint ID for ghost promotion |

---

## Dependencies

### NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `CycloneDDS.NET` | 0.2.2 | Eclipse CycloneDDS C# binding; provides `DdsParticipant`, `DdsReader<T>`, `DdsWriter<T>`, `[DdsTopic]`, `[DdsQos]`, `[DdsKey]` etc. |
| `NLog` | 5.2.8 | Structured logging via `FdpLog<T>` (wrapper used in cleanup system) |

### Project References

| Project | Purpose |
|---------|---------|
| `Fdp.ModuleHost` | `IEcsModule`, `IEcsModuleSystem`, `ISimulationView`, `IEntityCommandBuffer`, `SystemPhase`, `[UpdateInPhase]`, `SystemProfileData` |
| `Fdp.Toolkits` | `Fdp.Interfaces` (translator interfaces, `ISerializationProvider`, `INetworkIdAllocator`), `Fdp.Toolkit.Replication` (NetworkEntityMap, NetworkGatewaySystem, replication components), `Fdp.Toolkit.Lifecycle`, `Fdp.Toolkit.NetworkSpawning`, `UnsafeLayout<T>`, `MultiInstanceLayout<T>` |

### Internal Visibility

```
InternalsVisibleTo: Fdp.Network.Cyclone.Tests
InternalsVisibleTo: Network.Cyclone.Tests
```

---

## Usage Examples

### Example 1: Wiring the Module in an Application

```csharp
// Application startup (e.g., SimHostApp.cs)
var participant = new DdsParticipant(domainId: 0);
var nodeMapper = new NodeIdMapper(localDomain: 0, localInstance: 1);
var idAllocator = new DdsIdAllocator(participant, clientId: "SimHost");
var topology = new SingleNodeTopology(nodeMapper.LocalNodeId);
var elm = new EntityLifecycleModule();

// Custom domain translator provided by the application
var entityStateTranslator = new MyEntityStateTranslator(
    participant,
    nodeMapper,
    entityMap,
    ordinal: 1);

var module = new CycloneNetworkModule(
    participant,
    nodeMapper,
    idAllocator,
    topology,
    elm,
    serializationRegistry: myRegistry,
    customTranslators: new[] { entityStateTranslator });

// Register module with the simulation engine
simulation.RegisterModule(module);

// Register cleanup system manually (allows application to add its own translators)
simulation.RegisterSystem(new CycloneNetworkCleanupSystem(
    new[] { entityStateTranslator }));
```

### Example 2: Implementing a Custom Entity-State Translator

```csharp
// Concrete translator that maps EntityStateTopic <-> NetworkTransform component.
public class EntityStateTranslator
    : CycloneTranslator<EntityStateTopic, EntityStateTopic>
{
    private readonly int _localNodeId;

    public EntityStateTranslator(
        DdsParticipant participant,
        NodeIdMapper nodeMapper,
        NetworkEntityMap entityMap,
        long ordinal)
        : base(participant, "SST_EntityState", ordinal, entityMap)
    {
        _localNodeId = nodeMapper.LocalNodeId;
    }

    public override TranslatorDirection Direction => TranslatorDirection.Bidirectional;

    // Ingress: DDS -> ECS
    protected override void Decode(
        in EntityStateTopic data,
        IEntityCommandBuffer cmd,
        ISimulationView view)
    {
        if (!EntityMap.TryGet(data.EntityId, out Entity entity))
            return; // ghost not yet created or entity unknown

        cmd.SetComponent(entity, new NetworkTransform
        {
            Position = new Vector3((float)data.PositionX, (float)data.PositionY, (float)data.PositionZ),
            Rotation = new Quaternion(data.OrientationX, data.OrientationY,
                                     data.OrientationZ, data.OrientationW)
        });
    }

    // Egress: ECS -> DDS
    public override void ScanAndPublish(ISimulationView view)
    {
        var query = view.Query()
            .With<NetworkTransform>()
            .With<NetworkIdentity>()
            .With<NetworkOwnership>()
            .Build();

        foreach (var entity in query)
        {
            ref readonly var ownership = ref view.GetComponentRO<NetworkOwnership>(entity);
            if (!ownership.HasAuthority) continue;

            ref readonly var identity  = ref view.GetComponentRO<NetworkIdentity>(entity);
            ref readonly var transform = ref view.GetComponentRO<NetworkTransform>(entity);

            var sample = new EntityStateTopic
            {
                EntityId    = identity.Value,
                PositionX   = transform.Position.X,
                PositionY   = transform.Position.Y,
                PositionZ   = transform.Position.Z,
                OrientationX = transform.Rotation.X,
                OrientationY = transform.Rotation.Y,
                OrientationZ = transform.Rotation.Z,
                OrientationW = transform.Rotation.W,
                Timestamp   = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            Publish(sample);
        }
    }

    public override void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
}
```

### Example 3: Implementing a Zero-Allocation Event Translator

```csharp
// DDS struct for the network event
[DdsTopic("SIM_SwitchTimeMode")]
[DdsQos(Reliability = DdsReliability.Reliable,
        Durability  = DdsDurability.Volatile,
        HistoryKind = DdsHistoryKind.KeepAll)]
public partial struct DdsSwitchTimeModeEvent
{
    [DdsId(0)] public int NewMode;
    [DdsId(1)] public int OriginNodeId;
}

// ECS unmanaged event
public struct SwitchTimeModeEvent
{
    public int NewMode;
    public int OriginNodeId;
}

// Zero-allocation translator
public class SwitchTimeModeTranslator
    : CycloneNativeEventTranslator<SwitchTimeModeEvent, DdsSwitchTimeModeEvent>
{
    public SwitchTimeModeTranslator(DdsParticipant participant, NetworkEntityMap map)
        : base(participant, "SIM_SwitchTimeMode", map) { }

    public override TranslatorDirection Direction => TranslatorDirection.Bidirectional;

    protected override bool TryDecode(
        in DdsSwitchTimeModeEvent input,
        out SwitchTimeModeEvent output)
    {
        output = new SwitchTimeModeEvent
        {
            NewMode      = input.NewMode,
            OriginNodeId = input.OriginNodeId
        };
        return true;
    }

    protected override bool TryEncode(
        in SwitchTimeModeEvent input,
        out DdsSwitchTimeModeEvent output)
    {
        output = new DdsSwitchTimeModeEvent
        {
            NewMode      = input.NewMode,
            OriginNodeId = input.OriginNodeId
        };
        return true;
    }
}
```

### Example 4: Multi-Instance Weapon State Translator

```csharp
// Weapon turret data is keyed by (EntityId, InstanceId).
// MultiInstanceCycloneTranslator handles the routing automatically.
var ghostCreationSystem = simulation.GetSystem<GhostCreationSystem>();

var weaponTranslator = new MultiInstanceCycloneTranslator<WeaponStateTopic>(
    participant,
    topicName: "SST_WeaponState",
    ordinal: 2,
    entityMap,
    ghostCreationSystem);

// Register as both a network translator and a cleanup translator
module = new CycloneNetworkModule(
    participant, nodeMapper, idAllocator, topology, elm,
    customTranslators: new IDescriptorTranslator[] { weaponTranslator });

simulation.RegisterSystem(new CycloneNetworkCleanupSystem(
    new[] { weaponTranslator }));
```

### Example 5: Standalone ID Allocator Server

```csharp
// The server is typically started in the master simulation process.
var participant = new DdsParticipant(domainId: 0);
var server = new DdsIdAllocatorServer(participant);

// Drive the server in a background tick loop:
while (running)
{
    server.ProcessRequests();
    Thread.Sleep(10);
}

server.Dispose();
```

---

## Best Practices

### DDS-Specific Tips

1. **Use BestEffort QoS for high-frequency state.** Entity positions and
   velocities are sent every frame. A dropped sample causes at most one stale
   frame; using Reliable QoS would cause head-of-line blocking under load.
   `SST_EntityState` demonstrates this pattern.

2. **Use Reliable + TransientLocal for critical descriptors.** Master records
   (ownership, entity type, blueprint IDs) must reach late-joining nodes.
   `SST_EntityMaster` uses `TransientLocal` so that new participants receive
   the current state without needing a resend.

3. **Key your topics on a single `long` field.** A single `long EntityId` key
   field makes DDS instance lifecycle (write/dispose/unregister) predictable
   and avoids multi-field key matching overhead.

4. **Dispose instances explicitly on entity destruction.** Calling
   `Writer.DisposeInstance(keySample)` notifies all readers that the key no
   longer exists. `CycloneNetworkCleanupSystem` handles this by listening for
   `DestructionOrder` events. Forgetting to dispose leaves stale instances in
   late-joiner caches.

5. **Let `CycloneTranslator` be null-safe for tests.** The base constructor
   accepts a null `DdsParticipant` and leaves `Reader`/`Writer` as null. Guard
   against null in `PollIngress` (`if (Reader is null) return;`) to allow unit
   tests without a live DDS infrastructure.

6. **Never write to DDS on the egress path for non-authoritative entities.**
   Always check `NetworkOwnership.HasAuthority` (or `view.HasAuthority()` with
   the packed descriptor key) before calling `Publish()`. Writing to a topic
   for an entity owned by another node corrupts the simulation state.

7. **Pre-register known types in TypeIdMapper.** `TypeIdMapper` assigns IDs by
   arrival order, which is non-deterministic across sessions. Pre-registering
   fixed mappings at startup prevents divergence between live and replay runs.

8. **Start `DdsIdAllocatorServer` before client processes.** The client
   (`DdsIdAllocator`) waits up to `DiscoveryTimeout` (3 seconds) for the
   server's DDS reader to be matched. If the server starts after the clients,
   the first allocation call will block for the full timeout duration.

9. **Share the `DdsParticipant`.** Creating multiple participants in one
   process multiplies DDS discovery overhead. Pass the same `DdsParticipant`
   instance to all translators, the `DdsIdAllocator`, and the
   `DdsIdAllocatorServer`.

10. **Use `unsafe` blocks only through the provided base classes.** `CycloneTranslator`
    and `MultiInstanceCycloneTranslator` encapsulate all unsafe pointer arithmetic
    in `UnsafeLayout<T>` and `MultiInstanceLayout<T>` helpers. Avoid writing raw
    pointer code in application translators.

---

## Related Projects

| Project | Relationship |
|---------|-------------|
| `Fdp.ModuleHost` | Provides the ECS module host infrastructure: `IEcsModule`, `IEcsModuleSystem`, `ISimulationView`, `IEntityCommandBuffer`, `[UpdateInPhase]`, `SystemProfileData`. |
| `Fdp.Toolkits` (Fdp.Interfaces) | Defines the translator interfaces (`INetworkTranslator`, `IDescriptorTranslator`, `INetworkEventTranslator`), `ISerializationProvider`, `INetworkIdAllocator`, and `INetworkTopology`. |
| `Fdp.Toolkits` (Fdp.Toolkit.Replication) | Provides `NetworkEntityMap` (toolkit version), `NetworkGatewaySystem`, `GhostCreationSystem`, `UnsafeLayout<T>`, `MultiInstanceLayout<T>`, `OwnershipExtensions`, and replication ECS components (`NetworkTransform`, `NetworkVelocity`, `NetworkIdentity`, `NetworkOwnership`, `ForceNetworkPublish`, `ChildMap`, `PartMetadata`). |
| `Fdp.Toolkit.Lifecycle` | Provides `EntityLifecycleModule` and lifecycle events such as `DestructionOrder` that `CycloneNetworkCleanupSystem` listens for. |
| `Fdp.Toolkit.NetworkSpawning` | Provides `INetworkIdAllocator` (consumed by `CycloneNetworkModule`) and ghost spawning utilities. |
| `Fdp.Network.Cyclone.Tests` | Unit and integration tests for this project. Uses `InternalsVisibleTo` to access internal types. Includes mock simulation views and in-process `DdsIdAllocatorServer` tests. |
| `Fdp.Examples.NetworkDemo` | Example project demonstrating multi-node entity replication using this transport. |
| `Fdp.Examples.DDS` | Example project demonstrating raw DDS publish/subscribe patterns on top of `CycloneDDS.NET`. |

---

## Architecture Decision Notes

### Why a Separate ID Allocator?

FDP entities need globally unique IDs across multiple simulation nodes. Rather
than relying on GUID generation (GC allocation) or pre-assigned ranges (static
configuration), a DDS-based allocator is used:

- The server hands out chunks of 100 IDs per request, amortizing round-trip
  latency.
- The client maintains a local pool and refills when it drops below 10 IDs.
- This approach works across process boundaries and machines without any shared
  file system or database.

### Why `unsafe` in the Translator Base?

`CycloneTranslator` uses `unsafe` pointer arithmetic via `UnsafeLayout<T>` to
patch the `EntityId` field of a stack-allocated key sample when calling
`Writer.DisposeInstance()`. This avoids boxing the struct or using reflection
to find the field at runtime, keeping the hot egress path allocation-free.

### Why Not Register CycloneNetworkCleanupSystem Automatically?

`CycloneNetworkCleanupSystem` needs access to `IDescriptorTranslator`
implementations, which may include application-specific translators that have
side effects on disposal (e.g., sending a final "master entity destroyed"
update to peers before the DDS instance is disposed). Requiring the application
to register the system explicitly gives it control over the translator set and
the disposal sequence.
