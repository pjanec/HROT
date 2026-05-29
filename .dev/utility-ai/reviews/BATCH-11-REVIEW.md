# BATCH-11 Review — CurveWidget Standalone Widget

**Reviewer:** Dev Lead  
**Date:** 2025-07-27  
**Verdict:** CHANGES REQUIRED

---

## Summary

Build is green, all 35 tests pass. The overall structure and approach are sound: `CurveWidget`
correctly delegates to `ResponseCurve.Evaluate` for non-piecewise kinds, `UtilityCurve` round-trips
cleanly through `PiecewiseCurveCatalog`, and the test project is properly isolated from ImGui.

However, two substantive issues were found that require correction before this batch is approved.

---

## Issues

### P1 — `IsParamEditable` for Linear/InverseLinear has `b` and `c` inverted

**Severity:** P1 (wrong logic, all callers affected)

**Root cause:** The BATCH-11 instructions contained a typo: they wrote "c from left endpoint
(locked)" when Editor DD §5.2 says "`b` from left endpoint" in the Locked params column.

**What the table actually says (§5.2):**

| Column | Linear / InverseLinear |
|---|---|
| Handles | endpoint handles → `m`, `c` |
| Locked params | `k=1`, **`b` from left endpoint** |

`b` is in the Locked column because the left endpoint HANDLE controls b; the numeric field for b is
shown disabled (E-2). `c` is in the Handles column — it has an endpoint handle and the numeric
field must be editable.

**Current (wrong):**
```csharp
"b" => true,  // always editable — wrong for Linear/InverseLinear
"c" => kind is CurveKind.Threshold or CurveKind.Bell or CurveKind.Step
            or CurveKind.PiecewiseLinear,  // c=false for Linear — wrong
```

**Correct:**
```csharp
"b" => kind is not (CurveKind.Linear or CurveKind.InverseLinear),
"c" => kind is CurveKind.Linear or CurveKind.InverseLinear
            or CurveKind.Threshold or CurveKind.Bell or CurveKind.Step
            or CurveKind.PiecewiseLinear,
```

**Tests that must be updated** (they assert the wrong values and must be flipped):
```csharp
[InlineData(CurveKind.Linear,        "b", true)]   // -> should be false
[InlineData(CurveKind.Linear,        "c", false)]  // -> should be true
[InlineData(CurveKind.InverseLinear, "m", true)]   // keep
// add: [InlineData(CurveKind.InverseLinear, "b", false)]
// add: [InlineData(CurveKind.InverseLinear, "c", true)]
```

---

### P2 — Missing cross-check tests: `CurveWidget.Evaluate` vs `ResponseCurve.Evaluate`

**Severity:** P2 (test coverage gap — regression guard missing)

The batch instructions required at least two Theory tests that explicitly construct a
`ResponseCurve` and compare its `Evaluate(x)` result against `CurveWidget.Evaluate` at 16 sample
points across [0, 1]. These regression guards are the primary protection against any future
divergence between the widget's evaluation path and the runtime curve.

Currently the tests verify individual known-good float values (e.g. Linear identity returns x),
but none of them create an actual `ResponseCurve` and call `rc.Evaluate(x)` on it. If the
runtime math ever changes, the test will still pass because it asserts a hard-coded expected
value rather than delegating to the runtime formula.

**Required new tests:**
```csharp
[Theory]
[InlineData(0f)] [InlineData(0.0625f)] ... [InlineData(1f)]  // 16 values
public void Evaluate_Linear_MatchesResponseCurve(float x)
{
    var rc = new ResponseCurve(CurveKind.Linear, slope: 0.8f, exponent: 1f, xShift: 0.1f);
    var uc = new UtilityCurve { Kind = CurveKind.Linear, M = 0.8f, K = 1f, B = 0.1f, C = 0f };
    float expected = Math.Clamp(rc.Evaluate(x), 0f, 1f);
    Assert.Equal(expected, CurveWidget.Evaluate(in uc, x), precision: 5);
}

[Theory]
[InlineData(0f)] ... [InlineData(1f)]  // 16 values
public void Evaluate_Logistic_MatchesResponseCurve(float x)
{
    var rc = new ResponseCurve(CurveKind.Logistic, slope: 1f, exponent: 6f, xShift: 0.5f);
    var uc = new UtilityCurve { Kind = CurveKind.Logistic, M = 1f, K = 6f, B = 0.5f, C = 0f };
    float expected = Math.Clamp(rc.Evaluate(x), 0f, 1f);
    Assert.Equal(expected, CurveWidget.Evaluate(in uc, x), precision: 5);
}
```

Note: `ResponseCurve.Evaluate` already clamps to [0,1] for most kinds; the `Math.Clamp` call in
the expected value is belt-and-suspenders in case any kind returns slightly outside range before
the C shift.

---

## Minor Notes

### Report location
The sub-agent placed the batch report at `docs/BATCH-11-REPORT.md` instead of the required path
`.dev/utility-ai/reports/BATCH-11-REPORT.md`. The report content is correct; only the location is
wrong. Move the file as part of the corrective batch or before merging.

### Existing tests that are good
- `UtilityCurveTests` (4 tests): `FromResponseCurve` field preservation, `ToResponseCurve`
  round-trip, PiecewiseLinear catalog round-trip — all test meaningful computed values, not just
  type existence.
- `Evaluate` tests for C-shift and clamp boundary cases are correct and sufficient.
- `IsParamEditable` Theory for the 19 non-Linear/InverseLinear cases: all correct per §5.2.
- `AddPiecewisePoint` / `RemovePiecewisePoint` / `ComputeSamples` tests: all correct.

---

## Required Actions for Corrective Batch

1. Fix `IsParamEditable` for Linear and InverseLinear:
   - `b` → `false` (locked; set via left endpoint handle)
   - `c` → `true` (editable; set via endpoint handle)

2. Update the two affected `Theory` `InlineData` entries and add the missing
   `InverseLinear/b=false` and `InverseLinear/c=true` cases.

3. Add `Evaluate_Linear_MatchesResponseCurve` and `Evaluate_Logistic_MatchesResponseCurve`
   Theory tests (16 sample points each, comparing against `ResponseCurve.Evaluate`).

4. Move `docs/BATCH-11-REPORT.md` → `.dev/utility-ai/reports/BATCH-11-REPORT.md`.

The batch may then proceed with Phase 4 work (TASK-UAI-P4-01 through P4-03) once the above
fixes are in place and the full test suite is green.
