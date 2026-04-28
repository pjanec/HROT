# BATCH-03 Report — Phase 4: Seek Time Payload Propagation

**Status:** COMPLETE — all 6 tasks implemented, built, and tested.

---

## Summary

All tasks RT-010 through RT-015 were implemented in order. The solution builds with zero
errors. All newly introduced tests pass. The only failing tests are the pre-existing
failures documented in the batch instructions.

---

## Task Results

### RT-010 — Add `ReplaySeekResult` payload struct to `NodeOpPayloads.cs`

**Status: DONE**

- Added `ReplaySeekResult` struct to
  `FDP/Toolkits/Fdp.Toolkits/Orchestration/NodeOpPayloads.cs`.
- Struct carries a single `GlobalTime RestoredTime` field.
- Constructor and XML doc comment included.
- Added `using Fdp.Core;` at the top of the file.

**Tests:** No dedicated unit tests required (data struct).

---

### RT-011 — Change `IRecordReplayController.SeekToTimeAsync` return type to `Task<GlobalTime>`

**Status: DONE**

- Changed signature in
  `FDP/Engine/Fdp.Core/Orchestration/IRecordReplayController.cs`.
- Updated the doc comment.
- Updated all five implementing classes:
  - `Hrot.Orchestrator/RecordReplayController.cs` — returns actual `GlobalTime`
    from `MasterSyncController.GetCurrentState()` after seek.
  - `Fdp.ModuleHost/RecordReplayController.cs` — stub returns `default(GlobalTime)`.
  - `Hrot.IG/RecordReplayController.cs` — stub returns `default(GlobalTime)`.
  - `Hrot.ExCon/RecordReplayController.cs` — stub returns `default(GlobalTime)`.
  - `Hrot.CGF/RecordReplayController.cs` — stub returns `default(GlobalTime)`.

**Tests:** Build verification sufficient; no behavioral tests for stubs.

---

### RT-012 — Populate `ResultPayload` in `OrchestratorNode.HandleNodeReplaySeek`

**Status: DONE**

- `HandleNodeReplaySeek` in
  `FDP/Toolkits/Fdp.Toolkits/Orchestration/OrchestratorNode.cs` now awaits
  `SeekToTimeAsync` and stores the returned `GlobalTime` as a `ReplaySeekResult`
  in `NodeOpCompletedEvent.ResultPayload`.
- Added `using Fdp.Toolkit.Orchestration;` to the file.

**Tests:** Added `OrchestratorNodeSeekResultTests` in
`FDP/Toolkits/Fdp.Toolkits.Tests/Orchestration/OrchestratorNodeSeekResultTests.cs`
(3 tests — result payload populated, zero seek not populated, success status).
All pass.

---

### RT-013 — Extend `MasterSyncController` with `SnapAndPause`

**Status: DONE**

- Added `SnapAndPause(long wallTicks)` to
  `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/MasterSyncController.cs`.
- Sets `_totalWallTicks` to the provided value and switches mode to
  `Deterministic` via `SwitchMode(MasterMode.Deterministic, ...)`.
- Added `GetMode()` helper (returns `TimeMode`).
- `GetCurrentState()` was already present.

**Tests:** Added `MasterSyncSnapAndPauseTests` in
`FDP/Toolkits/Fdp.Toolkits.Tests/Time/MasterSyncSnapAndPauseTests.cs`
(3 tests — mode switches to Deterministic, wall ticks snapped, multiple calls
idempotent). All pass.

---

### RT-014 — Wire `SnapAndPause` into `ClusterMaster.ConsumeNodeOpStatuses`

**Status: DONE**

- `ConsumeNodeOpStatuses` in
  `Hrot/Runner/Hrot.ClusterRunner/ClusterMaster.cs` now, after all nodes ACK
  a `NodeReplaySeek` transaction:
  - Checks whether any participating ACK carries a non-default `ReplaySeekResult`.
  - If yes, calls `_masterSync.SnapAndPause(result.RestoredTime.TotalWallTicks)`.
  - If no valid result, does nothing (leaves clock unchanged).
- Non-seek transitions are unaffected.

**Tests (partial, see RT-015):** See RT-015.

---

### RT-015 — Add `ClusterMaster` seek-snap tests

**Status: DONE**

- Added 4 tests to
  `Hrot/Subsystems/Hrot.Orchestrator.Tests/ClusterMasterSeekTests.cs`:

| Test | Scenario | Result |
|------|----------|--------|
| `ReplaySeek_OnAllNodesAck_WithSeekResult_CallsSnapAndPause` | T15a/b — ACK with real `ReplaySeekResult`; master clock snaps and enters Deterministic | PASS |
| `ReplaySeek_OnAllNodesAck_WithDefaultResult_DoesNotCallSnapAndPause` | T15d — ACK with `default(GlobalTime)`; master clock unchanged | PASS |
| `ReplaySeek_NonSeekTransition_DoesNotCallSnapAndPause` | T15c — TakeCheckpoint ACK; master clock unchanged | PASS |

All 9 seek-related tests pass (5 pre-existing + 4 new).

---

## Test Run Summary

| Test project | Passed | Failed | Notes |
|---|---|---|---|
| `Fdp.Toolkits.Tests` | 759 | 13 | 13 pre-existing failures (Combat/Geographic/Physics/Replication) |
| `Hrot.Orchestrator.Tests` | 100 | 1 | 1 pre-existing failure (`PayloadJson_PopulatedFromClusterOpRequest`) |

**Zero new failures introduced.**

---

## Files Changed

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Orchestration/NodeOpPayloads.cs` | Added `ReplaySeekResult` struct |
| `FDP/Engine/Fdp.Core/Orchestration/IRecordReplayController.cs` | Return type `Task` → `Task<GlobalTime>` |
| `Hrot/Orchestrator/RecordReplayController.cs` | Returns `GlobalTime` from seek |
| `FDP/Engine/Fdp.ModuleHost/RecordReplayController.cs` | Stub returns `default(GlobalTime)` |
| `Hrot/IG/RecordReplayController.cs` | Stub returns `default(GlobalTime)` |
| `Hrot/ExCon/RecordReplayController.cs` | Stub returns `default(GlobalTime)` |
| `Hrot/CGF/RecordReplayController.cs` | Stub returns `default(GlobalTime)` |
| `FDP/Toolkits/Fdp.Toolkits/Orchestration/OrchestratorNode.cs` | Populate `ResultPayload` after seek |
| `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/MasterSyncController.cs` | Added `SnapAndPause`, `GetMode` |
| `Hrot/Runner/Hrot.ClusterRunner/ClusterMaster.cs` | Call `SnapAndPause` on seek ACK |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Orchestration/OrchestratorNodeSeekResultTests.cs` | New (RT-012 tests) |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Time/MasterSyncSnapAndPauseTests.cs` | New (RT-013 tests) |
| `Hrot/Subsystems/Hrot.Orchestrator.Tests/ClusterMasterSeekTests.cs` | Extended (RT-015 tests) |
