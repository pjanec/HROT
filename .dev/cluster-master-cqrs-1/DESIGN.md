# Design: ClusterMaster CQRS Decoupling

**Status:** In Design  
**Folder:** `.dev/cluster-master-cqrs-1/`  
**Design Talk:** [design_talk.md](./design_talk.md)

---

## 1. Background and Motivation

### 1.1 Architectural Asymmetry

The cluster state management architecture currently exhibits a fundamental asymmetry:

- **`ClusterSlave` (FDP.Toolkit.Orchestration):** Network-agnostic. All CycloneDDS I/O is delegated through the `IOrchestrationTransport` interface. Can be spun up in a headless unit test without a DDS participant.
- **`ClusterMaster` (Hrot.Orchestrator):** Tightly coupled to CycloneDDS infrastructure. Directly instantiates `DdsWriter<NodeOpCommand>`, `DdsWriter<SystemStateTopic>`, `DdsReader<NodeOpStatus>`, `DdsReader<ClusterOpRequest>`, and even a `Dictionary<int, DdsWriter<NodeOpCommand>>` writer cache. Business orchestration rules (timeout handling, transition graph evaluation) are tangled with DDS socket management. Impossible to unit test without a live network stack.

### 1.2 Additional Code Smells

Beyond direct DDS coupling, `ClusterMaster` and its handlers suffer from **Stringly Typed** design:

1. **Raw JSON parsing inside domain logic** — `ClusterMaster.ProcessSingleClusterOpRequest` and `TransitionPlanner` call `JsonDocument.Parse(req.PayloadJson)` and manually extract fields like `"TargetState"`, `"TargetWallTicks"`, `"TimeMode"` from raw JSON strings.
2. **Primitive Obsession in FDP toolkit** — `OrchestrationCommand.OperationId` and `TkClusterStateChangedEvent.NextStateId` use raw `int` to avoid coupling to `Hrot.NED` enums, sacrificing type safety and debugger readability.
3. **`IClusterStateHandler.CanHandle(int)` uses raw ints** — handler dispatch is done against untyped integer operation codes.

### 1.3 The Goal

Transform `ClusterMaster` to follow the same clean architecture as `ClusterSlave`:
- No DDS types, no `System.Text.Json`, no `Hrot.NED` references inside the domain.
- `ClusterMaster` and `ClusterSlave` communicate exclusively via strongly-typed local event structs on the `FdpEventBus`.
- Network adaptation is pushed to stateless translator classes in the application layer (`Hrot`).
- The entire cluster orchestration (2PC state machine) can be tested in a single process without any DDS participant.

---

## 2. Target Architecture

### 2.1 Layered Overview

```
┌────────────────────────────────────────────────────────────────┐
│ Application Infrastructure (Hrot.Orchestrator / Hrot.Common)   │
│                                                                  │
│  ClusterOpMasterTranslator  NodeOpMasterTranslator              │
│  NodeOpSlaveTranslator      (JSON payload DTOs live here)       │
└────────────────┬───────────────────┬────────────────────────────┘
                 │ FdpEventBus       │ FdpEventBus
┌────────────────▼───────────────────▼────────────────────────────┐
│ FDP Domain (FDP.Toolkit.Orchestration)                           │
│                                                                  │
│  ClusterMaster   ClusterSlave   IClusterStateHandler            │
│  (enums, intent/event structs defined here)                      │
└─────────────────────────────────────────────────────────────────┘
                 │ FdpEventBus (in AllInOne: shared bus)
┌────────────────▼────────────────────────────────────────────────┐
│ CycloneDDS Network (only touched by translators)                 │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 CQRS Flow (Sequence)

```
sequenceDiagram
    autonumber
    participant UI as ExCon (UI)
    participant DDS as CycloneDDS Network
    box Master Node (Orchestrator)
    participant MT as Master Translators
    participant MB as FdpEventBus (Master)
    participant CM as ClusterMaster
    end
    box Slave Node (SimHost/IG/CGF)
    participant ST as Slave Translators
    participant SB as FdpEventBus (Slave)
    participant CS as ClusterSlave
    end

    %% 1. UI to Master
    UI->>DDS: Write ClusterOpRequest
    DDS->>MT: PollIngress (ClusterOpMasterTranslator)
    MT->>MB: PublishManaged TransitionStateIntent
    MB->>CM: Consume TransitionStateIntent

    %% 2. Master to Slave Fan-Out
    CM->>CM: Create DistributedTransaction
    CM->>MB: PublishManaged ExecuteNodeOpIntent
    MB->>MT: Scan (NodeOpMasterTranslator)
    MT->>DDS: Write NodeOpCommand
    DDS->>ST: PollIngress (NodeOpSlaveTranslator)
    ST->>SB: PublishManaged ExecuteNodeOpIntent
    SB->>CS: Consume ExecuteNodeOpIntent

    %% 3. Slave Execution
    CS->>CS: Dispatch to IClusterStateHandler (Prepare/Commit)
    CS->>SB: PublishManaged NodeOpCompletedEvent
    SB->>ST: Scan (NodeOpSlaveTranslator)
    ST->>DDS: Write NodeOpStatus

    %% 4. Master Correlation & UI Feedback
    DDS->>MT: PollIngress (NodeOpMasterTranslator)
    MT->>MB: PublishManaged NodeOpCompletedEvent
    MB->>CM: Consume NodeOpCompletedEvent
    CM->>CM: Correlate ACKs & Close Transaction
    CM->>MB: PublishManaged ClusterOpCompletedEvent
    MB->>MT: Scan (ClusterOpMasterTranslator)
    MT->>DDS: Write ClusterOpStatus
    DDS->>UI: Observe Status
```

---

## 3. FDP Domain Layer Changes

### 3.1 Domain Enums (Dual-Enum Pattern)

Define pure domain enums inside `FDP.Toolkit.Orchestration` that mirror the `Hrot.NED.Descriptors.Orchestration` enums. These allow type safety in the FDP domain without taking a dependency on the Hrot application layer.

```csharp
// FDP/Toolkits/FDP.Toolkit.Orchestration/Enums/ClusterState.cs
namespace FDP.Toolkit.Orchestration
{
    public enum ClusterState
    {
        Idle = 0,
        LoadingEdit = 10, OperatingEdit = 11, UnloadingEdit = 12,
        LoadingPreview = 20, OperatingPreview = 21, UnloadingPreview = 22,
        LoadingLive = 30, OperatingLive = 31, UnloadingLive = 32,
        LoadingReplay = 40, OperatingReplay = 41, UnloadingReplay = 42,
        Degraded = 99
    }
}
```

```csharp
// FDP/Toolkits/FDP.Toolkit.Orchestration/Enums/ClusterOpType.cs
namespace FDP.Toolkit.Orchestration
{
    public enum ClusterOpType
    {
        TransitionState = 1, SaveScenario = 2, LoadZone = 3,
        TakeCheckpoint = 4, CollectCheckpoint = 5,
        ExportArchive = 6, ImportArchive = 7, ManageEpisode = 8,
        ReplaySeek = 9, PauseTime = 10, ResumeTime = 11,
        PrefetchScenario = 12, CancelOperation = 13,
        StepTime = 14, SetTimeScale = 15
    }
}
```

```csharp
// FDP/Toolkits/FDP.Toolkit.Orchestration/Enums/NodeOpType.cs
namespace FDP.Toolkit.Orchestration
{
    public enum NodeOpType
    {
        PrepareState = 1, CommitState = 2, AbortTransaction = 3,
        TakeSnapshot = 4, RestoreSnapshot = 5,
        PrepareZone = 7, CommitZone = 8,
        PrepareLive = 9,  FinalizeLive = 10,
        PrepareReplay = 11, FinalizeReplay = 12, NodeReplaySeek = 13,
        UploadChunk = 14, SerializeLocal = 15, CleanupTempFiles = 16,
        StartEpisode = 20, StopEpisode = 21, ReplayEpisode = 22,
        ForgetEpisode = 23, LoadEpisodeAssets = 24,
        PrefetchFiles = 25, PrepareEdit = 26, FinalizeEdit = 27
    }
}
```

**Constraint:** The integer values MUST stay in sync with the `Hrot.NED.Descriptors.Orchestration` counterparts. Verified by unit tests that cast between the two.

### 3.2 Core CQRS Event Bus DTOs

> **Memory model:** All Cluster/Node operation intents and result events are low-frequency **Control Plane** messages. They are defined as standard C# structs that may contain managed reference fields (`object?`, `string?`, nullable types). They must be routed exclusively via `_eventBus.PublishManaged<T>()` and `_eventBus.ConsumeManaged<T>()`. Do NOT apply `unmanaged` generic constraints or `FixedString` fields to these types.

> **No `ExecuteClusterOpIntent`:** High-level cluster operations are routed via operation-specific intent structs (Section 3.3), published **only** by the translator. There is no generic wrapper struct. This eliminates the correlation nightmare of split commands on the bus.

> **No JSON inside the FDP domain:** Neither `ExecuteNodeOpIntent` nor any result event carries a `string PayloadJson` or `string ResultJson` field. All operation-specific data travels as a strongly-typed `object? DomainPayload` / `object? ResultPayload`. JSON serialization and deserialization is the exclusive responsibility of the network translators in the Hrot application layer (see Section 6). This ensures `System.Text.Json` never appears inside `FDP.Toolkit.Orchestration`.

These three structs form the backbone of the two independent CQRS loops. They are `[DataPolicy(DataPolicy.NoRecord)]` so they are never saved to exercise recordings.

```csharp
[EventId(9011)]
[DataPolicy(DataPolicy.NoRecord)]
public struct ClusterOpCompletedEvent
{
    public Guid RequestId;
    public int StatusCode;          // Uses OrchestrationStatusCode constants
    public object? ResultPayload;   // Pure domain result object (e.g. MaxNetworkIdResult)
                                    // Translators serialize this to ResultJson for DDS
}

[EventId(9012)]
[DataPolicy(DataPolicy.NoRecord)]
public struct ExecuteNodeOpIntent
{
    public Guid TransactionId;
    public int TargetNodeId;
    public NodeOpType Operation;
    public object? DomainPayload;   // Strongly-typed payload struct (e.g. TransitionNodePayload)
                                    // Translators serialize this to PayloadJson for DDS
                                    // Handlers access it via: if (intent.DomainPayload is MyPayload p)
}

[EventId(9013)]
[DataPolicy(DataPolicy.NoRecord)]
public struct NodeOpCompletedEvent
{
    public Guid TransactionId;
    public int NodeId;
    public int StatusCode;
    public bool IsParticipating;
    public object? ResultPayload;   // Pure domain result object (e.g. FileManifestResult)
                                    // Translators serialize this to ResultJson for DDS
}
```

### 3.3 Specific Operation Payload Intents

**Two-loop design:**

- **Cluster Ops loop (UI → Master):** There is no generic `ExecuteClusterOpIntent`. The `ClusterOpMasterTranslator` inspects the incoming DDS `OperationType`, performs all JSON deserialization, and publishes **only** the specific strongly-typed intent (e.g., `TransitionStateIntent`). The `ClusterMaster` consumes these specific intents directly via `_eventBus.ConsumeManaged<T>()`. The `RequestId` is embedded in every intent struct so the master can open a `DistributedTransaction` without needing a generic wrapper.

- **Node Ops loop (Master → Slaves):** The generic `ExecuteNodeOpIntent` (Section 3.2) is retained so the `ClusterSlave` can act as a generic 2PC router without needing to know every operation type. However, it does **not** carry JSON. It carries an `object? DomainPayload` — a pure, strongly-typed payload struct placed there by the `ClusterMaster` (e.g., `TransitionNodePayload`). Network translators handle all JSON serialization/deserialization at the edge. `IClusterStateHandler` implementations access their data via safe type-casting: `if (intent.DomainPayload is TransitionNodePayload p) { ... }`. Zero JSON parsing anywhere in the FDP domain.

All cluster-op intents are `[DataPolicy(DataPolicy.NoRecord)]`. Even operations with no payload get a dedicated intent struct so `ClusterMaster` can consume them cleanly via a typed event.

#### TransitionStateIntent
```csharp
[EventId(9050)]
[DataPolicy(DataPolicy.NoRecord)]
public struct TransitionStateIntent
{
    public Guid TransactionId;
    public ClusterState TargetState;
    public long TargetWallTicks;    // 0 = not specified
    public string? ScenarioId;
    public string? ExerciseId;
    public string? TimeMode;
}
```

#### ManageEpisodeIntent
```csharp
[EventId(9051)]
[DataPolicy(DataPolicy.NoRecord)]
public struct ManageEpisodeIntent
{
    public Guid TransactionId;
    public bool IsStart;
    public Guid EpisodeId;
    public string? ScenarioId;
}
```

#### SeekReplayIntent
```csharp
[EventId(9052)]
[DataPolicy(DataPolicy.NoRecord)]
public struct SeekReplayIntent
{
    public Guid RequestId;
    public long TargetWallTicks;
}
```

#### CancelOperationIntent
```csharp
[EventId(9053)]
[DataPolicy(DataPolicy.NoRecord)]
public struct CancelOperationIntent
{
    public Guid TargetRequestId;
}
```

#### ExecuteStorageOpIntent
```csharp
public enum StorageOpType { Export, Import, SaveScenario }

[EventId(9054)]
[DataPolicy(DataPolicy.NoRecord)]
public struct ExecuteStorageOpIntent
{
    public Guid RequestId;
    public StorageOpType Operation;
    public string? ExerciseId;
}

[EventId(9055)]
[DataPolicy(DataPolicy.NoRecord)]
public struct StorageOpCompletedEvent
{
    public Guid RequestId;
    public int StatusCode;
    public int SuccessCount;
    public int FailureCount;
}
```

#### TakeCheckpointIntent
```csharp
[EventId(9056)]
[DataPolicy(DataPolicy.NoRecord)]
public struct TakeCheckpointIntent
{
    public Guid RequestId;
    // No payload — operation requires no additional parameters
}
```

#### LoadZoneIntent
```csharp
[EventId(9057)]
[DataPolicy(DataPolicy.NoRecord)]
public struct LoadZoneIntent
{
    public Guid RequestId;
    public string? ZoneId;
}
```

### 3.4 IClusterStateHandler: Eradicate OrchestrationCommand

`OrchestrationCommand` and `OrchestrationStatus` are legacy middle-man structs that existed solely because `IOrchestrationTransport` was the abstraction layer. Now that `FdpEventBus` is the unified mediator and `ExecuteNodeOpIntent` is the domain contract, these structs are redundant boilerplate. They must be **deleted** from `FDP.Toolkit.Orchestration`.

The `ClusterSlave` consumes `ExecuteNodeOpIntent` directly from the bus and passes it straight to the handler — no mapping, no intermediate struct.

`IClusterStateHandler` is updated to accept `ExecuteNodeOpIntent` directly:

```csharp
public interface IClusterStateHandler
{
    bool CanHandle(NodeOpType operation);

    // Handlers cast intent.DomainPayload to their expected payload type
    Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct);

    void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo);
    void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo);
}
```

Key changes from the old signature:
- `OrchestrationCommand cmd` → `ExecuteNodeOpIntent intent` — removes the middle-man mapping entirely.
- `Task<string?>` → `Task<object?>` — handlers return a strongly-typed result object (e.g., `FileManifestResult`, `MaxNetworkIdResult`) or `null`. `ClusterSlave` places this directly into `NodeOpCompletedEvent.ResultPayload`. No JSON serialization in the domain.
- `OrchestrationCommand.cs` and `OrchestrationStatus.cs` are **deleted** from `FDP.Toolkit.Orchestration`.

---

## 4. ClusterSlave Refactoring

### 4.1 Remove IOrchestrationTransport

`ClusterSlave` currently uses `IOrchestrationTransport` as its sole abstraction over the network. Once event-bus-based translators exist, this interface becomes redundant.

**After refactoring:**
- `ClusterSlave` no longer accepts an `IOrchestrationTransport` constructor parameter.
- It reads commands by consuming `ExecuteNodeOpIntent` from `FdpEventBus` (via `_eventBus.ConsumeManaged<ExecuteNodeOpIntent>()`).
- It publishes acknowledgements by pushing `NodeOpCompletedEvent` onto `_eventBus`.
- It publishes heartbeats by pushing a new `NodeHeartbeatEvent` (or via a dedicated heartbeat service).
- `IOrchestrationTransport` and `DdsOrchestrationTransport` are deleted once migration is complete.
- `OrchestrationCommand` and `OrchestrationStatus` are **deleted** from `FDP.Toolkit.Orchestration` as part of the same cleanup — they are entirely superseded by `ExecuteNodeOpIntent` and `NodeOpCompletedEvent`.

### 4.2 AllInOne / No-Network Mode

Without translators, `ClusterMaster` publishes `ExecuteNodeOpIntent` → `ClusterSlave` consumes it from the same `FdpEventBus` instance → `ClusterSlave` publishes `NodeOpCompletedEvent` → `ClusterMaster` consumes it. Zero network, zero translators. The 2PC works entirely in memory.

**Key rule:** `1 ECS World = 1 FdpEventBus = 1 ClusterSlave`. In AllInOne mode, register all domain handlers (SimHost, IG, Orchestrator) into the single slave.

---

## 5. ClusterMaster Refactoring

### 5.1 Remove DDS Dependencies

Remove all of the following from `ClusterMaster`:
- `DdsWriter<SystemStateTopic> _systemStateWriter`
- `DdsReader<NodeHeartbeat> _heartbeatReader`
- `DdsReader<ClusterOpRequest> _sysOpRequestReader`
- `DdsWriter<ClusterOpStatus> _sysOpStatusWriter`
- `DdsReader<NodeOpStatus> _nodeOpStatusReader`
- `DdsWriter<AssetInventoryTopic> _inventoryWriter`
- `Dictionary<int, DdsWriter<NodeOpCommand>> _nodeOpWriterCache`

### 5.2 Event Bus Integration

**Ingress (consuming from bus instead of DDS polling):**

| Old (DDS polling) | New (EventBus Consume) |
|---|---|
| `_sysOpRequestReader.Take()` | `_eventBus.ConsumeManaged<TransitionStateIntent>()`, `_eventBus.ConsumeManaged<ManageEpisodeIntent>()`, `_eventBus.ConsumeManaged<SeekReplayIntent>()`, etc. |
| `_nodeOpStatusReader.Take()` | `_eventBus.ConsumeManaged<NodeOpCompletedEvent>()` |
| Heartbeat from `_heartbeatReader` | `_eventBus.ConsumeManaged<NodeHeartbeatEvent>()` |

**Egress (publishing to bus instead of DDS writing):**

| Old (direct DDS write) | New (EventBus Publish) |
|---|---|
| `_nodeOpWriterCache[id].Write(NodeOpCommand)` | `_eventBus.PublishManaged(new ExecuteNodeOpIntent {...})` |
| `_sysOpStatusWriter.Write(ClusterOpStatus)` | `_eventBus.PublishManaged(new ClusterOpCompletedEvent {...})` |
| `_systemStateWriter.Write(SystemStateTopic)` | `_eventBus.PublishManaged(new ClusterStateTransitionedEvent {...})` |

### 5.3 Remove JSON Parsing

Remove all `JsonDocument.Parse(...)` calls from `ClusterMaster`, `TransitionPlanner` (Hrot wrapper), **and all `IClusterStateHandler` implementations** (e.g., `ReferenceEpisodeLoadHandler`, `ReferenceScenarioLoadHandler`). The entire `FDP.Toolkit.Orchestration` domain — including handlers — will communicate using pure domain structs injected into `ExecuteNodeOpIntent.DomainPayload` and `NodeOpCompletedEvent.ResultPayload`.

- `ClusterMaster` reads `TransitionStateIntent.TargetState` directly as a `ClusterState` enum — no JSON.
- Handler implementations type-cast `DomainPayload`: `if (intent.DomainPayload is TransitionNodePayload p)` — no JSON.
- Result payloads from handlers are pure objects placed into `NodeOpCompletedEvent.ResultPayload` — no JSON.
- The `System.Text.Json` `using` directive must be **entirely absent** from the `FDP.Toolkit.Orchestration` project.

### 5.4 `IClusterStateHandler` Dispatch

The master dispatches node operations with a `NodeOpType` enum — no more `(int)` casts or magic number switch statements.

---

## 6. Network Translators (Application Layer)

All translators live in the `Hrot` application layer and are the sole classes allowed to reference both `Hrot.NED.Descriptors.Orchestration` and `FDP.Toolkit.Orchestration`. They implement the Anti-Corruption Layer (ACL) pattern.

### 6.1 JSON Payload DTOs

These classes live in the application infrastructure layer (e.g., `Hrot.Orchestrator` or `Hrot.Common.Orchestration`). They use nullable C# properties for JSON serialization convenience.

```csharp
// Deserialized from ClusterOpRequest.PayloadJson when OperationType == TransitionState
public class TransitionPayloadDto
{
    public Hrot.NED.Descriptors.Orchestration.ClusterState? TargetState { get; set; }
    public long? TargetWallTicks { get; set; }
    public string? ScenarioId { get; set; }
    public string? ExerciseId { get; set; }
    public string? TimeMode { get; set; }
}

public class ManageEpisodePayloadDto
{
    public string? Mode { get; set; }     // "Start" | "Stop"
    public string? EpisodeId { get; set; }
    public string? ScenarioId { get; set; }
}

public class ArchivePayloadDto
{
    public string? ExerciseId { get; set; }
}

public class SeekReplayPayloadDto
{
    public long? TargetWallTicks { get; set; }
}
```

Serialization uses `JsonStringEnumConverter` so that JSON payloads use human-readable enum names:
```json
{ "TargetState": "OperatingLive", "ScenarioId": "UrbanAmbush_01" }
```

> **Translators are the only classes in the codebase allowed to reference `System.Text.Json`.** The `NodeOpMasterTranslator` serializes `ExecuteNodeOpIntent.DomainPayload` objects into JSON strings when writing `NodeOpCommand` to DDS. The `NodeOpSlaveTranslator` deserializes DDS JSON strings into strongly-typed domain payload objects (based on `NodeOpType`) and places them into `ExecuteNodeOpIntent.DomainPayload` before publishing to the bus. The same pattern applies to `ResultPayload` in the reverse direction.

### 6.2 ClusterOpMasterTranslator

**Location:** `Hrot.Orchestrator`

- **Ingress:** Polls `ClusterOpRequest` DDS topic. Inspects `OperationType`. Deserializes `PayloadJson` into the correct typed DTO. Casts `Hrot.NED` enum to `FDP.Toolkit.Orchestration` enum. Publishes specific typed intent to `FdpEventBus` (e.g., `TransitionStateIntent`).
- **Egress:** Consumes `ClusterOpCompletedEvent` from `FdpEventBus`. Translates to `ClusterOpStatus` and writes to DDS.
- Validates mandatory fields (e.g., missing `TargetState`). On validation failure: write error `ClusterOpStatus` immediately, skip domain.

### 6.3 NodeOpMasterTranslator

**Location:** `Hrot.Orchestrator`

- **Egress (Command):** Consumes `ExecuteNodeOpIntent` from `FdpEventBus`. Casts `FDP.Toolkit.Orchestration.NodeOpType` to `Hrot.NED.Descriptors.Orchestration.NodeOpType`. **Serializes `DomainPayload` to JSON string** (based on `NodeOpType`, dispatches to the correct serializer). Writes `NodeOpCommand { ..., PayloadJson = serializedPayload }` to DDS for the target node.
- **Ingress (Result):** Polls `NodeOpStatus` DDS topic. **Deserializes `ResultJson` into a strongly-typed domain result object** (based on `NodeOpType`). Publishes `NodeOpCompletedEvent { ..., ResultPayload = domainResult }` to `FdpEventBus`.

### 6.4 NodeOpSlaveTranslator

**Location:** `Hrot.Common.Orchestration` (replaces `DdsOrchestrationTransport`)

- **Ingress (Command):** Polls `NodeOpCommand` DDS topic, filtered by own `NodeId`. Casts `Hrot.NED` enum to `FDP.Toolkit.Orchestration` enum. **Deserializes `PayloadJson` into a strongly-typed domain payload object** (based on `NodeOpType`, e.g., `NodeOpCommand.PayloadJson` → `TransitionNodePayload`). Publishes `ExecuteNodeOpIntent { ..., DomainPayload = domainPayload }` to `FdpEventBus`. Zero JSON strings cross into the domain.
- **Egress (Result):** Consumes `NodeOpCompletedEvent` from `FdpEventBus`. **Serializes `ResultPayload` to JSON string** for the DDS wire. Writes `NodeOpStatus { ..., ResultJson = serializedResult }` to DDS.
- Also handles heartbeat egress: consumes `NodeHeartbeatEvent` from bus and writes `NodeHeartbeat` to DDS.

### 6.5 EventDrivenStorageGateway

**Location:** `Hrot.Orchestrator` (or `Hrot.Common`)

The `EventDrivenStorageGateway` is the infrastructure adapter that fulfills `ExecuteStorageOpIntent` messages from the `FdpEventBus`. Without this adapter, storage operation intents dropped by `ClusterMaster` would have no consumer and storage operations would silently be lost.

This class:
- Accepts `FdpEventBus` and a `StorageGatewayModule` reference in its constructor.
- In its tick method:
  - Drains `ExecuteStorageOpIntent` from `FdpEventBus`. Based on `Operation`, dispatches the appropriate async gateway method (`ExportArchive`, `ImportArchive`, `SaveScenario`).
  - Owns all `CancellationTokenSource` instances for in-flight operations (moved here from `ClusterMaster._activeCancellations`).
  - Also drains `CancelOperationIntent` from `FdpEventBus` to cancel in-flight operations by `TargetRequestId`.
  - On async completion (success or failure), publishes `StorageOpCompletedEvent` to `FdpEventBus`.

**Architectural significance:** By moving file I/O, `.NET Task` management, and `CancellationTokenSource` ownership into this adapter, `ClusterMaster` becomes a fully synchronous, deterministically testable state machine that only handles `StorageOpCompletedEvent` to close the transaction.

---

## 7. Topology Support

### 7.1 Distributed Multi-Node (Standard)

```
[Orchestrator App]
  FdpEventBus ← ClusterOpMasterTranslator → DDS ClusterOpRequest/Status
  FdpEventBus ← NodeOpMasterTranslator   → DDS NodeOpCommand/Status
  FdpEventBus ← ClusterMaster

[SimHost App]
  FdpEventBus ← NodeOpSlaveTranslator → DDS NodeOpCommand/Status
  FdpEventBus ← ClusterSlave (with SimHost handlers)

[IG App]
  FdpEventBus ← NodeOpSlaveTranslator → DDS NodeOpCommand/Status
  FdpEventBus ← ClusterSlave (with IG handlers)
```

### 7.2 AllInOne (No Network / Local Dev / Tests)

```
[Single Process]
  Shared FdpEventBus
  ClusterMaster
  ClusterSlave (all handlers: SimHost + IG + Orchestrator)
  NO translators, NO DDS
```

The same domain logic runs. The 2PC happens entirely in memory. Zero-latency state transitions.

### 7.3 Test Harness

Tests push `TransitionStateIntent` (or other intents) directly to `FdpEventBus`. Assert `ClusterOpCompletedEvent` and `NodeOpCompletedEvent` results. No mocking of IOrchestrationTransport required.

---

## 8. Implementation Phases

### Phase 1 — FDP Domain Enums and Event DTOs
Define the pure FDP domain enums and all CQRS intent/event structs. No existing behaviour changes.

| Task | Description |
|------|-------------|
| CMC-S001 | Define `ClusterState`, `ClusterOpType`, `NodeOpType` enums in `FDP.Toolkit.Orchestration` |
| CMC-S002 | Define core CQRS event structs: `ClusterOpCompletedEvent`, `ExecuteNodeOpIntent`, `NodeOpCompletedEvent` (no generic `ExecuteClusterOpIntent`) |
| CMC-S003 | Define specific operation intents: `TransitionStateIntent`, `ManageEpisodeIntent`, `SeekReplayIntent`, `CancelOperationIntent`, `ExecuteStorageOpIntent`, `StorageOpCompletedEvent` |

### Phase 2 — IClusterStateHandler Enum Migration
Replace `int OperationId` with `NodeOpType` enum throughout the handler interface and all implementations.

| Task | Description |
|------|-------------|
| CMC-S004 | Update `IClusterStateHandler.CanHandle(int)` → `CanHandle(NodeOpType)` and update all handler implementations |
| CMC-S005 | Update `OrchestrationCommand` struct to use `NodeOpType` instead of `int` |

### Phase 3 — ClusterSlave Event Bus Integration
Replace `IOrchestrationTransport` polling with `FdpEventBus` consumption.

| Task | Description |
|------|-------------|
| CMC-S006 | Refactor `ClusterSlave` to consume `ExecuteNodeOpIntent` from `FdpEventBus` and publish `NodeOpCompletedEvent` |
| CMC-S007 | Delete `IOrchestrationTransport` and `DdsOrchestrationTransport` (replaced by `NodeOpSlaveTranslator`) |

### Phase 4 — ClusterMaster Event Bus Integration
Remove all DDS coupling from `ClusterMaster`.

| Task | Description |
|------|-------------|
| CMC-S008 | Remove DDS reader/writer fields from `ClusterMaster`. Inject `FdpEventBus`. Consume typed intents from bus. |
| CMC-S009 | `ClusterMaster` egress: replace DDS writes with `FdpEventBus.PublishManaged` calls |
| CMC-S010 | Remove all `JsonDocument.Parse` / JSON parsing from `ClusterMaster`, `TransitionPlanner` (Hrot wrapper), and handler dispatch code |

### Phase 5 — Application Layer Translators
Create the three stateless translator classes and the storage gateway adapter in the Hrot application layer.

| Task | Description |
|------|-------------|
| CMC-S011 | Define Hrot-layer JSON payload DTOs (`TransitionPayloadDto`, etc.) with `JsonStringEnumConverter` support |
| CMC-S012 | Implement `NodeOpSlaveTranslator` (replaces `DdsOrchestrationTransport`) |
| CMC-S013 | Implement `NodeOpMasterTranslator` |
| CMC-S014 | Implement `ClusterOpMasterTranslator` |
| CMC-S015 | Implement `EventDrivenStorageGateway` (async file I/O + cancellation adapter) |

### Phase 6 — Composition Root and Integration
Wire everything together and validate all topologies.

| Task | Description |
|------|-------------|
| CMC-S016 | Update composition roots: Orchestrator app, SimHost app, IG app, AllInOne |
| CMC-S017 | Integration tests: AllInOne 2PC end-to-end, distributed topology with mock DDS |
