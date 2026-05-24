# BATCH-04 Review

**Batch:** BATCH-04  
**Reviewer:** Dev Lead  
**Date:** 2025-07-18  
**Decision:** APPROVED

---

## Summary

BATCH-04 implemented TASK-P001 (GlobalContextProcessManager) and TASK-P002 (AssetPrefetchProcessManager),
completing Phase 3 — Persistence and Prefetch Extractions. Build is clean (0 errors), 115/117 tests
pass (2 pre-existing failures, down from 3). The third pre-existing failure
(`PrefetchScenario_WhenGatewaySucceeds`) was unblocked by this batch.

---

## Test Results

**Before:** 114/117 (3 pre-existing failures)  
**After:** 115/117 (2 pre-existing failures)

The developer fixed one previously-blocking test during this batch:
- `PrefetchScenario_WhenGatewaySucceeds_PrefetchFilesIsFanOutAfterCompletion` — was pre-existing
  failure due to missing implementation. Now passes with AssetPrefetchProcessManager wiring.

The developer self-fixed two issues during implementation:
1. `StrictStringEnumConverter` rejecting integer enum in PayloadJson — fixed by using string enum format in test
2. `TransitionStateIntent` as trigger (not `ClusterStateTransitionedEvent`) — correct design choice for testability

---

## TASK-P001: GlobalContextProcessManager

**Verdict: APPROVED**

- `GlobalContextProcessManager.cs` correctly extracts context commit logic from `ClusterMaster`.
- Trigger on `TransitionStateIntent` is the right choice — `ClusterStateTransitionedEvent` requires
  node ACKs which don't occur in unit tests.
- `ResolveLoadState()` correctly handles all 4 relevant target states (LoadingLive, LoadingEdit,
  OperatingLive→LoadingLive, OperatingEdit→LoadingEdit).
- `StorageProcessManager` shim removed cleanly; replaced with bus-based `GlobalContextManifestReadyEvent`.
- `SetGlobalContextHandler()` removed from `ClusterMaster`.
- 3 context handler tests updated to bus-based pattern; all pass.
- 3 storage process manager tests updated to 3-arg constructor; all pass.

Minor note (P3 — no action required):
- `GlobalContextProcessManager` does not yet handle `ExecuteStorageOpIntent(LoadScenario)` for
  explicit load-by-request flows. This is a known gap, not a regression; current design covers
  transition-driven loads only.

---

## TASK-P002: AssetPrefetchProcessManager

**Verdict: APPROVED**

- `AssetPrefetchProcessManager.cs` correctly extracts the async prefetch barrier from `ClusterMaster`.
- `_pendingPrefetch` polling loop and `ExecutePrefetchScenario` removed from `ClusterMaster`.
- `DrainPendingPrefetch()` method removed; replaced by `ProcessPrefetchStagingCompleted()` which
  reads bus events.
- `ProcessPrefetchStagingCompleted()` placed first in `Tick()` so it can process events published
  by background tasks before `DrainInjectedRequests()` runs.
- `OrchestratorSubsystem` tick order (GlobalContext → AssetPrefetch → ClusterMaster) is correct.
- Both prefetch tests pass: success path (gateway completes → PrefetchFiles fan-out) and failure
  path (missing NAS dir → Timeout status, no PrefetchFiles).

---

## Phase 3 Completion

All Phase 3 tasks are complete:
- TASK-P001 ✅ GlobalContextProcessManager
- TASK-P002 ✅ AssetPrefetchProcessManager

`ClusterMaster` no longer owns:
- Global context file I/O (`_globalContextHandler`)
- NAS prefetch staging (`_pendingPrefetch`, `ExecutePrefetchScenario`)
- Episode state tracking (Phase 1)
- NAS pull I/O (Phase 1)
- Branch time-freezing (Phase 2)
- Seek clock-snap (Phase 2)

**All tasks in TASK-TRACKER.md are now complete.**

---

## Outstanding Debt

- `DEBT-01`: StorageProcessManager unit tests were missing for some scenarios (noted in BATCH-02)
- `DEBT-02`: ExportArchive still lives in `ClusterMaster` (noted in BATCH-02)
- `CancelOperation_CancelsActiveCts` pre-existing test failure (ExportArchive CTS registration)
- `PayloadJson_PopulatedFromClusterOpRequest` pre-existing test failure (not caused by this refactor)
