# BATCH-07 REVIEW

**Reviewer:** Dev Lead  
**Date:** 2025-07-18  
**Batch Report:** `.dev/utility-ai/reports/BATCH-07-REPORT.md`  
**Verdict:** APPROVED WITH DEV-LEAD FIXES

---

## Summary

BATCH-07 completed the Blueprint integration pipeline for `ScoreDecisionNode` and
`ReadRankedResultNode` (TASK-UAI-P1-09, Step 1-C-4 through 1-C-7). Build: 0 errors.
Both new runtime tests pass (SC-P1-09-3 and SC-P1-09-4).

---

## Architecture Review

### Stage5_Schedule.cs
`ScoreDecisionNode` case correctly inserted after `SpawnEqsSensorNode` block. The decision ID
is baked at compile time using an inlined FNV-1a-32 (`ComputeDecisionId` private method) —
this avoids introducing a compile-time dependency on `Fdp.Toolkit.Utility` in the compiler
project. The inlined implementation is byte-for-byte identical to `In.Fnv1a32` in
`UtilityDecisionBuilderInfra.cs` (both use `2166136261u` seed, same XOR-multiply loop).
Hash equivalence confirmed; the baked ID will match the registered ID at runtime. ✅

`ReadRankedResultNode` case mirrors `ReadEqsResultNode` exactly — same pin-iteration,
`IrOp_FieldRead` per output pin, same `_pinValueCache` pattern. ✅

**Note on SizeBytes=16:** The `IrTypeRef.SizeBytes = 16` is an IR-layer hint only; the actual
CLR struct (`bool IsValid; long Entity; float Score;` under `LayoutKind.Sequential`) will be
20-24 bytes depending on packing. The hint is only used for IR slot allocation, not emitted
code. The generated code is correct. No action required.

### StatementEmitter.cs
Two new cases added immediately after `IrOp_ReadEqsResult`. Variable naming (`__t{idx}`),
parameter order, and guard pattern (`if (idx >= 0)`) are consistent with the existing
`IrOp_ReadEqsResult` case. ✅

### InstanceEmitter.cs
`CollectScoreDecisionOps`, `CollectReadRankedResultOps`, `EmitScoreDecisionHelpers`,
`EmitReadRankedResultHelpers` all follow the `CollectReadEqsResultOps`/`EmitReadEqsResultHelpers`
structural template exactly. Wired at the correct position in `Emit(IrAsset)`. ✅

---

## Test Quality Review

### SC-P1-09-3: `ScoreDecisionNode_Produces_WinningOption`

**Pass/Fail:** PASS ✅

**Quality assessment:**
- Uses the REAL `CombatPostureDecision.AssetId` GUID (`"3c6f9e42-5d10-6f3a-ac23-posture0000001"`)
  — the developer's report called it "synthetic placeholder" but it is in fact the production
  constant. Confirmed by reading `CombatPostureDecision.cs` line 9.
- Full pipeline exercised: build asset → compile → register → tick → read output variable
- `UtilityDecisionCatalog.RegisterAll()` + `StandardInputs.RegisterAll()` called before tick
- All required components registered (Health, WeaponState, TargetMemory, UtilityResultBuffer, etc.)
- Assertion: `(byte)Posture.Hold` = `5`, which is the correct result for an entity with no
  live targets (Hold's `Constant(0.2f)` floor is the only positive-scoring path)
- This is NOT a trivial "returns 0 when not found" test — `Posture.Hold = 5 ≠ 0`

**One limitation:** The test does not cross-check the Blueprint result against an independent
`w.Scorer.SelectPosture(...)` call. However, the deterministic fixture (no live targets →
Hold wins) makes this acceptable for a SC test. The independent cross-check would be
a nice-to-have in a follow-up.

**Verdict:** GOOD. Genuinely exercises the full compile→register→tick→read pipeline. ✅

### SC-P1-09-4: `ReadRankedResultNode_Reads_TopBufferEntry`

**Pass/Fail:** PASS ✅

**Quality assessment:**
- Pre-seeds `UtilityResultBuffer` with `CandidateHandle=42L, Score=0.8f`
- Compiles `ReadRankedResultNode(Rank=0)` with three output pins (Entity, Score, IsValid)
- All three variables asserted: `TopEntity==42L`, `TopScore==0.8f`, `TopIsValid==true`
- Clean, focused test with no ambiguity in the assertions
- Direct data-path test (no scorer needed) — exactly the right scope for SC-P1-09-4

**Verdict:** EXCELLENT. Three-field assertion verifies the full result-struct pipeline. ✅

---

## Defect Found by Dev Lead

### D-09: `IsAssignedTarget_ReturnsZero_WhenNoSubordinate` expected wrong value

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StandardInputReaderTests.cs`

**Root cause:** BATCH-06 corrected `IsAssignedTarget` to return `1f` (neutral pass) when
the entity has no `UnitSubordinate` component. The test `IsAssignedTarget_ReturnsZero_WhenNoSubordinate`
was written under the old (incorrect) semantics and was never updated in BATCH-06.
The BATCH-06 and BATCH-07 sub-agents used narrow test filters that excluded
`StandardInputReaderTests`, so the failure was not caught until the dev-lead ran the full
`Fdp.Toolkit.Tests` namespace sweep.

**Fix applied:** Test renamed to `IsAssignedTarget_ReturnsOne_WhenNoSubordinate`, assertion
changed from `Assert.Equal(0f, ...)` to `Assert.Equal(1f, ...)`. A comment explains the
neutral-pass semantics.

**Post-fix result:** All 124 utility AI tests pass.

---

## Overall Statistics

| Suite | Before Fix | After Fix |
|-------|-----------|-----------|
| Fdp.Toolkit.Tests (utility AI) | 123/124 | 124/124 |
| Hrot.Blueprints.Tests (new) | 2/2 | 2/2 |
| Pre-existing unrelated failures | 61 | 61 (unchanged) |

---

## Verdict

**APPROVED WITH DEV-LEAD FIXES**

One defect found and fixed (D-09 — stale test expectation). The new production code
(Stage5, StatementEmitter, InstanceEmitter) is clean and pattern-consistent. Both runtime
tests meaningfully exercise the full compile→register→tick→assert pipeline.

P1-09 Steps 1-C-4 through 1-C-7 are complete. SC-P1-09-3 and SC-P1-09-4 pass.
