# CGF-1-BATCH-14 Report

**Batch:** CGF-1-BATCH-14  
**Developer:** GitHub Copilot  
**Date:** 2026-04-11  
**Status:** Complete — all Part A and Part B tasks implemented, build clean, all tests passing.

---

## Summary

CGF-1-BATCH-14 delivered two workstreams in order:

- **Part A** — Five BATCH-13 P2/P3 tech-debt items closed (prefetch ordering race, gateway
  fail-loud, null repo guard, test assertion depth, and TASK-DETAIL/DEBT-TRACKER alignment).
- **Part B** — CGF1-S0303 3-Step Binary Checkpointing implemented end-to-end
  (`CheckpointIOWorker` → `CheckpointDsmHandler` → `DrillSlave` polling → `LiveLoadDsmHandler`
  drain barrier).

Build: clean (zero new errors, 258 pre-existing warnings).  
Tests: Orchestrator.Tests 25/25 (+2 new), SimHost.Tests 371/371 (+4 new),
Fdp.Tests 717/717 (+4 new). Full solution pass.

---

## Part A — Tech Debt (BATCH-13 review follow-ups)

### A.1 — Prefetch barrier and transition gating

**Files:** `Bagira.Orchestrator/DrillMaster.cs`  
**Tests:** `Bagira.Orchestrator.Tests/DrillMasterPrefetchTests.cs` (2 new tests)

**Problem:** `ExecutePrefetchScenario` fired `PrefetchFiles` fan-out _before_ the
SMB gateway copy completed, and faults were silently swallowed (fire-and-forget `ContinueWith`).

**Fix:**
- Added `PendingPrefetchOp` nested class `{ Guid RequestId; string ScenarioId; Task<GatewayResult> GatewayTask; }` and `PendingPrefetchOp? _pendingPrefetch` field.
- `ExecutePrefetchScenario` now stores the task in `_pendingPrefetch` rather than wiring a continuation.
- Added `DrainPendingPrefetch()` called at the top of `Tick()` (before `ProcessSysOpRequests`):
  - If the task is not yet complete: return immediately (no fan-out).
  - If faulted/canceled or `FailureCount > 0`: publish `SysOpStatus.Failure` with the originating `RequestId`.
  - If success: fan-out `NodeOpType.PrefetchFiles` to all active roster nodes.
- Updated the call-site in `ProcessSysOpRequests` to pass `req.RequestId` to `ExecutePrefetchScenario`.
- Added `_idAllocatorServer` field declaration (was accidentally omitted in BATCH-13).

**Tests added:**
- `PrefetchScenario_WhenGatewaySucceeds_PrefetchFilesIsFanOutAfterCompletion` — real file copy
  into a local temp dir; verifies `PrefetchFiles` eventually arrives after drain.
- `PrefetchScenario_WhenNasSourceDirMissing_PublishesFailure_AndNoPrefetchFiles` — missing NAS
  dir; verifies `SysOpStatus.Failure` is published and no `PrefetchFiles` command is sent.

### A.2 — StorageGatewayModule fail-loud

**File:** `Bagira.Orchestrator/StorageGatewayModule.cs`

**Problem:** Missing `sourceDir` silently returned `GatewayResult{0,0}`, making it indistinguishable
from "nothing to copy," so the `Task` completed without fault and `DrainPendingPrefetch` fanned out
`PrefetchFiles` as if the copy had succeeded.

**Fix:**
- Changed the missing-`sourceDir` path to `throw new DirectoryNotFoundException(...)`.
- Added `FdpLog<StorageGatewayModule>.Error(...)` in the per-file failure catch block.
- The thrown exception faults the task; `DrainPendingPrefetch` detects `IsFaulted = true` and publishes `Failure`.

### A.3 — EditLoadDsmHandler null repo guard

**File:** `Bagira.SimHost/Modules/Orchestration/Handlers/EditLoadDsmHandler.cs`

**Problem:** `Commit()` issued `Warn + return` when both `repo` and `_world` are null with a pending
DOM — silently discarding the deserialization result.

**Fix:** Changed to `throw new InvalidOperationException(...)` — fail loud and early so the caller
(DrillSlave dispatch) surfaces the error through the normal fault path.

### A.4 — EditLoadDsmHandler test component-value assertions

**Files:** `Bagira.SimHost.Tests/EditLoadDsmHandlerTests.cs`, `.dev/cgf-1/CGF-1-TASK-DETAIL.md`

**Problem:** `LoadExistingScenario_SpawnsCorrectEntityCount` only asserted `EntityCount == 3`,
not that the deserialized component values matched the serialized source.

**Fix:**
- Extracted `CollectPositions(EntityRepository)` synchronous helper (avoids C# 12 `ref`-in-async
  limitation) that queries `With<EditLoadTestPos>()` and reads each component.
- Test now asserts all three `(X,Y,Z)` tuples via `Assert.Contains` on a `HashSet`.
- Updated §CGF1-S0302 in `CGF-1-TASK-DETAIL.md`: removed stale `EntityCommandBuffer`/
  `BaseTerrain` references; added canonical ScenarioSerializer DOM format + throw-on-null-repo
  description; updated success conditions to require component-value round-trip.

### A.5 — DEBT-TRACKER

**File:** `.dev/DEBT-TRACKER.md`

Closed all five open rows targeting CGF-1-BATCH-14 with ✅ markers referencing the specific
files changed.

---

## Part B — CGF1-S0303: 3-Step Binary Checkpointing

### B.1 — CheckpointIOWorker (Fdp.Kernel)

**New file:** `FDP/Kernel/Fdp.Kernel/Orchestration/CheckpointIOWorker.cs`

Step-3 background I/O worker for the checkpointing protocol.

**Design highlights:**
- Dedicated `Thread` (not `Task`) — prevents thread-pool starvation under concurrent checkpoint load.
- Pre-allocated `_rawBuffer` (32 MB) and `_compressedBuffer` used exclusively by the worker thread.
- Single `RecorderSystem _recorderSystem` instance reused across all writes (DRY: same LZ4
  pipeline as `AsyncRecorder`).
- `Enqueue(EntityRepository snapshot, Guid requestId)` — increments `_pendingCount`, enqueues
  work item, releases semaphore. Worker owns and disposes the snapshot after write.
- `DrainAsync()` — polls `Volatile.Read(ref _pendingCount) > 0` with 5 ms `Task.Delay`.
- `TakeCompletedResults()` — drains `ConcurrentDictionary<Guid, bool>`, consumed on call.
- File format: `[FDPC magic : 4][uncompressedSize : 4][compressedSize : 4][LZ4 payload]`.
- Output naming: `{requestId}_node_{nodeId}.fdp`.

### B.2 — ITickableDsmHandler (Bagira.Common)

**New file:** `Bagira.Common/Orchestration/ITickableDsmHandler.cs`

Interface extending `IDsmHandler` with `void DrainDeferredAcks()` — the per-frame polling
hook called by `DrillSlave.Tick()` for handlers that publish deferred ACKs from background threads.

### B.3 — CheckpointDsmHandler (Bagira.SimHost)

**New file:** `Bagira.SimHost/Modules/Orchestration/Handlers/CheckpointDsmHandler.cs`

Implements both `IDsmHandler` and `ITickableDsmHandler`. Three-step protocol:

| Step | Method | Action |
|------|--------|--------|
| 1 | `PrepareAsync` | Publish `NodeOpStatus(InProgress)` immediately; cache cmd |
| 2 | `Commit` | `snap = new EntityRepository(); snap.SyncFrom(liveRepo)` (~2 ms memcpy); `_worker.Enqueue(snap, txId)` — worker owns snap |
| 3 | `DrainDeferredAcks` | Poll `_worker.TakeCompletedResults()`; publish `Success/Failure` per result |

Handles null repo (both injected and commit-time) by publishing immediate Failure without enqueueing.

### B.4 — DrillSlave.Tick() ITickableDsmHandler polling

**File:** `Bagira.SimHost/Modules/Orchestration/DrillSlave.cs`

Added loop at the top of `Tick()` before command dispatch:
```csharp
foreach (var handler in _handlers)
    if (handler is ITickableDsmHandler tickable)
        tickable.DrainDeferredAcks();
```

### B.5 — LiveLoadDsmHandler DrainAsync barrier for FinalizeLive

**File:** `Bagira.SimHost/Modules/Orchestration/LiveLoadDsmHandler.cs`

Added optional `CheckpointIOWorker? _checkpointWorker` constructor parameter (default `null`).
`PrepareAsync` for `FinalizeLive` now `await _checkpointWorker.DrainAsync()` before returning,
ensuring all in-flight checkpoint writes complete before the live session is torn down.

### B.6 — Tests

**New files:**
- `FDP/Kernel/Fdp.Kernel.Tests/CheckpointIOWorkerTests.cs` — 4 tests, all in `Fdp.Tests` namespace
- `Bagira.SimHost.Tests/CheckpointDsmHandlerTests.cs` — 4 tests, namespace `Bagira.SimHost.Tests`

**`CheckpointIOWorkerTests` (4 tests):**
1. `DrainAsync_WaitsForQueueEmpty` — 3 enqueued items; all 3 files exist after drain.
2. `Enqueue_WritesFileWithExpectedName` — verifies `{reqId}_node_7.fdp` convention.
3. `TakeCompletedResults_ReportsSuccess_AfterWrite` — result `Success=true` after drain.
4. `TakeCompletedResults_EmptyOnSecondCall` — results consumed on first take.

**`CheckpointDsmHandlerTests` (4 tests):**
1. `TwoOverlappingCheckpoints_BothACKsDeferredUntilDrainComplete` — two rapid commits; both files created; both `TakeCompletedResults` entries `Success=true` after drain.
2. `SecondSnapshotCaptures_DifferentState_thanFirst` — file B ≥ file A in bytes (2 entities vs 1).
3. `LiveUnloading_WaitsForCheckpointDrain` — `LiveLoadDsmHandler.PrepareAsync(FinalizeLive)` returns only after checkpoint file exists on disk.
4. `NullRepo_NothingEnqueuedAndNoFileWritten` — null liveRepo + null commit repo: no file written, `TakeCompletedResults` empty after drain.

---

## Files Changed / Created

| File | Change |
|------|--------|
| `Bagira.Orchestrator/DrillMaster.cs` | A.1 prefetch latch; restored missing `_idAllocatorServer` field |
| `Bagira.Orchestrator/StorageGatewayModule.cs` | A.2 fail-loud DirectoryNotFoundException + per-file error log |
| `Bagira.SimHost/Modules/Orchestration/Handlers/EditLoadDsmHandler.cs` | A.3 null repo → throw |
| `Bagira.SimHost.Tests/EditLoadDsmHandlerTests.cs` | A.4 component-value assertions via CollectPositions helper |
| `.dev/cgf-1/CGF-1-TASK-DETAIL.md` | A.4 §CGF1-S0302 updated (DOM format, throw, success conditions) |
| `.dev/DEBT-TRACKER.md` | A.5 closed 5 rows |
| `Bagira.SimHost/Modules/Orchestration/DrillSlave.cs` | B.4 ITickableDsmHandler polling |
| `Bagira.SimHost/Modules/Orchestration/LiveLoadDsmHandler.cs` | B.5 DrainAsync barrier |
| `FDP/Kernel/Fdp.Kernel/Orchestration/CheckpointIOWorker.cs` | **NEW** B.1 |
| `Bagira.Common/Orchestration/ITickableDsmHandler.cs` | **NEW** B.2 |
| `Bagira.SimHost/Modules/Orchestration/Handlers/CheckpointDsmHandler.cs` | **NEW** B.3 |
| `FDP/Kernel/Fdp.Kernel.Tests/CheckpointIOWorkerTests.cs` | **NEW** B.6 (4 tests) |
| `Bagira.Orchestrator.Tests/DrillMasterPrefetchTests.cs` | **NEW** A.1 (2 tests) |
| `Bagira.SimHost.Tests/CheckpointDsmHandlerTests.cs` | **NEW** B.6 (4 tests) |
| `.dev/cgf-1/CGF-1-TASK-TRACKER.md` | Marked CGF1-S0303 `[x]` |

---

## Self-review checklist

- [x] No Bagira.* references in Fdp.Kernel (FDP layering constraint respected)
- [x] `_pendingCount` decremented in `finally` block — no leak on exception
- [x] Snapshot `Dispose()` inside `finally` — no unmanaged memory leak on write failure
- [x] C# 12 `ref`-in-async limitation navigated — `CollectPositions` extracted as sync helper
- [x] `_roster.ActiveNodes` empty-roster edge case handled in both prefetch tests
- [x] `SysOpStatus.InProgress`-before-`Failure` race handled in prefetch failure test
- [x] All tests pass: Orchestrator 25/25, SimHost 371/371, Fdp.Tests 717/717
