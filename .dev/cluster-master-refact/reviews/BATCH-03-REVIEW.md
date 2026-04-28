# BATCH-03 Review

**Batch:** BATCH-03  
**Reviewer:** Dev Lead  
**Date:** 2025-07-17  
**Decision:** APPROVED with P3 note

---

## Summary

BATCH-03 implemented TASK-T001 (LiveBranchProcessManager) and TASK-T002 (ReplaySeekAggregator +
ReplaySeekProcessManager). TASK-T000 (audit) was completed by dev lead directly before the batch.
Build is clean (0 errors), 114/117 tests pass (3 pre-existing failures unchanged).

---

## Test Results

**Before:** 114/117 (3 pre-existing only)  
**After:** 114/117 (3 pre-existing only)

The developer self-fixed 3 test failures during implementation:
1. Missing `RegisterAggregator(new ReplaySeekAggregator())` in seek test setup
2. `GlobalTime` struct fields not serialized with default options -- fixed by switching to
   `OrchestrationJsonOptions.Default` (IncludeFields=true) in the seek ACK serialization path
3. Branch test ACKed only 1 of 2 trajectory steps -- fixed by ACKing all non-CommitState intents

---

## TASK-T000 (Dev Lead Audit)

Audit comment placed in `ClusterMaster.ProcessTransitionStateIntent`. Finding: **SAFE**.
`ReferenceReplayLoadHandler.CanHandle(PrepareLive)` guards on `IsReplayActive`; it is the
correct and designed handler for Live-from-Replay branches. Integration test
`LiveFromReplayBranch_Passes` currently **FAILS** (pre-existing, unrelated test wiring issue).

---

## TASK-T001: LiveBranchProcessManager

- `LiveBranchProcessManager.cs`: correct. Ticked BEFORE `ClusterMaster.Tick()`.
- `FreezeTime()` called on `TransitionStateIntent` when `_lastKnownDsmState == OperatingReplay`
  and target is `LoadingLive`/`OperatingLive`.
- `RestoreTime()` + `SnapAndPause()` called on `ClusterOpCompletedEvent` with `LiveBranchResult`.
- `_replayMasterModule`, `_pendingBranchTasks`, `BranchTransitionTask`, `SetReplayMasterModule`
  removed from `ClusterMaster`.
- `isLiveFromReplayBranch` suppression removed; standard fan-out runs unconditionally.
- Unit tests: SC1 (FreezeTime on branch), SC2 (RestoreTime+SnapAndPause after ACK), SC3 (no
  freeze for non-replay branch), SC4 (compiler verification) -- all pass.
- `_masterSync.SnapAndPause()` called with `new HashSet<int>()` for now (TODO noted in code). P3.

---

## TASK-T002: ReplaySeekAggregator + ReplaySeekProcessManager

- `ReplaySeekAggregator.cs`: correct. Deserializes `ReplaySeekResult` with
  `OrchestrationJsonOptions.Default` (IncludeFields=true).
- `ReplaySeekProcessManager.cs`: correct. Maintains `_nodeSubsystems` replica from heartbeats.
  Publishes `SlaveNodeSetUpdatedEvent` + `PauseTimeIntent` on `SeekReplayIntent`.
  Calls `SnapAndPause` on `ClusterOpCompletedEvent` with `ReplaySeekResult`.
- `ClusterMaster.ProcessSeekReplayIntent`: `SlaveNodeSetUpdatedEvent` + `PauseTimeIntent`
  publications removed. Fan-out only remains.
- `BusTransitionAckTracker.SeekResult` removed; `NodeResponses` dict added (used by aggregator
  pipeline).
- `_masterSync` field removed from `ClusterMaster`.
- `SetMasterSync` kept as Obsolete no-op (see P3 note below).
- Unit tests: SC1 (precondition events), SC2 (SnapAndPause on ACK), SC3 (no snap when ticks=0)
  -- all pass.

---

## Open Items

| ID | Priority | Description |
|---|---|---|
| DEBT-02 | P2 | `_pendingSerializeTasks`, `SerializeLocalTask`, `HandleSerializeLocalCompletion` still in `ClusterMaster` (ExportArchive). Addressed in TASK-P001. |
| DEBT-03 | P2 | 3 pre-existing failures (Archive, FanOut, Prefetch). Addressed in TASK-P002. |
| DEBT-04 | P3 | `SetMasterSync` kept as Obsolete no-op instead of deleted. Can be removed once callers identified (none in codebase currently). |
| DEBT-05 | P3 | `LiveBranchProcessManager.SnapAndPause()` called with empty node set. Wire active roster in TASK-P001 or TASK-P002 when roster access is available. |

---

## Decision

**APPROVED.** BATCH-03 ships. Proceed to BATCH-04 (TASK-P001 + TASK-P002).
