# BATCH-02 Report: Utility AI — Scoring Core + Curve Evaluation + Aggregator

**Date:** 2026-05-29  
**Tasks:** Corrective-0 (SC-P0-01-2), TASK-UAI-P1-01, TASK-UAI-P1-02, TASK-UAI-P1-03  
**Status:** COMPLETE — all new tests pass, pre-existing failures unchanged

---

## Q1: Issues Encountered and Resolutions

### Issue 1: `ResponseCurve` as `partial struct` for method split

The batch instructions specified both a 16-byte struct layout (UtilityCore.cs) and an `Evaluate` method (separate file). Since `readonly struct` cannot simply have methods added via extension, and the struct fields + constructor live in `UtilityCore.cs`, the struct was declared `partial` so that `ResponseCurveEvaluate.cs` could add the `Evaluate` method in a second partial half. This is clean C# and compiles without issue.

### Issue 2: YShift vs. 16-byte target

The instructions initially showed conflicting layouts:
- Full parameterisation (m, k, b, c) = 4 floats = 16 bytes of float payload + 4 bytes header = 20 bytes
- Hard requirement: `sizeof(ResponseCurve) == 16`

**Resolution:** YShift (c) was excluded. Layout: `Kind(1) + Padding0(1) + CurveId(2) + Slope(4) + Exponent(4) + XShift(4) = 16 bytes`. All Phase-1 formulas use `c=0` implicitly. This matches the batch instructions' explicit resolution in the pitfalls section.

### Issue 3: `PiecewiseCurveCatalog` thread-safety in tests

Since `PiecewiseCurveCatalog` is a static singleton and tests register curve IDs, multiple test classes running in parallel could collide. `CurveEvaluationTests` uses `IDisposable` to call `ClearAll()` (an `internal` method) on teardown and registers at construction. This keeps tests isolated.

### Issue 4: Corrective-0 test verification

The existing WeaponMountTests fixture already had a 3-mount scenario; a new focused 2-mount test (`ThreeMountDefinition_WeaponStateMaxAmmo_SetByTranslator`) was added that would fail if `MaxAmmo = primary.InitialAmmunition` were removed from `CombatTkbTranslator.Inject`.

---

## Q2: `sizeof(UtilityConsideration)` — Field Layout

```
Field        Type              Size   Cumulative
-----------  ----------------  -----  ----------
InputId      ushort            2      2
Context      InputContext(byte)1      3
Padding0     byte              1      4
Weight       float             4      8
Curve        ResponseCurve     16     24
Params       InputParams       16     40
```

**`sizeof(UtilityConsideration) == 40 bytes`**

This is verified by the test `UtilityConsideration_SizeIsDeterministic`.

---

## Q3: Design Decisions

1. **File organisation:** Split into four files under `FDP/Toolkits/Fdp.Toolkits/Utility/Core/`:
   - `UtilityCore.cs` — all enums, structs, classes, `UtilityConstants`
   - `ResponseCurveEvaluate.cs` — `partial struct ResponseCurve` with `Evaluate(float)`
   - `PiecewiseCurveCatalog.cs` — static catalog with `Register`/`Evaluate`/`ClearAll`
   - `Aggregator.cs` — `static class Aggregator`

2. **`PiecewiseCurveCatalog.ClearAll()`** is `internal` (not public) to prevent accidental use in production code; it exists solely for test isolation.

3. **`ResponseCurve` constructor** accepts `slope`, `exponent`, `xShift`, `curveId` positionally so tests can construct curves without named-argument verbosity.

4. **Aggregator product formula:** Weight is used as the exponent (`curve^weight`), matching §5.4. For the special case `n=1`, `modificationFactor = 0` so `finalScore = rawProduct` exactly — this was verified explicitly in tests.

5. **Default curve fallback:** In `Evaluate`, an unknown `CurveKind` returns `x` (passthrough), clamped to [0,1]. This avoids silent 0-returns for future extension.

---

## Q4: Edge Cases Discovered

1. **Bell curve with `Exponent=0`**: `exp(0) = 1` everywhere, so with `Slope=1` the output is always 1.0 regardless of x. This is valid but unintuitive. The test uses `Exponent=10` to ensure a clear peak shape.

2. **Step curve with `Slope=0`**: The formula `x >= XShift ? (Slope > 0f ? Slope : 1f) : 0f` maps `Slope=0` to output 1.0 above threshold. This matches the "default to 1" intent.

3. **Logistic clamping:** With very high `Exponent`, `Evaluate` at x=0 or x=1 may produce extremely small/large intermediate values before clamping. `Math.Clamp` handles the output, but the intermediate `exp` computation could theoretically overflow for `Exponent > 80`. Acceptable for Phase 1 (design restricts k to sane values).

4. **Aggregator with `n=1` and `weight=0`:** `0.5^0 = 1.0`, so a zero-weight consideration acts as if it contributed no information (identity). Not a bug — this is the intended behaviour of weight-as-exponent.

5. **PiecewiseCurve with x exactly at a control point:** Binary search uses `pts[mid].x <= x`, so exact control-point hits correctly resolve without lerp.

---

## Q5: PiecewiseCurveCatalog Performance Concerns

The current implementation uses `lock(_table)` for both `Register` and `Evaluate`. For Phase 1 (authored definitions loaded at startup, not called per-frame), this is fine.

**For hot-path use in Phase 2+:**
- `lock` on every `Evaluate` call will contend under multi-threaded ECS systems.
- Recommended upgrade: switch to `ImmutableDictionary<short, (float,float)[]>` or `ConcurrentDictionary` with a `Lazy<>` value; or pre-bake the lookup into a `ReadOnlySpan` that requires no locking after startup.
- `ClearAll()` is already `internal` to prevent accidental hot-path misuse.

---

## Q6: Suggested Git Commit Message

```
feat(utility-ai): Scoring core + curve evaluation + aggregator (BATCH-02)

Completes Corrective-0 (SC-P0-01-2), TASK-UAI-P1-01, P1-02, P1-03.

- SC-P0-01-2: WeaponMountTests.ThreeMountDefinition_WeaponStateMaxAmmo_SetByTranslator
  verifies CombatTkbTranslator sets MaxAmmo via translator path
- P1-01: UtilityCore.cs — CurveKind, ScoringMode, InputContext, DecisionKind enums;
  ResponseCurve (16B), InputParams (16B), UtilityConsideration (40B) structs;
  UtilityOption, UtilityDecisionDef classes; UtilityConstants.TopN=16 with cap assert
- P1-02: ResponseCurve.Evaluate for all 9 curve kinds (Linear, InverseLinear,
  Threshold, Bell, Step, Logistic, Quadratic, InverseQuadratic, PiecewiseLinear);
  PiecewiseCurveCatalog static side-table with binary-search lerp
- P1-03: Aggregator.Aggregate — WeightedProduct with Dave Mark compensation (§4.3)
  and WeightedSum normalised mode (§4.4)

Tests: 42 new tests added (1 corrective, 7 P1-01, 27 P1-02, 8 P1-03).
Total: 1616 tests; 63 pre-existing failures (unchanged BATCH-02 tests).
sizeof(ResponseCurve)==16, sizeof(UtilityConsideration)==40.
```

---

## Test Results Summary

| Suite | New Tests | Result |
|-------|-----------|--------|
| WeaponMountTests (corrective SC-P0-01-2) | 1 | PASS |
| UtilityCoreTests (P1-01) | 7 | PASS |
| CurveEvaluationTests (P1-02) | 26 | PASS |
| AggregatorTests (P1-03) | 8 | PASS |
| **Total new** | **42** | **ALL PASS** |

**Full suite:** 1616 total — 1553 passed, 63 failed (all 63 pre-existing, none from BATCH-02).
