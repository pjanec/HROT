# BATCH-02: Utility AI — Scoring Core Data Structures + Curve Evaluation + Aggregator

**Batch Number:** BATCH-02  
**Tasks:** Corrective-0 (SC-P0-01-2 test fix), TASK-UAI-P1-01, TASK-UAI-P1-02, TASK-UAI-P1-03  
**Phase:** Phase 1 — Runtime core  
**Estimated Effort:** 12–15 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (Phase-0 prerequisites complete)

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Task Detail:** `.dev/utility-ai/TASK-DETAIL.md` — Phase 1, tasks P1-01, P1-02, P1-03 (§ "Phase 1 — Runtime core + trace buffer")
2. **Architecture:** `.dev/utility-ai/Utility_AI_Design_v1_1.md` — §4 (scoring core), §5 (response curves), §4.3–4.4 (aggregation), §8.2 ([InlineArray] trap)
3. **Previous Review:** `.dev/utility-ai/reviews/BATCH-01-REVIEW.md` — learn from Corrective-0 context
4. **Existing precedent — EQS curves:** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs` and `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsScoringCurve.cs` (the five reused curve kinds exist here as `EqsScoringCurveKind`; study shape)
5. **Existing precedent — combat translator tests:** `FDP/Toolkits/Fdp.Toolkits.Tests/Combat/WeaponMountTests.cs` (for Corrective-0 fix context)

### Source Code Locations

- **New Utility layer root:** `FDP/Toolkits/Fdp.Toolkits/Utility/` (create this folder)
- **Core data structures:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCore.cs` (all structs/enums in one file, or split — your choice, be consistent)
- **Aggregator:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/Aggregator.cs` (NEW)
- **PiecewiseCurveCatalog:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/PiecewiseCurveCatalog.cs` (NEW)
- **Existing Combat tests (corrective fix):** `FDP/Toolkits/Fdp.Toolkits.Tests/Combat/WeaponMountTests.cs`

### Test Projects

- **Corrective fix:** `FDP/Toolkits/Fdp.Toolkits.Tests/Combat/WeaponMountTests.cs` — update existing test to assert MaxAmmo
- **P1-01 tests:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityCoreTests.cs` — NEW
- **P1-02 tests:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/CurveEvaluationTests.cs` — NEW
- **P1-03 tests:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/AggregatorTests.cs` — NEW

### Build and Test Commands

```bat
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj
```

Run the full test suite. All P0 tests (45) plus all new BATCH-02 tests must pass.

### Report Submission

When done, submit your report to: `.dev/utility-ai/reports/BATCH-02-REPORT.md`  
If you have questions: `.dev/utility-ai/questions/BATCH-02-QUESTIONS.md`

---

## Context

BATCH-01 delivered all 7 Phase-0 prerequisites. BATCH-02 builds the foundational layer of the scoring runtime: the canonical data structures (P1-01), the response curve evaluator (P1-02), and the aggregation function (P1-03). These three are the bottom of the scoring stack — all higher layers (UtilityScorer, input readers, integration nodes) depend on them.

**Related Tasks:**
- [Corrective-0](../reviews/BATCH-01-REVIEW.md) — SC-P0-01-2 test gap from BATCH-01
- [TASK-UAI-P1-01](../TASK-DETAIL.md#task-uai-p1-01-scoring-core-data-structures) — Scoring core data structures
- [TASK-UAI-P1-02](../TASK-DETAIL.md#task-uai-p1-02-curve-evaluation-curveevaluate) — Curve evaluation
- [TASK-UAI-P1-03](../TASK-DETAIL.md#task-uai-p1-03-aggregator-product-with-compensation--sum) — Aggregator

---

## 🔄 MANDATORY WORKFLOW

Complete tasks in order. After each task: implement → write tests → ALL tests pass.
**DO NOT** move to the next task until current task's tests are green.
**DO NOT** stop and ask permission for obvious steps. Work autonomously until done, then write the report.

1. **Corrective-0:** Fix test → ALL tests pass ✅
2. **P1-01:** Implement data structures → Write tests → ALL tests pass ✅
3. **P1-02:** Implement curve evaluation → Write tests → ALL tests pass ✅
4. **P1-03:** Implement aggregator → Write tests → ALL tests pass ✅

---

## ✅ Tasks

### Corrective Task 0: SC-P0-01-2 translator-path test (fix from BATCH-01 review)

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Combat/WeaponMountTests.cs` — UPDATE

**Problem:** `WeaponStateTests.WeaponState_MaxAmmo_EqualsInitialAmmunition` creates the struct directly (not via `CombatTkbTranslator`). SC-P0-01-2 requires verifying the translator actually sets `MaxAmmo = InitialAmmunition`.

**Fix:** In `WeaponMountTests.ThreeMountDefinition_SpawnsThreeWeaponStateComponents` (or a new dedicated test), add assertions that `ws.MaxAmmo` equals the respective `InitialAmmunition` value that was passed to the translator. Minimum: assert both the owner's `MaxAmmo == 30` and the first child's `MaxAmmo == 20` (matching the three-mount fixture already in that test).

Alternatively, add a new test `WeaponState_MaxAmmo_SetByTranslator_MatchesInitialAmmunition` in `WeaponMountTests.cs` that explicitly uses `CombatTkbTranslator.Inject`, reads the resulting `WeaponState`, and asserts `ws.MaxAmmo == initialAmmunition`.

**Acceptance:** A test exists that would **fail** if you removed `MaxAmmo = primary.InitialAmmunition` from `CombatTkbTranslator`.

---

### Task 1: Scoring core data structures (TASK-UAI-P1-01)

**Files:**
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCore.cs` — NEW (or split into multiple files)
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityCoreTests.cs` — NEW

**Task Detail:** See `.dev/utility-ai/TASK-DETAIL.md#task-uai-p1-01-scoring-core-data-structures`.  
**Design:** `Utility_AI_Design_v1_1.md` §4.2 (canonical structs), §8.1 (cap invariant).

**Namespace:** `Fdp.Toolkit.Utility` (matching the existing `Fdp.Toolkit.Combat` pattern).

**Enums to define:**

```csharp
public enum CurveKind : byte
{
    Linear, InverseLinear, Threshold, Bell, Step,
    Logistic, Quadratic, InverseQuadratic, PiecewiseLinear
}

public enum ScoringMode : byte
{
    WeightedProduct,  // default; product-with-compensation (§4.3)
    WeightedSum       // escape hatch (§4.4)
}

public enum InputContext : byte
{
    Self, Target, Leader, Candidate
}

public enum DecisionKind : byte
{
    ThreatRanking, WeaponSelection, PostureSelect
}
```

**Structs to define:**

```csharp
// 16 bytes; CurveId used only for PiecewiseLinear (side-table key)
[StructLayout(LayoutKind.Sequential)]
public readonly struct ResponseCurve
{
    public readonly CurveKind Kind;
    public readonly byte      Padding0;
    public readonly short     CurveId;   // PiecewiseLinear: key into PiecewiseCurveCatalog; 0 for others
    public readonly float     Slope;     // m
    public readonly float     Exponent;  // k
    public readonly float     XShift;    // b
    public readonly float     YShift;    // c
}

// InputParams: 16-byte discriminated union (pad to 16 bytes)
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct InputParams
{
    [FieldOffset(0)] public uint   BlueprintId;   // for EQS sensor readers
    [FieldOffset(0)] public float  MaxRange;       // for DistanceToContext
    [FieldOffset(0)] public int    MountIndex;     // for per-mount weapon readers
    // extend as needed; 16 bytes reserved for future use
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct UtilityConsideration
{
    public readonly ushort       InputId;    // FNV-1a-16 of [UtilityInput] reader name
    public readonly InputContext Context;
    public readonly byte         Padding0;
    public readonly float        Weight;
    public readonly ResponseCurve Curve;
    public readonly InputParams  Params;
}

// UtilityOption: uses managed array — not unmanaged; that is intentional (authored definition, not per-tick hot data)
public sealed class UtilityOption
{
    public ushort OptionId;
    public ScoringMode Mode;
    public UtilityConsideration[] Considerations = Array.Empty<UtilityConsideration>();
}

// UtilityDecisionDef: authored definition; not a component
public sealed class UtilityDecisionDef
{
    public int           BlueprintId;   // FNV-1a of AssetId GUID
    public ulong         StructureHash;
    public ulong         ParamHash;
    public string        DebugName = string.Empty;
    public DecisionKind  Kind;
    public UtilityOption[] Options = Array.Empty<UtilityOption>();
}

public static class UtilityConstants
{
    /// <summary>
    /// Maximum number of ranked candidate results. Equal to <c>PerceptionConstants.MaxTrackedTargets</c>
    /// (both 16 after P0.03). The cap-invariant assertion lives in <c>UtilityScorer</c> (Phase 1).
    /// </summary>
    public const int TopN = 16;
}
```

**Cap-invariant assertion** — add as a static Debug.Assert in `UtilityConstants`:

```csharp
static UtilityConstants()
{
    System.Diagnostics.Debug.Assert(
        Fdp.Toolkit.Perception.PerceptionConstants.MaxTrackedTargets <= TopN,
        $"Perception tracks {Fdp.Toolkit.Perception.PerceptionConstants.MaxTrackedTargets} contacts " +
        $"but Utility ranks only {TopN}. Raise TopN or accept truncation.");
}
```

**Tests (UtilityCoreTests.cs):**
- `sizeof(ResponseCurve) == 16` (compile-time invariant)
- `sizeof(UtilityConsideration)` is deterministic and >= 24 (document the exact value)
- `UtilityConstants.TopN == 16`
- `PerceptionConstants.MaxTrackedTargets <= UtilityConstants.TopN` (cap invariant holds)
- All enum values are accessible and have distinct byte values (basic smoke test)

---

### Task 2: Curve evaluation (TASK-UAI-P1-02)

**Files:**
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/ResponseCurve.cs` — ADD `Evaluate` method (or extend the file where `ResponseCurve` was defined)
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/PiecewiseCurveCatalog.cs` — NEW static catalog
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/CurveEvaluationTests.cs` — NEW

**Task Detail:** See `.dev/utility-ai/TASK-DETAIL.md#task-uai-p1-02-curve-evaluation-curveevaluate`.  
**Design:** `Utility_AI_Design_v1_1.md` §5 (response curves), §5.3 (curve struct), §5.4 (weight-as-exponent).

**Add `Evaluate(float input) -> float` to `ResponseCurve`:**

The parameterization follows the standard family `output = m * (x - b)^k + c`:
- `Slope` = m, `Exponent` = k, `XShift` = b, `YShift` = c
- All outputs clamped to [0, 1]. All inputs assumed in [0, 1] (no guard needed; clamp output).

```csharp
// All formulas clamp result to [0, 1].
public float Evaluate(float x)
{
    float result;
    switch (Kind)
    {
        case CurveKind.Linear:
            result = Slope * (x - XShift) + YShift;
            break;

        case CurveKind.InverseLinear:
            result = 1f - (Slope * (x - XShift) + YShift);
            break;

        case CurveKind.Threshold:
            // 0 below XShift, 1 at or above
            result = x >= XShift ? 1f : 0f;
            break;

        case CurveKind.Bell:
            // Gaussian bell: output = Slope * exp(-k * (x - b)^2) + c
            float bell_dx = x - XShift;
            result = Slope * MathF.Exp(-Exponent * bell_dx * bell_dx) + YShift;
            break;

        case CurveKind.Step:
            // Like Threshold but uses Slope as output above threshold (default 1)
            result = x >= XShift ? (Slope > 0f ? Slope : 1f) : 0f;
            break;

        case CurveKind.Logistic:
            // Sigmoid: output = 1 / (1 + exp(-k * (x - b))) * m + c
            result = 1f / (1f + MathF.Exp(-Exponent * (x - XShift))) * Slope + YShift;
            break;

        case CurveKind.Quadratic:
            // output = m * (x - b)^2 + c
            float q_dx = x - XShift;
            result = Slope * (q_dx * q_dx) + YShift;
            break;

        case CurveKind.InverseQuadratic:
            // output = 1 - m * (x - b)^2 + c
            float iq_dx = x - XShift;
            result = 1f - Slope * (iq_dx * iq_dx) + YShift;
            break;

        case CurveKind.PiecewiseLinear:
            result = PiecewiseCurveCatalog.Evaluate(CurveId, x);
            break;

        default:
            result = x;  // passthrough fallback
            break;
    }
    return Math.Clamp(result, 0f, 1f);
}
```

**PiecewiseCurveCatalog:**

```csharp
/// <summary>
/// Thread-safe static side-table for PiecewiseLinear control points.
/// Keyed by <c>ResponseCurve.CurveId</c>. Points must be sorted by X ascending.
/// </summary>
public static class PiecewiseCurveCatalog
{
    private static readonly System.Collections.Generic.Dictionary<short, (float x, float y)[]> _table = new();

    /// <summary>Register control points for a PiecewiseLinear curve.</summary>
    /// <param name="curveId">Key matching <see cref="ResponseCurve.CurveId"/>.</param>
    /// <param name="points">Control points sorted by X ascending. Must have >= 2 points.</param>
    public static void Register(short curveId, (float x, float y)[] points)
    {
        if (points == null || points.Length < 2) throw new ArgumentException("PiecewiseLinear needs >= 2 points.", nameof(points));
        lock (_table) _table[curveId] = points;
    }

    /// <summary>Evaluate PiecewiseLinear at <paramref name="x"/>. Returns 0 if curveId is unregistered.</summary>
    public static float Evaluate(short curveId, float x)
    {
        lock (_table)
        {
            if (!_table.TryGetValue(curveId, out var pts)) return 0f;
            if (x <= pts[0].x) return pts[0].y;
            if (x >= pts[^1].x) return pts[^1].y;
            // Binary search for the segment
            int lo = 0, hi = pts.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (pts[mid].x <= x) lo = mid; else hi = mid;
            }
            float t = (x - pts[lo].x) / (pts[hi].x - pts[lo].x);
            return pts[lo].y + t * (pts[hi].y - pts[lo].y);
        }
    }
}
```

**Tests (CurveEvaluationTests.cs):**

For SC-P1-02-1, pin reference values at {0, 0.25, 0.5, 0.75, 1.0} for each kind. Choose default parameters (Slope=1, Exponent=1, XShift=0, YShift=0) as the baseline. Document the expected values in the test comments.

**Required test coverage:**

- **Linear (baseline):** Slope=1,E=1,b=0,c=0 → evaluate(0)=0, (0.5)=0.5, (1)=1
- **InverseLinear:** Slope=1 → evaluate(0)=1, (0.5)=0.5, (1)=0
- **Threshold (b=0.5):** evaluate(0.49)=0, evaluate(0.5)=1, evaluate(1.0)=1
- **Bell (b=0.5, k=10):** evaluate(0.5) is near 1.0, evaluate(0.0) is near 0 (Gaussian peak)
- **Step (b=0.5):** identical behavior to Threshold with default Slope=1
- **Logistic (b=0.5, k=10):** evaluate(0.5)≈0.5 (inflection point), evaluate(0.9) > 0.98, evaluate(0.1) < 0.02
- **Quadratic (Slope=1, b=0):** evaluate(0.5)=0.25, evaluate(1.0)=1.0
- **InverseQuadratic (Slope=1, b=0):** evaluate(0)=1, evaluate(0.5)=0.75, evaluate(1)=0
- **PiecewiseLinear:** Register a 3-point curve: (0,0), (0.5,0.8), (1,1). Evaluate at exact control points (exact match), between points (lerp), below first (clamp), above last (clamp).

**Property test (SC-P1-02-2):** For each curve kind (non-PiecewiseLinear), verify output ∈ [0,1] for 100 input values spread across [0,1] (linspace, not random — deterministic test is fine).

**SC-P1-02-3:** Threshold/Step: `evaluate(0.499) == 0` AND `evaluate(0.5) >= 0.95` for threshold at 0.5.

**SC-P1-02-4:** PiecewiseLinear monotonic: for the 3-point curve above, verify evaluate(0.0) <= evaluate(0.25) <= evaluate(0.5) <= evaluate(0.75) <= evaluate(1.0).

---

### Task 3: Aggregator (TASK-UAI-P1-03)

**File:**
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/Aggregator.cs` — NEW
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/AggregatorTests.cs` — NEW

**Task Detail:** See `.dev/utility-ai/TASK-DETAIL.md#task-uai-p1-03-aggregator-product-with-compensation--sum`.  
**Design:** `Utility_AI_Design_v1_1.md` §4.3 (product-with-compensation), §4.4 (weighted sum), §5.4 (weight-as-exponent).

**Implementation:**

```csharp
/// <summary>
/// Aggregates consideration outputs into an option score.
/// </summary>
public static class Aggregator
{
    /// <summary>
    /// Aggregate curve outputs and weights into a single score using the specified mode.
    /// </summary>
    /// <param name="curveOutputs">Normalized curve output values in [0,1]. Length == n.</param>
    /// <param name="weights">Consideration weights in [0,1]. Parallel to curveOutputs.</param>
    /// <param name="mode">Scoring mode (WeightedProduct or WeightedSum).</param>
    /// <returns>Final score in [0,1].</returns>
    public static float Aggregate(ReadOnlySpan<float> curveOutputs, ReadOnlySpan<float> weights, ScoringMode mode)
    {
        if (curveOutputs.IsEmpty) return 0f;

        if (mode == ScoringMode.WeightedSum)
            return AggregateSum(curveOutputs, weights);

        return AggregateProduct(curveOutputs, weights);
    }

    private static float AggregateProduct(ReadOnlySpan<float> curveOutputs, ReadOnlySpan<float> weights)
    {
        int n = curveOutputs.Length;
        float rawProduct = 1f;
        for (int i = 0; i < n; i++)
        {
            // Each consideration contributes curve^weight (weight as exponent, §5.4)
            float w = weights.Length > i ? weights[i] : 1f;
            rawProduct *= MathF.Pow(curveOutputs[i], w);
        }

        // Dave Mark's compensation factor (§4.3):
        // modificationFactor = 1 - (1 / n)
        // makeUpValue = (1 - rawProduct) * modificationFactor
        // finalScore = rawProduct + makeUpValue * rawProduct
        float modificationFactor = 1f - (1f / n);
        float makeUpValue = (1f - rawProduct) * modificationFactor;
        return rawProduct + makeUpValue * rawProduct;
    }

    private static float AggregateSum(ReadOnlySpan<float> curveOutputs, ReadOnlySpan<float> weights)
    {
        float numerator = 0f, denominator = 0f;
        for (int i = 0; i < curveOutputs.Length; i++)
        {
            float w = weights.Length > i ? weights[i] : 1f;
            numerator   += w * curveOutputs[i];
            denominator += w;
        }
        return denominator > 0f ? numerator / denominator : 0f;
    }
}
```

**Tests (AggregatorTests.cs):**

All tests must verify ACTUAL numeric values — not just "no exception" or "result > 0".

- **SC-P1-03-1 (product):** Single consideration, curve=0.5, weight=1.0 → score == 0.5
  - n=1 → modFactor=0, makeUp=0, finalScore=rawProduct=0.5 ✓
- **SC-P1-03-1 (sum):** Single consideration, curve=0.5, weight=1.0 → score == 0.5
- **SC-P1-03-2 (product):** Two considerations (0.5,w=1), (0.5,w=1):
  - rawProduct = 0.5^1 * 0.5^1 = 0.25
  - modFactor = 1 - 1/2 = 0.5
  - makeUp = (1 - 0.25) * 0.5 = 0.375
  - finalScore = 0.25 + 0.375 * 0.25 = 0.34375
  - Assert.Equal(0.34375f, result, precision: 5)
- **SC-P1-03-3 (sum):** Three considerations (0.6,w=1), (0.4,w=2), (0.0,w=1):
  - numerator = 1.0*0.6 + 2.0*0.4 + 1.0*0.0 = 0.6 + 0.8 + 0.0 = 1.4
  - denominator = 1 + 2 + 1 = 4
  - result = 1.4 / 4 = 0.35
  - Assert.Equal(0.35f, result, precision: 5)
- **SC-P1-03-4 (hard-gate):** Two product-mode considerations where one curve output is 0:
  - curveOutputs = [0.8f, 0f], weights = [1f, 1f] → finalScore == 0f
  - The zero term drives rawProduct to 0; after compensation finalScore remains 0
- **Additional:** empty span → 0f; single high-weight consideration curve=0.9, weight=2: rawProduct = 0.9^2 = 0.81, verify > 0.8
- **Additional (sum):** verify WeightedSum handles all-zero-weight denominator gracefully (returns 0 not NaN)

---

## 🧪 Testing Requirements

**Minimum new tests:** 35 across all tasks (including corrective fix).

**Distribution:**
- Corrective-0: ≥ 1 test (verifies translator sets MaxAmmo via translator path)
- P1-01: ≥ 5 tests (struct sizes, constants, cap invariant)
- P1-02: ≥ 18 tests (≥ 2 per curve kind + property test + piecewise tests)
- P1-03: ≥ 8 tests (all SC conditions + edge cases)

**Test quality:**
- All numeric assertions must use specific values (not just `> 0` or `!= null`)
- Curve evaluation tests must pin exact float values at the reference inputs (0, 0.25, 0.5, 0.75, 1.0)
- Aggregator tests must verify the exact compensation arithmetic (SC-P1-03-2 value 0.34375f)
- Property test for curves: iterate over at least 100 evenly-spaced inputs; assert output clamped

---

## ⚠️ Common Pitfalls

1. **`UtilityOption` and `UtilityDecisionDef` are not unmanaged** — they carry managed arrays (`UtilityConsideration[]`). Do NOT try to use `sizeof()` on them or put them in `[InlineArray]`. Use `class` (or `sealed class`) for these authored definitions.

2. **`ResponseCurve` IS unmanaged** — all fields are value types. `sizeof(ResponseCurve)` must be exactly 16 bytes. Use `[StructLayout(LayoutKind.Sequential)]` and verify the size in a test.

3. **`UtilityConsideration` IS unmanaged** — contains only value types (including `ResponseCurve` and `InputParams`). Verify size in a test.

4. **Bell curve with wrong params gives 0 output everywhere** — pin the Exponent (k) value in the test to something > 1 (e.g. k=10) and compute the expected output at the peak (x=b).

5. **Logistic sigmoid direction** — the formula `1 / (1 + exp(-k*(x-b))) * m + c` with k>0 produces LOW output at low x and HIGH output at high x. With k<0 it reverses. Make sure the test uses k>0 for the standard S-curve shape.

6. **PiecewiseCurveCatalog thread-safety** — the catalog uses a `lock`. This is acceptable for authored definitions (set up at startup, not hot-path). Do NOT call it per-frame per-entity without caching.

7. **Aggregator with n=1** — modificationFactor = 1 - 1/1 = 0, so makeUpValue = 0, finalScore = rawProduct = curve^weight. Verify the single-consideration case explicitly in tests.

8. **Weight-as-exponent** — in `WeightedProduct` mode, weight is the EXPONENT, not a multiplier. `curve^weight`. A weight of 0 makes the term 1.0 (no effect). A weight of 1 gives curve^1 = curve. Do NOT accidentally multiply weights in product mode.

9. **`InputParams` layout** — `[StructLayout(LayoutKind.Explicit, Size=16)]` with `[FieldOffset(0)]` for each union field. Verify `sizeof(InputParams) == 16`.

---

## 📊 Report Requirements

Submit to `.dev/utility-ai/reports/BATCH-02-REPORT.md`. Answer:

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** What exact size did you get for `sizeof(UtilityConsideration)`? Show the field layout (kind, padding, fields, sizes).

**Q3:** What design decisions did you make beyond the spec? (e.g. exact PiecewiseCurveCatalog API, how you structured the files)

**Q4:** What edge cases did you discover that weren't mentioned in the instructions?

**Q5:** Are there any concerns about the PiecewiseCurveCatalog's thread-safety or performance for a hot-path scenario?

**Q6:** Suggested git commit message for this batch.

---

## 🎯 Success Criteria

This batch is DONE when:

- [ ] **Corrective-0**: Test verifies `CombatTkbTranslator` sets `WeaponState.MaxAmmo = InitialAmmunition` via translator path; test would fail if the line were removed
- [ ] **P1-01**: All data structures defined; `sizeof(ResponseCurve) == 16`; `UtilityConstants.TopN == 16`; cap invariant assertion present; all P1-01 tests pass
- [ ] **P1-02**: `ResponseCurve.Evaluate` implemented for all 9 curve kinds; `PiecewiseCurveCatalog` implemented; all property tests pass; all P1-02 tests pass
- [ ] **P1-03**: `Aggregator.Aggregate` implemented for both modes; exact compensation arithmetic verified in tests; all P1-03 tests pass
- [ ] `dotnet build IOS-IG-SimHost.sln` — zero errors, zero new warnings
- [ ] `dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj` — all previously passing tests still pass; new tests all pass

---

## 📚 Reference Materials

- **Task Detail:** `.dev/utility-ai/TASK-DETAIL.md` — P1-01, P1-02, P1-03
- **Architecture §4–5:** `.dev/utility-ai/Utility_AI_Design_v1_1.md` — scoring core, response curves
- **Existing EQS curve precedent:** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsScoringCurve.cs`
- **Existing EQS inline array:** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs`
- **BATCH-01 review (Corrective-0 context):** `.dev/utility-ai/reviews/BATCH-01-REVIEW.md`
- **WeaponMountTests (corrective fix target):** `FDP/Toolkits/Fdp.Toolkits.Tests/Combat/WeaponMountTests.cs`
