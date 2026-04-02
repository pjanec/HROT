# BATCH-02: IClusterStateHandler Enum Migration

**Batch Number:** BATCH-02  
**Tasks:** CMC-S004, CMC-S005  
**Phase:** Phase 2 — IClusterStateHandler Migration (Breaking Interface Change)  
**Estimated Effort:** 6–10 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 complete (FDP domain enums and event structs must exist)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch migrates the `IClusterStateHandler` interface from legacy weakly-typed signatures (`int operationId`, `OrchestrationCommand`, `string? PayloadJson`) to the new CQRS vocabulary introduced in BATCH-01 (`NodeOpType` enum, `ExecuteNodeOpIntent`, `object? DomainPayload`).

This is a **breaking interface change**. Every implementation and every test that uses `IClusterStateHandler` or `OrchestrationCommand` must be updated.

### Required Reading

1. **DESIGN.md §3.4:** `.dev/cluster-master-cqrs-1/DESIGN.md` — IClusterStateHandler migration
2. **TASK-DETAIL.md CMC-S004 + CMC-S005:** `.dev/cluster-master-cqrs-1/TASK-DETAIL.md`
3. **BATCH-01 report** (context on ambiguity aliases): `.dev/cluster-master-cqrs-1/reports/BATCH-01-REPORT.md`

### Build & Test Commands

```powershell
# Run from d:\Work\IOS-IG-SimHost-FDP-2
dotnet build FDP/FDP.sln -v q
dotnet build IOS-IG-SimHost.sln -v q

dotnet test FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/FDP.Toolkit.Orchestration.Tests.csproj
dotnet test Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj
dotnet test Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj
dotnet test Hrot.SimHost.Integration.Tests/Hrot.SimHost.Integration.Tests.csproj
dotnet test Hrot.Orchestrator.Integration.Tests/Hrot.Orchestrator.Integration.Tests.csproj
```

### Report Submission

`.dev/cluster-master-cqrs-1/reports/BATCH-02-REPORT.md`  
Questions: `.dev/cluster-master-cqrs-1/questions/BATCH-02-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW

1. **CMC-S004 first:** Change `CanHandle` parameter type only. Build and confirm the solution compiles.
2. **CMC-S005 after CMC-S004 passes build:** Update remaining method signatures + delete OrchestrationCommand/Status.
3. After each task: run full test suite, fix all failures before moving on.

---

## ✅ Task 1: CMC-S004 — CanHandle: int → NodeOpType

**Scope:** Minimal. Only the `CanHandle` signature changes. No other method changes.

**File changes:**

### 1.1 `FDP/Toolkits/FDP.Toolkit.Orchestration/IClusterStateHandler.cs`

Change:
```csharp
bool CanHandle(int operationId);
```
To:
```csharp
bool CanHandle(NodeOpType operation);
```

Update the XML doc to reflect the change.

### 1.2 All 9 FDP Reference Handlers in `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/`

Each handler has a `CanHandle(int operationId)` implementation. Change to `CanHandle(NodeOpType operation)`.

Replace `int` constant comparisons with enum value comparisons:
```csharp
// Before:
public bool CanHandle(int operationId) => operationId == PrepareLiveOperationId;  // where PrepareLiveOperationId = 9

// After:
public bool CanHandle(NodeOpType operation) =>
    operation == NodeOpType.PrepareLive || operation == NodeOpType.FinalizeLive;
```

The `const int FooOperationId = N` fields MAY be retained for documentation, but the `CanHandle` comparison must use enum values. Do NOT use magic integers inside `CanHandle`.

### 1.3 `Hrot.Common/Orchestration/HrotHandlerAdapter.cs`

Change:
```csharp
public bool CanHandle(int operationId) =>
    _inner.CanHandle((Hrot.NED.Descriptors.Orchestration.NodeOpType)operationId);
```
To:
```csharp
public bool CanHandle(FDP.Toolkit.Orchestration.NodeOpType operation) =>
    _inner.CanHandle((Hrot.NED.Descriptors.Orchestration.NodeOpType)(int)operation);
```

### 1.4 `Hrot.IG/Modules/Orchestration/IgZoneDummyHandler.cs`

Update `CanHandle(int operationId)` → `CanHandle(FDP.Toolkit.Orchestration.NodeOpType operation)`.  
(Check what operations it currently handles and match them using enum values.)

### 1.5 `FDP/Toolkits/FDP.Toolkit.Orchestration/ClusterSlave.cs`

In `DispatchCommand`, change the `CanHandle` call to cast the command's operation ID:
```csharp
// Before:
if (!handler.CanHandle(cmd.OperationId)) continue;

// After:
if (!handler.CanHandle((NodeOpType)cmd.OperationId)) continue;
```

Also update the `CommitState` detection from magic int to enum:
```csharp
// Before:
private const int CommitStateOperationId = 2;
if (cmd.OperationId == CommitStateOperationId)

// After:
if ((NodeOpType)cmd.OperationId == NodeOpType.CommitState)
```

### 1.6 Test stubs

Fix compilation errors in all test stubs implementing `IClusterStateHandler`:
- `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/ClusterSlaveTests.cs` (inner `StubHandler`)
- `Hrot.SimHost.Integration.Tests/DdsOrchestrationTransportTests.cs` (`StubHandler`)

Update their `CanHandle(int operationId)` → `CanHandle(NodeOpType operation)`.

---

**CMC-S004 Success Check:** `dotnet build IOS-IG-SimHost.sln` succeeds. No `CanHandle(int` in solution.

---

## ✅ Task 2: CMC-S005 — Delete OrchestrationCommand/Status; Update IClusterStateHandler

**Scope:** Large. Deletes two types. Changes `PrepareAsync`, `Commit`, `Abort` signatures everywhere. Updates `ClusterSlave` dispatch. Updates `IOrchestrationTransport`.

### 2.1 Update `IClusterStateHandler` interface

New full interface:
```csharp
public interface IClusterStateHandler
{
    bool CanHandle(NodeOpType operation);
    Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct);
    void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo);
    void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo);
}
```

### 2.2 Update `IOrchestrationTransport`

Replace `OrchestrationCommand`/`OrchestrationStatus` with `ExecuteNodeOpIntent`/`NodeOpCompletedEvent`:
```csharp
public interface IOrchestrationTransport : IDisposable
{
    void PublishHeartbeat(int nodeId, string subsystemName, int localStateId, long wallTicksUtc);
    void PublishStatus(NodeOpCompletedEvent status);
    bool TryDequeueCommand(out ExecuteNodeOpIntent intent);
}
```

### 2.3 Update `ClusterSlave`

Replace `OrchestrationCommand` references with `ExecuteNodeOpIntent`:
- `_pendingPrepare` field type: `(Task<object?> PrepareTask, ExecuteNodeOpIntent Intent, IClusterStateHandler Handler)?`
- `EnqueueCommandForTest(OrchestrationCommand cmd)` → **rename to** `EnqueueIntentForTest(ExecuteNodeOpIntent intent)` — this is the test entry point
- `DispatchCommand(OrchestrationCommand cmd)` → rename to `DispatchIntent(ExecuteNodeOpIntent intent)`
- The `CommitState` path: detect via `intent.Operation == NodeOpType.CommitState`. Get the new state from `intent.DomainPayload`:
  ```csharp
  if (intent.Operation == NodeOpType.CommitState)
  {
      int nextStateId = intent.DomainPayload is int stateId ? stateId : _localStateId;
      var previousStateId = _localStateId;
      _localStateId = nextStateId;
      _eventBus?.Publish(new TkClusterStateChangedEvent { PreviousStateId = previousStateId, NextStateId = nextStateId });
      return;
  }
  ```
- Dedup set: change type from `HashSet<(Guid, int)>` to `HashSet<(Guid, NodeOpType)>`
- The `PrepareAsync` / `Commit` / `Abort` calls: pass `intent` directly
- `PublishStatus` call (if any): update to use `NodeOpCompletedEvent`

### 2.4 Update `DdsOrchestrationTransport` (`Hrot.Common/Orchestration/DdsOrchestrationTransport.cs`)

- Change queue type: `ConcurrentQueue<ExecuteNodeOpIntent>`
- In the DDS receive callback, map DDS `NodeOpCommand` → `ExecuteNodeOpIntent`:
  ```csharp
  _inboundQueue.Enqueue(new ExecuteNodeOpIntent
  {
      TransactionId = ddsCmd.TransactionId,
      TargetNodeId  = ddsCmd.TargetNodeId,
      Operation     = (FDP.Toolkit.Orchestration.NodeOpType)ddsCmd.OperationId,
      DomainPayload = null,   // JSON→domain translation happens in Phase 5 translators
  });
  ```
  Note: `ddsCmd.OperationId` is `int`, `ddsCmd.TransactionId` is `Guid`, verify field names from the NED struct  
- `TryDequeueCommand(out ExecuteNodeOpIntent intent)`: update implementation
- `PublishStatus(NodeOpCompletedEvent status)`: update to build the DDS `NodeOpStatus`:
  ```csharp
  _statusWriter.Write(new NodeOpStatus
  {
      TransactionId  = status.TransactionId,
      NodeId         = status.NodeId,
      StatusCode     = status.StatusCode,
      IsParticipating = status.IsParticipating,
      ResultJson     = status.ResultPayload is string s ? s : string.Empty,
  });
  ```
  Note: verify the exact field names of `NodeOpStatus` DDS type in `Hrot.NED`

### 2.5 Update `HrotHandlerAdapter` (`Hrot.Common/Orchestration/HrotHandlerAdapter.cs`)

Change `PrepareAsync`, `Commit`, `Abort` to accept `ExecuteNodeOpIntent`:
```csharp
public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
{
    var cmd = ToNodeOpCommand(intent);
    return _inner.PrepareAsync(cmd, ct)
                 .ContinueWith(t => (object?)t.Result, TaskContinuationOptions.ExecuteSynchronously);
}

public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) =>
    _inner.Commit(ToNodeOpCommand(intent), repo ?? _repo);

public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) =>
    _inner.Abort(ToNodeOpCommand(intent), repo ?? _repo);
```

Update `ToNodeOpCommand` to take `ExecuteNodeOpIntent`:
```csharp
private static NodeOpCommand ToNodeOpCommand(ExecuteNodeOpIntent intent)
{
    string payloadJson = intent.DomainPayload switch
    {
        null             => string.Empty,
        string s         => s,
        _                => System.Text.Json.JsonSerializer.Serialize(intent.DomainPayload),
    };
    return new NodeOpCommand
    {
        TransactionId = intent.TransactionId,
        TargetNodeId  = intent.TargetNodeId,
        Operation     = (Hrot.NED.Descriptors.Orchestration.NodeOpType)(int)intent.Operation,
        PayloadJson   = payloadJson,
    };
}
```

`System.Text.Json` is allowed in `Hrot.Common` (Hrot application layer) — it is NOT allowed in `FDP.Toolkit.Orchestration`.

### 2.6 Update `IgZoneDummyHandler` (`Hrot.IG/Modules/Orchestration/IgZoneDummyHandler.cs`)

Change `PrepareAsync`, `Commit`, `Abort` from `OrchestrationCommand` to `ExecuteNodeOpIntent`. No behavior change needed — the handler is a no-op stub.

### 2.7 Update FDP Reference Handlers (`FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/`)

For all 9 reference handlers:

**A. Change method signatures:**
- `Task<string?> PrepareAsync(OrchestrationCommand cmd, ...)` → `Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, ...)`
- `void Commit(OrchestrationCommand cmd, ...)` → `void Commit(ExecuteNodeOpIntent intent, ...)`
- `void Abort(OrchestrationCommand cmd, ...)` → `void Abort(ExecuteNodeOpIntent intent, ...)`

**B. Replace `cmd.PayloadJson` with `intent.DomainPayload` type-checking:**

Handlers that currently parse `cmd.PayloadJson` must now check `intent.DomainPayload`. Use simple payload structs defined at the end of each handler's file (or a shared `Payloads/` folder).

**Pattern (per handler):**

`ReferenceArchiveHandler`:
- Define (in the same file): `public record struct ArchiveHandlerPayload(string? ExerciseId);`
- `Commit`: `var exerciseId = intent.DomainPayload is ArchiveHandlerPayload p ? p.ExerciseId : null;`

`ReferenceScenarioLoadHandler` / `ReferenceEditLoadHandler` / `ReferenceReplayLoadHandler` / `ReferencePreviewHandler`:
- Define: `public record struct LoadHandlerPayload(string? ScenarioId);` (shared or per handler)
- Use: `var scenarioId = intent.DomainPayload is LoadHandlerPayload p ? p.ScenarioId : null;` 

`ReferenceEpisodeLoadHandler`:
- Define: `public record struct EpisodeHandlerPayload(Guid EpisodeId, string? ScenarioId, bool IsStart);`
- Use: check `intent.DomainPayload is EpisodeHandlerPayload p`

`ReferencePrefetchHandler`:
- Check what `cmd.PayloadJson` currently contains and define a matching payload struct.

`ReferenceLiveLoadHandler`, `ReferenceCheckpointHandler`:
- These may not have meaningful payload parsing — check and simplify if the payload is currently unused.

**C. Publishing status ACKs:**

Handlers that call `_transport?.PublishStatus(new OrchestrationStatus(...))` must change to:
```csharp
_transport?.PublishStatus(new NodeOpCompletedEvent
{
    TransactionId   = intent.TransactionId,
    NodeId          = _nodeId,
    StatusCode      = OrchestrationStatusCode.Success,
    IsParticipating = true,
    ResultPayload   = resultObject,  // typed result, not JSON string
});
```

The `ResultPayload` of `NodeOpCompletedEvent` is `object?`. For the reference handlers that currently serialize a file manifest to JSON, define a result type:
```csharp
// In ReferenceArchiveHandler.cs
public record struct FileManifestResult(string SourceUnc, string RelativeDest);
```
And set `ResultPayload = new[] { new FileManifestResult(file, relativeDest) }`.

**D. Remove `System.Text.Json` from FDP handlers:**

After this task:
```powershell
grep -r "System.Text.Json" FDP/Toolkits/FDP.Toolkit.Orchestration/
```
Must return zero results.

### 2.8 Delete Legacy Types

- Delete `FDP/Toolkits/FDP.Toolkit.Orchestration/OrchestrationCommand.cs`
- Delete `FDP/Toolkits/FDP.Toolkit.Orchestration/OrchestrationStatus.cs`

Verify with grep that no remaining `.cs` files in `FDP/Toolkits/FDP.Toolkit.Orchestration/` reference `OrchestrationCommand` or `OrchestrationStatus` as types (doc comments and string usage are OK to ignore).

### 2.9 Update All Tests

Tests using `OrchestrationCommand` or `OrchestrationStatus` must be updated to use `ExecuteNodeOpIntent` and `NodeOpCompletedEvent`. A full list of affected test files:

- `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/ClusterSlaveTests.cs`
- `Hrot.SimHost.Tests/ReplayLoadClusterOpHandlerTests.cs`
- `Hrot.SimHost.Tests/EditLoadClusterOpHandlerTests.cs`
- `Hrot.SimHost.Tests/ClusterSlaveHandlerTests.cs`
- `Hrot.SimHost.Tests/CheckpointClusterOpHandlerTests.cs`
- `Hrot.SimHost.Tests/FullBranchPipelineTests.cs`
- `Hrot.SimHost.Tests/EpisodeLoadClusterOpHandlerTests.cs`
- `Hrot.SimHost.Tests/LiveFromReplayTests.cs`
- `Hrot.SimHost.Tests/NodeBootstrapperReplayTests.cs`
- `Hrot.SimHost.Integration.Tests/EpisodeInjectionTests.cs`
- `Hrot.SimHost.Integration.Tests/DdsOrchestrationTransportTests.cs`
- `Hrot.SimHost.Integration.Tests/CgfPrepareLiveDispatchTests.cs`
- `Hrot.Orchestrator.Tests/ReferenceArchiveHandlerTests.cs`
- `Hrot.Orchestrator.Integration.Tests/ScenarioSaveLoadTests.cs`

**Test migration rules:**

```csharp
// Before:
slave.EnqueueCommandForTest(new OrchestrationCommand(
    TransactionId: Guid.NewGuid(),
    TargetNodeId: 1,
    OperationId: (int)NodeOpType.PrepareLive,
    PayloadJson: "{}"));

// After:
slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
{
    TransactionId = Guid.NewGuid(),
    TargetNodeId  = 1,
    Operation     = NodeOpType.PrepareLive,
    DomainPayload = null,   // or new LoadHandlerPayload("scenario-id") if the test exercises payload
});
```

For tests that relied on specific `PayloadJson` content, the `DomainPayload` should now be a typed struct instead. Check what the test verified and set an appropriate typed payload. If a handler test was testing JSON parse failure previously (e.g., "empty string"), change to `DomainPayload = null` and verify the no-payload path still passes.

For tests creating `OrchestrationStatus` → they will need to become `NodeOpCompletedEvent`.

The `DdsOrchestrationTransportTests.StubHandler` also implements `IClusterStateHandler` — update it.

---

## 🧪 Testing Requirements

After BATCH-02, the following test suites must be 100% green:
- `FDP.Toolkit.Orchestration.Tests` — all existing tests + any new handler payload tests
- `Hrot.Orchestrator.Tests` — all including new enum sync tests
- `Hrot.SimHost.Tests` — all handler tests
- `Hrot.SimHost.Integration.Tests` — integration tests
- `Hrot.Orchestrator.Integration.Tests` — integration tests

**New tests to add (CMC-S005 success conditions):**
1. Test in `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/ReferenceHandlerTests.cs` (extend): call `PrepareAsync(new ExecuteNodeOpIntent { Operation = NodeOpType.PrepareLive, DomainPayload = null }, ct)` — verify returns without exception.
2. Test: call `PrepareAsync(new ExecuteNodeOpIntent { Operation = NodeOpType.TakeSnapshot, DomainPayload = null }, ct)` — verify no-op or returns null.
3. Verify `IClusterStateHandler.PrepareAsync` return type is `Task<object?>` via reflection.

---

## ⚠️ Quality Standards

- `grep -r "OrchestrationCommand" FDP/` → 0 results (excluding `.cs` file comments)
- `grep -r "OrchestrationStatus" FDP/` → 0 results (excluding `OrchestrationStatusCode` which stays!)  
  Note: `OrchestrationStatusCode.cs` is NOT deleted — it contains the `StatusCode` int constants
- `grep -r "System.Text.Json" FDP/Toolkits/FDP.Toolkit.Orchestration/` → 0 results
- `dotnet build IOS-IG-SimHost.sln` → 0 errors
- All test suites green

---

## 🎯 Success Criteria

- [ ] CMC-S004: `IClusterStateHandler.CanHandle(NodeOpType)` — no `int` parameter anywhere
- [ ] CMC-S004: All handler `CanHandle` implementations use enum values (no magic integers)
- [ ] CMC-S005: `IClusterStateHandler` methods use `ExecuteNodeOpIntent` and return `Task<object?>`
- [ ] CMC-S005: `OrchestrationCommand.cs` and `OrchestrationStatus.cs` deleted
- [ ] CMC-S005: `IOrchestrationTransport` uses `ExecuteNodeOpIntent`/`NodeOpCompletedEvent`
- [ ] CMC-S005: `DdsOrchestrationTransport` updated, `DomainPayload = null` bridge in place
- [ ] CMC-S005: `HrotHandlerAdapter` converts `ExecuteNodeOpIntent` → `NodeOpCommand`
- [ ] CMC-S005: Zero `System.Text.Json` in `FDP.Toolkit.Orchestration`
- [ ] All payload structs defined inline for reference handlers
- [ ] All tests updated and passing
- [ ] Report submitted

---

## 📝 Key Caveats

1. **`OrchestrationStatusCode` stays.** Do NOT delete `OrchestrationStatusCode.cs`. Only `OrchestrationCommand.cs` and `OrchestrationStatus.cs` are deleted.

2. **`DomainPayload = null` is intentional.** During this transition period, `DdsOrchestrationTransport.TryDequeueCommand` sets `DomainPayload = null` because JSON→domain translation is Phase 5 work. Reference handlers must gracefully skip payload-dependent logic when `DomainPayload` is null.

3. **`HrotHandlerAdapter` may serialize `DomainPayload` to JSON.** This is the bridge during the transition: if `DomainPayload` is `null`, it passes empty string. Eventually Phase 5 will set real `DomainPayload` and the adapter will serialize it. This is correct — `HrotHandlerAdapter` is Hrot.Common and is allowed to use `System.Text.Json`.

4. **Hrot SimHost handlers use `IClusterOpHandler` not `IClusterStateHandler`.** The Hrot.SimHost handlers (`ReplayLoadClusterOpHandler`, `EditLoadClusterOpHandler`, etc.) implement `IClusterOpHandler` which takes `NodeOpCommand`. They do NOT directly implement `IClusterStateHandler`. They are wrapped by `HrotHandlerAdapter`. Only update the tests that construct `OrchestrationCommand` to instead use `ExecuteNodeOpIntent`.

5. **`NodeOpCommand` is NOT changed.** `NodeOpCommand` is a Hrot.NED DDS type and must not be modified. `IClusterOpHandler` is not changed. Only the FDP side (`IClusterStateHandler`) changes.

6. **Check `NodeOpStatus` DDS type fields.** In `DdsOrchestrationTransport.PublishStatus`, verify the actual field names of the `NodeOpStatus` DDS struct from `Hrot.NED` before mapping.
