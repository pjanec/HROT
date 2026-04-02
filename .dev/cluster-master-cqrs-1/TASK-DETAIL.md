# Task Details: ClusterMaster CQRS Decoupling

**Reference Design:** [DESIGN.md](./DESIGN.md)  
**Tracker:** [TASK-TRACKER.md](./TASK-TRACKER.md)

> Each task carries a unique ID in the format `CMC-Snnn`. The ID is used in commit messages and batch instructions.  
> Success conditions are phrased as unit/integration test requirements — they define what "done" means.

---

## Phase 1 — FDP Domain Enums and Event DTOs

### CMC-S001 — Domain Enums in FDP.Toolkit.Orchestration

**Reference:** [DESIGN.md §3.1](./DESIGN.md#31-domain-enums-dual-enum-pattern)

**What to build:**  
Create three new enum files inside `FDP/Toolkits/FDP.Toolkit.Orchestration/Enums/`:

- `ClusterState.cs` — mirrors `Hrot.NED.Descriptors.Orchestration.ClusterState`  
- `ClusterOpType.cs` — mirrors `Hrot.NED.Descriptors.Orchestration.ClusterOpType`  
- `NodeOpType.cs` — mirrors `Hrot.NED.Descriptors.Orchestration.NodeOpType`  

All enum integer values MUST be identical to the corresponding `Hrot.NED` enum values. No project in `FDP/Toolkits/` may reference `Hrot.NED`. No project in `Hrot/` is modified in this task.

**Success Conditions:**

1. Unit test `FdpOrchestraionEnumSyncTests.ClusterStateValuesMatchHrot`: casts every member of `FDP.Toolkit.Orchestration.ClusterState` to `int`, cast to `Hrot.NED.Descriptors.Orchestration.ClusterState`, and compares `.ToString()` — all must match.
2. Unit test `FdpOrchestraionEnumSyncTests.NodeOpTypeValuesMatchHrot`: same pattern for `NodeOpType`.
3. Unit test `FdpOrchestraionEnumSyncTests.ClusterOpTypeValuesMatchHrot`: same pattern for `ClusterOpType`.
4. `dotnet build FDP/FDP.sln` succeeds with no new errors.
5. No reference to `Hrot.NED` appears anywhere inside `FDP/Toolkits/FDP.Toolkit.Orchestration/`.

---

### CMC-S002 — Core CQRS Event Bus Structs

**Reference:** [DESIGN.md §3.2](./DESIGN.md#32-core-cqrs-event-bus-dtos)

**What to build:**  
Create `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterCqrsEvents.cs` containing the three core event structs:

- `ClusterOpCompletedEvent` — EventId 9011, DataPolicy.NoRecord  
- `ExecuteNodeOpIntent` — EventId 9012, DataPolicy.NoRecord  
- `NodeOpCompletedEvent` — EventId 9013, DataPolicy.NoRecord  

**There is no `ExecuteClusterOpIntent`.** High-level cluster operations are routed via operation-specific intent structs (CMC-S003). The `ExecuteNodeOpIntent` is the only generic intent and is used exclusively for the 2PC Node Ops fan-out loop.

`ExecuteNodeOpIntent` carries an `object? DomainPayload` field (NOT `string? PayloadJson`). The `ClusterMaster` places a strongly-typed payload struct (e.g., `TransitionNodePayload`) here. Handlers access it via type-casting: `if (intent.DomainPayload is TransitionNodePayload p)`. JSON serialization of this payload is exclusively the translator's responsibility.

Similarly, `NodeOpCompletedEvent` and `ClusterOpCompletedEvent` carry `object? ResultPayload` (NOT `string? ResultJson`). Result objects are pure domain types; translators serialize them to JSON when writing DDS messages.

All three structs are managed (may contain `object?` fields) and must be routed via `_eventBus.PublishManaged<T>()` / `_eventBus.ConsumeManaged<T>()`. Do **not** apply `unmanaged` constraints to these types. `System.Text.Json` must NOT be referenced anywhere in `FDP.Toolkit.Orchestration`.

**Success Conditions:**

1. Unit test: all three structs are annotated with `[DataPolicy(DataPolicy.NoRecord)]` (reflection check).
2. Unit test: all three structs have unique `[EventId(...)]` values with no collision with existing event IDs in the codebase.
3. `FdpEventBus.PublishManaged<ExecuteNodeOpIntent>(...)` and `FdpEventBus.ConsumeManaged<ExecuteNodeOpIntent>()` compile and execute in a test without exception.
4. The structs are `public` and reside in the `FDP.Toolkit.Orchestration` namespace.
5. No struct named `ExecuteClusterOpIntent` exists anywhere in the `FDP.Toolkit.Orchestration` namespace.
6. `ExecuteNodeOpIntent` has field `object? DomainPayload` — no field named `PayloadJson`.
7. `NodeOpCompletedEvent` has field `object? ResultPayload` — no field named `ResultJson`.
8. `ClusterOpCompletedEvent` has field `object? ResultPayload` — no field named `ResultJson`.
9. `grep -r "System.Text.Json" FDP/Toolkits/FDP.Toolkit.Orchestration/` returns zero results.

---

### CMC-S003 — Specific Operation Payload Intent Structs

**Reference:** [DESIGN.md §3.3](./DESIGN.md#33-specific-operation-payload-intents)

**What to build:**  
Create `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterOpIntents.cs` containing:

- `TransitionStateIntent` — EventId 9050  
- `ManageEpisodeIntent` — EventId 9051  
- `SeekReplayIntent` — EventId 9052  
- `CancelOperationIntent` — EventId 9053  
- `StorageOpType` enum  
- `ExecuteStorageOpIntent` — EventId 9054  
- `StorageOpCompletedEvent` — EventId 9055  
- `TakeCheckpointIntent` — EventId 9056 (no payload fields beyond `RequestId`)  
- `LoadZoneIntent` — EventId 9057  

All structs: `[DataPolicy(DataPolicy.NoRecord)]`, managed (may contain `string?`), reside in `FDP.Toolkit.Orchestration` namespace, no `Hrot.NED` references.

**Success Conditions:**

1. Unit test: all nine types have `[DataPolicy(DataPolicy.NoRecord)]`.
2. Unit test: `TransitionStateIntent` has field `ClusterState TargetState` (using FDP enum, not Hrot enum).
3. Unit test: `ManageEpisodeIntent` has `bool IsStart`, `Guid EpisodeId`, `string? ScenarioId`.
4. Unit test: `TakeCheckpointIntent` has only `Guid RequestId` (no other fields).
5. Unit test: `LoadZoneIntent` has `Guid RequestId` and `string? ZoneId`.
6. Unit test: EventId values 9050–9057 are not colliding with existing registered event IDs.
7. `dotnet build FDP/FDP.sln` succeeds.

---

## Phase 2 — IClusterStateHandler Enum Migration

### CMC-S004 — IClusterStateHandler.CanHandle → NodeOpType

**Reference:** [DESIGN.md §3.4](./DESIGN.md#34-iclusterstatehandler-enum-migration)

**What to build:**  
Change `IClusterStateHandler.CanHandle(int operationId)` to `CanHandle(NodeOpType operation)`.  
Update all known implementations:

| Handler | File |
|---------|------|
| `ReferenceLiveLoadHandler` | `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/` |
| `ReferenceReplayLoadHandler` | same |
| `ReferenceScenarioLoadHandler` | same |
| `ReferenceEditLoadHandler` | same |
| `ReferenceEpisodeLoadHandler` | same |
| `ReferencePrefetchHandler` | same |
| `ReferenceArchiveHandler` | same |
| `ReferenceCheckpointHandler` | same |
| `ReferencePreviewHandler` | same |
| `IgZoneDummyHandler` | `Hrot.IG/Modules/Orchestration/` |
| `HrotHandlerAdapter` | `Hrot.Common/Orchestration/` |
| `GlobalContextClusterOpHandler` | `Hrot.Orchestrator/` |

Also update `ClusterSlave` dispatch logic to call `handler.CanHandle(intent.Operation)` where `intent` is the `ExecuteNodeOpIntent` consumed from the bus (not an `OrchestrationCommand` field).

**Success Conditions:**

1. No implicit `(int)` cast to `NodeOpType` inside handler `CanHandle` implementations — each explicitly matches enum values.
2. `dotnet build IOS-IG-SimHost.sln` succeeds with no new errors.
3. All handler `CanHandle` methods use `switch` on `NodeOpType` enum values or explicit enum comparisons (no magic integer literals).

---

### CMC-S005 — Delete OrchestrationCommand and OrchestrationStatus; Update IClusterStateHandler

**Reference:** [DESIGN.md §3.4](./DESIGN.md#34-iclusterstatehandler-eradicate-orchestrationcommand)

**What to build:**

**Part A — Delete legacy structs:**
- Delete `FDP/Toolkits/FDP.Toolkit.Orchestration/OrchestrationCommand.cs`.
- Delete `FDP/Toolkits/FDP.Toolkit.Orchestration/OrchestrationStatus.cs`.
- Remove all usages: `ClusterSlave.DispatchCommand` (no longer maps to `OrchestrationCommand`), `DdsOrchestrationTransport` (being deleted in CMC-S007 anyway), `HrotHandlerAdapter`, any test helpers.

**Part B — Update `IClusterStateHandler`:**  
Change all three method signatures from accepting `OrchestrationCommand` to accepting `ExecuteNodeOpIntent`:

```csharp
public interface IClusterStateHandler
{
    bool CanHandle(NodeOpType operation);
    Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct);
    void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo);
    void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo);
}
```

**Part C — Update all handler implementations:**  
Update every `IClusterStateHandler` implementation to match the new signature. Inside each `PrepareAsync` / `Commit` / `Abort`, replace `cmd.PayloadJson` JSON parsing with type-casting `intent.DomainPayload`:

```csharp
// Before:
var doc = JsonDocument.Parse(cmd.PayloadJson);
var scenarioId = doc.RootElement.GetProperty("ScenarioId").GetString();

// After:
if (intent.DomainPayload is TransitionNodePayload p)
    await LoadScenarioAsync(p.ScenarioId, ct);
```

Change all `PrepareAsync` return values from `string?` (error message) to `object?` (typed result payload or `null`).

**Part D — Update `ClusterSlave` dispatch:**  
`ClusterSlave` passes the `ExecuteNodeOpIntent` directly to handlers. The result of `PrepareAsync` is placed directly into `NodeOpCompletedEvent.ResultPayload`.

**Success Conditions:**

1. `OrchestrationCommand` class does not exist anywhere in the solution.
2. `OrchestrationStatus` class does not exist anywhere in the solution.
3. `IClusterStateHandler.PrepareAsync` signature is `Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)`.
4. No handler implementation contains `cmd.PayloadJson`, `JsonDocument`, or `System.Text.Json` — all payload access is via `intent.DomainPayload` type-casting.
5. Unit test: construct a handler, call `PrepareAsync(new ExecuteNodeOpIntent { Operation = NodeOpType.PrepareLive, DomainPayload = new TransitionNodePayload { TargetState = ClusterState.OperatingLive } }, ct)` — assert it executes without exception and returns `null` or a typed result.
6. `dotnet build IOS-IG-SimHost.sln` succeeds with 0 errors.

---

## Phase 3 — ClusterSlave Event Bus Integration

### CMC-S006 — ClusterSlave Reads from FdpEventBus

**Reference:** [DESIGN.md §4](./DESIGN.md#4-clusterslave-refactoring)

**What to build:**  
Refactor `FDP/Toolkits/FDP.Toolkit.Orchestration/ClusterSlave.cs`:

1. Add constructor parameter `FdpEventBus eventBus` (already present on some paths, verify).
2. In `Tick()`: after existing heartbeat logic, call `_eventBus.ConsumeManaged<ExecuteNodeOpIntent>()` in a loop; for each consumed intent, dispatch it directly to handler — **no mapping to `OrchestrationCommand`**.
3. Replace `_transport.TryDequeueCommand(out var cmd)` loop with the above event-bus consumption.
4. Dispatch to handlers using `handler.CanHandle(intent.Operation)` and passing `intent` directly to `PrepareAsync(intent, ct)` / `Commit(intent, repo)` / `Abort(intent, repo)`.
5. Obtain the `object?` result from `PrepareAsync` and place it into `NodeOpCompletedEvent.ResultPayload`. Publish the event via `_eventBus.PublishManaged(...)`.
6. Heartbeat publication: publish a new `NodeHeartbeatEvent` struct to the event bus (define in CMC-S002 or add here), rather than calling `_transport.PublishHeartbeat(...)`.
7. Keep `IOrchestrationTransport` plumbing for now (do NOT delete yet — that is CMC-S007) but make it optional/nullable and no longer the primary path.

**Success Conditions:**

1. Unit test: `ClusterSlave` constructed with `FdpEventBus` and NO transport. Publish `ExecuteNodeOpIntent { Operation = NodeOpType.PrepareLive, DomainPayload = new TransitionNodePayload { ... } }` to the bus. Assert that the registered `IClusterStateHandler.PrepareAsync` is called with that exact intent.
2. Unit test: After handler `PrepareAsync` returns a typed result (e.g., `new MaxNetworkIdResult(42)`), `NodeOpCompletedEvent.ResultPayload` on the bus equals that result instance.
3. Unit test: `ClusterSlave.Tick()` does NOT throw when `_transport` is null.
4. No reference to `OrchestrationCommand` or `OrchestrationStatus` anywhere in `ClusterSlave.cs`.
5. Existing `ClusterSlave` integration tests still pass (test setup updated to push `ExecuteNodeOpIntent` with `DomainPayload` objects — changes to test construction are acceptable).

---

### CMC-S007 — Delete IOrchestrationTransport and DdsOrchestrationTransport

**Reference:** [DESIGN.md §4.1](./DESIGN.md#41-remove-iorchestatransport)

**What to build:**  
After CMC-S006 is complete and no code path uses `IOrchestrationTransport` as the primary routing mechanism:

1. Remove the `_transport` field from `ClusterSlave`.
2. Remove the `IOrchestrationTransport transport` constructor parameter from `ClusterSlave`.
3. Delete `FDP/Toolkits/FDP.Toolkit.Orchestration/IOrchestrationTransport.cs`.
4. Delete `Hrot.Common/Orchestration/DdsOrchestrationTransport.cs`.
5. Remove `HrotHandlerAdapter.StatusWriter` property that exposed `DdsWriter<NodeOpStatus>` to legacy handlers (legacy handlers should be updated to use event bus by this point).
6. Update all composition roots that previously injected `DdsOrchestrationTransport` into `ClusterSlave`.

**Success Conditions:**

1. `IOrchestrationTransport` is referenced nowhere in the solution.
2. `DdsOrchestrationTransport` class does not exist.
3. `dotnet build IOS-IG-SimHost.sln` succeeds with 0 errors.
4. All existing orchestration integration tests pass.

---

## Phase 4 — ClusterMaster Event Bus Integration

### CMC-S008 — Remove DDS from ClusterMaster (Ingress)

**Reference:** [DESIGN.md §5](./DESIGN.md#5-clustermaster-refactoring)

**What to build:**  
Refactor `Hrot.Orchestrator/ClusterMaster.cs`:

1. Remove constructor injection / instantiation of: `_sysOpRequestReader`, `_nodeOpStatusReader`, `_heartbeatReader`.
2. Add `FdpEventBus _eventBus` field (inject via constructor).
3. In `Tick()`: replace `_sysOpRequestReader.Take()` loop with `_eventBus.ConsumeManaged<TransitionStateIntent>()`, `_eventBus.ConsumeManaged<ManageEpisodeIntent>()`, `_eventBus.ConsumeManaged<SeekReplayIntent>()`, `_eventBus.ConsumeManaged<CancelOperationIntent>()`, `_eventBus.ConsumeManaged<ExecuteStorageOpIntent>()`.
4. Replace `_nodeOpStatusReader.Take()` loop with `_eventBus.ConsumeManaged<NodeOpCompletedEvent>()`.
5. Replace heartbeat reader with `_eventBus.ConsumeManaged<NodeHeartbeatEvent>()`.
6. Remove `_injectedRequests ConcurrentQueue<ClusterOpRequest>` (test injection now goes through the bus directly).

**Success Conditions:**

1. `ClusterMaster` constructor no longer accepts any `DdsReader<T>` parameters.
2. No `using CycloneDDS` or `using Hrot.NED` in `ClusterMaster.cs` (all DDS types removed from this file).
3. Unit test: construct `ClusterMaster` with only `FdpEventBus`. Publish `TransitionStateIntent` to the bus. Assert that a `DistributedTransaction` is created (inspect via accessible state or next published intent).
4. `dotnet build IOS-IG-SimHost.sln` succeeds.

---

### CMC-S009 — Remove DDS from ClusterMaster (Egress)

**Reference:** [DESIGN.md §5.2](./DESIGN.md#52-event-bus-integration)

**What to build:**  
Continue `ClusterMaster.cs` refactoring (egress side):

1. Remove `_sysOpStatusWriter`, `_systemStateWriter`, `_inventoryWriter`, `_nodeOpWriterCache`.
2. Replace all `_sysOpStatusWriter.Write(...)` calls with `_eventBus.PublishManaged(new ClusterOpCompletedEvent {...})`.
3. Replace all `_nodeOpWriterCache[id].Write(NodeOpCommand {...})` calls with `_eventBus.PublishManaged(new ExecuteNodeOpIntent {...})`.
4. Replace `_systemStateWriter.Write(SystemStateTopic {...})` with `_eventBus.PublishManaged(new ClusterStateTransitionedEvent {...})` (define this struct if not yet added in CMC-S002).
5. Remove `_inventoryWriter` and move inventory publishing to a dedicated `AssetInventoryTranslator` in the application layer (or keep as a separate concern — see DESIGN.md).

**Success Conditions:**

1. `ClusterMaster.cs` has zero references to `DdsWriter<T>`.
2. Unit test: after `ClusterMaster` processes a `TransitionStateIntent`, an `ExecuteNodeOpIntent` appears on the bus.
3. Unit test: after all `NodeOpCompletedEvent`s are received, a `ClusterOpCompletedEvent` appears on the bus.
4. `dotnet build IOS-IG-SimHost.sln` succeeds.

---

### CMC-S010 — Remove JSON Parsing from ClusterMaster and Handlers

**Reference:** [DESIGN.md §5.3](./DESIGN.md#53-remove-json-parsing)

**What to build:**  
Delete all `JsonDocument.Parse`, `doc.RootElement.TryGetProperty(...)`, and manual string `.GetString()` / `.GetInt32()` calls from the **entire** `FDP.Toolkit.Orchestration` domain:

- `Hrot.Orchestrator/ClusterMaster.cs` — `ProcessSingleClusterOpRequest` and related helpers
- `Hrot.Orchestrator/TransitionPlanner.cs` — (Hrot wrapper) — any JSON extraction for `TargetState`, `TargetWallTicks`, `TimeMode`, etc.
- **All `IClusterStateHandler` implementations** that currently parse `cmd.PayloadJson` — e.g., `ReferenceEpisodeLoadHandler`, `ReferenceScenarioLoadHandler`, `ReferenceArchiveHandler`, and any other handler that calls `JsonDocument.Parse` or accesses a JSON string.

The data previously extracted from JSON is now carried in `ExecuteNodeOpIntent.DomainPayload` as a pure typed struct. Handlers access it via safe type-casting:

```csharp
// Before (brittle stringly-typed):
var doc = JsonDocument.Parse(cmd.PayloadJson);
var scenarioId = doc.RootElement.GetProperty("ScenarioId").GetString();

// After (type-safe domain payload):
if (intent.DomainPayload is TransitionNodePayload p)
    LoadScenario(p.ScenarioId);
```

Result objects returned by handlers are placed into `NodeOpCompletedEvent.ResultPayload` as pure domain objects (e.g., `FileManifestResult`), not JSON strings.

**Success Conditions:**

1. `grep -r "JsonDocument\|PayloadJson\|TryGetProperty\|System.Text.Json" FDP/Toolkits/FDP.Toolkit.Orchestration/` returns zero results.
2. `grep -r "JsonDocument\|PayloadJson\|TryGetProperty" Hrot.Orchestrator/ClusterMaster.cs` returns zero results.
3. `grep -r "JsonDocument\|PayloadJson\|TryGetProperty" Hrot.Orchestrator/TransitionPlanner.cs` returns zero results.
4. All `IClusterStateHandler` implementations pass `DomainPayload` by type-casting — no `string` field access for payload data.
5. All existing cluster integration tests pass (test setup updated to push typed intents with `DomainPayload` objects instead of raw `ClusterOpRequest` with JSON payloads).
6. `dotnet build IOS-IG-SimHost.sln` succeeds.

---

## Phase 5 — Application Layer Translators

### CMC-S011 — Hrot JSON Payload DTOs

**Reference:** [DESIGN.md §6.1](./DESIGN.md#61-json-payload-dtos)

**What to build:**  
Create `Hrot.Orchestrator/Translators/Payloads/OrchestrationPayloadDtos.cs` (or equivalent location) containing:

- `TransitionPayloadDto` — nullable properties; `TargetState` is `Hrot.NED.Descriptors.Orchestration.ClusterState?`
- `ManageEpisodePayloadDto` — `Mode?`, `EpisodeId?`, `ScenarioId?`
- `ArchivePayloadDto` — `ExerciseId?`
- `SeekReplayPayloadDto` — `TargetWallTicks?`

All DTOs configured for `System.Text.Json` with `JsonStringEnumConverter` so that `"OperatingLive"` maps to the correct enum member. Use `JsonIgnoreCondition.WhenWritingNull` so absent fields do not appear in serialized output.

**Success Conditions:**

1. Unit test: `JsonSerializer.Deserialize<TransitionPayloadDto>(@"{""TargetState"": ""OperatingLive"", ""ScenarioId"": ""Test""}", opts)` returns `TargetState == ClusterState.OperatingLive` and `ScenarioId == "Test"`.
2. Unit test: `JsonSerializer.Deserialize<TransitionPayloadDto>(@"{""TargetState"": 31}", opts)` returns `null` or throws `JsonException` (integer values NOT accepted — only strings).
3. Unit test: serializing a `TransitionPayloadDto` with only `TargetState` set produces JSON containing only `"TargetState"` (no null fields).
4. Unit test: unknown enum string `"OperatingReplay_V2"` causes a graceful `JsonException` (not silently mapped to 0).

---

### CMC-S012 — NodeOpSlaveTranslator

**Reference:** [DESIGN.md §6.4](./DESIGN.md#64-nodeopslavetranslator)

**What to build:**  
Create `Hrot.Common/Orchestration/NodeOpSlaveTranslator.cs`.

This class:
- Accepts a `DdsReader<NodeOpCommand>`, `DdsWriter<NodeOpStatus>`, `DdsWriter<NodeHeartbeat>`, `FdpEventBus`, and `int nodeId` in its constructor.
- In its tick method (`PollIngress` / `Tick`):
  - Drains `NodeOpCommand` DDS reader, filtering `TargetNodeId == nodeId`.
  - Translates `Hrot.NED.Descriptors.Orchestration.NodeOpType` → `FDP.Toolkit.Orchestration.NodeOpType` via integer cast.
  - **Deserializes `NodeOpCommand.PayloadJson` into a strongly-typed domain payload object** based on the `NodeOpType` (e.g., `PrepareLive` → `TransitionNodePayload`, `StartEpisode` → `EpisodeNodePayload`). Sets `ExecuteNodeOpIntent.DomainPayload` to this object. If the operation has no payload, `DomainPayload` is `null`.
  - Publishes `ExecuteNodeOpIntent { ..., DomainPayload = domainPayload }` to `FdpEventBus`. **No JSON string crosses into the domain.**
  - Drains `NodeOpCompletedEvent` from `FdpEventBus`. **Serializes `ResultPayload` to a JSON string** (or empty string if null). Writes `NodeOpStatus { ..., ResultJson = serialized }` to DDS writer.
  - Drains `NodeHeartbeatEvent` from `FdpEventBus`, writes `NodeHeartbeat` to DDS.

**Success Conditions:**

1. Unit test: push a mock `NodeOpCommand { TargetNodeId = 1, Operation = Hrot.NED.NodeOpType.PrepareLive, PayloadJson = "{\"TargetState\": \"OperatingLive\"}" }` to a fake DDS reader. Call `Tick()`. Assert `ExecuteNodeOpIntent { Operation = FDP.Toolkit.Orchestration.NodeOpType.PrepareLive, DomainPayload is TransitionNodePayload { TargetState = ClusterState.OperatingLive } }` is on the `FdpEventBus`.
2. Unit test: push `NodeOpCompletedEvent { TransactionId = X, NodeId = 1, StatusCode = 0, ResultPayload = new FileManifestResult(...) }` to the `FdpEventBus`. Call `Tick()`. Assert `NodeOpStatus { TransactionId = X, NodeId = 1, ResultJson contains serialized manifest }` was written to the DDS writer.
3. Unit test: `NodeOpCompletedEvent` with `ResultPayload = null` produces `NodeOpStatus` with empty `ResultJson`.
4. Unit test: commands for `TargetNodeId != nodeId` are NOT published to the bus (filtered correctly).
5. Class has no reference to `ClusterMaster` or `ClusterSlave`.
6. `grep "System.Text.Json" Hrot.Common/Orchestration/NodeOpSlaveTranslator.cs` returns results — this class IS allowed to use it (translator is ACL).

---

### CMC-S013 — NodeOpMasterTranslator

**Reference:** [DESIGN.md §6.3](./DESIGN.md#63-nopopmmastertranslator)

**What to build:**  
Create `Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs`.

This class:
- Accepts a per-node `DdsWriter<NodeOpCommand>` factory or writer cache, `DdsReader<NodeOpStatus>`, and `FdpEventBus`.
- **Egress (Command):** Drains `ExecuteNodeOpIntent` from `FdpEventBus`. Casts `FDP.Toolkit.Orchestration.NodeOpType` to `Hrot.NED.Descriptors.Orchestration.NodeOpType`. **Serializes `DomainPayload` to a JSON string** based on `NodeOpType` (using `JsonSerializer` with `JsonStringEnumConverter`). Writes `NodeOpCommand { ..., PayloadJson = serialized }` to DDS for the target node. If `DomainPayload` is null, writes empty `PayloadJson`.
- **Ingress (Result):** Drains `NodeOpStatus` DDS reader. **Deserializes `ResultJson` into a strongly-typed domain result object** based on `NodeOpType` (e.g., `PrepareReplay` result → `MaxNetworkIdResult`). Publishes `NodeOpCompletedEvent { ..., ResultPayload = domainResult }` to `FdpEventBus`.

**Success Conditions:**

1. Unit test: publish `ExecuteNodeOpIntent { TargetNodeId = 2, Operation = NodeOpType.PrepareLive, TransactionId = X, DomainPayload = new TransitionNodePayload { TargetState = ClusterState.OperatingLive } }` to the bus. Assert `NodeOpCommand { TargetNodeId = 2, Operation = Hrot.NED.NodeOpType.PrepareLive, PayloadJson = "{\"TargetState\": \"OperatingLive\"}" }` was written to the DDS writer for node 2.
2. Unit test: `ExecuteNodeOpIntent` with `DomainPayload = null` produces `NodeOpCommand` with empty `PayloadJson`.
3. Unit test: `NodeOpStatus { TransactionId = Y, NodeId = 3, ResultJson = "{\"MaxNetworkId\": 42}" }` from DDS reader → `NodeOpCompletedEvent { TransactionId = Y, NodeId = 3, ResultPayload is MaxNetworkIdResult { MaxNetworkId = 42 } }` on bus.
4. Class has no reference to `ClusterMaster` internals (pure I/O adapter).
5. `grep "System.Text.Json" Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs` returns results — this class IS allowed to use it (translator is ACL).

---

### CMC-S014 — ClusterOpMasterTranslator

**Reference:** [DESIGN.md §6.2](./DESIGN.md#62-clusteropmastertranslator)

**What to build:**  
Create `Hrot.Orchestrator/Translators/ClusterOpMasterTranslator.cs`.

This class:
- Accepts `DdsReader<ClusterOpRequest>`, `DdsWriter<ClusterOpStatus>`, `FdpEventBus`, and `JsonSerializerOptions` (with `JsonStringEnumConverter`).
- **Ingress:** Drains `ClusterOpRequest`. Based on `OperationType`, deserializes `PayloadJson` into the correct DTO, validates mandatory fields, casts Hrot enums to FDP enums, publishes typed intent to bus. On validation failure, writes error `ClusterOpStatus` immeidately.
- **Egress:** Drains `ClusterOpCompletedEvent` from bus. Writes `ClusterOpStatus` to DDS.

Supported operation types to handle in ingress:
- `TransitionState` → `TransitionStateIntent`
- `ManageEpisode` → `ManageEpisodeIntent`
- `ReplaySeek` → `SeekReplayIntent`
- `CancelOperation` → `CancelOperationIntent`
- `ExportArchive`, `ImportArchive`, `SaveScenario` → `ExecuteStorageOpIntent`
- `TakeCheckpoint` → `TakeCheckpointIntent`
- `LoadZone` → `LoadZoneIntent`
- `PauseTime`, `ResumeTime`, `StepTime`, `SetTimeScale` → time-control intents (unchanged from current)

**Success Conditions:**

1. Unit test: `ClusterOpRequest { OperationType = TransitionState, PayloadJson = "{\"TargetState\": \"OperatingLive\"}" }` from DDS reader → `TransitionStateIntent { TargetState = FDP.ClusterState.OperatingLive }` on bus.
2. Unit test: `ClusterOpRequest { OperationType = TransitionState, PayloadJson = "{}" }` (missing TargetState) → `ClusterOpStatus { StatusCode = ValidationFailed }` written to DDS writer. Nothing published to bus.
3. Unit test: `ClusterOpCompletedEvent { RequestId = X, StatusCode = 0 }` on bus → `ClusterOpStatus { RequestId = X, StatusCode = 0 }` written to DDS writer.
4. Integration test: full round-trip — push `ClusterOpRequest` through translator → `ClusterMaster` (event-bus only, no DDS) processes it → `ClusterOpCompletedEvent` on bus → translator writes `ClusterOpStatus`.

---

### CMC-S015 — EventDrivenStorageGateway

**Reference:** [DESIGN.md §6.5](./DESIGN.md#65-eventdrivenstoragegateway)

**What to build:**  
Create `Hrot.Orchestrator/EventDrivenStorageGateway.cs` (or `Hrot.Common/Orchestration/EventDrivenStorageGateway.cs`).

This class:
- Accepts `FdpEventBus` and `StorageGatewayModule` in its constructor.
- In its tick method:
  - Drains `ExecuteStorageOpIntent` from `FdpEventBus`. Based on `StorageOpType`, dispatches to the appropriate `StorageGatewayModule` async method.
  - Owns `Dictionary<Guid, CancellationTokenSource>` for in-flight operations (moved from `ClusterMaster._activeCancellations`).
  - Drains `CancelOperationIntent` from `FdpEventBus`; cancels the matching in-flight operation by `TargetRequestId`.
  - On async task completion (success or failure), publishes `StorageOpCompletedEvent` to `FdpEventBus`.

**Success Conditions:**

1. Unit test: publish `ExecuteStorageOpIntent { Operation = Export, ExerciseId = "X" }` to bus. Assert that `StorageGatewayModule.ExportArchive` was called with `ExerciseId = "X"`.
2. Unit test: when export completes, `StorageOpCompletedEvent { RequestId, StatusCode = Success }` is published to bus.
3. Unit test: publish `CancelOperationIntent { TargetRequestId = Y }` while export is in-flight. Assert that the `CancellationToken` for Y was cancelled.
4. Unit test: `ClusterMaster` has no `CancellationTokenSource` fields — ownership is fully in `EventDrivenStorageGateway`.
5. `dotnet build IOS-IG-SimHost.sln` succeeds.

---

## Phase 6 — Composition Root and Integration

### CMC-S016 — Update Composition Roots

**Reference:** [DESIGN.md §7](./DESIGN.md#7-topology-support)

**What to build:**  
Update the application startup / composition roots for all process topologies:

**Distributed Orchestrator process (`Hrot.Orchestrator`):**
- Instantiate `ClusterOpMasterTranslator`, `NodeOpMasterTranslator`.
- Instantiate `ClusterMaster` with `FdpEventBus` only.
- Register translators with the simulation/tick loop.

**Distributed SimHost process (`Hrot.SimHost`):**
- Instantiate `NodeOpSlaveTranslator`.
- Instantiate `ClusterSlave` with `FdpEventBus` and SimHost-specific handlers only.

**Distributed IG process (`Hrot.IG` / `Hrot.CGF`):**
- Instantiate `NodeOpSlaveTranslator`.
- Instantiate `ClusterSlave` with `FdpEventBus` and IG-specific handlers.

**AllInOne process (`Hrot.ClusterRunner` or similar):**
- Single `FdpEventBus`.
- NO translators.
- `ClusterMaster` + single `ClusterSlave` with all handlers (SimHost + IG + Orchestrator context).

**Success Conditions:**

1. `build_all_standalone.bat` (or `dotnet build IOS-IG-SimHost.sln`) succeeds with 0 errors.
2. Manual smoke test: start AllInOne mode, trigger a `TransitionState` to `OperatingLive` — system transitions correctly without DDS.
3. Existing integration tests in `Hrot.ClusterRunner.Integration.Tests` and `Hrot.Orchestrator.Integration.Tests` pass.

---

### CMC-S017 — Integration Tests for CQRS Orchestration

**Reference:** [DESIGN.md §7](./DESIGN.md#7-topology-support)

**What to build:**  
Add integration tests covering the full CQRS 2PC scenario in both topologies:

**AllInOne (no-network) integration test:**
- Construct `ClusterMaster` + `ClusterSlave` (with mock handlers) on a shared `FdpEventBus`.
- Publish `TransitionStateIntent { TargetState = OperatingLive }` to bus.
- Tick both master and slave until `ClusterOpCompletedEvent` is on the bus.
- Assert `ClusterOpCompletedEvent.StatusCode == OrchestrationStatusCode.Success`.

**Translator round-trip integration test:**
- Use fake/in-memory DDS readers/writers (or existing test harness `HrotRunnerHarness`).
- Construct `ClusterOpMasterTranslator`, `NodeOpMasterTranslator`, `NodeOpSlaveTranslator`, `ClusterMaster`, `ClusterSlave` all wired to a shared `FdpEventBus`.
- Route DDS messages between translators in-process via fake reader/writer pairs.
- Push `ClusterOpRequest { TransitionState, TargetState = OperatingLive }` to the fake master DDS reader.
- Tick all components.
- Assert `ClusterOpStatus { Success }` appears in the fake DDS writer.

**Success Conditions:**

1. AllInOne test passes with `ClusterOpCompletedEvent.StatusCode == 0 (Success)` after correct tick sequence.
2. Translator round-trip test passes end-to-end.
3. Echo-chamber regression test: verify that a slave's `NodeOpCompletedEvent` does NOT cause the slave translator to re-publish an `ExecuteNodeOpIntent` (no infinite loop).
4. Test coverage for validation failure path: malformed `TransitionPayloadDto` → `ClusterOpStatus.StatusCode == ValidationFailed`.
