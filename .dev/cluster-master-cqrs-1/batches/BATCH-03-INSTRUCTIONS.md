# BATCH-03: ClusterSlave Event Bus Integration

**Batch Number:** BATCH-03  
**Tasks:** CMC-S006, CMC-S007  
**Phase:** Phase 3 — ClusterSlave Event Bus Integration  
**Estimated Effort:** 5–8 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-02 complete

---

## 📋 Context

This batch completes the ClusterSlave decoupling from DDS. After BATCH-03:
- `ClusterSlave` reads `ExecuteNodeOpIntent` exclusively from `FdpEventBus`
- `ClusterSlave` publishes `NodeHeartbeatEvent` and `NodeOpCompletedEvent` to `FdpEventBus`
- `IOrchestrationTransport` and `DdsOrchestrationTransport` are deleted
- The DDS heartbeat/status path is temporarily inactive (resumed in Phase 5 translators)

### Build & Test Commands

```powershell
# From d:\Work\IOS-IG-SimHost-FDP-2
dotnet build FDP/FDP.sln -v q
dotnet build IOS-IG-SimHost.sln -v q
dotnet test FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/FDP.Toolkit.Orchestration.Tests.csproj
dotnet test Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj
dotnet test Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj
dotnet test Hrot.SimHost.Integration.Tests/Hrot.SimHost.Integration.Tests.csproj
dotnet test Hrot.Orchestrator.Integration.Tests/Hrot.Orchestrator.Integration.Tests.csproj
```

### Report Submission

`.dev/cluster-master-cqrs-1/reports/BATCH-03-REPORT.md`

---

## ✅ Task 1: CMC-S006 — ClusterSlave Reads from FdpEventBus

### 1.1 Define `NodeHeartbeatEvent`

Add `NodeHeartbeatEvent` to `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterCqrsEvents.cs`:

```csharp
/// <summary>
/// Published by <c>ClusterSlave</c> once per second.
/// Consumed by <c>NodeOpSlaveTranslator</c> to write <c>NodeHeartbeat</c> DDS topic.
/// </summary>
[EventId(9014)]
[DataPolicy(DataPolicy.NoRecord)]
public struct NodeHeartbeatEvent
{
    public int    NodeId;
    public int    LocalStateId;
    public long   WallTicksUtc;
    public string SubsystemName;
}
```

**EventId 9014 is free** (confirmed: 9011-9013 used by BATCH-01, next free ID in cluster management range).

`NodeHeartbeatEvent` contains a `string` field so it must be routed via `PublishManaged<T>` / `ConsumeManaged<T>`.

### 1.2 Refactor `ClusterSlave.Tick()`

**A. Replace heartbeat via transport with heartbeat via bus:**

```csharp
// Before:
if (_transport != null && _heartbeatTimer.Elapsed.TotalSeconds >= 1.0)
{
    _heartbeatTimer.Restart();
    _transport.PublishHeartbeat(_nodeId, _subsystemName, _localStateId, DateTimeOffset.UtcNow.Ticks);
}

// After:
if (_heartbeatTimer.Elapsed.TotalSeconds >= 1.0)
{
    _heartbeatTimer.Restart();
    _eventBus?.PublishManaged(new NodeHeartbeatEvent
    {
        NodeId        = _nodeId,
        LocalStateId  = _localStateId,
        WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        SubsystemName = _subsystemName,
    });
}
```

**B. Replace transport poll loop with bus consumption:**

```csharp
// Remove this block:
if (_transport == null) return;
while (_transport.TryDequeueCommand(out var intent))
{
    DispatchIntent(intent);
    if (_pendingPrepare.HasValue) break;
}

// Replace with:
if (_eventBus != null)
{
    foreach (var intent in _eventBus.ConsumeManaged<ExecuteNodeOpIntent>())
    {
        DispatchIntent(intent);
        if (_pendingPrepare.HasValue) break;
    }
}
```

**C. Publish `NodeOpCompletedEvent` to bus after prepare completes:**

In `DispatchIntent`, after `handler.Commit(intent, repo: null)`, publish the completion:
```csharp
var result = prepareTask.Result;
handler.Commit(intent, repo: null);
_eventBus?.PublishManaged(new NodeOpCompletedEvent
{
    TransactionId   = intent.TransactionId,
    NodeId          = _nodeId,
    StatusCode      = OrchestrationStatusCode.Success,
    IsParticipating = true,
    ResultPayload   = result,
});
```

Also publish for the deferred-prepare path (in the `_pendingPrepare.HasValue` pending resolution block).

For the faulted case, publish a failure:
```csharp
_eventBus?.PublishManaged(new NodeOpCompletedEvent
{
    TransactionId   = pending.Intent.TransactionId,
    NodeId          = _nodeId,
    StatusCode      = OrchestrationStatusCode.Failure,
    IsParticipating = true,
    ResultPayload   = null,
});
```

**D. Keep transport field for now — CMC-S007 removes it.** The transport is no longer polled for commands or used for heartbeats. It can stay in the field for backward compat until S007.

### 1.3 Update `ClusterSlave` constructor

The existing `FdpEventBus? eventBus` parameter is still optional (`?`). Since without a bus the slave can't do anything useful, consider making it non-optional but keep backward compat for now by leaving it `FdpEventBus?`.

### 1.4 Tests for CMC-S006

Add the following tests to `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/ClusterSlaveTests.cs`:

1. **Bus dispatch test:** Construct `ClusterSlave(eventBus)` with NO transport. Register a stub handler for `NodeOpType.PrepareLive`. Publish `ExecuteNodeOpIntent` to bus. Call `Tick()`. Assert handler was called.

2. **NodeOpCompleted published:** Same setup. Assert after Tick(), bus has a `NodeOpCompletedEvent` with `IsParticipating = true`.

3. **Heartbeat published:** `ClusterSlave` constructed with a bus. Advance time past 1 second (use `Stopwatch` or force via threading). Call `Tick()`. Assert bus has a `NodeHeartbeatEvent` with correct NodeId.

4. **Transport-null no crash:** Construct `ClusterSlave(null, 0, "test")`. Call `Tick()`. Assert no exception thrown.

---

## ✅ Task 2: CMC-S007 — Delete IOrchestrationTransport and DdsOrchestrationTransport

### 2.1 Remove `_transport` from `ClusterSlave`

After CMC-S006 passes all tests:

1. Remove the `_transport` field declaration from `ClusterSlave`.
2. Remove the `IOrchestrationTransport transport` parameter from the production constructor.
3. Update the test constructor (no-arg constructor) — already doesn't use transport.
4. Remove any remaining `_transport` usage (should be none after S006).

**Resulting production constructor:**
```csharp
public ClusterSlave(
    int          nodeId,
    string       subsystemName,
    FdpEventBus? eventBus = null)
{
    _nodeId        = nodeId;
    _subsystemName = subsystemName ?? throw new ArgumentNullException(nameof(subsystemName));
    _eventBus      = eventBus;
}
```

Also update `Dispose()` — remove `_transport?.Dispose()`.

### 2.2 Delete `IOrchestrationTransport.cs`

Delete `FDP/Toolkits/FDP.Toolkit.Orchestration/IOrchestrationTransport.cs`.

Verify with grep: `IOrchestrationTransport` must appear zero times in C# source files after deletion.

### 2.3 Delete `DdsOrchestrationTransport.cs`

Delete `Hrot.Common/Orchestration/DdsOrchestrationTransport.cs`.

### 2.4 Delete `DdsOrchestrationTransportTests.cs`

Delete `Hrot.SimHost.Integration.Tests/DdsOrchestrationTransportTests.cs` — the direct DDS transport test is obsolete. DDS connectivity testing resumes in Phase 5 via translator tests.

### 2.5 Update Composition Roots

Update the following files to remove `DdsOrchestrationTransport` creation:

**`Hrot.SimHost/NodeBootstrapper.cs`**  
Find: `new DdsOrchestrationTransport(participant, nodeId)`  
Change to: constructor no longer passes a transport. Update `ClusterSlave` construction to pass `(nodeId, subsystemName, eventBus)`.  
Also remove `using Hrot.Common.Orchestration` if it was only used for the transport.

**`Hrot.CGF/CgfApplication.cs`**  
Find: `new DdsOrchestrationTransport(_participant, nodeId)` and the `ClusterSlave` construction that follows.  
Update to use the new constructor signature (no transport parameter).

**`Hrot.IG/IgApplication.cs`**  
Find: `new DdsOrchestrationTransport(participant, igNodeId)`  
Same treatment — remove transport, update ClusterSlave construction.

**`Hrot.ClusterRunner/Services/ExConSubsystem.cs`**  
Find: `new DdsOrchestrationTransport(_participant, iosNodeId)`  
Remove and update ClusterSlave construction.

### 2.6 Update Tests That Used Transport in ClusterSlave

Any test that passes `transport` to `ClusterSlave` must be updated to the new constructor. Use a grep to find all `new ClusterSlave(` and verify they compile.

The `StubHandler` in `DdsOrchestrationTransportTests.cs` is deleted with the file in step 2.4.

---

## ⚠️ Important Notes

### DDS Heartbeat/Status Temporarily Disabled

After BATCH-03, the DDS `NodeHeartbeat` and `NodeOpStatus` topics are no longer written by ClusterSlaves. This is **intentional and expected** — Phase 5 translators (CMC-S012, CMC-S013) will add `NodeOpSlaveTranslator` that reads `NodeHeartbeatEvent`/`NodeOpCompletedEvent` from the bus and writes the DDS topics.

If there are integration tests that check DDS heartbeat delivery, update them to check for `NodeHeartbeatEvent` on the bus instead.

### `IOrchestrationTransport` Everywhere Check

Before deleting, run:
```powershell
# From workspace root
Get-ChildItem -Recurse -Filter "*.cs" | Select-String "IOrchestrationTransport" | Select Path,LineNumber,Line
```
Fix any remaining usages before deleting the file.

### Heartbeat Still Needed for Some Tests

The existing `ClusterSlaveTests.cs` and handler tests may create a `ClusterSlave` with the old constructor signature. After CMC-S007 changes the constructor, all test construction calls must be updated.

---

## 🧪 Testing Requirements

All suites must pass after this batch (except the 3 confirmed pre-existing failures from earlier batches):

- `FDP.Toolkit.Orchestration.Tests` — all pass + 4 new bus dispatch tests
- `Hrot.Orchestrator.Tests` — all pass
- `Hrot.SimHost.Tests` — all pass  
- `Hrot.SimHost.Integration.Tests` — all pass (minus the deleted `DdsOrchestrationTransportTests` which disappears)
- `Hrot.Orchestrator.Integration.Tests` — all pass

---

## 🎯 Success Criteria

- [ ] `NodeHeartbeatEvent` defined with EventId 9014, `[DataPolicy(DataPolicy.NoRecord)]`
- [ ] `ClusterSlave.Tick()` reads from `_eventBus.ConsumeManaged<ExecuteNodeOpIntent>()` (no transport poll)
- [ ] `ClusterSlave.Tick()` publishes `NodeHeartbeatEvent` to bus (no `_transport.PublishHeartbeat`)
- [ ] `ClusterSlave.DispatchIntent` publishes `NodeOpCompletedEvent` to bus after prepare completes (success + failure cases)
- [ ] CMC-S006 tests pass (bus dispatch, NodeOpCompleted, heartbeat, null transport)
- [ ] `IOrchestrationTransport.cs` deleted
- [ ] `DdsOrchestrationTransport.cs` deleted
- [ ] `_transport` field removed from `ClusterSlave`
- [ ] Transport constructor parameter removed from `ClusterSlave`
- [ ] 4 composition roots updated (NodeBootstrapper, CgfApplication, IgApplication, ExConSubsystem)
- [ ] `DdsOrchestrationTransportTests.cs` deleted
- [ ] `dotnet build IOS-IG-SimHost.sln` succeeds
- [ ] All test suites pass (see above)
- [ ] Report submitted

---

## 📚 Reference Materials

- DESIGN.md §4: `.dev/cluster-master-cqrs-1/DESIGN.md`
- TASK-DETAIL.md CMC-S006, CMC-S007: `.dev/cluster-master-cqrs-1/TASK-DETAIL.md`
- BATCH-02 REPORT (context on current ClusterSlave state): `.dev/cluster-master-cqrs-1/reports/BATCH-02-REPORT.md`
- `FDP/Toolkits/FDP.Toolkit.Orchestration/ClusterSlave.cs`
- `FDP/Toolkits/FDP.Toolkit.Orchestration/IOrchestrationTransport.cs`
- `Hrot.Common/Orchestration/DdsOrchestrationTransport.cs`
