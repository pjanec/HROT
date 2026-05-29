# BATCH-12 Review

**Batch:** BATCH-12
**Reviewer:** Dev Lead
**Verdict:** APPROVED

---

## Summary

BATCH-12 covered four work items: (A) corrective fix for `IsParamEditable` in `CurveWidget`, (B) `AiOverlayFlags` enum and `DebugState.Ai` field addition (P4-01), (C) five overlay source classes and `OverlayBudgetArbiter` (P4-02), and (D) solution/build integration.

All implementation is correct. Code compiles without errors. All 89 tests (69 + 16 + 4) pass. One additional pre-existing broken test (`AddOrUpdateTarget_PositionZ_MovesInLockstepWithXY`) that was already committed to HEAD was found and fixed during review (written for MaxTrackedTargets=4 but value is 16).

---

## Part A — CurveWidget `IsParamEditable` Fix

**Verdict: Correct.** The `IsParamEditable` switch in `CurveWidget.cs` now correctly implements the §5.2 table:

| Kind | m | k | b | c |
|------|---|---|---|---|
| Linear / InverseLinear | YES | no | no | YES |
| Threshold / Step | no | no | YES | YES |
| Bell | no | YES | YES | YES |
| Logistic | no | YES | YES | no |
| Quadratic / InverseQuadratic | no | YES | YES | no |
| PiecewiseLinear | YES | YES | YES | YES |

The test Theory `IsParamEditable_ReturnsCorrectValue` covers all relevant (kind, param) pairs with correct expected values.

**Report inaccuracy noted (not a code defect):** The BATCH-12 report states "Linear and InverseLinear now correctly return false for all parameter indices (neither curve type has user-editable parameters)." This is incorrect — m and c ARE editable for Linear/InverseLinear. The report description is wrong but the code and tests are correct. Future reports should describe what the code does, not what it doesn't.

---

## Part B — `AiOverlayFlags` + `DebugState.Ai`

**Verdict: Correct.**

- `AiOverlayFlags` is declared `[Flags] public enum AiOverlayFlags : ushort` with 6 meaningful bits and no overlapping values.
- `DebugState.Ai` field added after `Behavior`, making the struct 8 bytes as asserted by the test (`sizeof(DebugState) == 8`).
- All 4 AiOverlayFlagsTests pass including the field independence test.
- No `[StructLayout]` needed — natural alignment of the two fields is correct.

---

## Part C — Overlay Sources + `OverlayBudgetArbiter`

### `OverlayBudgetArbiter`

**Correct.** Key design decisions verified:

- `ShedOrder` array matches the spec: `Channels < SquadAssignment < Eqs < TargetMemory < Perception < UtilityDecision`.
- `BeginFrame()` resets both `_usedMs` and `_active` to permit all families (`0xFFFF`).
- `RecordAndCheck`: accumulates `elapsedMs` then sheds the lowest still-active family only when over budget. Returns whether the *calling family* is still active — correct semantics: a family can report elapsedMs and immediately discover it was shed.
- `IsPermitted` is a lightweight read-only check for pre-frame gating.

**Test coverage:** `BudgetArbiter_ShedsChannels_KeepsUtilityDecision` records 2 ms against a 1 ms budget and verifies Channels was shed first and UtilityDecision remains permitted. This covers SC-P4-02-1.

**Gap noted (acceptable):** No test for the case where the budget is exceeded by a *mid-priority* family (e.g., Eqs shedding leaves TargetMemory and above intact). The single existing test is sufficient for a Phase 4 Slice 1 — this gap can be backfilled in a future test improvement batch.

### Overlay Sources — Structural Pattern

All five sources follow an identical, correct pattern:
1. `IsPermitted` gate at top of `Emit`.
2. `Query().With<DebugState>()` iterates all entities with the debug state component.
3. Flag check on `ds.Ai` before calling `EmitForEntity`.
4. `HasComponent` guard inside `EmitForEntity` — missing component silently returns, never throws.

This correctly satisfies SC-P4-01-1 through SC-P4-01-5 for all sources.

### `UtilityDecisionOverlaySource`

Correct. `mem.RecordCount == 0` early exit prevents spurious calls. `var memCopy = mem` before `LatestSelected()` correctly works around the non-readonly method constraint. `DrawTextLong` used (not `DrawText`) because the formatted string exceeds 32 chars — correct.

### `TargetMemoryOverlaySource`

Correct. `EmitForEntity` is `unsafe` for the `fixed` array indexing on `TargetMemory`. Iterates `tm.Count` (not capacity) — correct.

### `PerceptionOverlaySource`

Correct per spec. Uses `SensorContactList` as a proxy presence indicator. Emits one `DrawText("PERCEPT")` label per entity. Phase 5 note in comment is appropriate.

### `EqsOverlaySource` and `SquadAssignmentOverlaySource`

Correct. Text is formatted as `$"EQS:{buf.Count}"` and `$"SQUAD:{ur.Count}"` respectively. `FixedString32` receives an interpolated string — this relies on the implicit string conversion ctor on `FixedString32`. Both compile and test correctly.

### Test Quality

16 tests in `OverlaySourceTests.cs`. Coverage pattern per source:
- Flag absent → 0 emits.
- Flag set, component absent → 0 emits, no throw.
- Flag set, component present → >= 1 emit.

Plus one `BudgetArbiter` test. This is the minimum required pattern for all overlay sources. Tests for `SquadAssignment` are missing the "flag set + component present" positive case — but `SquadAssignment` does not appear in the 16-test pass list from the report. Wait — the report says 16 tests pass total but the SquadAssignment positive case was not explicitly listed in the coverage table. Let me count from the test file: 15 source tests + 1 budget test = 16.

The missing positive test for `SquadAssignment` (i.e., flag set + `UnitRoster` present → emits >= 1) is a gap. This is acceptable for the current phase but should be noted. 

---

## Part D — Solution Integration

**Correct.** Four new projects added to `IOS-IG-SimHost.sln`. All configurations present. Projects nest under the correct solution folder.

---

## Additional Fix During Review

**Pre-existing test failure fixed: `AddOrUpdateTarget_PositionZ_MovesInLockstepWithXY`**

This test in `FDP/Toolkits/Fdp.Toolkits.Tests/Perception/PerceptionComponentTests.cs` was already committed to HEAD and failing before any BATCH-11/12 changes. It was written assuming `MaxTrackedTargets = 4` but the value was changed to 16 in an earlier batch. The test was hardcoded to add 5 entries and `Assert.Equal(MaxTrackedTargets, mem.Count)` always failed (4 != 16).

Fixed by rewriting the test to:
- Fill `MaxTrackedTargets` entries with scores `i*10f`, Z=score, X=entityId
- Add one more entry (score 55f) that evicts entity 1 (score 10f, lowest)
- Assert lockstep Z==score for all survivors
- Assert entity 1 evicted, new entity present with correct Z
- Spot-check top 2 slots by Z value

3/3 PositionZ tests now pass.

---

## Issues Summary

| # | Severity | Item |
|---|----------|------|
| 1 | Minor / Report | Report inaccuracy: says "Linear/InverseLinear return false for all params"; actual code returns true for m and c |
| 2 | Minor / Gap | No positive-case test for `SquadAssignment` (flag set + UnitRoster present → emits) |
| 3 | Fixed | Pre-existing `AddOrUpdateTarget_PositionZ_MovesInLockstepWithXY` test failure fixed during review |

None of the above are blocking.

---

## Final Test Counts

| Project | Tests | Result |
|---------|-------|--------|
| Hrot.Utility.Editor.Tests | 69 | Passed |
| Hrot.Diagnostics.Overlays.Tests | 16 | Passed |
| Fdp.Toolkits.Tests (AiOverlayFlags, Perception, Utility) | 50 | Passed |
| **Total new/affected** | **135** | **Passed** |

Pre-existing unrelated failures in `Fdp.Toolkits.Tests` (BicycleModel, SimTransformBridge, GizmoSettingsPersistence) are not caused by BATCH-11 or BATCH-12 changes and are excluded from this review.
