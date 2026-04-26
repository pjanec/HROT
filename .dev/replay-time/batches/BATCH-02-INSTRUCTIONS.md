# BATCH-02: 2PC Seek Fix + ClusterUiCache Visual Fix

**Batch Number:** BATCH-02  
**Tasks:** RT-007, RT-008, RT-009, RT-016  
**Phase:** Phase 3 (2PC Seek Fix) + Phase 5 (ClusterUiCache Visual Fix)  
**Estimated Effort:** 4-6 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (completed)

---

## Onboarding & Workflow

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Design Document:** `.dev/replay-time/DESIGN.md` — Phase 3 and Phase 5 sections
3. **Task Definitions:** `.dev/replay-time/TASK-DETAIL.md` — RT-007 through RT-009 and RT-016
4. **Previous Report:** `.dev/replay-time/reports/BATCH-01-REPORT.md`

### Source Code Location
- **Primary Work Area 1:** `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs`
- **Primary Work Area 2:** `Hrot/Subsystems/Hrot.Orchestrator/Panels/ClusterUiCache.cs`
- **Test Projects:**
  - `Hrot/Subsystems/Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj`

### Build & Test Commands
```powershell
# Build (check for errors):
dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln --no-restore -v quiet 2>&1 | Select-String "error CS|Build succeeded|FAILED"

# Run tests:
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot\Subsystems\Hrot.Orchestrator.Tests\Hrot.Orchestrator.Tests.csproj -v normal
```

### Report Submission
**When done, submit your report to:**
`.dev/replay-time/reports/BATCH-02-REPORT.md`

**If you have questions, create:**
`.dev/replay-time/questions/BATCH-02-QUESTIONS.md`

---

## Context

Phase 3 fixes a 2PC correctness bug: `ClusterMaster` currently sends `PublishOpStatus(Success)` immediately upon receiving a `ReplaySeek` request, before any node has ACKed the underlying `NodeReplaySeek` fan-out. This means the caller believes the seek is done before nodes have actually moved their replay cursors. RT-007/RT-008 replace this with proper `BusTransitionAckTracker`-based 2PC completion. RT-009 injects a cluster-wide pause before the seek fan-out so all slave time controllers are in `Stepping` mode when the seek command arrives.

Phase 5 (RT-016) is a standalone one-line visual fix in `ClusterUiCache.Process2PcNetworkTraffic`: the default `TargetDsmState = ClusterState.Idle` makes seek operations appear as `Idle→Idle` in the 2PC history panel, which is confusing. Defaulting to `CurrentState` shows the correct `OperatingReplay→OperatingReplay`.

---

## MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: Complete tasks strictly in order with passing tests between each.**

1. **RT-007:** Remove premature `PublishOpStatus` → fix existing tests → **ALL tests pass** ✅
2. **RT-008:** Refactor `ProcessSeekReplayIntent` with ACK tracker → write new tests → **ALL tests pass** ✅
3. **RT-009:** Add server-side pause before seek fan-out → write new tests → **ALL tests pass** ✅
4. **RT-016:** `ClusterUiCache` default state fix → write new test → **ALL tests pass** ✅

Do NOT move to the next task until all tests pass.

---

## Tasks

### RT-007: Remove Premature `PublishOpStatus` from `ReplaySeek` Case

**File:** `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-007-remove-premature-publishopstatus-from-replaysee-case) — RT-007

There are **two** places where a premature `PublishOpStatus(Success)` must be removed for the seek operation:

**1. In `ProcessSingleClusterOpRequest` (injection path, around line 510):**

Current code:
```csharp
case ClusterOpType.ReplaySeek:
    ProcessSeekReplayIntent(ClusterOpRequestAdapter.ToSeekReplayIntent(req));
    PublishOpStatus(req.RequestId, OrchestrationStatusCode.Success);
    break;
```

Remove the `PublishOpStatus` line. The `ProcessSeekReplayIntent` call stays. After RT-008 registers the ACK tracker, completion is published when all nodes ACK.

**2. In `ProcessSeekReplayIntents` (bus drain path, around line 693):**

Current code:
```csharp
private void ProcessSeekReplayIntents()
{
    foreach (var intent in _eventBus.ReadManaged<SeekReplayIntent>())
    {
        ProcessSeekReplayIntent(intent);
        PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Success);
    }
}
```

Remove the `PublishOpStatus` line from the drain loop as well. After RT-008, `ProcessSeekReplayIntent` itself registers the tracker (or immediately publishes Success when there are 0 nodes).

**Impact on existing tests:**

`ClusterMasterCheckpointTests.ReplaySeek_BusMode_PublishesImmediateSuccess` currently asserts immediate success. After RT-007+RT-008, success is still published immediately when there are **0 active nodes** (the early-return path in RT-008). The test registers no nodes, so it still passes. No test change needed.

`ClusterMasterFanOutTests.ReplaySeekStep_FansOutNodeReplaySeek` just checks that `NodeReplaySeek` is fanned out — that behavior is unchanged.

---

### RT-008: Refactor `ProcessSeekReplayIntent` with `BusTransitionAckTracker`

**File:** `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-008-refactor-processseekereplayintent-with-bustransitionacktracker) — RT-008

Replace the current `ProcessSeekReplayIntent` body with:

```csharp
private void ProcessSeekReplayIntent(SeekReplayIntent intent)
{
    var seekNodeIds = new List<int>(_roster.ActiveNodes.Keys);
    if (seekNodeIds.Count == 0)
    {
        PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Success);
        return;
    }
    var txId = Guid.NewGuid();
    FanOutNodeOp(NodeOpType.NodeReplaySeek, txId,
        new ReplaySeekPayload(intent.TargetWallTicks), seekNodeIds);
    _pendingBusTransitionAcks[txId] = new BusTransitionAckTracker
    {
        RequestId = intent.RequestId,
        Expected  = seekNodeIds.Count,
    };
}
```

The existing `ConsumeNodeOpStatuses` loop already handles `_pendingBusTransitionAcks` — when `tracker.Received >= tracker.Expected`, it publishes `Success`. No new code needed in `ConsumeNodeOpStatuses` for this task.

**New test in `Hrot.Orchestrator.Tests`:**
- `ReplaySeek_WithActiveNodes_RegistersAckTracker_AndDoesNotPublishImmediateSuccess`:
  1. Bootstrap one node into the cluster (send a heartbeat, call `Tick` to register it).
  2. Send a `ReplaySeek` request via `HandleClusterOpRequest`.
  3. Call `Tick`.
  4. Swap bus buffers and read `ClusterOpCompletedEvent` — assert NONE are published yet.
  5. Assert a `NodeReplaySeek` fan-out command was published (via `ExecuteNodeOpIntent` on bus).
  6. Simulate the node ACK by publishing `NodeOpCompletedEvent { TransactionId = fanned-out txId, NodeId = ..., StatusCode = Success }`.
  7. Call `Tick` again.
  8. Swap + read — assert `ClusterOpCompletedEvent(Success)` is now published with the original `RequestId`.

---

### RT-009: Server-Side Pause Precondition in `ProcessSeekReplayIntent`

**File:** `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-009-server-side-pause-precondition-in-processseekereplayintent) — RT-009

At the TOP of `ProcessSeekReplayIntent` (before the empty-node guard), add:

```csharp
var slaveIds = _roster.ActiveNodes
    .Where(kv => kv.Value.SubsystemName is "SimHost" or "IG" or "CGF")
    .Select(kv => kv.Key)
    .ToHashSet();
_eventBus.PublishManaged(new SlaveNodeSetUpdatedEvent { SlaveNodeIds = slaveIds });
_eventBus.PublishManaged(new PauseTimeIntent());
```

The full method becomes:

```
[RT-009 pause events]
[RT-008 guard + fan-out + ACK tracker]
```

**New test:**
- `ReplaySeek_AlwaysPublishes_SlaveNodeSetUpdatedEvent_And_PauseTimeIntent`:
  1. Bootstrap one SimHost node.
  2. Send a `ReplaySeek` via `HandleClusterOpRequest`.
  3. Call `Tick`.
  4. Swap bus buffers; read managed events.
  5. Assert the bus contains `SlaveNodeSetUpdatedEvent` and `PauseTimeIntent` events.

---

### RT-016: Default `SourceDsmState`/`TargetDsmState` to `CurrentState` in `ClusterUiCache`

**File:** `Hrot/Subsystems/Hrot.Orchestrator/Panels/ClusterUiCache.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-016-default-sourcedsmsatetargetdsmstate-to-currentstate) — RT-016

In `Process2PcNetworkTraffic`, in the block that creates a `DistributedTransaction` for a new `txId`:

**Before:**
```csharp
var targetState = ClusterState.Idle;
if (intent.DomainPayload is EditLoadHandlerPayload ep)
    targetState = (ClusterState)ep.TargetState;
else if (intent.DomainPayload is CommitStatePayload cp)
    targetState = (ClusterState)cp.TargetStateId;
else if (intent.DomainPayload is int raw)
    targetState = (ClusterState)raw;

var tx = new DistributedTransaction
{
    TransactionId  = txId,
    TargetDsmState = targetState,
    PayloadJson    = SerializePayload(intent.DomainPayload),
};
```

**After:**
```csharp
var targetState = CurrentState;
if (intent.DomainPayload is EditLoadHandlerPayload ep)
    targetState = (ClusterState)ep.TargetState;
else if (intent.DomainPayload is CommitStatePayload cp)
    targetState = (ClusterState)cp.TargetStateId;
else if (intent.DomainPayload is int raw)
    targetState = (ClusterState)raw;

var tx = new DistributedTransaction
{
    TransactionId  = txId,
    SourceDsmState = CurrentState,
    TargetDsmState = targetState,
    PayloadJson    = SerializePayload(intent.DomainPayload),
};
```

**New test in `Hrot.Orchestrator.Tests` (or in the existing `OrchestratorSubsystemBusTests`):**
- `ClusterUiCache_ReplaySeekOp_HasSourceAndTargetEqualToCurrentState`:
  1. Construct `ClusterUiCache`.
  2. Set `CurrentState` to `OperatingReplay` (by publishing a `ClusterStateChangedEvent` on the bus and calling `Update`).
  3. Publish an `ExecuteNodeOpIntent` with `Operation = NodeReplaySeek` and `DomainPayload = new ReplaySeekPayload(12345)`.
  4. Call `Update`.
  5. Assert the newly created `DistributedTransaction` in `TransactionHistory` has `SourceDsmState == OperatingReplay` and `TargetDsmState == OperatingReplay`.

---

## Quality Standards

- Match existing code style and brace placement in `ClusterMaster.cs`.
- Do not change any unrelated methods.
- Tests should use real `FdpEventBus` instances (same pattern as existing `ClusterMasterCheckpointTests`).

---

## Success Criteria

This batch is DONE when:
- [ ] RT-007: Premature `PublishOpStatus` removed from both `ProcessSingleClusterOpRequest` (injection path) and `ProcessSeekReplayIntents` (bus drain path)
- [ ] RT-008: `ProcessSeekReplayIntent` registers `BusTransitionAckTracker`; deferred success on ACK
- [ ] RT-009: `SlaveNodeSetUpdatedEvent` + `PauseTimeIntent` published before every seek fan-out
- [ ] RT-016: `ClusterUiCache` defaults `SourceDsmState` and `TargetDsmState` to `CurrentState`
- [ ] All existing `Hrot.Orchestrator.Tests` pass
- [ ] New tests for T7a, T8a-T8c, T9a-T9b, T16a-T16c pass
- [ ] Solution builds with zero `error CS` lines

---

## Developer Insights (Required in Report)

**Q1:** What issues did you encounter? How did you resolve them?

**Q2:** For RT-008: is there a race condition risk if two `ReplaySeek` requests arrive in the same tick? How does the `BusTransitionAckTracker` dictionary handle that?

**Q3:** What design decisions did you make beyond the spec?

**Q4:** Any weak points in `ClusterMaster` you noticed while working on this?

**Q5:** Suggested commit message.
