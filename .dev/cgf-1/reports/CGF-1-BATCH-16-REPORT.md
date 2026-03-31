# CGF-1-BATCH-16 Report

**Batch:** CGF-1-BATCH-16  
**Developer:** GitHub Copilot  
**Date:** 2026-04-03  
**Status:** Complete — Part A (5 tech debt items) and Part B (CGF1-S0304 Dynamic Recording Modules) fully implemented; build clean; all new and existing tests passing.

**Lead review:** [CGF-1-BATCH-16-REVIEW.md](../reviews/CGF-1-BATCH-16-REVIEW.md) — **CONDITIONALLY APPROVED** (production **`ReplayLoadDsmHandler`** registration gap → [CGF-1-BATCH-17](../batches/CGF-1-BATCH-17-INSTRUCTIONS.md) Part A).

---

## Summary

CGF-1-BATCH-16 delivered two workstreams:

- **Part A** — Five targeted tech debt items from the CGF-1-BATCH-15 review: TASK-DETAIL
  accuracy (S0309 path + DryRunTestPos), test strengthening (entity removal on rewind),
  checkpoint path configurability via `NodeConfiguration.LocalTempRoot`, S0303 wording
  correction, and DEBT-TRACKER row closure.
- **Part B** — Full end-to-end implementation of CGF1-S0304 Dynamic Recording Modules:
  `IRecordReplayController` interface, `MaxNetworkId` exposure through the recording
  pipeline, `GhostCreationSystem.BypassLifecycle`, `NetworkLifecycleSystemGroup`,
  `ReplayLoadDsmHandler`, full `LiveLoadDsmHandler` wiring, `NodeBootstrapper` registration,
  and 6 success-condition tests.

Build: clean (zero new errors, pre-existing warnings only).  
Tests: 380/380 in `Hrot.SimHost.Tests`; 16/16 in `FDP.Toolkit.Replay.Tests`; 3/3 new
`NetworkLifecycleSystemGroupTests`; 1/1 new `SeekToWallClockTicks_UsesBinarySearch`.
Full test suites pass.

---

## Part A — Tech Debt (BATCH-15 review + DEBT-TRACKER)

### A.1 — §CGF1-S0309 TASK-DETAIL alignment

**File:** `.dev/cgf-1/CGF-1-TASK-DETAIL.md`

**Changes:**
- Updated §CGF1-S0309 "Work to do" item 1: file path changed from
  `Hrot.SimHost/Modules/Orchestration/Handlers/DryRunDsmHandler.cs` to
  `Hrot.Common/Orchestration/Handlers/DryRunDsmHandler.cs` (correct project).
- Replaced `SimPosition` / rigid entity-count prose with normative language referencing
  `DryRunTestPos (ComponentId 210)`.
- Updated registration note to reference `NodeBootstrapper.BuildOrchestration`.

**DEBT-TRACKER row:** S0309 path/DryRunTestPos → closed `✅ CGF-1-BATCH-16`.

---

### A.2 — Strengthen `UnloadingDryRun_RewindsLiveRepo`

**File:** `Hrot.SimHost.Tests/DryRunDsmHandlerTests.cs`

**Changes:** Rewrote test to the pattern mandated by the updated task detail:
1. Create 4 entities with `DryRunTestPos` values `(i*10, i*20, i*30)`.
2. Take snapshot (`LoadingDryRun`).
3. Tick `liveRepo` (so `GlobalVersion` advances for `SyncDirtyChunks`).
4. Mutate `entities[0]` to `(99, 99, 99)`.
5. Create a 5th entity during dry run.
6. Assert `EntityCount == 5` (5th entity is visible inside dry run).
7. Call `UnloadingDryRun`.
8. Assert `EntityCount == 4` — 5th entity was removed by `SyncFrom`.
9. Assert `entities[0]` pos reverted to `(0, 0, 0)`.

This proves `SyncFrom` removes entities created during the dry run, not only reverts component values.

**DEBT-TRACKER row:** `UnloadingDryRun_RewindsLiveRepo` test → closed `✅ CGF-1-BATCH-16`.

---

### A.3 — Checkpoint storage path configurability

**Files:** `Hrot.SimHost/NodeConfiguration.cs`, `Hrot.SimHost/SimHostApp.cs`

**Changes:**
- Added `LocalTempRoot` property to `NodeConfiguration` (default `@"C:\FDP_Temp"`); fully
  XML-documented with usage examples for scenario staging and checkpoint subdirectories.
- `SimHostApp.OnLoad` now derives checkpoint path via
  `Path.Combine(nodeConfig.LocalTempRoot, "checkpoints")` instead of a hardcoded literal.
- `BuildOrchestration` receives `localTempRoot: nodeConfig.LocalTempRoot` so handlers that
  stage recordings ($S0304) use the same root.

**DEBT-TRACKER row:** hard-coded checkpoint path → closed `✅ CGF-1-BATCH-16`.

---

### A.4 — §CGF1-S0303 success-condition wording

**File:** `.dev/cgf-1/CGF-1-TASK-DETAIL.md`

**Change:** Replaced `OnItemWritten callback` reference with
`TakeCompletedResults()` / deferred DDS ACK wording to match the actual
`CheckpointDsmHandler` + `CheckpointIOWorker` API.

---

### A.5 — DEBT-TRACKER closure

Three rows targeting CGF-1-BATCH-16 in `.dev/DEBT-TRACKER.md` closed with ✅
(items A.1, A.2, A.3 above).

---

## Part B — CGF1-S0304: Dynamic Recording Modules

### B.1 — `IRecordReplayController` interface

**New file:** `FDP/Kernel/Fdp.Kernel/Orchestration/IRecordReplayController.cs`

Pure FDP interface with no Hrot references:
```csharp
Task PrepareRecordingAsync(Guid drillId, string storageDirectory);
Task FinalizeRecordingAsync();
Task PrepareReplayAsync(Guid drillId, string storageDirectory);
Task SeekToTimeAsync(long targetWallClockTicks);
void ProcessPlaybackTick(GlobalTime currentTime);
Task TeardownReplayAsync();
```

---

### B.2 — `MaxNetworkId` exposure

**Files modified:**
- `FDP/Kernel/Fdp.Kernel/FlightRecorder/Metadata/RecordingMetadata.cs` — Added `MaxNetworkId`
  property (`long`, default 0; written by `AsyncRecorder.Dispose()`).
- `FDP/Kernel/Fdp.Kernel/FlightRecorder/AsyncRecorder.cs` — Added `MaxNetworkId` settable
  property; `Dispose()` copies it to `_metadata.MaxNetworkId` before writing `.meta.json`.
- `FDP/Kernel/Fdp.Kernel/FlightRecorder/PlaybackController.cs` — Added `Metadata` property
  exposing `RecordingMetadata` for external access to `MaxNetworkId` after open.
- `FDP/Toolkits/FDP.Toolkit.Replay/ReplayModule.cs` — Added `MaxNetworkId` forwarding
  property (`_playback?.Metadata.MaxNetworkId ?? 0`).
- `FDP/Toolkits/FDP.Toolkit.Replay/RecordingModule.cs` — Added `SetMaxNetworkId(long)`
  method; forwarded `FinalizeRecordingAsync(maxNetworkId)` parameter path.

---

### B.3 — `GhostCreationSystem.BypassLifecycle`

**File:** `FDP/Toolkits/FDP.Toolkit.Replication/Systems/GhostCreationSystem.cs`

Added `public bool BypassLifecycle { get; set; } = false;` with XML doc explaining the
replay use case. Set to `true` by `ReplayLoadDsmHandler.Commit(PrepareReplay)` and
reset to `false` by `Commit(FinalizeReplay)`.

---

### B.4 — `NetworkLifecycleSystemGroup`

**New file:** `FDP/ModuleHost/ModuleHost.Core/Scheduling/NetworkLifecycleSystemGroup.cs`

Gates three network lifecycle systems (`LifecycleSystem`, `GhostPromotionSystem`,
`NetworkGatewaySystem`) under a single `Enabled` flag. When `Enabled = false`,
`ExecuteGroup` is a no-op — zero inner systems execute.

---

### B.5 — `EcsRecordReplayController.FinalizeRecordingAsync`

**File:** `Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs`

Updated signature to `FinalizeRecordingAsync(long maxNetworkId = 0)` — sets
`MaxNetworkId` on the `RecordingModule` before uninstalling so the value is
flushed to `.meta.json` by `RecordingModule.Dispose()`.

---

### B.6 — `LiveLoadDsmHandler` full implementation

**File:** `Hrot.SimHost/Modules/Orchestration/LiveLoadDsmHandler.cs`

Replaced the Phase 2.0 stub with the full S0304 implementation:
- Constructor gains `EcsRecordReplayController? controller` and `string storageDirectory`
  optional parameters.
- `PrepareAsync(PrepareLive)`: calls `controller.PrepareRecordingAsync(drillId, storageDir)`.
- `PrepareAsync(FinalizeLive)`: awaits checkpoint drain (CDG1-S0303); calls
  `controller.FinalizeRecordingAsync()`.
- `ExerciseId` parsed from `cmd.PayloadJson["ExerciseId"]` (falls back to `Guid.NewGuid()`).

---

### B.7 — `ReplayLoadDsmHandler`

**New file:** `Hrot.SimHost/Modules/Orchestration/Handlers/ReplayLoadDsmHandler.cs`

Handles `NodeOpType.PrepareReplay` and `NodeOpType.FinalizeReplay`:

| Method | PrepareReplay | FinalizeReplay |
|--------|--------------|---------------|
| `PrepareAsync` | Calls `PrepareReplayAsync`, extracts `MaxNetworkId`, publishes `NodeOpStatus(Success, ResultJson={"MaxNetworkId":N})` | Calls `TeardownReplayAsync` |
| `Commit` | Sets `SimGroup.Enabled=false`, `LifecycleGroup.Enabled=false`, `GhostSys.BypassLifecycle=true` | Resets all three to live defaults |
| `Abort` | no-op | no-op |

---

### B.8 — `NodeBootstrapper` registration

**File:** `Hrot.SimHost/NodeBootstrapper.cs`

`BuildOrchestration` gains three optional parameters:
```csharp
SimulationSystemGroup?            simGroup         = null,
NetworkLifecycleSystemGroup?      lifecycleGroup   = null,
GhostCreationSystem?              ghostCreationSystem = null
```

When all three are provided and the role is Brain/AllInOne, a `ReplayLoadDsmHandler` is
registered with the `ClusterSlave`. `LiveLoadDsmHandler` now receives the `EcsRecordReplayController`
instance and `localTempRoot` for recording start/stop.

---

### B.9 — Tests (6 success conditions)

| Test | Location | Status |
|------|----------|--------|
| `RecordingModuleTests.AfterInstall_RecorderTickSystemIsRegistered` | `FDP.Toolkit.Replay.Tests` | ✅ Pass |
| `RecordingModuleTests.AfterUninstall_RecorderTickSystemIsAbsent` | `FDP.Toolkit.Replay.Tests` | ✅ Pass |
| `EcsRecordReplayControllerTests.FinalizeRecording_WritesMetaJson` | `Hrot.SimHost.Tests` | ✅ Pass |
| `PlaybackControllerTests.SeekToWallClockTicks_UsesBinarySearch` | `Fdp.Kernel.Tests` | ✅ Pass |
| `NetworkLifecycleSystemGroupTests.Enabled_False_SkipsAllInnerSystems` | `ModuleHost.Core.Tests` | ✅ Pass |
| `ReplayLoadDsmHandlerTests.FullReplayTransition_DisablesSimGroups` | `Hrot.SimHost.Tests` | ✅ Pass |

Additional coverage tests added:
- `NetworkLifecycleSystemGroupTests.Enabled_True_ExecutesAllInnerSystems`
- `NetworkLifecycleSystemGroupTests.Enabled_CanBeToggledAtRuntime`
- `ReplayLoadDsmHandlerTests.FinalizeReplay_ReEnablesSimGroups`

---

## Test Results

| Assembly | Passed | Failed | Total |
|----------|--------|--------|-------|
| `Hrot.SimHost.Tests` | 380 | 0 | 380 |
| `FDP.Toolkit.Replay.Tests` | 16 | 0 | 16 |
| `ModuleHost.Core.Tests` | +3 new | 0 | — |
| `Fdp.Kernel.Tests (Fdp.Tests)` | +1 new | 0 | 720 |

Pre-existing intermittent flaky tests in `Fdp.Kernel.Tests/CheckpointIOWorkerTests`
(timing-based) are unrelated to this batch.

---

## Build

```
Build succeeded.
0 Error(s) — Hrot.SimHost
0 Error(s) — Hrot.SimHost.Tests
0 Error(s) — FDP.Toolkit.Replay.Tests
0 Error(s) — ModuleHost.Core.Tests
0 Error(s) — Fdp.Kernel.Tests
```

---

## Files Changed

**New files:**
- `FDP/Kernel/Fdp.Kernel/Orchestration/IRecordReplayController.cs`
- `FDP/ModuleHost/ModuleHost.Core/Scheduling/NetworkLifecycleSystemGroup.cs`
- `Hrot.SimHost/Modules/Orchestration/Handlers/ReplayLoadDsmHandler.cs`
- `FDP/ModuleHost/ModuleHost.Core.Tests/NetworkLifecycleSystemGroupTests.cs`
- `Hrot.SimHost.Tests/ReplayLoadDsmHandlerTests.cs`

**Modified files:**
- `.dev/cgf-1/CGF-1-TASK-DETAIL.md` (§S0309 path + DryRunTestPos; §S0303 wording)
- `.dev/cgf-1/CGF-1-TASK-TRACKER.md` (CGF1-S0304 marked `[x]`)
- `.dev/DEBT-TRACKER.md` (3 rows closed)
- `Hrot.SimHost.Tests/DryRunDsmHandlerTests.cs` (test strengthened)
- `Hrot.SimHost/NodeConfiguration.cs` (`LocalTempRoot` property)
- `Hrot.SimHost/SimHostApp.cs` (checkpoint path derivation)
- `Hrot.SimHost/NodeBootstrapper.cs` (replay handler registration; new params)
- `Hrot.SimHost/Modules/Orchestration/LiveLoadDsmHandler.cs` (full impl)
- `Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs` (`FinalizeRecordingAsync(maxNetworkId)`)
- `FDP/Kernel/Fdp.Kernel/FlightRecorder/Metadata/RecordingMetadata.cs` (`MaxNetworkId`)
- `FDP/Kernel/Fdp.Kernel/FlightRecorder/AsyncRecorder.cs` (`MaxNetworkId` + Dispose)
- `FDP/Kernel/Fdp.Kernel/FlightRecorder/PlaybackController.cs` (`Metadata` property)
- `FDP/Toolkits/FDP.Toolkit.Replay/RecordingModule.cs` (`SetMaxNetworkId`)
- `FDP/Toolkits/FDP.Toolkit.Replay/ReplayModule.cs` (`MaxNetworkId` property)
- `FDP/Toolkits/FDP.Toolkit.Replication/Systems/GhostCreationSystem.cs` (`BypassLifecycle`)
- `FDP/Toolkits/FDP.Toolkit.Replay.Tests/RecordingModuleTests.cs` (2 new tests)
- `Hrot.SimHost.Tests/EcsRecordReplayControllerTests.cs` (`FinalizeRecording_WritesMetaJson`)
- `FDP/Kernel/Fdp.Kernel.Tests/PlaybackControllerTests.cs` (`SeekToWallClockTicks_UsesBinarySearch`)
