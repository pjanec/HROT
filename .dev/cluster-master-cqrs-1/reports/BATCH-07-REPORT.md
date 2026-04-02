# BATCH-07 Report

**Batch:** BATCH-07 — Tech Debt: ClusterSlave Multi-Intent Queue and Dedup Fix  
**Developer:** AI Developer (Claude Sonnet 4.6)  
**Date:** 2026-04-02  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| DEBT-007 | ✅ Complete | `_pendingIntents` queue + 3-tuple dedup key; both AllSubsystems tests now pass |
| DEBT-004 | ✅ Complete | XML docs added to `NodeOpCompletedEvent.ResultPayload` |
| DEBT-008 | ✅ Complete (documented) | Comment added to bus-mode constructor in `ClusterMaster.cs` |
| DEBT-002 | ✅ Verified resolved | `ExConSubsystem` already uses `const string SubsystemName = "ExCon"` |
| DEBT-003 | ✅ Verified resolved | `ClusterSlave` test constructor already uses named optional params |
| DEBT-005 | ✅ Verified resolved | `ReferencePrefetchHandler.Commit` clears `_pendingScenarioId` and `_pendingTransactionId` |

---

## 🧪 Testing Results

**FDP Unit Tests:** 32 / 32 passed  
- 3 new DEBT-007 tests added and passing:
  - `Queue_Survives_SwapBuffers_When_AsyncPrepareIsActive`
  - `MultiStep_Trajectory_BothCommitStatesApplied`
  - `FaultedPrepare_ClearsPendingQueue`

**ClusterRunner Integration Tests:** 39 / 43 (up from 37 / 43)
- ✅ `AllSubsystems_TransitionToOperatingLive_CommitStateIsNotDroppedAsDuplicate` — **now PASSES**
- ✅ `AllSubsystems_FullCycleTwice_LoadOperateUnloadIdle` — **now PASSES**
- Still failing (pre-existing, separate root cause):
  - `ClusterOpE2eScriptTests.OverlappingCheckpoints_Passes`
  - `ClusterOpE2eScriptTests.RecordAndReplaySeek_Passes`
  - `ClusterOpE2eScriptTests.PreviewStateRestore_Passes`
  - `ClusterOpE2eScriptTests.LiveFromReplayBranch_Passes`

**Build:** 0 errors, 0 warnings (solution-level)

---

## 📂 Files Modified

| File | Change |
|------|--------|
| `FDP/Toolkits/FDP.Toolkit.Orchestration/ClusterSlave.cs` | (1) Changed dedup `HashSet` from 2-tuple to 3-tuple including `stateDiscriminant`; (2) Added `_pendingIntents` queue; (3) Rewrote `Tick()` intent dispatch to buffer unseen intents when async prepare active; (4) Added `_pendingIntents.Clear()` in faulted prepare path |
| `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/ClusterSlaveTests.cs` | Added 3 new DEBT-007 unit tests |
| `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterCqrsEvents.cs` | Added XML doc to `NodeOpCompletedEvent.ResultPayload` (DEBT-004) |
| `Hrot.Orchestrator/ClusterMaster.cs` | Added NOTE comment to bus-mode constructor explaining DdsIdAllocatorServer hosting requirement (DEBT-008) |
| `.dev/cluster-master-cqrs-1/DEBT-TRACKER.md` | Marked DEBT-002, DEBT-003, DEBT-005, DEBT-007, DEBT-008 as resolved |

---

## 📝 Developer Insights

**Q1: Did the fix to `ClusterSlave` resolve the `AllSubsystems` tests? Were there any additional failures?**

Yes, the fix resolved both `AllSubsystems_TransitionToOperatingLive_CommitStateIsNotDroppedAsDuplicate` and `AllSubsystems_FullCycleTwice_LoadOperateUnloadIdle`. No additional failures were introduced. The pass count increased from 37 to 39 in the ClusterRunner integration suite.

**Q2: Did `ClusterOpE2eScriptTests` failures change? If so, what's the new failure count and pattern?**

No change — still exactly 4 failures (`OverlappingCheckpoints_Passes`, `RecordAndReplaySeek_Passes`, `PreviewStateRestore_Passes`, `LiveFromReplayBranch_Passes`). These tests fail with timeouts relating to E2e script execution, which has a different root cause unrelated to the multi-intent queue or dedup key. The BATCH-07 instructions explicitly noted these may have separate root causes and should not be assumed to be fixed by this batch.

**Q3: After resolving DEBT-002/003/005, were they actually done? Any surprises?**

All three were already done in earlier batches (BATCH-04/05):
- **DEBT-002**: `ExConSubsystem` already has `private const string SubsystemName = "ExCon";` (line 55), used in the ClusterSlave constructor call at line 144.
- **DEBT-003**: The `ClusterSlave` test constructor already uses named optional parameters (`FdpEventBus? eventBus = null, int nodeId = 0, string subsystemName = "TestNode"`), making intent explicit at call sites.
- **DEBT-005**: `ReferencePrefetchHandler.Commit` already clears both `_pendingScenarioId` and `_pendingTransactionId` and logs the ack. There was no surprise — the fixes were solid.

**Q4: What was the most subtle aspect of the `_pendingIntents` queue design?**

The subtlety is that intents must be dedup-checked at two points:
1. When buffering into `_pendingIntents` (we check `_seenTransactionIds.Contains(...)` WITHOUT adding to the set, to avoid marking unseen intents as seen before they are dispatched).
2. When `DispatchIntent` is called from the drain loop, it adds to `_seenTransactionIds` via `_seenTransactionIds.Add(dedupKey)`.

This ensures that if the bus delivers the same intent twice across two SwapBuffers cycles, the second delivery is correctly deduplicated by `DispatchIntent`. It also ensures that an intent that was buffered into `_pendingIntents` (because the async prepare was running) will still be correctly deduplicated when actually dispatched — preventing double-dispatch if a network retransmit arrives later.

Additionally, the test for `Queue_Survives_SwapBuffers_When_AsyncPrepareIsActive` revealed an interesting edge: after the async prepare task completes (and frees `_pendingPrepare`), the code drains `_pendingIntents` BEFORE reading from the bus again. This ensures in-order delivery: intents seen in a prior tick are processed before any new intents arriving in the current bus read buffer.

**Q5: Any weak points discovered in ClusterMaster's fan-out pattern?**

Yes. `ClusterMaster.PlanTransitionState()` fans out ALL steps of a multi-step trajectory (e.g., Idle→LoadingLive→OperatingLive) to slaves in a single `Tick()`. This means the bus write buffer always contains interleaved `PrepareXxx + CommitState + FinalizeXxx + CommitState` sequences for all steps at once. While this batch's fix handles this correctly on the slave side, there is still a question of ordering on multi-node setups: all nodes will receive all steps in the same buffer, but individual nodes may have different async prepare latencies. A node that finishes its first prepare quickly will drain its `_pendingIntents` and start the second prepare before other nodes have even started theirs. In a strict 2PC protocol, `CommitState` should only be dispatched after all nodes have ACKed the preceding prepare — but `ClusterSlave`'s autonomous `CommitState` processing (no ACK-wait) side-steps that concern. The risk is that local state and master's perceived state can diverge temporarily during multi-step trajectories if a node processes steps faster or slower than expected.
