# BATCH-18 Report

## Status: APPROVED

## Tasks Completed

### P6-01: CurveFieldEditor + CurveFieldDrawer

| File | Action | Description |
|---|---|---|
| `Hrot/Editor/Hrot.Utility.Editor/FieldEdit/UtilityCurveFieldEditor.cs` | Created | `ICustomFieldEditor` plugin collapsing `UtilityCurve` into `EditNodeKind.Custom` |
| `Hrot/Editor/Hrot.Utility.Editor/FieldEdit/UtilityCurveFieldDrawer.cs` | Created | `IImGuiFieldDrawer` rendering `UtilityCurve` via `CurveWidget.Draw` |
| `Hrot/Editor/Hrot.Utility.Editor.Tests/FieldEdit/UtilityCurveFieldEditorTests.cs` | Created | 4 tests: TargetType, CreateNode KindIsCustom, CreateNode TypeIsUtilityCurve, Drawer TargetType |

### P6-02: Piecewise translate-on-apply

| File | Action | Description |
|---|---|---|
| `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/CurveTunable.cs` | Created | Tunable record for `UtilityCurve`-typed fields |
| `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/TuningRegistry.cs` | Modified | Added `_curveTunables`, `_curveApplyQueue`, `MaxPiecewisePoints`, `RegisterCurve`, `ApplyCurve`, `TryGetCurve`; fixed `BeginFrame` early-return to also drain curve queue |
| `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/UtilityTuningBinder.cs` | Modified | Added 5th registration per consideration: whole-curve `CurveTunable` via `RegisterCurve` |
| `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/Gizmos/TuningConsoleGizmo.cs` | Modified | Added `TryApplyCurveProperty` + `DeserializeUtilityCurve` private methods; extended `OnStructUpdate` to route Object-valued properties to curve path |
| `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj` | Modified | Added `InternalsVisibleTo` for `Hrot.Diagnostics.Tuning.Tests` to access `PiecewiseCurveCatalog.ClearAll()` and `GetPoints()` |
| `Hrot/Diagnostics/Hrot.Diagnostics.Tuning.Tests/PiecewiseTranslateTests.cs` | Created | 6 tests covering end-to-end translate-on-apply, clamping, mixed float+curve batches, integer Kind deserialization |

## Test Results

| Test Project | Before | After | Delta |
|---|---|---|---|
| `Hrot.Utility.Editor.Tests` | 137 | 141 | +4 (P6-01) |
| `Hrot.Diagnostics.Tuning.Tests` | 18 | 24 | +6 (P6-02) |

Both projects: **Failed: 0, Passed: all**.

## Deviations from Instructions

| # | Description | Resolution |
|---|---|---|
| 1 | Instructions showed `IValueBinding.GetValue()`/`SetValue()` | Actual interface has `GetBoxed()`/`SetBoxed()`/`ValueType`/`TryGetSpan()`. Used actual names. |
| 2 | Instructions referenced `EditNode.FieldType` | Actual property is `ClrType`. Used `ClrType`. |
| 3 | Instructions stated `CurveKind.PiecewiseLinear = 4` | Actual value is `8` (it is the 9th member: Linear=0..PiecewiseLinear=8). Used 8 in tests. |
| 4 | `JsonElement.TryGetSingle` throws on non-Number elements in .NET 8 | Added `prop.Value.ValueKind == JsonValueKind.Number &&` guard before `TryGetSingle` call in `OnStructUpdate`. |
| 5 | Test float formatting was locale-dependent | Used `CultureInfo.InvariantCulture` in `BuildCurveJson` to generate valid JSON on all locales. |

## Key Implementation Notes

- `OnStructUpdate` now checks `ValueKind == Number` before calling `TryGetSingle`, then routes `ValueKind == Object` to `TryApplyCurveProperty`. The original code called `TryGetSingle` unconditionally, which throws `InvalidOperationException` on Object elements in .NET 8.
- `BeginFrame` early-return on empty float queue was replaced with an empty-array path so the curve drain loop always executes.
- `DeserializeUtilityCurve` handles `Kind` as both integer (`"Kind":8`) and string (`"Kind":"PiecewiseLinear"`).
- Piecewise points exceeding `MaxPiecewisePoints` (64) are clamped with a warning to `Console.Error`.
