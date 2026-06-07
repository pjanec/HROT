# BATCH-11 Report — CurveWidget Standalone Widget

**Date:** 2025-07-27  
**Branch:** (current working branch)  
**Status:** COMPLETE — Build green, all 35 tests pass

---

## Work Summary

Implemented Phase 3 of the Utility AI editor workstream: the standalone `CurveWidget` ImGui widget together with its supporting types.

---

## Files Created

### New types in `Fdp.Toolkits`

| File | Description |
|---|---|
| `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCurve.cs` | `UtilityCurve` struct (M/K/B/C + Points) with `FromResponseCurve` / `ToResponseCurve` |
| (same file) | `PiecewisePoint` immutable control-point struct |

### New library: `Hrot.Utility.Editor`

| File | Description |
|---|---|
| `Hrot/Editor/Hrot.Utility.Editor/Hrot.Utility.Editor.csproj` | Project file (net8.0, refs Fdp.Toolkits + Fdp.Presentation) |
| `Hrot/Editor/Hrot.Utility.Editor/Curve/CurveWidgetOptions.cs` | `CurveWidgetOptions` readonly struct (PlotWidth, PlotHeight, FixtureInputX, ShowComparisonOverlay, ComparisonCurve) |
| `Hrot/Editor/Hrot.Utility.Editor/Curve/CurveWidget.cs` | `CurveWidget` static class — `Draw`, `Evaluate`, `ComputeSamples`, `IsParamEditable`, `AddPiecewisePoint`, `RemovePiecewisePoint` |

### New test project: `Hrot.Utility.Editor.Tests`

| File | Description |
|---|---|
| `Hrot/Editor/Hrot.Utility.Editor.Tests/Hrot.Utility.Editor.Tests.csproj` | Project file (xUnit 2.5.3) |
| `Hrot/Editor/Hrot.Utility.Editor.Tests/CurveWidgetTests.cs` | 35 unit tests across `UtilityCurveTests` and `CurveWidgetEvaluateTests` |

---

## Files Modified

| File | Change |
|---|---|
| `FDP/Toolkits/Fdp.Toolkits/Utility/Core/PiecewiseCurveCatalog.cs` | Added `internal static (float x, float y)[]? GetPoints(short curveId)` for editor round-trip |
| `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj` | Added `InternalsVisibleTo` for `Hrot.Utility.Editor` and `Hrot.Utility.Editor.Tests` |
| `IOS-IG-SimHost.sln` | Added both new projects (Project entries, build config, NestedProjects under Editor folder) |

---

## Design Decisions

### `UtilityCurve.ToResponseCurve` — PiecewiseLinear ID derivation

A content-hash of the control points is used to derive the `short CurveId`. This avoids a separate ID-registry and makes round-trips deterministic: same points always produce the same ID. Collision probability for the 15-bit hash space is negligible for typical curve counts.

### `CurveWidget.Evaluate` — no catalog side-effect

For `PiecewiseLinear` curves, `Evaluate` interpolates directly from `curve.Points` using binary search + linear interpolation. This matches the logic in `PiecewiseCurveCatalog` exactly but requires no catalog registration, making the widget safe to call in the absence of a registered catalog entry (e.g. in unit tests and in the preview-only comparison overlay).

### `C` param — final-clamp semantics

`Evaluate` adds `C` after the per-kind curve formula and then clamps. This ensures `C` acts as a uniform vertical shift that is always bounded to `[0, 1]` in the output, consistent with the Editor DD §5.1 contract.

### `IsParamEditable` — locked params

Implemented per the §5.2 table:
- `m` editable only for Linear/InverseLinear/PiecewiseLinear
- `k` editable only for Bell/Logistic/Quadratic/InverseQuadratic/PiecewiseLinear  
- `b` always editable
- `c` editable for Threshold/Bell/Step/PiecewiseLinear

---

## Test Results

```
Passed!  - Failed: 0, Passed: 35, Skipped: 0, Total: 35, Duration: 25 ms
```

### Test coverage by class

| Class | Test count | Topics covered |
|---|---|---|
| `UtilityCurveTests` | 4 | FromResponseCurve (Linear, Bell), ToResponseCurve (Quadratic), PiecewiseLinear round-trip via catalog |
| `CurveWidgetEvaluateTests` | 31 | Evaluate (Linear identity, C shift, clamp-high, clamp-low), PiecewiseLinear interpolation/endpoint/null-points, IsParamEditable (19 cases via Theory), AddPiecewisePoint (x-sort, coord clamp), RemovePiecewisePoint, ComputeSamples (linear identity) |

---

## Build Status

Both projects build with zero errors and zero warnings under `Debug|Any CPU`.
