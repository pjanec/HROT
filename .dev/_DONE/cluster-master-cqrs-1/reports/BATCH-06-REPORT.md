# BATCH-06 Report

**Batch:** BATCH-06 — Composition Root Wiring and Integration Tests  
**Developer:** AI Developer (Claude Sonnet 4.6)  
**Date:** 2025-07-08  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| CMC-S016 | ✅ Complete | All 6 composition roots wired: OrchestratorSubsystem, ExConSubsystem, NodeBootstrapper, SimHostApp, IgApplication, CgfApplication |
| CMC-S017 | ✅ Complete | 7 new integration tests: 6 bus-mode AllInOne + 1 DDS translator round-trip |

---

## 🧪 Testing Results

**Unit Tests Passed:** 79 / 79 (`Hrot.Orchestrator.Tests`)  
**Integration Tests Passed (new):** 12 / 12 (`Hrot.Orchestrator.Integration.Tests`)  
**Regression:** 37 / 43 (`Hrot.ClusterRunner.Integration.Tests`) — 6 pre-existing failures unchanged

**Pre-existing failures (unaffected):**
- `AllSubsystems_TransitionToOperatingLive_CommitStateIsNotDroppedAsDuplicate`
- `AllSubsystems_FullCycleTwice_LoadOperateUnloadIdle`
- `ClusterOpE2eScriptTests.OverlappingCheckpoints_Passes`
- `ClusterOpE2eScriptTests.RecordAndReplaySeek_Passes`
- `ClusterOpE2eScriptTests.PreviewStateRestore_Passes`
- `ClusterOpE2eScriptTests.LiveFromReplayBranch_Passes`

**Key Test Scenarios Verified:**
- [x] AllInOne 2PC `TransitionState` round-trip → `ClusterOpCompletedEvent.Success`
- [x] No fan-out when no node registered
- [x] `ManageEpisode` (IsStart=true) fans out `StartEpisode` ExecuteNodeOpIntent
- [x] `CancelOperation` produces `AbortTransaction` ExecuteNodeOpIntent fan-out
- [x] Failure propagation: FailingPrepareHandler → `ClusterOpCompletedEvent.Failure`
- [x] No echo-chamber: `NodeOpCompletedEvent` does NOT cause slave to re-publish `ExecuteNodeOpIntent`
- [x] Translator round-trip via real DDS loopback (domain 19): `ClusterOpRequest` → `ClusterOpStatus.Success`

---

## 📂 Files Modified

| File | Change |
|------|--------|
| `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs` | Bus-mode ClusterMaster; ClusterOpMasterTranslator/NodeOpMasterTranslator; DdsIdAllocatorServer hosted here |
| `Hrot.ClusterRunner/Services/ExConSubsystem.cs` | NodeOpSlaveTranslator wired; SwapBuffers + slaveTranslator.Tick() before slave.Tick() |
| `Hrot.SimHost/NodeBootstrapper.cs` | NodeOpSlaveTranslator created and exposed as `SlaveTranslator` property |
| `Hrot.SimHost/SimHostApp.cs` | SlaveTranslator.Tick() called in tick loop |
| `Hrot.IG/IgApplication.cs` | NodeOpSlaveTranslator wired; Tick() added |
| `Hrot.CGF/CgfApplication.cs` | NodeOpSlaveTranslator wired; _slaveTranslator.Tick() before _clusterSlave.Tick() |
| `Hrot.Orchestrator/ClusterMaster.cs` | BusTransitionAckTracker; _pendingBusTransitionAcks; activeNodeIds hoisted out of if-block |
| `Hrot.Orchestrator.Integration.Tests/CqrsOrchestrationIntegrationTests.cs` | New file: 6 AllInOne integration tests |
| `Hrot.Orchestrator.Integration.Tests/TranslatorRoundTripTests.cs` | New file: DDS translator round-trip test |

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

1. **Build error cascade (7 errors):** Adding `_orchestrationBus = _eventBus` to `CgfApplication` failed because `_orchestrationBus` was a read-only property (`=> _eventBus`), not a field. Fixed by removing the assignment. In `ClusterMaster.cs`, `activeNodeIds` was declared inside an `if (!isLiveFromReplayBranch)` block but referenced outside — moved declaration before the block and renamed the inner one to `branchNodeIds`. In `IgApplication.cs`, the DDS participant local variable is `participant` (not `_ddsParticipant`), and `NodeHeartbeat` lives in `Hrot.NED.Descriptors.Orchestration` (not `Hrot.NED.Messages`). `ExConSubsystem.cs` ended up with duplicate field declarations from two successive edits — removed the duplicates. Adding `using FDP.Toolkit.Orchestration` to `OrchestratorSubsystem.cs` caused a `ClusterOpType` ambiguity with the existing `using Hrot.NED.Descriptors.Orchestration` — removed the new using and relied on fully-qualified names where needed.

2. **Critical regression — DdsIdAllocatorServer missing in bus-mode:** The bus-mode `ClusterMaster` constructor sets `_idAllocatorServer = null!` and never creates the server. When `OrchestratorSubsystem` switched to bus-mode ClusterMaster, the `DdsIdAllocatorServer` stopped running, causing `SimHostSubsystem` to time out with "DdsIdAllocator publication match not established within 30s". This caused 28+ test failures. Fix: create and manage `DdsIdAllocatorServer` + background `Thread` directly in `OrchestratorSubsystem`, started in `Initialize()`, stopped in `Dispose()`.

3. **ManageEpisode test — multi-step transition loss:** `PlanManageEpisode` requires `ClusterState.OperatingLive`. The initial attempt was to set state via a multi-step `Idle→LoadingLive→OperatingLive` transition in one test. This failed because `ClusterSlave.Tick()` processes only one `ExecuteNodeOpIntent` per frame (breaks after setting `_pendingPrepare`), and `SwapBuffers()` clears unconsumed intents. The fix was to use two separate single-frame transitions: one to reach `LoadingLive`, one to reach `OperatingLive`, then publish the `ManageEpisodeIntent`.

4. **ManageEpisode test — ScenarioId required:** `PlanManageEpisode` throws `InvalidOperationException` when `IsStart=true` and `ScenarioId` is null/empty. The intent must include `ScenarioId = "test_episode_scenario"`.

5. **CancelOperation produces no ClusterOpCompletedEvent:** `CancelOperationIntent` triggers an `AbortTransaction` `ExecuteNodeOpIntent` fan-out, not a `ClusterOpCompletedEvent`. The test was rewritten to assert on the `AbortTransaction` intent.

6. **NodeOpCompleted failure test — injected events have wrong txId:** Manually publishing `NodeOpCompletedEvent` with a fabricated `TransactionId` does not match any pending transaction in ClusterMaster — the status is ignored. Used a `FailingPrepareHandler` (throws during prepare) to trigger the actual failure path instead.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

1. **Single-intent-per-frame limitation in ClusterSlave:** `Tick()` processes only one `ExecuteNodeOpIntent` per frame (breaks on `_pendingPrepare`). When the bus is the transport, this means multi-step cluster operations (e.g., reaching `OperatingLive` from `Idle` requires two transitions) cannot be issued in the same frame. This is the root cause of the two pre-existing AllSubsystems failures and four ClusterOpE2e failures. The fix is to loop in `Tick()` until `_pendingPrepare` is null or the buffer is empty, but that requires careful design to avoid runaway processing. **(DEBT-007, P2)**

2. **DdsIdAllocatorServer tightly coupled to DDS ClusterMaster constructor:** The fact that the bus-mode constructor silently skips the ID allocator server is a footgun. The server should either be a separate injectable dependency or both constructors should document clearly that the caller is responsible for hosting the server. **(DEBT-008, P3)**

3. **`FdpEventBus.SwapBuffers` clears read buffer on each swap:** Any event that is not consumed before `SwapBuffers` is silently dropped. This makes multi-intent sequences in a single tick impossible and makes debugging lost events difficult. Consider whether a draining-queue mode would be safer for the orchestration path. **(DEBT-009, P3)**

**Q3: What design decisions did you make beyond the instructions?**

- `DdsIdAllocatorServer` in `OrchestratorSubsystem`: The instructions did not mention this server. Discovered it was the root cause of 28+ test regressions and added it as a fix rather than leaving the regressions unresolved.
- ManageEpisode test uses two single-step transitions instead of one multi-step: This is a workaround for the single-intent-per-frame limitation. An alternative would have been to test from pre-seeded state, but using the real tick loop is more robust.
- `FailingPrepareHandler` inner class: Used for the failure propagation test instead of injecting fabricated events, making the test more realistic.
- Bus ACK tracking keyed by `tx.TransactionId` (the `DistributedTransaction` ID), not the `intent.TransactionId` — this is correct because a single cluster op can fan out to multiple nodes and all ACKs must reference the same transaction.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- `ManageEpisode` with `IsStart=true` requires a non-empty `ScenarioId` — this constraint is enforced inside `PlanManageEpisode` but is not documented in the spec or intent struct.
- `ClusterSlave` in bus mode only processes one `ExecuteNodeOpIntent` per `Tick()` call, making single-step transitions the only safe pattern in integration tests.
- `ClusterOpMasterTranslator.Tick()` must be called BEFORE `ClusterMaster.Tick()` so ingress is processed first. If called after, the first tick loses the ingress event.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- `DdsIdAllocatorServer` uses `Thread.Sleep(1)` polling in a background thread. A semaphore or event-driven approach would be more efficient but is acceptable for the current use case.
- The `BusTransitionAckTracker` dictionary in `ClusterMaster` allocates a new `List<int>` per ACK tracking entry. For high-frequency orchestration, a pooled or struct-based approach would be better.

---

## ⚠️ Outstanding Issues / Next Steps

- [ ] **DEBT-007 (P2):** ClusterSlave processes only 1 `ExecuteNodeOpIntent` per frame — fix by looping until `_pendingPrepare` is null. This will also fix the 6 pre-existing AllSubsystems/ClusterOpE2e failures.
- [ ] **DEBT-008 (P3):** `DdsIdAllocatorServer` should be a first-class injectable dependency, not silently skipped by the bus-mode ClusterMaster constructor.
- [ ] **DEBT-009 (P3):** `FdpEventBus.SwapBuffers` silently drops unprocessed events — evaluate whether a draining-queue mode is safer for orchestration path.
- [ ] **DEBT-004 (P2):** `NodeOpCompletedEvent.ResultPayload` is `object?` — document or close the allowed type set once translators are stable.
- [ ] **DEBT-006 (P3):** `ReferenceArchiveHandler.PrepareAsync` has no timeout for file-scan.

---

## 📌 Git Commit Message

```
BATCH-06: CMC-S016/S017 — Composition root wiring and integration tests

CMC-S016 — Wire bus-mode ClusterMaster into all composition roots:
- OrchestratorSubsystem: bus ClusterMaster + translators + DdsIdAllocatorServer
- ExConSubsystem: NodeOpSlaveTranslator + SwapBuffers in tick loop
- NodeBootstrapper/SimHostApp: NodeOpSlaveTranslator wired and ticked
- IgApplication/CgfApplication: NodeOpSlaveTranslator wired and ticked
- ClusterMaster: BusTransitionAckTracker, activeNodeIds scope fix

CMC-S017 — Integration tests:
- CqrsOrchestrationIntegrationTests: 6 bus-mode AllInOne 2PC tests
- TranslatorRoundTripTests: DDS loopback round-trip test (domain 19)

Results: 79/79 unit, 12/12 integration (orchestrator), 37/43 cluster runner
(6 pre-existing ClusterOpE2e/AllSubsystems failures unchanged)
```
