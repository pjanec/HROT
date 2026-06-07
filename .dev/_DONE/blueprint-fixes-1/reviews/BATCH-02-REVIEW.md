# BATCH-02 Review

**Batch:** BATCH-02  
**Reviewer:** Development Lead  
**Date:** 2026-05-31  
**Status:** APPROVED WITH REQUIRED CORRECTIONS IN BATCH-03

---

## Summary

6 debug map / debug protocol defects implemented (BPF-002, BPF-021, BPF-001, BPF-003, BPF-004, BPF-005). 29 new tests added. 852/860 passing. Core structural work is solid; two test coverage gaps must be addressed in BATCH-03.

---

## Issues Found

### Issue 1 (P1): BPF-001 -- AiPrimitive field-value reading is completely untested

**File:** `DebugMapExtensionTests.cs` `StateSnapshotTests`  
**Problem:** The `CaptureAiPrimitiveState` path (reads `Blackboard1024` via `MemoryMarshal`, checks structure-hash, iterates `StateLayout.Fields`) has no test. All four `StateSnapshotTests` tests either use Library dispatch (empty fields) or do not inject a `Blackboard1024` component (the `StubSimulationView.HasComponent` returns false). The batch instructions explicitly required: "Snapshot `FieldValues` contains at least one field with the correct value for a known blueprint parameter." This was not delivered.  
**Fix:** Add a test with a `StubSimulationView` that returns a pre-populated `Blackboard1024` (unsafe fixed buffer with known bytes at known offsets), a registered `BlueprintDefinition` of AiPrimitive kind with matching `StructureHash` and `StateFields`, and assert `FieldValues["Speed"] == 3.14f` (or similar concrete value). This exercises the `MemoryMarshal` path and verifies the structure-hash check works.

### Issue 2 (P2): BPF-003 -- HitCount accumulation across same-frame entities not tested

**File:** `DebugMapExtensionTests.cs` `BreakpointHashSafetyTests.OnNewTick_ResetsDedupSet_AllowingSecondTickHit`  
**Problem:** Design §9.2 requires "hit-count accumulation across entities" within the same frame: subsequent same-frame hits on other entities should increment `HitCount` even though no new pause is triggered. The current test verifies dedup (no second pause) but never checks `HitCount` increments on the second entity's hit.  
**Fix:** After the E2 same-tick hit in `OnNewTick_ResetsDedupSet_AllowingSecondTickHit`, assert `session.GetBreakpoints()[0].HitCount == 2` (both E1 and E2 incremented it).

### Issue 3 (Minor): Report placed in `batches/` folder instead of `reports/`

**File:** `.dev/blueprint-fixes-1/batches/BATCH-02-REPORT.md` -- should be `.dev/blueprint-fixes-1/reports/BATCH-02-REPORT.md`  
**Fix:** Move file in BATCH-03 setup.

---

## Test Quality Assessment

Approved areas:
- **BPF-002/021**: Debug map serialization tests verify actual field names, offsets, sizes, type names, and paths -- not just presence. Graph/pin/stateLayout round-trips all check concrete values. `DebugMapIndex` lookup tests verify actual content.
- **BPF-003**: `StaleBreakpoint_DoesNotPause` correctly verifies no pause after stale marking. `RegisterDebugMap_WithChangedHash_MarksExistingBreakpointsStale` checks `bp.IsStale`. `SetBreakpoint_CapturesStructureHash_WhenMapRegistered` checks exact hash value. `RegisterDebugMap_WithSameHash_DoesNotMarkBreakpointsStale` verifies no false-positive staleness.
- **BPF-004**: Entity presence in `GetActiveEntities(assetId)` is verified with correct Guid; Guid.Empty fallback tested.
- **BPF-005**: `StepOut_AtDepthZero_PausesOnNextTickBoundary` verifies no pause same tick, then pause on next tick -- behavioral sequence test. `StepOut_EntityDies_AbandonsStepping` verifies abandonment via `EntityAlive = false`.

Required additions (see corrective tasks above).

---

## Verdict

**Status: APPROVED** -- core implementation accepted. Two test coverage gaps must be addressed as corrective tasks at the start of BATCH-03 before any new work begins.

---

## 📝 Commit Message

```
fix: blueprint debug map extension + debug protocol fixes (BATCH-02)

Completes BPF-002, BPF-021, BPF-001, BPF-003, BPF-004, BPF-005

Extends the compiler debug map with pins, graphs, stateLayout, assetName,
generatedSourcePath, and NodeKind/DisplayName on map entries. Implements
GetCurrentStateSnapshot with AiPrimitive field-value reading via MemoryMarshal.
Adds structure-hash safety and staleness to breakpoints; implements per-frame
entity dedup with OnNewTick reset. Aligns peer-call probe signature to use
Guid-based asset id for correct active-entity keying. Fixes StepOut at depth 0
(tick-boundary re-pause) and entity-death step abandonment.

DebugMapBuilder/Serializer/Index: pins, graphs, stateLayout, assetName, generatedSourcePath
BlueprintStateSnapshot: expanded to (Self, AssetId, AssetName, Dispatch, FieldValues, Cursor)
Breakpoint: AssetStructureHashAtSetTime, IsStale; RegisterDebugMap marks stale not clears
IBlueprintProbeSink: OnPeerCallEnter/Exit use string peerAssetIdString (Guid.TryParse on enter)
IBlueprintDebugSession: OnNewTick() added; StepOut tracks _stepFromTick for depth-0 boundary

Tests: 852 passing, 8 skipped (up from 823). 29 new tests.
```

---

**Next Batch:** BATCH-03 (Corrective: BPF-001 field values test + BPF-003 HitCount; then HSM Host + BTree Host fixes)
