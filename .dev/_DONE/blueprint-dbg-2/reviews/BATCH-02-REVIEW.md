# BATCH-02 Review
**Status:** ✅ APPROVED (with 2 items carried to BATCH-03 Corrective Task 0)   **Date:** 2026-06-10

## Summary
`SubTickSnapshotRecorder` wired into `BlueprintDebugSession`: live repo via `SetLiveRepository` (EditorSubsystem), `BeginTick` on tick boundary in `OnNewTick`, `RecordNodeEntry` in `OnNodeEnter` behind a `RecordingActive` gate. Recording works end-to-end through a real compiled blueprint. CF-6 stepping / `_isPaused` undisturbed.

## Verification performed (independent)
- Read full diff. `OnNodeEnter` recording added AFTER history+overlay, BEFORE temp-BP logic — does not alter CF-6 flow. `RecordingActive = _liveRepo != null && (_breakpoints.Count>0 || _tempBreakpoints.Count>0)` → zero work when unarmed. EditorSubsystem wires `SetLiveRepository(_world)` at the correct site (after `SetDataBreakpointManager`). `BlueprintAssetBuilder.Sequence` is a benign test helper.
- Ran new integration tests → **4/4 pass** (first attempt hit a transient unrelated DDS IDL codegen artifact; clean on retry).
- Ran FULL `Hrot.Blueprints.Tests` → **1712 passed / 7 failed / 8 skipped / 1727 total**. Reconciles exactly: 1708 (post-BATCH-01) + 4 new = 1712; 7 failures unchanged (pre-existing), 8 skipped unchanged → **zero new failures**. (Report said "1719" — a typo; actual verified count is 1712.)

## Issues Found (carried to BATCH-03 Corrective Task 0)
### Issue 1 (P1): Recording is not entity-scoped
**File:** `BlueprintDebugSession.cs` `RecordingActive` / `OnNodeEnter`.
**Problem:** the gate checks only "armed + live repo", not `_entityFilter`. `RecordNodeEntry` fires for every entity passing the (possibly-null) filter; multiple instrumented entities in one tick interleave into one ring → scrambled recordings + cross-entity `BumpMemoryVersion`. Tests use one entity so it's masked.
**Inert today** (nothing consumes recordings yet), but MUST be fixed before BATCH-03 wires navigation.
**Fix:** scope recording to the single debugged entity (record only when `self == _entityFilter`, or define the debugged entity explicitly for the recording session).

### Issue 2 (P2): Integration Test 2 intermediate assertion is loose
**File:** `SubTickRecorderIntegrationTests.cs` Test 2.
**Problem:** asserts `countAtLastNode < 20 && != 20`, which passes even at `0` (== pre-tick state) — so it does NOT robustly prove the *intermediate* value (A==10) was captured. `restore(0)==0` + `final==20` is solid for "recording happens", but the headline sub-tick-visibility claim needs a recorded node asserting exactly `10`.
**Fix:** with navigation + known node ordering in BATCH-03, assert some recorded node shows exactly the intermediate value.

## Verdict
APPROVED — wiring correct, zero regressions, recording proven through the real pipeline. BATCH-03 starts with Corrective Task 0 (Issues 1 & 2).

## Commit Message
(see commit)
