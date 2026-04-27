# BATCH-04: Replay-to-Live Time Handover

**Batch Number:** BATCH-04  
**Tasks:** RT-017, RT-018, RT-019, RT-020, RT-021  
**Phase:** Phase 6 (Replay-to-Live Time Handover)  
**Estimated Effort:** 4-6 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 through BATCH-03 (all complete)

---

## Onboarding & Workflow

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Design Document:** `.dev/replay-time/DESIGN.md` — Phase 6 section
3. **Task Definitions:** `.dev/replay-time/TASK-DETAIL.md` — RT-017 through RT-021
4. **Previous Reports:** `.dev/replay-time/reports/BATCH-03-REPORT.md`

### Source Code Locations
- `FDP/Engine/Fdp.Core/Orchestration/IRecordReplayController.cs` — RT-017
- `Hrot/Subsystems/Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs` — RT-017
- `Hrot/Network/Hrot.Network.Orchestration/ListenerRecordReplayController.cs` — RT-017
- `Hrot/Subsystems/Hrot.CGF/Modules/Orchestration/CgfRecordReplayController.cs` — RT-017
- `FDP/Toolkits/Fdp.Toolkits/Orchestration/NodeOpPayloads.cs` — RT-018
- `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs` — RT-019
- `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs` — RT-020, RT-021

### Build & Test Commands
```powershell
# Build (check for errors):
dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln --no-restore -v quiet 2>&1 | Select-String "error CS|Build succeeded|FAILED"

# Run Hrot.Orchestrator tests:
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot\Subsystems\Hrot.Orchestrator.Tests\Hrot.Orchestrator.Tests.csproj -v normal

# Run Fdp.Toolkits tests:
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj -v normal
```

**Pre-existing test failures to ignore:**
- `Fdp.Toolkit.Combat.Tests` (5) — unmanaged struct size
- `Fdp.Toolkit.Geographic.Tests.SimTransformBridgeSystemTests` (5)
- `Fdp.Toolkit.Physics.Tests.PhysicsQueryActionNodeTests` (1)
- `Fdp.Toolkit.Replication.Tests.IdAllocationTests` (2)
- `ClusterMasterFanOutTests.PayloadJson_PopulatedFromClusterOpRequest` (1)

### Report Submission
`.dev/replay-time/reports/BATCH-04-REPORT.md`

**IMPORTANT:** Write the report to `.dev/replay-time/reports/BATCH-04-REPORT.md`, NOT to the batches folder.

---

## Context

When a live-from-replay branch completes, the master clock must be snapped to the exact historical position captured by the nodes before they tore down their replay modules. Without this, the master clock continues from wherever it was (often far ahead), causing a permanent offset between the master and all slaves entering `OperatingLive`.

Phase 6:
1. Adds `GetCurrentReplayTime()` — synchronous snapshot of the ECS repo's `GlobalTime` at the moment before replay teardown.
2. Adds `LiveBranchResult` — the ACK payload type carrying `HistoricalTime`.
3. Updates `ReferenceReplayLoadHandler.PrepareLive` to capture time before teardown and return `LiveBranchResult`.
4. Extends `BranchTransitionTask` to store the extracted `HistoricalTime` from the first valid node ACK.
5. Calls `SnapAndPause` in `ConsumeNodeOpStatuses` before `RestoreTime` on branch completion.

---

## MANDATORY WORKFLOW: Test-Driven Task Progression

Complete tasks strictly in order. Build and run relevant tests between each.

1. **RT-017:** Add `GetCurrentReplayTime()` → build passes ✅
2. **RT-018:** Add `LiveBranchResult` struct → build passes ✅
3. **RT-019:** `ReferenceReplayLoadHandler` captures historical time and returns `LiveBranchResult` → build passes ✅
4. **RT-020:** Extend `BranchTransitionTask`, update `ConsumeNodeOpStatuses` capture loop → build + tests pass ✅
5. **RT-021:** Add `SnapAndPause` call on branch completion → build + integration tests pass ✅

---

## Tasks

### RT-017: Add `GetCurrentReplayTime()` to `IRecordReplayController`

**File:** `FDP/Engine/Fdp.Core/Orchestration/IRecordReplayController.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-017-add-getcurrentreplaytime-to-irecordreplaycontroller) — RT-017

Add the following to `IRecordReplayController`:
```csharp
/// <summary>
/// Returns the current replay position as a <see cref="GlobalTime"/> snapshot.
/// Must be called BEFORE <see cref="TeardownReplayAsync"/> — after teardown,
/// the replay module is uninstalled and the time singleton reverts.
/// Listener and CGF implementations return <c>default(GlobalTime)</c>.
/// </summary>
GlobalTime GetCurrentReplayTime();
```

**Implementations:**

`EcsRecordReplayController`:
```csharp
public GlobalTime GetCurrentReplayTime() =>
    _activeReplayModule != null ? _timeController.GetCurrentState() : default;
```
Check whether `_timeController` exists as a field in `EcsRecordReplayController` (added in BATCH-01). If the field is named differently (e.g., `_kernel.GetTimeController()`), use that.

`ListenerRecordReplayController`:
```csharp
public GlobalTime GetCurrentReplayTime() => default;
```

`CgfRecordReplayController`:
```csharp
public GlobalTime GetCurrentReplayTime() => default;
```

**Verify:** `dotnet build` passes. No dedicated unit test needed here (integration covered by RT-019 tests).

---

### RT-018: Define `LiveBranchResult` Payload Struct

**File:** `FDP/Toolkits/Fdp.Toolkits/Orchestration/NodeOpPayloads.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-018-define-livebranchresult-payload-struct) — RT-018

Add immediately after `ReplaySeekResult`:
```csharp
public readonly record struct LiveBranchResult(GlobalTime HistoricalTime);
```

**Verify:** `dotnet build` passes.

---

### RT-019: `ReferenceReplayLoadHandler` Returns `LiveBranchResult` on `PrepareLive`

**File:** `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-019-referencereplayloadhandler-returns-livebranchresult-on-preparelive) — RT-019

Current `PrepareLive` branch in `PrepareAsync`:
```csharp
else if (intent.Operation == NodeOpType.PrepareLive)
{
    // CGF1-S0305: Live-from-Replay branch.
    var branchedExerciseId = ResolveExerciseId(intent.DomainPayload);
    await _controller.TeardownReplayAsync().ConfigureAwait(false);
    await _controller.PrepareRecordingAsync(branchedExerciseId, _storageDirectory)
        .ConfigureAwait(false);

    FdpLog<ReferenceReplayLoadHandler>.Info(
        "[ReferenceReplayLoadHandler] Live-from-Replay branch complete (branchedExerciseId={0}).",
        branchedExerciseId);
}
```

Change to:
```csharp
else if (intent.Operation == NodeOpType.PrepareLive)
{
    // CGF1-S0305: Live-from-Replay branch.
    // Capture the historical time BEFORE teardown; after TeardownReplayAsync the
    // replay module is gone and _controller.GetCurrentReplayTime() returns default.
    GlobalTime historicalTime = _controller.GetCurrentReplayTime();

    var branchedExerciseId = ResolveExerciseId(intent.DomainPayload);
    await _controller.TeardownReplayAsync().ConfigureAwait(false);
    await _controller.PrepareRecordingAsync(branchedExerciseId, _storageDirectory)
        .ConfigureAwait(false);

    FdpLog<ReferenceReplayLoadHandler>.Info(
        "[ReferenceReplayLoadHandler] Live-from-Replay branch complete (branchedExerciseId={0}, historicalWallTicks={1}).",
        branchedExerciseId,
        historicalTime.TotalWallTicks);

    return new LiveBranchResult(historicalTime);
}
```

The existing `return null;` at the end of `PrepareAsync` handles all other branches.

**Verify:** `dotnet build` passes.

---

### RT-020: Add `TimeExtracted` Flag and `HistoricalTime` to `BranchTransitionTask`

**File:** `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs` (UPDATE — 2 changes)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-020-add-timeextracted-flag-to-branchtransitiontask) — RT-020

#### Change 1: Extend `BranchTransitionTask`

Current:
```csharp
private sealed class BranchTransitionTask
{
    public int  RemainingAcks;
    public Guid RequestId;  // for bus-mode: publish ClusterOpStatus(Success) when branch ACKs complete
}
```

Change to:
```csharp
private sealed class BranchTransitionTask
{
    public int        RemainingAcks;
    public Guid       RequestId;  // for bus-mode: publish ClusterOpStatus(Success) when branch ACKs complete
    public bool       TimeExtracted;
    public GlobalTime HistoricalTime;
}
```

Note: `GlobalTime` is already imported in `ClusterMaster.cs` (it's used elsewhere). No new `using` needed.

#### Change 2: Capture `LiveBranchResult` in the ACK accumulation loop

In `ConsumeNodeOpStatuses`, the branch task block currently is:
```csharp
if (_pendingBranchTasks.TryGetValue(ev.TransactionId, out var branchTask))
{
    branchTask.RemainingAcks--;
    if (branchTask.RemainingAcks <= 0)
    {
        _pendingBranchTasks.Remove(ev.TransactionId);
        _replayMasterModule?.RestoreTime();
        PublishOpStatus(branchTask.RequestId, OrchestrationStatusCode.Success);
        FdpLog<ClusterMaster>.Info(
            "[Orchestrator] S0305 (bus): All branch ACKs received — time scale restored.");
    }
    continue;
}
```

Change to:
```csharp
if (_pendingBranchTasks.TryGetValue(ev.TransactionId, out var branchTask))
{
    if (!branchTask.TimeExtracted &&
        ev.ResultPayload is LiveBranchResult lbr &&
        lbr.HistoricalTime.TotalWallTicks != 0)
    {
        branchTask.TimeExtracted  = true;
        branchTask.HistoricalTime = lbr.HistoricalTime;
    }
    branchTask.RemainingAcks--;
    if (branchTask.RemainingAcks <= 0)
    {
        _pendingBranchTasks.Remove(ev.TransactionId);
        _replayMasterModule?.RestoreTime();
        PublishOpStatus(branchTask.RequestId, OrchestrationStatusCode.Success);
        FdpLog<ClusterMaster>.Info(
            "[Orchestrator] S0305 (bus): All branch ACKs received — time scale restored.");
    }
    continue;
}
```

(RT-021 will update the finalization block in the next step.)

**Verify:** `dotnet build` passes. Run `Hrot.Orchestrator.Tests` — all existing tests pass.

---

### RT-021: Master Atomic Snap on Branch Completion

**File:** `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-021-master-atomic-snap-on-branch-completion-in-consumernodeopstatuses) — RT-021

Update the branch completion block (from RT-020 change 2) to add `SnapAndPause` before `RestoreTime`:

```csharp
if (branchTask.RemainingAcks <= 0)
{
    _pendingBranchTasks.Remove(ev.TransactionId);
    if (branchTask.TimeExtracted)
    {
        _masterSync?.SnapAndPause(
            branchTask.HistoricalTime.TotalWallTicks,
            branchTask.HistoricalTime.TotalTime,
            new HashSet<int>(_roster.ActiveNodes.Keys));
    }
    _replayMasterModule?.RestoreTime();
    PublishOpStatus(branchTask.RequestId, OrchestrationStatusCode.Success);
    FdpLog<ClusterMaster>.Info(
        "[Orchestrator] S0305 (bus): All branch ACKs received — time scale restored.");
}
```

**New tests in `Hrot.Orchestrator.Tests`** (add to existing `ClusterMasterReplayTests.cs` or create `ClusterMasterBranchTests.cs`):

- `LiveBranch_OnAllNodesAck_WithLiveBranchResult_SnapsAndPausesMasterClock` (T21a/T21b):
  1. Create `MasterSyncController` with fake tick source.
  2. Create `ClusterMaster`, call `SetMasterSync(masterSync)` and `SetReplayMasterModule(module)`.
  3. Bootstrap cluster with one SimHost node (look at `ClusterMasterReplayTests.cs` for the bootstrap pattern).
  4. Trigger a `PrepareLive` branch fan-out (or simulate it by directly calling the internal path that populates `_pendingBranchTasks`).
     
     **Simpler approach:** Look at how `ClusterMasterReplayTests.cs` bootstraps the branch fan-out. The test calls `HandleClusterOpRequest` with `TransitionState` to the `OperatingLive` state from `OperatingReplay`. Or you can simulate the ACK directly:
     - Inject a `BranchTransitionTask` by triggering the transition. Look at the existing tests for the pattern.
  5. Publish `NodeOpCompletedEvent { TransactionId = branchTxId, NodeId = nodeId, StatusCode = Success, ResultPayload = new LiveBranchResult(new GlobalTime { TotalWallTicks = 7777L, TotalTime = 3.0f }) }`.
  6. Tick + swap buffers.
  7. Assert `masterSync.GetCurrentState().TotalWallTicks == 7777L`.
  8. Assert `masterSync.GetMode() == TimeMode.Deterministic`.

- `LiveBranch_OnAllNodesAck_WithDefaultResult_DoesNotSnapMasterClock` (T21d):
  1. Same setup but ACK with `new LiveBranchResult(default(GlobalTime))`.
  2. Verify master clock was NOT changed (TotalWallTicks unchanged from initial value).

**Pattern for getting `branchTxId`:**  
Look at `ClusterMasterReplayTests.cs` — it shows how the branch transition is triggered. The `ExecuteNodeOpIntent` with `Operation == NodeOpType.PrepareLive` carries the `TransactionId` that maps to the `BranchTransitionTask`. After `Tick()`, read `ExecuteNodeOpIntent` from the bus.

**Finding the correct approach for bootstrapping the branch:**
Read `ClusterMasterReplayTests.cs` (around 40-140 lines) to understand how the existing tests set up a `PrepareLive` transition. The existing tests call `SetReplayMasterModule` and set the cluster state to `OperatingReplay` before triggering the branch. Follow the exact same pattern.

---

## Quality Standards

- Match existing code style in all files.
- Preserve all existing comments exactly.
- Do not use unicode characters in comments or string literals.
- Only change lines required for the functional fix.

---

## Success Criteria

This batch is DONE when:
- [ ] RT-017: `GetCurrentReplayTime()` added to interface + 3 implementations
- [ ] RT-018: `LiveBranchResult(GlobalTime HistoricalTime)` added to `NodeOpPayloads.cs`
- [ ] RT-019: `ReferenceReplayLoadHandler.PrepareLive` captures historical time before teardown, returns `LiveBranchResult`
- [ ] RT-020: `BranchTransitionTask` has `TimeExtracted` + `HistoricalTime`; ACK accumulation captures first valid `LiveBranchResult`
- [ ] RT-021: `SnapAndPause` called before `RestoreTime` when `TimeExtracted == true`; T21a-T21d tests pass
- [ ] All existing tests that were passing before this batch still pass
- [ ] Solution builds with zero `error CS` lines

---

## Developer Insights (Required in Report)

**Q1:** For RT-017: why must `GetCurrentReplayTime()` be called BEFORE `TeardownReplayAsync()`? What happens to the ECS time singleton after teardown?

**Q2:** For RT-021: the order is `SnapAndPause` → `RestoreTime` → `PublishOpStatus`. Why is `SnapAndPause` placed BEFORE `RestoreTime`? What would happen if they were swapped?

**Q3:** Any issues encountered with the `LiveBranchResult` payload casting (boxing/unboxing)?

**Q4:** Suggested commit message.

---

## This is the FINAL BATCH

After completing this batch, all 21 replay-time tasks (RT-001 through RT-021) will be complete. Update the TASK-TRACKER.md after the review and commit.
