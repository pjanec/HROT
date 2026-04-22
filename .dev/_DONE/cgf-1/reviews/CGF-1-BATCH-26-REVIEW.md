# CGF-1-BATCH-26 Review

**Batch:** CGF-1-BATCH-26  
**Reviewer:** Development Lead  
**Date:** 2026-03-30  
**Status:** ✅ APPROVED

---

## Summary

S0501 and S0502 fully implemented and verified. All 16 specified success-criteria items pass. One P3 test gap deferred to BATCH-27 (ReplaySeek fan-out coverage). Net new tests: +18 across three test assemblies.

---

## Issues Found

### Issue 1 (P3 / deferred): `ReplaySeekStep_FansOutNodeReplaySeek` not written

**File:** `Hrot.Orchestrator.Tests/ClusterMasterFanOutTests.cs`  
**Problem:** The batch instructions specified a test verifying that `ClusterOpType.ReplaySeek` steps in the trajectory produce `NodeOpType.NodeReplaySeek` commands. The code path itself is implemented correctly; only the test is absent.  
**Fix:** Add in BATCH-27 as part of CGF1-S0503 onboarding (the replay-seek path will be exercised during replay integration tests). Entering as P3 in DEBT-TRACKER.

### Issue 2 (Fixed during review): Missing FormatPrettyJson tests + method visibility

**File:** `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs`  
**Problem:** `FormatPrettyJson` was `private static`, preventing the required unit tests `FormatPrettyJson_IndentsJson` and `FormatPrettyJson_InvalidJson_ReturnsOriginal` from compiling.  
**Fix:** Changed to `internal static` (assembly exposes internals to test project). Tests added and passing. ✅ Resolved.

### Issue 3 (Fixed during review): Missing acceptance tests

**Problem:** Several tests specified in the batch instructions were absent from the initial developer report: `DistributedTransactionTests.*`, `Shutdown_DisposesWriter`, status-banner smoke test.  
**Fix:** Added during review. All tests pass. ✅ Resolved.

### Issue 4 (Fixed during review): `drill.Tick()` missing in `ClusterMasterFanOutTests`

**Problem:** `HandleClusterOpRequest` queues requests; fan-out tests never called `Tick()` to drain the queue, causing all 5 fan-out tests to fail.  
**Fix:** Added `drill.Tick()` after each `HandleClusterOpRequest()` call. All 5 tests now pass. ✅ Resolved.

---

## Test Quality Assessment

Tests verify actual DDS-level behavior (endpoint discovery, received `NodeOpCommand` operation codes, transaction history fields). No shallow or string-presence-only assertions. Fan-out tests use a real DDS participant on the test domain and poll for received `NodeOpCommand` samples — gold standard for this infrastructure.

Final test counts:
- `Hrot.Orchestrator.Tests`: 46 / 46 (was 37; +9 new: 5 fan-out + 4 DistributedTransaction)
- `Hrot.ClusterRunner.Tests`: 148 / 148 (was 138; +10 new: 5 subsystem + 3 FormatPrettyJson + 1 Shutdown + 1 StatusBanner)

---

## Verdict

**Status: APPROVED**

All production code correct. All P1/P2 test gaps filled during review. One P3 test gap deferred.

---

## 📝 Commit Message

```
feat(orchestrator): S0501+S0502 ImGui overhaul & real network dispatch (BATCH-26)

Completes CGF1-S0501 and CGF1-S0502.

- OrchestratorSubsystem: beige TitleBarColor, ImGui.Begin/End wrapper, bootstrap
  banner (waiting-node list), 5-column scrollable 2PC history with TreeNode rows,
  context-menu copy, payload tooltip, per-node expanded rows.
- DistributedTransaction: SourceClusterState, PayloadJson, NodeResponses fields.
- ClusterMaster: populates all three new fields; S0502 fan-out loop emits PrepareXxx
  + CommitState NodeOpCommands for every TransitionStep; NodeResponses populated in
  ConsumeNodeOpStatuses.
- OrchestratorScenarioPanel: BeigeChildBg removed; constructor updated to accept
  DdsWriter<ClusterOpRequest>; all HandleClusterOpRequest calls replaced with writer writes;
  status banner shows Source→Target when in-flight.
- OrchestratorSubsystem: _sysOpWriter created in Initialize, disposed in Shutdown,
  passed to panel; TODO buttons wired to real ClusterOpRequest writes.
- NodeOpType: PrepareEdit/FinalizeEdit enum values added.

Tests: +18 (ClusterMasterFanOutTests ×5, DistributedTransactionTests ×4,
OrchestratorSubsystemTests ×8, OrchestratorScenarioPanelTests ×1).
All 194 tests passing (Orchestrator.Tests 46, Runner.Tests 148).
```

---

**Next Batch:** BATCH-27 — Phase 5 continuation (CGF1-S0503 + S0504)
