# BATCH-03 Report

**Batch:** BATCH-03  
**Developer:** Agent  
**Date:** 2025-07-17  
**Status:** Complete

---

## Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| TASK-T001 | [x] Complete | LiveBranchProcessManager created; isLiveFromReplayBranch + MasterSync ownership removed from ClusterMaster |
| TASK-T002 | [x] Complete | ReplaySeekAggregator + ReplaySeekProcessManager created; seek preconditions and SnapAndPause moved out of ClusterMaster |

---

## Testing Results

**Unit Tests Passed:** 114 / 117  
**Integration Tests Passed:** N/A

Pre-existing failures (not caused by this batch):
- `ClusterMasterArchiveTests.CancelOperation_CancelsActiveCts` -- ExportArchive CTS registration
- `ClusterMasterFanOutTests.PayloadJson_PopulatedFromClusterOpRequest` -- PayloadJson uses JsonSerializer.Serialize, not empty string
- `ClusterMasterPrefetchTests.PrefetchScenario_WhenGatewaySucceeds_PrefetchFilesIsFanOutAfterCompletion` -- PrefetchFiles command never received

**Key Test Scenarios Verified:**
- [x] T21 -- PrepareLive is fanned out as a standard 2PC op; ClusterOpCompletedEvent published after all ACKs
- [x] SC1 (LiveBranch) -- FreezeTime called when transitioning from OperatingReplay to LoadingLive/OperatingLive
- [x] SC2 (LiveBranch) -- RestoreTime + SnapAndPause called when ClusterOpCompletedEvent carries LiveBranchResult
- [x] SC3 (LiveBranch) -- No freeze when transitioning from non-Replay state
- [x] SC1 (ReplaySeek) -- SlaveNodeSetUpdatedEvent + PauseTimeIntent published by ReplaySeekProcessManager on SeekReplayIntent
- [x] SC2 (ReplaySeek) -- SnapAndPause called on masterSync after all nodes ACK with non-zero ReplaySeekResult
- [x] SC3 (ReplaySeek) -- No SnapAndPause when ACK carries default(GlobalTime)
- [x] BranchTransition_FansOut_PrepareLiveAsStandardOp -- verifies standard fan-out includes PrepareLive
- [x] T9a/T9b -- ClusterMaster no longer publishes SlaveNodeSetUpdatedEvent or PauseTimeIntent on seek

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Three issues required fixes during this batch:

1. **GlobalTime serialization bug.** `ConsumeNodeOpStatuses` serialized `ev.ResultPayload` using default `JsonSerializerOptions`, which excludes struct fields. `GlobalTime.TotalWallTicks` is a field (not a property), so it serialized as `0`. Fixed by using `OrchestrationJsonOptions.Default` (which sets `IncludeFields = true`).

2. **Branch test ACK count mismatch.** The trajectory from OperatingReplay to OperatingLive has two TransitionSteps (LoadingLive and OperatingLive), so `expectedAcks = 2`. The original test only ACKed one intent (PrepareLive). Fixed by reading all `ExecuteNodeOpIntent` events and ACKing all non-CommitState ones.

3. **Missing RegisterAggregator in seek test T15a.** `TryAggregate` found no registered aggregator for NodeReplaySeek, so `ResultPayload` in `ClusterOpCompletedEvent` was null. Fixed by adding `master.RegisterAggregator(new ReplaySeekAggregator())`.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `TryAggregate` has a subtle routing ambiguity: when `_inflightTransitionTx.TransactionId == txId`, it uses `_inflightTransitionTx.NodeResponses` (which my code now populates for transition ACKs). When txId does not match, it falls back to `fallbackResponses`. This works correctly, but the two code paths are not symmetric — a future aggregator for `LiveBranchResult` on the transition path would need the `_inflightTransitionTx.NodeResponses` to be populated, which I added as a side effect fix in this batch.

- The pre-existing `PayloadJson` test failure (line ~748 in ClusterMaster) suggests the serialization of `TransitionStateIntent` inside the transaction payload uses ad-hoc options rather than `OrchestrationJsonOptions.Default`. This is not addressed in this batch.

**Q3: What design decisions did you make beyond the instructions?**

- Populated `_inflightTransitionTx.NodeResponses` alongside `tracker.NodeResponses` in `ConsumeNodeOpStatuses`. The instructions only required the seek path to work, but the data is symmetrically useful for any future transition-path aggregator (e.g., LiveBranchResultAggregator). This is a minimal addition with no breaking impact.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- `CommitState` fan-outs share the same `TransactionId` as prepare ops, but they do not generate `NodeOpCompletedEvent` ACKs. The `expectedAcks` counter correctly counts only TransitionSteps (not commits). The test must skip CommitState when ACKing, which is now explicitly documented in the test comment.

- `ReplaySeekProcessManager` reads `ClusterOpCompletedEvent` from the bus. This event is also published for non-seek operations (e.g., TransitionState completions). The guard `ev.ResultPayload is ReplaySeekResult sr` implicitly filters them, which is correct but fragile if a future operation also returns a `ReplaySeekResult`-shaped payload.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

No significant concerns. The new process managers add one `ReadManaged<T>()` call each per tick, which is O(1) with the double-buffered bus.

---

## Outstanding Issues / Next Steps

- [ ] Pre-existing failures in Archive, FanOut, and Prefetch test suites were present before this batch and remain unaddressed.
- [ ] A `LiveBranchResultAggregator` (parallel to `ReplaySeekAggregator`) may be needed in a future batch if `LiveBranchProcessManager` must receive the `HistoricalTime` result from the aggregator pipeline rather than from a directly published event.
