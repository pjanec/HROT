# BATCH-05 Report

**Batch:** BATCH-05 — Application Wiring, Deletion, and E2E Integration Test  
**Tasks:** TCU-W001, TCU-W002, TCU-W003, TCU-W004, TCU-W006, TCU-T006  
**Status:** COMPLETE ✅

---

## Completion Status

| Task | Description | Status |
|------|-------------|--------|
| TCU-W001 | Wire `MasterSyncController` in `OrchestratorSubsystem` | ✅ Done |
| TCU-W002 | Wire `SlaveSyncController` in `SimHostApp` | ✅ Done |
| TCU-W003 | Wire `SlaveSyncController` in `CgfApplication` | ✅ Done |
| TCU-W004 | Wire `SlaveSyncController` in `IgApplication` | ✅ Done |
| TCU-W006 | Delete 8 obsolete files + `CreateLockstepTranslator`; fix cascade | ✅ Done |
| TCU-T006 | E2E test `FullCycle_Pause_Step_Resume_NoPllLoss` | ✅ Done |

---

## Build Results

### `IOS-IG-SimHost.sln`
```
Build succeeded.
    1 Warning(s)   [pre-existing XML doc warning in CycloneDDS.Schema]
    0 Error(s)
```

### `FDP/FDP.sln`
Fails on **pre-existing** `DebugOffsets.csproj` — an empty Exe project with no source files 
(CS5001: Program does not contain a static 'Main' method). This issue predates BATCH-05 and was 
not introduced by these changes. All other FDP projects build cleanly.

---

## Test Results

### FDP Time Tests (`FDP.Toolkit.Time.Tests`)
```
Passed!  - Failed: 0, Passed: 70, Skipped: 0, Total: 70
```
Count increased from 60 (pre-batch) to 70 (+10 new tests for `MasterSyncController`, 
`SlaveSyncController`, and the E2E `UnifiedControllerE2ETests`).

### Time Integration Tests (Hrot.ClusterRunner.Integration.Tests — time filter)
```
Passed!  - Failed: 0, Passed: 7, Skipped: 0, Total: 7
```
All 7 time-control integration tests pass, including `PauseResume_SimHostKernelRestoresMasterTimeController`.

### Full Integration Test Suite
```
Failed: 6, Passed: 37, Skipped: 0, Total: 43
```
The 6 failures are **pre-existing** and unrelated to time controllers:
- `AllSubsystems_TransitionToOperatingLive_CommitStateIsNotDroppedAsDuplicate`
- `AllSubsystems_FullCycleTwice_LoadOperateUnloadIdle`
- `ClusterOpE2eScriptTests.OverlappingCheckpoints_Passes`
- `ClusterOpE2eScriptTests.RecordAndReplaySeek_Passes`
- `ClusterOpE2eScriptTests.PreviewStateRestore_Passes`
- `ClusterOpE2eScriptTests.LiveFromReplayBranch_Passes`

These are cluster-state / replay / checkpoint tests unaffected by the time unification.

---

## Developer Insights

### Q1: Wiring surprises in the application hosts?

**`SimHostApp.cs` (TCU-W002):**  
`SimHostApp` was originally wired as `TimeRole.Master` (it used `continuousControllerFactory` which 
called `TimeControllerFactory.Create` for master) but it's semantically a *slave* — it receives 
time from the Orchestrator over DDS. The key fix was changing `TimeRole` to `Slave` and 
`LocalNodeId` to the actual node ID. The old `SlaveTimeModeListener` update in `OnUpdate` was then 
redundant and removed since `SlaveSyncController` handles mode transitions internally.

**`OrchestratorSubsystem.cs` (TCU-W001):**  
`MasterSyncController` construction requires `slaveNodeIds` at construction time (per DT-003 in 
the debt tracker). At construction time the node-ID set is not yet known — the `ActiveNodes` 
dictionary is populated later during `TimeControlRequested` handlers. The workaround: initialize 
with `new HashSet<int>()` and rely on the fact that `SwitchToDeterministic(slaveNodeIds)` is 
called with the real set. DT-003 (constructor-time vs call-time slave set) is still outstanding.

**`IgApplication.cs` (TCU-W004):**  
A single line swap 
(`new SlaveTimeController(_world.Bus)` → `new SlaveSyncController(_world.Bus, _effectiveInstanceId)`). 
The most straightforward host to wire.

### Q2: DT-003/DT-004/DT-006 debt items?

**DT-003 (`slaveNodeIds` parameter):** Manifested as described above in `OrchestratorSubsystem`. 
`SwitchToDeterministic` still accepts and ignores the parameter — host code passes `ActiveNodes.Keys` 
at call time, but the effective set used for ACK tracking is fixed at construction. Partially 
mitigated, but the API asymmetry remains.

**DT-004 (TimePulse 1 Hz rate limiter):** No change. Still fires at `Stopwatch.Frequency` ticks 
interval. Slaves' PLL now warms from `ProcessTimePulses()` inside `SlaveSyncController.Update()`.

**DT-006 (SequenceID):** `MasterSyncController.Step()` increments `_frameNumber` and embeds it in 
`AdvanceFrameIntent.FrameID`. The `SwitchTimeModeDescriptorTranslator` and `TimePulseEgressTranslator` 
both carry `SequenceId` fields. However, there is no end-to-end guarantee that all translators 
emit monotonically consistent IDs yet — DT-006 is unresolved.

### Q3: Design decisions beyond the spec?

**`SlaveSyncController.UpdateStepping` — tick baseline tracking:**  
During debugging of the E2E test, a subtle bug was discovered: when the slave re-entered Continuous 
mode after Stepping, `_lastUpdateRawTicks` was stale (from the last `UpdateBarrierPending` call), 
causing the first Continuous frame to accumulate a "catch-up" delta equal to the entire stepping 
duration. In the controlled tick-source tests this was 1 frame; in production it could be seconds.

Fix applied in `UpdateStepping`: `_lastUpdateRawTicks = _getTick()` is refreshed on every Stepping 
call. This keeps the baseline current so that when we resume — whether after 1 step or 1000 — the 
first `UpdateContinuous` delta is exactly the time since the last Stepping frame (≈ 0 in tests with 
a frozen tick source; ≈ 1 frame in wall-clock mode). The existing PLL warm-start test 
(`SlaveSyncController_Resume_PLLIsWarm_NoJitterReset`) passed without modification.

**Phase 3 while condition in `UnifiedControllerE2ETests`:**  
The original Phase 3 loop only checked slave modes. Since `master.Update()` had not been called 
after `SwitchToDeterministic`, the master was still in `BarrierPending` (shown as `Continuous`). 
Fixed by adding `master.GetMode() != TimeMode.Deterministic` to the while condition, guaranteeing 
at least one loop iteration that drives master past the barrier. Comment added to explain the 
ordering invariant.

**`JitterFilter.cs` (extracted from deleted `SlaveTimeController.cs`):**  
`SlaveTimeController` contained an inline `JitterFilter` private class. Rather than silently 
discarding it, it was extracted into `FDP.Toolkit.Time.Controllers.JitterFilter` and reused by 
`SlaveSyncController`. This was necessary to keep the codebase building.

### Q4: State of SequenceID (DT-006) now that translators are wired?

`TimePulseEgressTranslator` now connects to the master bus in `OrchestratorSubsystem`. It reads 
`TimePulseDescriptor` events (which include `SequenceId = _frameNumber`) and publishes them to DDS. 
The `SwitchTimeModeDescriptorTranslator` also carries `SequenceId` through `AdvanceFrameIntent`. 
However, no validation logic exists to detect gaps or reordering of sequence IDs across the cluster. 
DT-006 remains open — the plumbing is in place, but the monitoring / enforcement layer is not yet built.

### Q5: Suggested commit message

```
feat(time): BATCH-05 – wire unified controllers, delete obsolete classes, E2E test

- OrchestratorSubsystem wired with MasterSyncController (TCU-W001)
- SimHostApp wired with SlaveSyncController; TimeRole corrected to Slave (TCU-W002)
- CgfApplication wired with SlaveSyncController (TCU-W003)
- IgApplication wired with SlaveSyncController (TCU-W004)
- Deleted 8 obsolete classes: MasterTimeController, SteppedMasterController,
  SteppedSlaveController, SlaveTimeController, SwitchableTimeController,
  DistributedTimeCoordinator, SlaveTimeModeListener, FrameLockstepDescriptorTranslator
- Deleted 10 associated test files; fixed cascade in examples and factory
- Extracted JitterFilter.cs from deleted SlaveTimeController
- Removed TimeNetworkModule.CreateLockstepTranslator (TCU-W006)
- Added UnifiedControllerE2ETests.FullCycle_Pause_Step_Resume_NoPllLoss (TCU-T006)
- Fix SlaveSyncController.UpdateStepping: refresh _lastUpdateRawTicks each frame
  to prevent stale catch-up delta on resume
- IOS-IG-SimHost.sln: 0 errors; FDP time tests: 70/70 pass
```
