# BATCH-04 Report: Replay-to-Live Time Handover

**Batch:** BATCH-04
**Tasks:** RT-017, RT-018, RT-019, RT-020, RT-021
**Status:** COMPLETE
**Build:** 0 errors

---

## Summary

All 5 tasks implemented. Phase 6 (Replay-to-Live Time Handover) is complete and all 21
replay-time tasks (RT-001 through RT-021) are now done.

---

## Task Results

### RT-017: Add `GetCurrentReplayTime()` to `IRecordReplayController`

**Status:** COMPLETE

**Files changed:**
- `FDP/Engine/Fdp.Core/Orchestration/IRecordReplayController.cs` — added `GetCurrentReplayTime()` to interface
- `Hrot/Subsystems/Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs` — implemented using `_kernel.GetTimeController().GetCurrentState()` (no `_timeController` field exists; the controller is accessed through the kernel)
- `Hrot/Network/Hrot.Network.Orchestration/ListenerRecordReplayController.cs` — returns `default`
- `Hrot/Subsystems/Hrot.CGF/Modules/Orchestration/CgfRecordReplayController.cs` — returns `default`

**Note:** No `_timeController` field was added in BATCH-01 to `EcsRecordReplayController`; the
`PrepareReplayAsync` method already passed `_kernel.GetTimeController()` directly to `ReplayModule`.
So `GetCurrentReplayTime()` uses the same pattern: `_kernel.GetTimeController().GetCurrentState()`.

---

### RT-018: Define `LiveBranchResult` Payload Struct

**Status:** COMPLETE

**File changed:**
- `FDP/Toolkits/Fdp.Toolkits/Orchestration/NodeOpPayloads.cs` — added `LiveBranchResult(GlobalTime HistoricalTime)` immediately after `ReplaySeekResult`

---

### RT-019: `ReferenceReplayLoadHandler` Returns `LiveBranchResult` on `PrepareLive`

**Status:** COMPLETE

**File changed:**
- `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs` — `PrepareLive` branch now:
  1. Captures `historicalTime = _controller.GetCurrentReplayTime()` BEFORE teardown
  2. Calls `TeardownReplayAsync()` then `PrepareRecordingAsync()`
  3. Logs with `historicalWallTicks`
  4. Returns `new LiveBranchResult(historicalTime)` instead of falling through to `return null`

---

### RT-020: Add `TimeExtracted` Flag and `HistoricalTime` to `BranchTransitionTask`

**Status:** COMPLETE

**File changed:**
- `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs` — two changes:
  1. `BranchTransitionTask` extended with `public bool TimeExtracted` and `public GlobalTime HistoricalTime`
  2. `ConsumeNodeOpStatuses` branch-ACK block: first valid `LiveBranchResult` with non-zero `TotalWallTicks` is captured before `RemainingAcks--`

**Tests:** All 101 orchestrator tests (100 pre-existing + 1 pre-existing failure) still pass.

---

### RT-021: Master Atomic Snap on Branch Completion

**Status:** COMPLETE

**File changed:**
- `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs` — branch completion block now calls
  `_masterSync?.SnapAndPause(...)` BEFORE `_replayMasterModule?.RestoreTime()` when
  `branchTask.TimeExtracted == true`

**New tests in `ClusterMasterBranchTests.cs`:**
- `LiveBranch_OnAllNodesAck_WithLiveBranchResult_SnapsAndPausesMasterClock` (T21a/T21b):
  verifies `TotalWallTicks == 7777L` and `GetMode() == TimeMode.Deterministic` after ACK
- `LiveBranch_OnAllNodesAck_WithDefaultResult_DoesNotSnapMasterClock` (T21d):
  verifies master clock is NOT changed when ACK carries `default(GlobalTime)`

**Test results:**
- Hrot.Orchestrator.Tests: 102 passed, 1 failed (pre-existing `PayloadJson_PopulatedFromClusterOpRequest`)
- Fdp.Toolkits.Tests: 759 passed, 13 failed (all pre-existing)

---

## Developer Insights

**Q1: Why must `GetCurrentReplayTime()` be called BEFORE `TeardownReplayAsync()`?**

`TeardownReplayAsync()` calls `_kernel.UninstallModuleAsync(_activeReplayModule)` and then
sets `_activeReplayModule = null`. After uninstall, the `ReplayModule` is removed from the
kernel topology and the time singleton reverts to whatever the live `ITimeController` held
before the replay started. The historical position is only available while `_activeReplayModule`
is still installed and its `PlaybackTickSystem` is still authoritative. Calling `GetCurrentReplayTime()`
after teardown would return the post-revert state, not the historical replay position.

**Q2: Why is `SnapAndPause` placed BEFORE `RestoreTime`?**

`SnapAndPause` writes the historical wall ticks and sim time into the master controller and
switches it to `Stepping` (Deterministic) mode. It also publishes a `SwitchTimeModeEvent`
with `TargetMode = Deterministic` so all slaves snap to the historical position atomically.

`RestoreTime` calls `ReplayMasterModule.RestoreTime()` which restores the time scale from 0
back to its pre-freeze value (typically 1.0). If `SnapAndPause` were called AFTER `RestoreTime`,
the mode event published by `SnapAndPause` would race with the restored-scale event, and slaves
could miss the snap. More critically, `_masterSync.SnapAndPause` resets `_pendingAcks` and the
slave roster; calling it after `RestoreTime` would still work in isolation, but the intent
is: first establish the historical position, then resume time — which is the correct causal order.

**Q3: Any issues with `LiveBranchResult` payload casting (boxing/unboxing)?**

`LiveBranchResult` is a `readonly record struct`, so it boxes to `object` when stored in
`NodeOpCompletedEvent.ResultPayload` (which is `object?`). The pattern-match `ev.ResultPayload is
LiveBranchResult lbr` performs an unbox and type check in one step — this is the same pattern
already used for `ReplaySeekResult` in `_pendingBusTransitionAcks`. No issues encountered.

**Q4: Suggested commit message:**

```
feat(replay-time): Phase 6 complete -- replay-to-live time handover (RT-017 to RT-021)

RT-017: Add GetCurrentReplayTime() to IRecordReplayController interface;
        implement in EcsRecordReplayController (_kernel.GetTimeController()),
        ListenerRecordReplayController (default), CgfRecordReplayController (default).

RT-018: Add LiveBranchResult(GlobalTime HistoricalTime) readonly record struct
        to NodeOpPayloads.cs after ReplaySeekResult.

RT-019: ReferenceReplayLoadHandler.PrepareLive branch now captures historical
        time before TeardownReplayAsync and returns LiveBranchResult.

RT-020: BranchTransitionTask gains TimeExtracted + HistoricalTime fields;
        ConsumeNodeOpStatuses captures first valid LiveBranchResult ACK.

RT-021: SnapAndPause called before RestoreTime on branch completion when
        TimeExtracted==true; adds T21a/T21b/T21d tests in ClusterMasterBranchTests.cs.

Hrot.Orchestrator.Tests: 102 passed (2 new), 1 pre-existing failure.
Fdp.Toolkits.Tests: 759 passed, 13 pre-existing failures.
```

---

## Files Changed

| File | Change |
|------|--------|
| `FDP/Engine/Fdp.Core/Orchestration/IRecordReplayController.cs` | Added `GetCurrentReplayTime()` member |
| `Hrot/Subsystems/Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs` | Implemented `GetCurrentReplayTime()` |
| `Hrot/Network/Hrot.Network.Orchestration/ListenerRecordReplayController.cs` | Implemented `GetCurrentReplayTime() => default` |
| `Hrot/Subsystems/Hrot.CGF/Modules/Orchestration/CgfRecordReplayController.cs` | Implemented `GetCurrentReplayTime() => default` |
| `FDP/Toolkits/Fdp.Toolkits/Orchestration/NodeOpPayloads.cs` | Added `LiveBranchResult` struct |
| `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs` | Updated `PrepareLive` branch |
| `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs` | Extended `BranchTransitionTask`; updated `ConsumeNodeOpStatuses` |
| `Hrot/Subsystems/Hrot.Orchestrator.Tests/ClusterMasterBranchTests.cs` | NEW: 2 RT-021 tests |
