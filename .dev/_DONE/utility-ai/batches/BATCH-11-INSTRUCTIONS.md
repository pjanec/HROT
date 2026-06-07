# BATCH-11: Phase 3 — CurveWidget Standalone Widget

**Batch Number:** BATCH-11
**Tasks:** TASK-UAI-P3-01
**Phase:** Phase 3 — Standalone curve widget
**Estimated Effort:** 10-14 hours
**Priority:** HIGH
**Dependencies:** BATCH-10 complete (Phase 2 source generator done)

---

## Onboarding & Workflow

### Developer Instructions

This batch implements the standalone `CurveWidget.Draw` host-agnostic ImGui widget for `UtilityCurve`
curves. This is Phase 3 of the interleaved Utility AI + Tuning Console build order. The widget is
built standalone before either of its two UI consumers — the Utility Editor (Phase 5) and the Tuning
Console Slice 2 (Phase 6) — so it can be shared without duplication.

You MUST complete implementation, write all tests, and ensure everything builds and all tests pass
before submitting your report. Do NOT stop to ask permission to run tests or to proceed with obvious
implementation steps.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Build Order:** `.dev/utility-ai/Build_Order_UtilityAI_Tuning_Overlays_v1_0.md` — Phase 3 (pp. §3 "The curve widget, standalone") and §6 summary table
3. **Curve Editor Guide:** `.dev/utility-ai/Curve_Editor_in_StructEdit_Guide_v1_1.md` — this is the primary design reference for this batch; read §1–§3 (Steps 1–2) and §8 carefully
4. **Architecture §5.3:** `.dev/utility-ai/Utility_AI_Design_v1_1.md` lines 265–300 — the `ResponseCurve` struct fields and curve evaluation
5. **Editor DD §5:** `.dev/utility-ai/Utility_AI_Editor_Design_v1_2.md` lines 200–280 — the curve editor anatomy, handle ↔ param mapping table, piecewise editing rules
6. **Previous Review:** `.dev/utility-ai/reviews/BATCH-10-REVIEW.md`
7. **Task Detail:** `.dev/utility-ai/TASK-DETAIL.md` — section "TASK-UAI-P3-01"

### Source Code Location

- **Existing runtime curves:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCore.cs` (CurveKind, ResponseCurve), `FDP/Toolkits/Fdp.Toolkits/Utility/Core/ResponseCurveEvaluate.cs` (Evaluate method), `FDP/Toolkits/Fdp.Toolkits/Utility/Core/PiecewiseCurveCatalog.cs`
- **Precedent ImGui widgets:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs`, `FDP/Engine/Fdp.Presentation/ImGui/Editing/ComponentEditDrawer.cs`
- **Precedent field drawers:** `FDP/Engine/Fdp.Presentation/ImGui/Editing/GuidFieldDrawer.cs`, `FDP/Engine/Fdp.Presentation/ImGui/Editing/IImGuiFieldDrawer.cs`
- **New code destination:** `Hrot/Editor/Hrot.Utility.Editor/` (NEW project — see Task 1)
- **Test destination:** `Hrot/Editor/Hrot.Utility.Editor.Tests/` (NEW project — see Task 3)

### Build and Test Commands

```bat
dotnet build IOS-IG-SimHost.sln
dotnet test Hrot\Editor\Hrot.Utility.Editor.Tests\Hrot.Utility.Editor.Tests.csproj
```

### Report Submission

**When done, submit your report to:**
`.dev/utility-ai/reports/BATCH-11-REPORT.md`

**If you have questions, create:**
`.dev/utility-ai/questions/BATCH-11-QUESTIONS.md`

---

## Context

Phase 2 (source generator + analyzer) is complete. We now have `UtilityDecisionDef` objects with
`ResponseCurve` instances describing each consideration's curve. Phase 3 builds the visual editing
widget for those curves.

The key architectural insight (Curve Editor guide §1): build the widget ONCE as a standalone
host-agnostic function, then use it in both the Utility Editor card inspector (Phase 5) and as a
StructEdit drawer in the Tuning Console (Phase 6). Building it now before either host prevents the
"build the curve editor twice" trap.

**Related Tasks:**
- [TASK-UAI-P3-01](../TASK-DETAIL.md#task-uai-p3-01-curvewidgetdraw-host-agnostic-widget) — The standalone widget, detailed success conditions

---

## Batch Objectives

1. Add `UtilityCurve` and `PiecewisePoint` as the editor-side curve types to `Fdp.Toolkits`
2. Create the `Hrot.Utility.Editor` project and implement `CurveWidget.Draw` + `CurveWidgetOptions`
3. Create `Hrot.Utility.Editor.Tests` and verify all four success conditions with unit tests
4. Add both new projects to `IOS-IG-SimHost.sln`

---

## MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Task 1:** Add `UtilityCurve`/`PiecewisePoint` to Fdp.Toolkits → build passes ✅
2. **Task 2:** Create `Hrot.Utility.Editor` + `CurveWidget` → build passes ✅
3. **Task 3:** Create `Hrot.Utility.Editor.Tests` + write tests → **ALL tests pass** ✅

DO NOT skip to the next task until the current one builds and tests pass.

---

## Tasks

### Task 1: `UtilityCurve` and `PiecewisePoint` types (Fdp.Toolkits)

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCurve.cs` — NEW FILE

**Design reference:** Curve Editor Guide §3 Step 1 (the `UtilityCurve` struct definition); Architecture DD §5.3 (curve fields); Editor DD §5.2 (locked params table per CurveKind).

Add the editor-side curve model to `Fdp.Toolkits`. This struct is the StructEdit target type
(Curve Editor Guide §3 Step 4) and the type the widget binds to. The runtime `ResponseCurve` struct
(already in `UtilityCore.cs`) stays unchanged — `UtilityCurve` is the richer editor representation.

**Requirements:**

```csharp
namespace Fdp.Toolkit.Utility
{
    // Editor-side curve model — all four m/k/b/c params plus optional piecewise points.
    // The runtime ResponseCurve (UtilityCore.cs) is the blittable subset used at tick time.
    public struct UtilityCurve
    {
        public CurveKind Kind;
        public float M;   // slope (m)
        public float K;   // exponent (k)
        public float B;   // x-shift (b)
        public float C;   // y-shift (c)

        // Null unless Kind == PiecewiseLinear.
        // Must stay x-sorted at all times (enforced by CurveWidget).
        public PiecewisePoint[]? Points;

        // Convenience factory from the runtime struct (C defaults to 0 — runtime has no YShift).
        public static UtilityCurve FromResponseCurve(ResponseCurve rc);

        // Convert to the runtime struct.
        // For PiecewiseLinear, registers the points in PiecewiseCurveCatalog and returns the
        // ResponseCurve with the resulting CurveId. C is discarded (runtime has no YShift).
        public ResponseCurve ToResponseCurve();
    }

    // Immutable control-point for PiecewiseLinear curves.
    public readonly struct PiecewisePoint
    {
        public readonly float X; // in [0, 1]
        public readonly float Y; // in [0, 1]
        public PiecewisePoint(float x, float y) { X = x; Y = y; }
    }
}
```

`FromResponseCurve` maps: `Slope→M`, `Exponent→K`, `XShift→B`, `C=0`. For `PiecewiseLinear` it
reads the existing control points from `PiecewiseCurveCatalog` via `CurveId`.

`ToResponseCurve` maps: `M→Slope`, `K→Exponent`, `B→XShift`. For `PiecewiseLinear` it registers
the `Points` array in `PiecewiseCurveCatalog` (or reuses an existing registration if the content is
identical) and returns a curve with the resulting `CurveId`.

Check `PiecewiseCurveCatalog.cs` to understand the existing registration API before implementing.

### Task 2: Create `Hrot.Utility.Editor` project with `CurveWidget`

**New project:** `Hrot/Editor/Hrot.Utility.Editor/Hrot.Utility.Editor.csproj` — NEW PROJECT

Create the project file following the pattern of `Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj`:
- `net8.0`, `ImplicitUsings`, `Nullable`, `TreatWarningsAsErrors`
- `InternalsVisibleTo` for `Hrot.Utility.Editor.Tests`
- References: `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj`, `FDP/Engine/Fdp.Presentation/Fdp.Presentation.csproj`

Add the project to `IOS-IG-SimHost.sln`.

**File:** `Hrot/Editor/Hrot.Utility.Editor/Curve/CurveWidgetOptions.cs` — NEW FILE

```csharp
namespace Hrot.Utility.Editor.Curve
{
    /// Options passed to CurveWidget.Draw.
    public readonly struct CurveWidgetOptions
    {
        /// Width of the plot area in ImGui units. 0 = fill available width.
        public readonly float PlotWidth;
        /// Height of the plot area in ImGui units.
        public readonly float PlotHeight;
        /// If >= 0, draw a vertical marker at this x position and label the output.
        public readonly float FixtureInputX;
        /// If true, draw the comparison curve stored in ComparisonCurve on the same axes.
        public readonly bool ShowComparisonOverlay;
        /// The comparison curve to draw when ShowComparisonOverlay is true.
        public readonly UtilityCurve? ComparisonCurve;

        public static readonly CurveWidgetOptions Default = new CurveWidgetOptions(
            plotWidth: 0f, plotHeight: 80f, fixtureInputX: -1f,
            showComparisonOverlay: false, comparisonCurve: null);

        public CurveWidgetOptions(float plotWidth, float plotHeight, float fixtureInputX,
                                   bool showComparisonOverlay, UtilityCurve? comparisonCurve)
        {
            PlotWidth = plotWidth;
            PlotHeight = plotHeight;
            FixtureInputX = fixtureInputX;
            ShowComparisonOverlay = showComparisonOverlay;
            ComparisonCurve = comparisonCurve;
        }
    }
}
```

**File:** `Hrot/Editor/Hrot.Utility.Editor/Curve/CurveWidget.cs` — NEW FILE

**Design reference:** Curve Editor Guide §3 Step 2 (the widget contract); Editor DD §5.1–§5.4 (anatomy, handle mapping table, piecewise editing rules, test-fixture marker). Build Order §3 (no StructEdit, no Utility-editor dependency).

The widget must:
1. Draw a `PlotWidth × PlotHeight` ImGui canvas with the evaluated curve shape (16 evenly-spaced
   sample points, connected as a polyline)
2. Draw draggable handles per the Editor DD §5.2 handle↔param table — each handle drag updates the
   corresponding `m`, `k`, `b`, or `c` field on `curve`
3. Show four numeric `ImGui.DragFloat` fields for `m`, `k`, `b`, `c`; locked params are shown
   disabled (`ImGui.BeginDisabled` / `ImGui.EndDisabled`) and non-editable per Editor DD §5.2
4. For `PiecewiseLinear`: show a point-editor instead of drag-float sliders; left-click on plot
   adds a point at (x,y) clamped to [0,1]; right-click on an existing point deletes it; drag
   moves a point; points kept x-sorted after every edit
5. If `opts.FixtureInputX >= 0`, draw a vertical line on the plot at that x; label the output value
6. If `opts.ShowComparisonOverlay`, draw the comparison curve polyline in a distinct color
7. Return `true` if and only if any field in `curve` changed this frame

**Testable internal methods** (mark `internal` with `InternalsVisibleTo`):

```csharp
// Evaluates the curve at x, using the actual ResponseCurve.Evaluate path.
// For PiecewiseLinear uses the Points array directly (not the catalog) to avoid
// catalog side-effects in unit tests.
internal static float Evaluate(in UtilityCurve curve, float x);

// Fills output with Evaluate(curve, i/(count-1)) for i in [0, count-1].
// Asserts output.Length >= count.
internal static void ComputeSamples(in UtilityCurve curve, int count, Span<float> output);

// Returns true if the parameter is user-editable for the given CurveKind.
// Follow the locked-params column in Editor DD §5.2 table exactly.
internal static bool IsParamEditable(CurveKind kind, string param);  // param: "m","k","b","c"

// Adds a PiecewisePoint at (x,y), both clamped to [0,1], then x-sorts the array.
// Returns the new points array. Input array may be null (creates a new one).
internal static PiecewisePoint[] AddPiecewisePoint(PiecewisePoint[]? existing, float x, float y);

// Removes the point at index. Returns new sorted array.
internal static PiecewisePoint[] RemovePiecewisePoint(PiecewisePoint[] points, int index);
```

The `Evaluate` method for non-piecewise curves must create a `ResponseCurve` from the `UtilityCurve`
fields and call `ResponseCurve.Evaluate(x)` — this is the "actual runtime curve function" guarantee.
For `PiecewiseLinear`, evaluate directly from `curve.Points` via linear interpolation (same math as
`PiecewiseCurveCatalog` uses internally).

### Task 3: Create `Hrot.Utility.Editor.Tests` project with unit tests

**New project:** `Hrot/Editor/Hrot.Utility.Editor.Tests/Hrot.Utility.Editor.Tests.csproj` — NEW PROJECT

Follow the pattern of `Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj`:
- `net8.0`, `IsTestProject`, standard xunit packages
- References: `Hrot/Editor/Hrot.Utility.Editor/Hrot.Utility.Editor.csproj`

Add to `IOS-IG-SimHost.sln`.

**File:** `Hrot/Editor/Hrot.Utility.Editor.Tests/CurveWidgetTests.cs` — NEW FILE

Write tests that cover all four success conditions (SC-P3-01-1 through SC-P3-01-4). Test the
internal helper methods directly — they exercise the logic without requiring an ImGui render frame.

**Required tests (minimum 10):**

**Group 1 — Evaluate matches runtime (SC-P3-01-2):**

```
CurveWidget_Evaluate_Linear_MatchesResponseCurve
  - curve: Kind=Linear, M=0.8f, K=1f, B=0.1f, C=0f
  - for x in {0f, 0.125f, 0.25f, 0.375f, 0.5f, 0.625f, 0.75f, 0.875f, 1f, +7 more = 16 total}
  - Assert.Equal(expected, CurveWidget.Evaluate(curve, x), tolerance 0.0001f)
  - expected = new ResponseCurve(CurveKind.Linear, slope:0.8f, xShift:0.1f).Evaluate(x)
```

```
CurveWidget_Evaluate_Logistic_MatchesResponseCurve
  - curve: Kind=Logistic, M=1f, K=5f, B=0.5f, C=0f; 16 sample points; same cross-check
```

```
CurveWidget_ComputeSamples_Returns16ValuesMatchingEvaluate
  - var samples = new float[16]; CurveWidget.ComputeSamples(curve, 16, samples);
  - for each i: Assert.Equal(CurveWidget.Evaluate(curve, i/15f), samples[i], tolerance)
```

```
CurveWidget_Evaluate_YShift_C_AppliesOffset
  - curve: Kind=Linear, M=1f, K=1f, B=0f, C=0.1f
  - Evaluate at x=0.5f must equal Clamp(0.5f + 0.1f, 0, 1) = 0.6f
  - This verifies C (YShift) is applied (ResponseCurve has no YShift; CurveWidget.Evaluate adds it)
```

**Group 2 — Locked params per kind (SC-P3-01-3):**

```
CurveWidget_IsParamEditable_Linear_LocksBAndK
  - Kind=Linear: m=editable, k=NOT editable, b=editable, c=NOT editable
  - (per Editor DD §5.2 table: Linear/InverseLinear locks k=1, c from left endpoint)
```

```
CurveWidget_IsParamEditable_Step_LocksM
  - Kind=Step: m=NOT editable, k=NOT editable, b=editable, c=editable
```

```
CurveWidget_IsParamEditable_Bell_LocksM
  - Kind=Bell: m=NOT editable, k=editable, b=editable, c=editable
```

Read the Editor DD §5.2 table for each CurveKind and test every entry.

**Group 3 — Handle ↔ param mapping (SC-P3-01-1):**

```
CurveWidget_AddPiecewisePoint_SortsOnInsert
  - start with points [(0.1f,0.5f),(0.8f,0.3f)]; add (0.4f,0.7f)
  - result must be [(0.1,0.5),(0.4,0.7),(0.8,0.3)] (x-sorted)
```

**Group 4 — PiecewiseLinear (SC-P3-01-4):**

```
CurveWidget_AddPiecewisePoint_ClampsToUnitSquare
  - add point (-0.1f, 1.5f) → result has point (0f, 1f) (clamped)
```

```
CurveWidget_RemovePiecewisePoint_RemovesCorrectIndex
  - points [(0.1,0.5),(0.4,0.7),(0.8,0.3)]; remove index 1
  - result = [(0.1,0.5),(0.8,0.3)]
```

```
CurveWidget_Evaluate_PiecewiseLinear_InterpolatesCorrectly
  - points = [(0f,0f),(0.5f,1f),(1f,0f)] (triangle)
  - Evaluate at x=0.25f must be 0.5f (midpoint of first segment)
  - Evaluate at x=0.75f must be 0.5f (midpoint of second segment)
  - Evaluate at x=0f must be 0f; at x=1f must be 0f
```

```
CurveWidget_Evaluate_PiecewiseLinear_ClampedOutput
  - any Evaluate call must return a value in [0,1]
```

**Additional required test:**

```
UtilityCurve_FromResponseCurve_PreservesFields
  - rc = new ResponseCurve(CurveKind.Bell, slope:0.9f, exponent:3f, xShift:0.4f)
  - uc = UtilityCurve.FromResponseCurve(rc)
  - uc.Kind==Bell, uc.M==0.9f, uc.K==3f, uc.B==0.4f, uc.C==0f
```

---

## Testing Requirements

- **Minimum 12 unit tests**
- All tests must use `Assert.Equal` / `Assert.True` on **actual computed values**, not just string presence or type existence
- Tests for `CurveWidget.Evaluate` must cross-check against `ResponseCurve.Evaluate` directly — if the runtime evaluation changes, the widget tests must catch it
- The YShift (C) test is critical: `ResponseCurve` has no C field; `CurveWidget.Evaluate` must add C after calling the runtime eval
- Do NOT test ImGui calls (no headless ImGui harness needed) — test the logic/math methods only

---

## Success Criteria

- [ ] TASK-UAI-P3-01 completed: `UtilityCurve`, `PiecewisePoint`, `CurveWidget`, `CurveWidgetOptions` all exist
- [ ] SC-P3-01-2 verified: `CurveWidget.Evaluate` matches `ResponseCurve.Evaluate` at 16 sample points for at least two curve kinds
- [ ] SC-P3-01-3 verified: `CurveWidget.IsParamEditable` returns correct locked/editable per all CurveKinds
- [ ] SC-P3-01-4 verified: PiecewiseLinear add/remove/eval tests pass
- [ ] Both new projects added to `IOS-IG-SimHost.sln`
- [ ] Full solution builds without errors
- [ ] All new tests pass

---

## Quality Standards

**TEST QUALITY:**
- Tests must verify ACTUAL evaluated values, not just compilation or struct existence
- `CurveWidget_Evaluate_Linear_MatchesResponseCurve` must assert specific float values
- Cross-checking `CurveWidget.Evaluate` against `ResponseCurve.Evaluate` is the GOLD STANDARD here

**CODE QUALITY:**
- `CurveWidget` must have NO dependency on StructEdit (Build Order §3: "no StructEdit dependency")
- `UtilityCurve` must live in `Fdp.Toolkit.Utility` namespace (Curve Editor Guide §8, C-1 resolution)
- Follow `TreatWarningsAsErrors = true` in `Hrot.Utility.Editor.csproj`

---

## Common Pitfalls

- Do NOT put `UtilityCurve` in `Fdp.Toolkit.Behavior` — the guide says the *assembly* is `Fdp.Toolkits` but the namespace convention for utility types is `Fdp.Toolkit.Utility`
- `CurveWidget.Evaluate` for PiecewiseLinear must NOT register with `PiecewiseCurveCatalog` — use the `Points` array directly for evaluation (catalog is for runtime dispatch, not widget math)
- For non-piecewise kinds, `CurveWidget.Evaluate` must call `ResponseCurve.Evaluate` to avoid math drift; adding C *after* the clamp would be wrong — add C before the final clamp
- `IsParamEditable` must match the Editor DD §5.2 table exactly — read it carefully for each CurveKind

---

## Reference Materials

- **Task Detail:** `.dev/utility-ai/TASK-DETAIL.md` — TASK-UAI-P3-01 section
- **Curve Editor Guide:** `.dev/utility-ai/Curve_Editor_in_StructEdit_Guide_v1_1.md` — §3 Steps 1-2
- **Editor DD §5:** `.dev/utility-ai/Utility_AI_Editor_Design_v1_2.md` — §5.1–§5.4
- **Architecture §5.3:** `.dev/utility-ai/Utility_AI_Design_v1_1.md` — curve struct and eval
- **Existing csproj pattern:** `Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj`
- **Existing test pattern:** `Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj`
