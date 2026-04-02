# BATCH-05: Application Layer Translators (ACL)

**Batch Number:** BATCH-05  
**Tasks:** CMC-S011, CMC-S012, CMC-S013, CMC-S014, CMC-S015  
**Phase:** Phase 5 — Anti-Corruption Layer Translators  
**Estimated Effort:** 12–16 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-04 complete (ClusterMaster and ClusterSlave both bus-based)

---

## 📋 Context

After Phases 1–4, the domain layer (`FDP.Toolkit.Orchestration`, `Hrot.Orchestrator`, `Hrot.Common`) communicates internally via typed `FdpEventBus` events with no JSON or DDS. However, the system still must bridge to DDS for external communication (other nodes, existing protocol).

Phase 5 adds the **Anti-Corruption Layer (ACL)**: a set of stateless translator classes that live at the Hrot application layer boundary and perform:
- DDS → bus (ingress): deserialize `PayloadJson` → typed `DomainPayload` objects; publish typed events to bus
- Bus → DDS (egress): serialize typed `DomainPayload` → `PayloadJson` strings; write DDS topics

**The rule is strict:** `System.Text.Json` is allowed ONLY in:
- `Hrot.Orchestrator/Translators/` and  
- `Hrot.Common/Orchestration/NodeOpSlaveTranslator.cs`  
Nowhere else.

### Existing domain payload types (defined in FDP handlers — already there)
| Handler payload type | File |
|---|---|
| `ArchiveHandlerPayload(string? ExerciseId)` | `ReferenceArchiveHandler.cs` |
| `EditLoadHandlerPayload(string? ScenarioId, bool IsNewScenario, int TargetState)` | `ReferenceEditLoadHandler.cs` |
| `EpisodeHandlerPayload(Guid EpisodeId, string? ScenarioId, bool IsStart)` | `ReferenceEpisodeLoadHandler.cs` |
| `PrefetchHandlerPayload(string? ScenarioId)` | `ReferencePrefetchHandler.cs` |

### DDS types (Hrot.NED) — translators bridge to/from these
- `ClusterOpRequest { Guid RequestId, ClusterOpType OperationType, string PayloadJson }`
- `ClusterOpStatus { Guid RequestId, int StatusCode, string ResultJson }`
- `NodeOpCommand { int TargetNodeId, Guid TransactionId, NodeOpType Operation, string PayloadJson }`
- `NodeOpStatus { Guid TransactionId, int NodeId, int StatusCode, bool IsParticipating, string ResultJson }`
- `NodeHeartbeat { int NodeId, string SubsystemName, ClusterState LocalClusterState, long WallTicksUtc, ... }`

### Build & Test Commands

```powershell
# From d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln -v q
dotnet test Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj
dotnet test Hrot.Orchestrator.Integration.Tests/Hrot.Orchestrator.Integration.Tests.csproj
dotnet test Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj
dotnet test Hrot.SimHost.Integration.Tests/Hrot.SimHost.Integration.Tests.csproj
```

### Report

`.dev/cluster-master-cqrs-1/reports/BATCH-05-REPORT.md`

---

## ✅ Task 1: CMC-S011 — JSON Payload DTOs

**Location:** `Hrot.Orchestrator/Translators/Payloads/OrchestrationPayloadDtos.cs`

Define JSON-serializable payload DTOs for every operation intent that carries data. These DTOs are the wire shapes in `PayloadJson`.

```csharp
using System.Text.Json.Serialization;
// must have this using for JsonStringEnumConverter

// JSON serializer options required for all DTOs:
// new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() },
//                             DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }
// Use enum STRINGS (e.g. "OperatingLive"), NOT integers.

public record TransitionPayloadDto(
    [property: JsonPropertyName("TargetState")]   ClusterState?  TargetState,
    [property: JsonPropertyName("ScenarioId")]    string?        ScenarioId,
    [property: JsonPropertyName("ExerciseId")]    string?        ExerciseId,
    [property: JsonPropertyName("TimeMode")]      string?        TimeMode
);

public record ManageEpisodePayloadDto(
    [property: JsonPropertyName("IsStart")]       bool           IsStart,
    [property: JsonPropertyName("EpisodeId")]     Guid?          EpisodeId,
    [property: JsonPropertyName("ScenarioId")]    string?        ScenarioId
);

public record ArchivePayloadDto(
    [property: JsonPropertyName("ExerciseId")]    string?        ExerciseId
);

public record SeekReplayPayloadDto(
    [property: JsonPropertyName("TargetWallTicks")] long          TargetWallTicks
);

public record NodeTransitionPayloadDto(
    [property: JsonPropertyName("TargetState")]   string?        TargetState,   // ClusterState as string
    [property: JsonPropertyName("ScenarioId")]    string?        ScenarioId,
    [property: JsonPropertyName("ExerciseId")]    string?        ExerciseId
);

public record NodeEpisodePayloadDto(
    [property: JsonPropertyName("IsStart")]       bool           IsStart,
    [property: JsonPropertyName("EpisodeId")]     Guid?          EpisodeId,
    [property: JsonPropertyName("ScenarioId")]    string?        ScenarioId
);

public record NodePrefetchPayloadDto(
    [property: JsonPropertyName("ScenarioId")]    string?        ScenarioId
);
```

Note: The `ClusterState`/`NodeOpType` fields in DTOs must use `[JsonConverter(typeof(JsonStringEnumConverter))]` OR use raw string fields + manual string→enum mapping. Choose the approach that avoids the integer-as-enum JSON bug (**integers must NOT be silently parsed as enum values**).

**Tests for CMC-S011** — add to `Hrot.Orchestrator.Tests/TranslatorDtoTests.cs`:
1. `Deserialize<TransitionPayloadDto>("{\"TargetState\":\"OperatingLive\", \"ScenarioId\":\"Test\"}")` → `TargetState == ClusterState.OperatingLive`, `ScenarioId == "Test"`
2. `Deserialize<TransitionPayloadDto>("{\"TargetState\": 31}")` → throws `JsonException` (integer not accepted for enum)
3. Serialize `TransitionPayloadDto(TargetState: ClusterState.LoadingLive, ScenarioId: null, ...)` → JSON contains only `"TargetState"` key (WhenWritingNull suppresses others)
4. Unknown enum string `"OperatingLive_V2"` → `JsonException`

---

## ✅ Task 2: CMC-S012 — NodeOpSlaveTranslator

**Location:** `Hrot.Common/Orchestration/NodeOpSlaveTranslator.cs`

This translator wires DDS ↔ FdpEventBus for the slave side.

**Constructor:**
```csharp
public NodeOpSlaveTranslator(
    DdsReader<NodeOpCommand>   commandReader,
    DdsWriter<NodeOpStatus>    statusWriter,
    DdsWriter<NodeHeartbeat>   heartbeatWriter,
    FdpEventBus                bus,
    int                        nodeId,
    JsonSerializerOptions?     jsonOptions = null)
```

**`Tick()` method (ingress — DDS → Bus):**

For each `NodeOpCommand` in `commandReader.Take()` where `cmd.TargetNodeId == _nodeId`:

```csharp
var domainPayload = DeserializeNodePayload(cmd.Operation, cmd.PayloadJson);
bus.PublishManaged(new ExecuteNodeOpIntent
{
    TransactionId = cmd.TransactionId,
    TargetNodeId  = cmd.TargetNodeId,
    Operation     = (FDP.Toolkit.Orchestration.NodeOpType)(int)cmd.Operation,
    DomainPayload = domainPayload,
});
```

`DeserializeNodePayload` mapping (read from `NodeOpCommand.PayloadJson`):

| `NodeOpType` | Resulting `DomainPayload` type |
|---|---|
| `PrepareState`, `PrepareLive`, `PrepareReplay`, `PrepareEdit`, `FinalizeEdit` | `EditLoadHandlerPayload` (or `null` if payload empty/missing) |
| `StartEpisode`, `StopEpisode` | `EpisodeHandlerPayload` |
| `PrefetchFiles` | `PrefetchHandlerPayload` |
| `SerializeLocal` | `ArchiveHandlerPayload` |
| `CommitState` | `int` (cast `int.Parse(cmd.PayloadJson)` — CommitState carries the new state ID as a raw int string) |
| All others | `null` |

**`Tick()` method (ingress heartbeat — Bus → DDS):**

For each `NodeHeartbeatEvent` in `bus.ConsumeManaged<NodeHeartbeatEvent>()`:
```csharp
heartbeatWriter.Write(new NodeHeartbeat
{
    NodeId             = hb.NodeId,
    SubsystemName      = hb.SubsystemName ?? string.Empty,
    LocalClusterState  = (Hrot.NED.Descriptors.Orchestration.ClusterState)hb.LocalStateId,
    WallTicksUtc       = hb.WallTicksUtc,
    CpuUsagePercent    = 0,     // not tracked at FDP level
    RamUsedBytes       = 0,     // not tracked at FDP level
    SimTickAdvancing   = false, // not tracked at FDP level
    SubsystemsJson     = string.Empty,
});
```

**`Tick()` method (egress — Bus → DDS):**

For each `NodeOpCompletedEvent` in `bus.ConsumeManaged<NodeOpCompletedEvent>()`:
```csharp
statusWriter.Write(new NodeOpStatus
{
    TransactionId  = ev.TransactionId,
    NodeId         = ev.NodeId,
    StatusCode     = ev.StatusCode,
    IsParticipating = ev.IsParticipating,
    ResultJson     = SerializeResultPayload(ev.ResultPayload),
});
```

`SerializeResultPayload`: serialize `ev.ResultPayload` to JSON string if non-null; empty string if null.

**Tests for CMC-S012** — add to `Hrot.Common.Tests/NodeOpSlaveTranslatorTests.cs` (create project if needed, or add to existing test project):

1. `DDS NodeOpCommand` with `TargetNodeId == nodeId`, `Operation = PrepareLive`, `PayloadJson = "{\"TargetState\":\"OperatingLive\"}"` → after `Tick()`, bus has `ExecuteNodeOpIntent { Operation = FDP.NodeOpType.PrepareLive, DomainPayload is EditLoadHandlerPayload }`.
2. `DDS NodeOpCommand` with `TargetNodeId != nodeId` → after `Tick()`, bus has NO `ExecuteNodeOpIntent`.
3. `NodeOpCompletedEvent { StatusCode = 0, ResultPayload = null }` on bus → `NodeOpStatus { ResultJson = "" }` written to DDS writer.
4. `NodeHeartbeatEvent { NodeId = 5, LocalStateId = 30 }` on bus → `NodeHeartbeat { NodeId = 5, LocalClusterState = ClusterState.LoadingLive }` written to DDS.

---

## ✅ Task 3: CMC-S013 — NodeOpMasterTranslator

**Location:** `Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs`

This translator wires DDS ↔ FdpEventBus for the master side (command egress, status ingress).

**Constructor:**
```csharp
public NodeOpMasterTranslator(
    Func<int, DdsWriter<NodeOpCommand>> commandWriterFactory,
    DdsReader<NodeOpStatus>             statusReader,
    FdpEventBus                         bus,
    JsonSerializerOptions?              jsonOptions = null)
```

Or, if per-node writers are pre-created, accept a `Dictionary<int, DdsWriter<NodeOpCommand>>`.

**`Tick()` method (egress — Bus → DDS):**

For each `ExecuteNodeOpIntent` in `bus.ConsumeManaged<ExecuteNodeOpIntent>()`:
```csharp
var payloadJson = SerializeNodePayload(intent.Operation, intent.DomainPayload);
var writer = _commandWriterFactory(intent.TargetNodeId);
writer.Write(new NodeOpCommand
{
    TargetNodeId  = intent.TargetNodeId,
    TransactionId = intent.TransactionId,
    Operation     = (Hrot.NED.Descriptors.Orchestration.NodeOpType)(int)intent.Operation,
    PayloadJson   = payloadJson,
});
```

`SerializeNodePayload`: serialize `DomainPayload` to JSON. If `DomainPayload is int stateId` (CommitState), write `stateId.ToString()`. If null, empty string.

**`Tick()` method (ingress — DDS → Bus):**

For each `NodeOpStatus` in `statusReader.Take()`:
```csharp
var resultPayload = DeserializeResultPayload(status.TransactionId, status.ResultJson);
bus.PublishManaged(new NodeOpCompletedEvent
{
    TransactionId  = status.TransactionId,
    NodeId         = status.NodeId,
    StatusCode     = status.StatusCode,
    IsParticipating = status.IsParticipating,
    ResultPayload  = resultPayload,
});
```

For Phase 5, `DeserializeResultPayload` can simply return `status.ResultJson` as a `string?` — or define a basic result type. Do not over-engineer; `ResultPayload = null` is acceptable if `ResultJson` is empty.

**Tests for CMC-S013** — `Hrot.Orchestrator.Tests/NodeOpMasterTranslatorTests.cs`:

1. `ExecuteNodeOpIntent { TargetNodeId = 2, Operation = PrepareLive, DomainPayload = new EditLoadHandlerPayload("scene1", ...) }` on bus → after `Tick()`, `NodeOpCommand` written to writer-for-node-2 with `Operation = Hrot.NED.NodeOpType.PrepareLive` and `PayloadJson` containing `"TargetState"`.
2. `ExecuteNodeOpIntent { DomainPayload = null }` → `NodeOpCommand { PayloadJson = "" }`.
3. `NodeOpStatus { NodeId = 3, StatusCode = 0, ResultJson = "" }` in DDS reader → after `Tick()`, `NodeOpCompletedEvent { NodeId = 3, ResultPayload = null }` on bus.



---

## ✅ Task 4: CMC-S014 — ClusterOpMasterTranslator

**Location:** `Hrot.Orchestrator/Translators/ClusterOpMasterTranslator.cs`

Bridges external `ClusterOpRequest` → typed intents for `ClusterMaster`, and `ClusterOpCompletedEvent` → `ClusterOpStatus`.

**Constructor:**
```csharp
public ClusterOpMasterTranslator(
    DdsReader<ClusterOpRequest>  requestReader,
    DdsWriter<ClusterOpStatus>   statusWriter,
    FdpEventBus                  bus,
    JsonSerializerOptions?       jsonOptions = null)
```

**`Tick()` ingress (DDS → Bus):**

For each `ClusterOpRequest`:

```csharp
switch (req.OperationType)
{
    case ClusterOpType.TransitionState:
        var dto = Deserialize<TransitionPayloadDto>(req.PayloadJson);
        if (dto?.TargetState is null) { WriteError(req.RequestId, StatusCode.ValidationFailed); break; }
        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId  = req.RequestId,
            TargetState    = (FDP.Toolkit.Orchestration.ClusterState)(int)dto.TargetState.Value,
            ScenarioId     = dto.ScenarioId,
            ExerciseId     = dto.ExerciseId,
            TimeMode       = dto.TimeMode,
        });
        break;

    case ClusterOpType.ManageEpisode:
        var epDto = Deserialize<ManageEpisodePayloadDto>(req.PayloadJson);
        if (epDto?.EpisodeId is null) { WriteError(req.RequestId, StatusCode.ValidationFailed); break; }
        bus.PublishManaged(new ManageEpisodeIntent
        {
            TransactionId = req.RequestId,
            IsStart       = epDto.IsStart,
            EpisodeId     = epDto.EpisodeId.Value,
            ScenarioId    = epDto.ScenarioId,
        });
        break;

    case ClusterOpType.ReplaySeek:
        var seekDto = Deserialize<SeekReplayPayloadDto>(req.PayloadJson);
        bus.PublishManaged(new SeekReplayIntent
        {
            RequestId       = req.RequestId,
            TargetWallTicks = seekDto?.TargetWallTicks ?? 0,
        });
        break;

    case ClusterOpType.CancelOperation:
        bus.PublishManaged(new CancelOperationIntent { TargetRequestId = req.RequestId });
        break;

    case ClusterOpType.ExportArchive:
        var archDto = Deserialize<ArchivePayloadDto>(req.PayloadJson);
        bus.PublishManaged(new ExecuteStorageOpIntent
        {
            RequestId   = req.RequestId,
            Operation   = StorageOpType.Export,
            ExerciseId  = archDto?.ExerciseId,
        });
        break;

    case ClusterOpType.ImportArchive:
        var impDto = Deserialize<ArchivePayloadDto>(req.PayloadJson);
        bus.PublishManaged(new ExecuteStorageOpIntent
        {
            RequestId   = req.RequestId,
            Operation   = StorageOpType.Import,
            ExerciseId  = impDto?.ExerciseId,
        });
        break;

    case ClusterOpType.SaveScenario:
        bus.PublishManaged(new ExecuteStorageOpIntent
        {
            RequestId   = req.RequestId,
            Operation   = StorageOpType.SaveScenario,
            ExerciseId  = null,
        });
        break;

    case ClusterOpType.TakeCheckpoint:
        bus.PublishManaged(new TakeCheckpointIntent { RequestId = req.RequestId });
        break;

    case ClusterOpType.LoadZone:
        bus.PublishManaged(new LoadZoneIntent
        {
            RequestId = req.RequestId,
            ZoneId    = Deserialize<ArchivePayloadDto>(req.PayloadJson)?.ExerciseId,
        });
        break;
    // Time control and other ops: pass through via HandleClusterOpRequest injection
}
```

**`Tick()` egress (Bus → DDS):**

For each `ClusterOpCompletedEvent` in `bus.ConsumeManaged<ClusterOpCompletedEvent>()`:
```csharp
statusWriter.Write(new ClusterOpStatus
{
    RequestId  = ev.RequestId,
    StatusCode = ev.StatusCode,
    ResultJson = ev.ResultPayload is string s ? s : string.Empty,
});
```

**Tests for CMC-S014** — `Hrot.Orchestrator.Tests/ClusterOpMasterTranslatorTests.cs`:

1. `ClusterOpRequest { OperationType = TransitionState, PayloadJson = "{\"TargetState\":\"OperatingLive\"}" }` → `TransitionStateIntent { TargetState = FDP.ClusterState.OperatingLive }` on bus.
2. `ClusterOpRequest { OperationType = TransitionState, PayloadJson = "{}" }` → `ClusterOpStatus { StatusCode = ValidationFailed }` written to DDS writer; nothing on bus.
3. `ClusterOpCompletedEvent { RequestId = X, StatusCode = 0 }` → `ClusterOpStatus { RequestId = X, StatusCode = 0 }` written to DDS writer.
4. End-to-end: push `ClusterOpRequest` through translator → `ClusterMaster(bus, ...)` receives `TransitionStateIntent` → produces `ExecuteNodeOpIntent` on bus.

---

## ✅ Task 5: CMC-S015 — EventDrivenStorageGateway

**Location:** `Hrot.Orchestrator/EventDrivenStorageGateway.cs`

Moves the async storage lifecycle (archive, save-scenario, import) out of `ClusterMaster` and into a self-contained service.

**Constructor:**
```csharp
public EventDrivenStorageGateway(
    FdpEventBus            bus,
    StorageGatewayModule   storage)
```

**Internal state:**
```csharp
private readonly Dictionary<Guid, CancellationTokenSource> _activeCancellations = new();
```

**`Tick()` method:**

1. Drain `ExecuteStorageOpIntent` from bus. For each:
   - Create a `CancellationTokenSource` and store in `_activeCancellations[intent.RequestId]`.
   - Start async operation: `Task.Run(() => ExecuteStorageOp(intent, cts.Token))`.
   - On completion (via callback or `ContinueWith`), publish `StorageOpCompletedEvent { RequestId, StatusCode }` to bus; remove from `_activeCancellations`.

2. Drain `CancelOperationIntent` from bus. For each:
   - If `_activeCancellations.TryGetValue(intent.TargetRequestId, out var cts)`, call `cts.Cancel()`.

**Private `ExecuteStorageOp` mapping:**

```csharp
private async Task ExecuteStorageOp(ExecuteStorageOpIntent intent, CancellationToken ct)
{
    try
    {
        switch (intent.Operation)
        {
            case StorageOpType.Export:     await _storage.ExportArchiveAsync(intent.ExerciseId, ct); break;
            case StorageOpType.Import:     await _storage.ImportArchiveAsync(intent.ExerciseId, ct); break;
            case StorageOpType.SaveScenario: await _storage.SaveScenarioAsync(ct); break;
        }
        _eventBus.PublishManaged(new StorageOpCompletedEvent
            { RequestId = intent.RequestId, StatusCode = OrchestrationStatusCode.Success,
              SuccessCount = 1, FailureCount = 0 });
    }
    catch (OperationCanceledException)
    {
        _eventBus.PublishManaged(new StorageOpCompletedEvent
            { RequestId = intent.RequestId, StatusCode = OrchestrationStatusCode.Cancelled, ... });
    }
    catch (Exception ex)
    {
        _eventBus.PublishManaged(new StorageOpCompletedEvent
            { RequestId = intent.RequestId, StatusCode = OrchestrationStatusCode.Failure, ... });
    }
}
```

**Note:** Check `StorageGatewayModule` API in `Hrot.Orchestrator/StorageGatewayModule.cs` for the actual method signatures. Adapt to what exists.

**If `OrchestrationStatusCode.Cancelled` doesn't exist**, add it (e.g., `Cancelled = 2`). Check `OrchestrationStatusCode.cs` for existing constants first.

**Tests for CMC-S015** — `Hrot.Orchestrator.Tests/EventDrivenStorageGatewayTests.cs`:

1. Publish `ExecuteStorageOpIntent { Operation = Export, ExerciseId = "X" }`. Tick once. Assert `StorageGatewayModule.ExportArchiveAsync("X", ...)` was called (use stub/mock).
2. When export completes, `StorageOpCompletedEvent { StatusCode = Success }` is on the bus.
3. Publish `CancelOperationIntent { TargetRequestId = Y }` while export in flight. Assert its `CancellationToken` was cancelled.

---

## 🧪 Testing Summary

| Test file | Project | Scenarios |
|---|---|---|
| `TranslatorDtoTests.cs` | `Hrot.Orchestrator.Tests` | CMC-S011: JSON round-trip |
| `NodeOpSlaveTranslatorTests.cs` | `Hrot.SimHost.Tests` or new | CMC-S012: bus dispatch, filtering |
| `NodeOpMasterTranslatorTests.cs` | `Hrot.Orchestrator.Tests` | CMC-S013: egress serialization, ingress deserialization |
| `ClusterOpMasterTranslatorTests.cs` | `Hrot.Orchestrator.Tests` | CMC-S014: all op types, validation error |
| `EventDrivenStorageGatewayTests.cs` | `Hrot.Orchestrator.Tests` | CMC-S015: dispatch, cancellation |

Minimum new tests: 4 + 4 + 3 + 4 + 3 = **18 tests**

---

## ⚠️ Important Notes

1. **`System.Text.Json` is ALLOWED in translator files.** It is NOT allowed outside translator files.

2. **`FdpEventBus.SwapBuffers()` pattern:** If your tests double-buffer (publish then Tick to read), you may need to call `bus.SwapBuffers()` in tests. Read `FdpEventBus` API carefully to understand its buffering model.

3. **`NodeHeartbeatEvent.LocalStateId` vs `NodeHeartbeat.LocalClusterState`:** The FDP event uses `int LocalStateId`; the DDS type uses `Hrot.NED.ClusterState`. Cast with `(Hrot.NED.ClusterState)hb.LocalStateId`.

4. **`ClusterOpRequest` DDS type.** Field names: `{ Guid RequestId, ClusterOpType OperationType, string PayloadJson }`. Double-check with actual Hrot.NED source if names differ.

5. **`StorageGatewayModule` API.** Check the actual method signatures. The gateway may not expose `ExportArchiveAsync(exerciseId, ct)` directly — adapt accordingly.

6. **IntegrationTest `ScenarioSaveLoadTests.cs`** in `Hrot.Orchestrator.Integration.Tests` uses `ClusterMaster(DdsParticipant)` path. Do NOT break it. The old DDS-based constructor stays.

---

## 🎯 Success Criteria

- [ ] CMC-S011: 4 DTO tests pass
- [ ] CMC-S012: `NodeOpSlaveTranslator` with 4 tests
- [ ] CMC-S013: `NodeOpMasterTranslator` with 3 tests  
- [ ] CMC-S014: `ClusterOpMasterTranslator` with 4 tests (including end-to-end)
- [ ] CMC-S015: `EventDrivenStorageGateway` with 3 tests
- [ ] `dotnet build IOS-IG-SimHost.sln` → 0 errors
- [ ] All prior test suites still pass
- [ ] Report submitted

---

## 📚 References

- DESIGN.md §6: `.dev/cluster-master-cqrs-1/DESIGN.md`
- TASK-DETAIL.md CMC-S011–S015
- `Hrot.NED/Orchestration/OrchestrationMessages.cs` — DDS type definitions
- `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/` — existing domain payload types
- `Hrot.Orchestrator/StorageGatewayModule.cs` — storage API
- `Hrot.Orchestrator/ClusterOpRequestAdapter.cs` — existing DDS→intent bridge (may overlap with CMC-S014)
