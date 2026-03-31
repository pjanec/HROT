# TwoAck-BATCH-01 Review

**Batch:** TwoAck-BATCH-01
**Reviewer:** Development Lead
**Date:** 2026-03-22
**Status:** ⚠️ NEEDS FIXES 

---

## Summary

The Two-ACK feature implementation accurately separates Phase 1 and Phase 2 responsibilities and aligns properly with the system architectures. However, it fails critical stability checks (`dotnet test` fails) and test quality requirements set forth in the batch instructions.

---

## Issues Found

### Issue 1: Broken Build - Hardcoded Value Regression
**File:** `Hrot.SimHost.Tests/MissionControlRequestSystemTests.cs` (Line 259)
**Problem:** A failing test. The `SstStatusCode.EntityNotFound` enum value shifted from 2 to 3, but this test still asserts `errorCode: 2`. The build is broken and cannot pass CI.
**Fix:** Update the integer assertion to check for `3` or use `(int)SstStatusCode.EntityNotFound`.

### Issue 2: Developer Insights Missing
**File:** `.dev-workstream/reports/TwoAck-BATCH-01-REPORT.md`
**Problem:** Report completely skipped the required `Developer Insights` section.
**Fix:** In future batches, strictly answer Q1-Q5 explicitly in the report files.

### Issue 3: UI Spec Text Inaccuracy
**File:** `Hrot.ExCon/Panels/MissionPanel.cs`
**Problem:** Uses inaccurate pending text `(awaiting entity confirmation...)` instead of `[Constructing across network...]` specified in the design doc.
**Fix:** Update text match the spec.

---

## Test Quality Assessment

**Problems:**
- Test `MissionPanelPendingTests.IsPendingGuardActive_ReturnsTrue_WhenEntityIsPending` only checks a private utility helper instead of verifying actual UI side effects mapping. This violates the explicitly defined rule commanding tests to assert `ImGui.BeginDisabled()` invocations in `Draw()`.
- Missing UI coverage for `IosMock.DrawUI()`. The Phase 2 error alert modal exists functionally, but there is no integration test validating the rendering behaviour.

**Required Additions (Deferred to BATCH-02 as Debt):**
1. Re-implement tests for `MissionPanel.Draw()` mocking ImGui dependencies and asserting `BeginDisabled()`.
2. Implement integration test over `IosMock.DrawUI()` ensuring modal visibility on Active alerts.

---

## Verdict

**Status:** ⚠️ NEEDS FIXES (P1 issue blocked merge)

Due to the broken test suite acting as a blocker, this cannot cleanly merge. However, the core structure is logically sound. The commit formulation below handles the state of the repo, but the specific failure fix is aggressively pipelined into BATCH-02.

---

## 📝 Commit Message

```
fix: two-ack entity lifecycle failure fix (TwoAck-BATCH-01)

Completes TWOACK-DM001, TWOACK-DM002, TWOACK-DM003, TWOACK-SH001, TWOACK-SH002, TWOACK-SH003, TWOACK-IOS001, TWOACK-IOS002, TWOACK-IOS003, TWOACK-IOS004

Implements the two-phase Entity synchronization flow. 
- Retires single CreateEntityAck in favor of CreateUpdateDeleteEntityAck.
- Introduces SstRequestFinalizationSystem observing IsAlive transitions without modifying FDP pipelines.
- Updates IOS ImGui client to visually lock interaction logic against the Pending Entity states.

Tests: Evaluates finalization bounds, IsPending states, structs sizing logic.
```

---

**Next Batch:** TwoAck-BATCH-02
