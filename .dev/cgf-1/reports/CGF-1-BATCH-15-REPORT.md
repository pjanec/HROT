# CGF-1-BATCH-15 Report

**Batch:** CGF-1-BATCH-15  
**Developer:** GitHub Copilot  
**Date:** 2026-03-29  
**Status:** Complete — all Part A and Part B tasks implemented, build clean, all tests passing.

---

## Summary

CGF-1-BATCH-15 delivered two workstreams:

- **Part A** — Production wiring of `CheckpointIOWorker` / `CheckpointDsmHandler` /
  `LiveLoadDsmHandler(checkpointWorker)` in `SimHostApp` + `NodeBootstrapper`; fail-loud
  policy for empty NAS scenario directories; two DEBT-TRACKER rows closed.
- **Part B** — CGF1-S0309 `DryRunDsmHandler` implemented in `Bagira.Common`, registered in
  all four subsystems (SimHost, CGF, IG, IOS), and covered by 6 unit tests.

Build: clean (zero new errors, 269 pre-existing warnings).  
Tests: `Bagira.SimHost.Tests` 6/6 new `DryRunDsmHandler*` tests; `Bagira.Orchestrator.Tests`
6/6 StorageGateway tests (+1 new empty-dir test). Full solution pass.

---

## Part A — Production Wiring + Empty-Dir Policy

### A.1 — CheckpointIOWorker / CheckpointDsmHandler / LiveLoadDsmHandler wiring

**Files changed:**

| File | Change |
|------|--------|
| `Bagira.SimHost/SimHostApp.cs` | Added `private CheckpointIOWorker? _checkpointWorker;` field; created in `OnLoad` (`C:\FDP_Temp\checkpoints`, `localNodeId`); passed to `BuildOrchestration`; disposed in `Shutdown`. |
| `Bagira.SimHost/NodeBootstrapper.cs` | Added `CheckpointIOWorker? checkpointWorker = null` parameter; wired `LiveLoadDsmHandler(drillSlave, eventBus, checkpointWorker)`; registered `CheckpointDsmHandler` when `checkpointWorker != null`; registered `DryRunDsmHandler(world)` unconditionally. |

**Before:** `NodeBootstrapper.BuildOrchestration` created `LiveLoadDsmHandler(drillSlave, eventBus)`
with a null worker, and never registered `CheckpointDsmHandler`.  Production SimHost could not handle
`TakeSnapshot` commands or drain checkpoints on `FinalizeLive`.

**After:** `SimHostApp.OnLoad` creates `CheckpointIOWorker(@"C:\FDP_Temp\checkpoints", localNodeId)`,
passes it through to `BuildOrchestration`, which registers both `CheckpointDsmHandler` and the
updated `LiveLoadDsmHandler`. `Shutdown` disposes the worker after `DrillSlave` disposes (ensuring
the background I/O thread has finished any in-flight writes).

**DEBT-TRACKER row:** `CheckpointIOWorker / CheckpointDsmHandler / LiveLoadDsmHandler wiring` →
closed `✅ CGF-1-BATCH-15`.

### A.2 — Fail-loud on empty NAS scenario directory

**File changed:** `Bagira.Orchestrator/StorageGatewayModule.cs` — `PrefetchScenarioAsync`

**Problem:** When the NAS source directory existed but contained no files, `files.Length == 0` caused
`PrefetchScenarioAsync` to return `GatewayResult{SuccessCount=0, FailureCount=0}`.
`DrainPendingPrefetch` treated this as success and fanned out `PrefetchFiles` — resulting in nodes
receiving a staging notification for a scenario with no actual content.

**Fix:** Added guard immediately after `Directory.GetFiles(sourceDir)`:
```csharp
if (files.Length == 0)
    throw new InvalidOperationException(
        $"[Gateway] PrefetchScenario: NAS source directory '{sourceDir}' is empty ...");
```
The thrown exception faults the gateway task; `DrainPendingPrefetch` detects `IsFaulted = true` and
publishes `SysOpStatus.Failure` without fanning out `PrefetchFiles`.

**Test added:** `Bagira.Orchestrator.Tests/StorageGatewayTests.cs`
- `PrefetchScenarioAsync_EmptyDirectory_ThrowsInvalidOperation` — creates an empty temp directory
  and asserts `InvalidOperationException` containing `"empty"` and the scenario ID.

**DEBT-TRACKER row:** `PrefetchScenarioAsync empty NAS scenario directory` → closed
`✅ CGF-1-BATCH-15`.

---

## Part B — DryRunDsmHandler (CGF1-S0309)

### B.1 — Implementation

**New file:** `Bagira.Common/Orchestration/Handlers/DryRunDsmHandler.cs`

**Design:**
- Constructor: `DryRunDsmHandler(EntityRepository? liveRepo)` — subsystems with no ECS state
  (IG, IOS, CGF skeleton) pass `null`; `DryRunDsmHandler` then no-ops safely (log warn, no throw).
- `CanHandle`: returns `true` for `NodeOpType.PrepareState` only.
- `PrepareAsync`: `return Task.FromResult<string?>(null)` — all work is synchronous in `Commit`.
- `Commit` dispatches on `ParseTargetState(cmd.PayloadJson)`:
  - `LoadingDryRun`: allocates `_snap = new EntityRepository(); _snap.SyncFrom(_liveRepo)` —
    captures in-memory snapshot; if `_liveRepo == null`, logs warn and sets `_snap = null`.
  - `UnloadingDryRun`: calls `_liveRepo.SyncFrom(_snap)`, then `_snap.Dispose(); _snap = null` —
    restores live repo to pre-dry-run state; if `_snap == null`, logs warn and returns (no throw).
  - All other `PrepareState` targets: no-op.
- `Abort`: disposes and nulls `_snap` to prevent stale snapshots.
- `TestHook_Snap` (`internal`): exposes `_snap` for unit tests.
- `ParseTargetState`: static helper copied verbatim from `EditLoadDsmHandler` pattern.

**`Bagira.Common.csproj` change:** Added `InternalsVisibleTo("Bagira.SimHost.Tests")` so the
`TestHook_Snap` internal accessor is visible from the test project.

### B.2 — Registrations

| Subsystem | File | Registration |
|-----------|------|-------------|
| **SimHost** | `Bagira.SimHost/NodeBootstrapper.cs` | `drillSlave.RegisterHandler(new DryRunDsmHandler(world))` — in `BuildOrchestration`, always (world = live EntityRepository). |
| **CGF** | `Bagira.CGF/CgfApplication.cs` | `_drillSlave.RegisterHandler(new DryRunDsmHandler(liveRepo: null))` — in constructor after `ScenarioLoadDsmHandler`; `using Bagira.Common.Orchestration.Handlers;` added. |
| **IG** | `Bagira.IG/IgApplication.cs` | `_drillSlave.RegisterHandler(new DryRunDsmHandler(liveRepo: null))` — in `InitializeNetwork()` after DrillSlave creation; `using Bagira.Common.Orchestration.Handlers;` added. |
| **IOS** | `Bagira.Runner/Services/IosSubsystem.cs` | `_drillSlave.RegisterHandler(new DryRunDsmHandler(liveRepo: null))` — in `Initialize()` after DrillSlave creation; `using Bagira.Common.Orchestration.Handlers;` added. |

### B.3 — Tests

**New file:** `Bagira.SimHost.Tests/DryRunDsmHandlerTests.cs`

Test component: `[ComponentId(210)] internal struct DryRunTestPos { float X, Y, Z; }` (distinct from
`CkptPos` at ID 206 used by `CheckpointDsmHandlerTests`).

| Test | Verifies |
|------|----------|
| `LoadingDryRun_SnapshotCapturesLiveState` | `_snap` holds entity component values matching live at snapshot time. |
| `UnloadingDryRun_RewindsLiveRepo` | Live repo component values are restored to pre-dry-run state after unload commit. `_liveRepo.Tick()` called between snapshot and mutation to ensure `SyncDirtyChunks` version mismatch triggers the copy. |
| `UnloadingDryRun_DisposesSnapshot` | `TestHook_Snap == null` after unload commit. |
| `Abort_DuringLoadingDryRun_DiscardsSnap` | `TestHook_Snap == null` after abort. |
| `OtherPrepareStateTargets_AreNoOps` | Six non-dry-run `DSMState` targets leave snap null and live unchanged. |
| `UnloadingDryRun_WithNullSnap_LogsWarningAndReturns` | No exception when no snapshot exists; live repo unchanged. |

---

## Changed Files Summary

| File | Type | Change |
|------|------|--------|
| `Bagira.SimHost/SimHostApp.cs` | Modified | `_checkpointWorker` field + `OnLoad` creation + `Shutdown` disposal |
| `Bagira.SimHost/NodeBootstrapper.cs` | Modified | `checkpointWorker` param, `CheckpointDsmHandler` + `DryRunDsmHandler` registrations |
| `Bagira.Orchestrator/StorageGatewayModule.cs` | Modified | Throw on empty NAS source directory |
| `Bagira.Orchestrator.Tests/StorageGatewayTests.cs` | Modified | +1 empty-dir test |
| `Bagira.Common/Orchestration/Handlers/DryRunDsmHandler.cs` | **New** | Full implementation |
| `Bagira.Common/Bagira.Common.csproj` | Modified | `InternalsVisibleTo("Bagira.SimHost.Tests")` |
| `Bagira.CGF/CgfApplication.cs` | Modified | `DryRunDsmHandler(null)` registration |
| `Bagira.IG/IgApplication.cs` | Modified | `DryRunDsmHandler(null)` registration |
| `Bagira.Runner/Services/IosSubsystem.cs` | Modified | `DryRunDsmHandler(null)` registration |
| `Bagira.SimHost.Tests/DryRunDsmHandlerTests.cs` | **New** | 6 unit tests for CGF1-S0309 |
| `.dev/DEBT-TRACKER.md` | Modified | 2 rows closed (`✅ CGF-1-BATCH-15`) |
| `.dev/cgf-1/CGF-1-TASK-TRACKER.md` | Modified | S0309 `[x]`, progress line updated to 6/9 |

---

## Test Results

```
Bagira.SimHost.Tests       Passed:  6  Failed: 0  (DryRunDsmHandlerTests — all new)
Bagira.Orchestrator.Tests  Passed:  6  Failed: 0  (StorageGatewayTests — 5 existing + 1 new)
Full solution build        0 errors, 269 warnings (all pre-existing)
```
