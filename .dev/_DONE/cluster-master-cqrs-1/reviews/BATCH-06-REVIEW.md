# BATCH-06 Review

**Batch:** BATCH-06  
**Tasks:** CMC-S016, CMC-S017  
**Reviewer:** Dev-Lead  
**Decision:** ✅ APPROVED

---

## Verification Summary

- Build: `0 errors` ✅
- `Hrot.Orchestrator.Tests`: 79/79 ✅
- `Hrot.Orchestrator.Integration.Tests`: 12/12 ✅ (7 new + 5 existing ScenarioSaveLoad)
- `Hrot.ClusterRunner.Integration.Tests`: 37/43 ✅ (6 pre-existing failures unchanged)

---

## Quality Assessment

### CMC-S016 — Composition Root Wiring

**OrchestratorSubsystem**
- ✅ Bus-mode `ClusterMaster` correctly constructed with `FdpEventBus`
- ✅ `ClusterOpMasterTranslator` and `NodeOpMasterTranslator` created and ticked before `_clusterMaster.Tick()` — ingress first ordering is correct
- ✅ `DdsIdAllocatorServer` hosted here with a clean background thread and CTS-based shutdown in `Dispose()` — critical fix that prevented 28+ test regressions
- ✅ `using FDP.Toolkit.Orchestration` ambiguity avoidance: removed conflicting using, relied on fully-qualified `NodeHeartbeatEvent`

**ExConSubsystem**
- ✅ `_nodeOpSlaveTranslator.Tick()` called before `_clusterSlave.Tick()` — correct ordering
- ✅ `_orchestrationBus.SwapBuffers()` called before translator tick
- ✅ No duplicate field declarations

**NodeBootstrapper / SimHostApp**
- ✅ `SlaveTranslator` property exposed for external access
- ✅ `_nodeOpSlaveTranslator.Tick()` wired into tick loop

**IgApplication / CgfApplication**
- ✅ `NodeOpSlaveTranslator` created with correct `participant` local variable (not `_ddsParticipant`)
- ✅ `NodeHeartbeat` correctly qualified as `Hrot.NED.Descriptors.Orchestration.NodeHeartbeat`
- ✅ `_slaveTranslator.Tick()` before `_clusterSlave.Tick()`

**ClusterMaster**
- ✅ `BusTransitionAckTracker` nested class is clean and correctly keyed by `tx.TransactionId`
- ✅ `activeNodeIds` variable scope fix: hoisted before `if (!isLiveFromReplayBranch)` block; inner variable renamed `branchNodeIds` — no CS0136 ambiguity

### CMC-S017 — Integration Tests

**CqrsOrchestrationIntegrationTests.cs**
- ✅ Test helpers (`Frame`, `RunUntilCompleted`, `RegisterNode`) are concise and reusable
- ✅ `TransitionState_AllInOne_CompletesCqrsRoundTrip` — real 2PC round trip with `StubAllOpsHandler`; asserts `Success` status ✅
- ✅ `TransitionState_WithNoNodeRegistered_NoFanOut` — boundary condition; verifies no `ExecuteNodeOpIntent` published without registered nodes ✅
- ✅ `ManageEpisode_AllInOne_FansOutStartEpisodeIntent` — two-phase setup (Idle→LoadingLive→OperatingLive) correctly works around single-intent-per-frame limitation in ClusterSlave; asserts `StartEpisode` fan-out ✅
- ✅ `CancelOperation_FansOutAbortTransaction` — correctly asserts `AbortTransaction` ExecuteNodeOpIntent (NOT a `ClusterOpCompletedEvent`, which doesn't fire for cancel) ✅
- ✅ `NodeOpCompleted_WithFailure_PropagatesFailureStatus` — uses `FailingPrepareHandler` (throws in prepare) to trigger real failure path; asserts `Failure` status code ✅
- ✅ `NoBusEchoChamber_AfterNodeOpCompleted` — echo chamber regression test; verifies slave doesn't re-publish `ExecuteNodeOpIntent` after emitting `NodeOpCompletedEvent` ✅

**TranslatorRoundTripTests.cs**
- ✅ Uses domain 19 (reserved for this purpose) with real DDS loopback
- ✅ `ClusterOpRequest_ThroughTranslators_ProducesClusterOpStatus` — full translator chain: DDS reader → `ClusterOpMasterTranslator` → bus → `ClusterMaster` → bus → `NodeOpMasterTranslator` → DDS writer → `NodeOpSlaveTranslator` → bus → `ClusterSlave` → bus → `ClusterOpMasterTranslator` → DDS writer; asserts `Success` ✅

**Test assertions quality:**
- ✅ All tests assert on concrete values (StatusCode checks, Operation type checks) — not just "event not null"
- ✅ `FailingPrepareHandler` asserts on the actual runtime exception path, not just on a fabricated event

### Developer Insights Absorbed

| Finding | Debt Item | Action |
|---------|-----------|--------|
| ClusterSlave processes 1 intent/frame | DEBT-007 (P2) | Scheduled BATCH-07 |
| DdsIdAllocatorServer silently skipped in bus ctor | DEBT-008 (P3) | Scheduled BATCH-07 |
| FdpEventBus SwapBuffers silently drops events | DEBT-009 (P3) | Backlog |

---

## Minor Notes

- Debug assertion `Assert.True(master.NodeRoster.ActiveNodes.Count > 0, ...)` was present during development; correctly removed before commit ✅
- `ClusterOpRequestAdapter.cs` (BATCH-04) may overlap with `ClusterOpMasterTranslator` — evaluate consolidation in a future batch (not blocking) **(DEBT noted in BATCH-05 review, still open)**

---

## Suggested Git Commit Message

```
BATCH-06: CMC-S016/S017 - Composition root wiring and integration tests

CMC-S016 - Wire bus-mode ClusterMaster into all composition roots:
- OrchestratorSubsystem: bus ClusterMaster + translators + DdsIdAllocatorServer
- ExConSubsystem: NodeOpSlaveTranslator + SwapBuffers in tick loop
- NodeBootstrapper/SimHostApp: NodeOpSlaveTranslator wired and ticked
- IgApplication/CgfApplication: NodeOpSlaveTranslator wired and ticked
- ClusterMaster: BusTransitionAckTracker, activeNodeIds scope fix

CMC-S017 - Integration tests:
- CqrsOrchestrationIntegrationTests: 6 bus-mode AllInOne 2PC tests
- TranslatorRoundTripTests: DDS loopback round-trip test (domain 19)

Results: 79/79 unit, 12/12 integration (orchestrator), 37/43 cluster runner
(6 pre-existing ClusterOpE2e/AllSubsystems failures unchanged)
```
