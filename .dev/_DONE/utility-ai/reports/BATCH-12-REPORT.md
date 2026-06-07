# BATCH-12 Report

**Batch:** BATCH-12
**Status:** APPROVED — all tasks complete, build green, all tests passing

---

## Summary

All four parts of BATCH-12 were implemented and verified.

---

## Part A — CurveWidget `IsParamEditable` Fix

### Files Modified

- `Hrot/Editor/Hrot.Utility.Editor/Curve/CurveWidget.cs`
- `Hrot/Editor/Hrot.Utility.Editor.Tests/CurveWidgetTests.cs`

### Changes

**CurveWidget.cs** — `IsParamEditable(CurveType, int)`:
- `Linear` and `InverseLinear` now correctly return `false` for all parameter indices (neither curve type has user-editable parameters).
- Previously the method returned `true` for those types, causing spurious editable-parameter indicators in the editor UI.

**CurveWidgetTests.cs**:
- Fixed two `InlineData` values that were wrong (expected `true` where `false` is correct for Linear/InverseLinear).
- Added `InlineData` entries covering `InverseLinear`.
- Added `SixteenSamples_ReturnsCorrectCount` `[Fact]`.
- Added two `[Theory]` tests: `IsParamEditable_Linear_NeverEditable` and `IsParamEditable_InverseLinear_NeverEditable`.

### Test Results

`Passed! — Failed: 0, Passed: 69, Skipped: 0, Total: 69`

---

## Part B — `AiOverlayFlags` Enum + `DebugState.Ai` Field

### Files Created / Modified

- **Created** `FDP/Toolkits/Fdp.Toolkits/Behavior/Diagnostics/AiOverlayFlags.cs`
- **Modified** `FDP/Toolkits/Fdp.Toolkits/Behavior/Diagnostics/DebugState.cs`
- **Created** `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/Diagnostics/AiOverlayFlagsTests.cs`

### Changes

**AiOverlayFlags.cs** (namespace `Fdp.Toolkit.Behavior.Diagnostics`):

```csharp
[Flags]
public enum AiOverlayFlags : ushort
{
    None             = 0,
    Perception       = 1 << 0,
    TargetMemory     = 1 << 1,
    Eqs              = 1 << 2,
    UtilityDecision  = 1 << 3,
    SquadAssignment  = 1 << 4,
    Channels         = 1 << 5,
}
```

**DebugState.cs** — added `public AiOverlayFlags Ai;` after the existing `BehaviorDebugFlags Behavior` field.

**AiOverlayFlagsTests.cs** — 4 tests:
- `AiOverlayFlags_IsUshort_WithFlagsAttribute`
- `DebugState_HasAiField_And_SizeIsEight`
- `DebugState_DefaultAiFieldIsNone`
- `DebugState_BehaviorFieldUnchanged_WhenAiSet`

### Test Results

`Passed! — Failed: 0, Passed: 4, Skipped: 0, Total: 4`

---

## Part C — `Hrot.Diagnostics.Overlays` Project

### Files Created

| File | Description |
|------|-------------|
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/Hrot.Diagnostics.Overlays.csproj` | Main project file; references Fdp.Toolkits, Fdp.Diagnostics.Contracts, GizmoMap.Contracts |
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/OverlayBudgetArbiter.cs` | Budget arbiter — sheds overlay families when frame budget is exceeded |
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/PerceptionOverlaySource.cs` | Emits "PERCEPT" text for entities with SensorContactList + Perception flag |
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/TargetMemoryOverlaySource.cs` | Emits one sphere per tracked target in TargetMemory |
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/EqsOverlaySource.cs` | Emits "EQS:{Count}" text label |
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/UtilityDecisionOverlaySource.cs` | Emits utility winner option/margin via DrawTextLong |
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/SquadAssignmentOverlaySource.cs` | Emits "SQUAD:{Count}" text label |
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays.Tests/Hrot.Diagnostics.Overlays.Tests.csproj` | Test project file |
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays.Tests/OverlaySourceTests.cs` | 16 tests |

### Design Notes

- `OverlayBudgetArbiter` is `internal sealed`. All overlay source classes are also `internal sealed` (required — public constructors cannot accept internal parameter types).
- `InternalsVisibleTo("Hrot.Diagnostics.Overlays.Tests")` allows the test project to access internal types.
- `TargetMemoryOverlaySource.EmitForEntity` is marked `unsafe` because it indexes into `fixed` array fields of `TargetMemory`.
- `FixedString32` is ambiguous between `Fdp.Core` and `Fdp.Toolkit.Diagnostics.Gizmos`; resolved with `using FixedString32 = Fdp.Toolkit.Diagnostics.Gizmos.FixedString32;` in each affected file.
- `UtilityTraceWorkingMemory1024.LatestSelected()` is not `readonly`; a mutable copy `var memCopy = mem;` is used before calling it.
- `SensorContactList` has no position fields; `PerceptionOverlaySource` emits `DrawText("PERCEPT")` as a presence indicator.

### `OverlayBudgetArbiter` shed order (lowest to highest priority)

```
Channels, SquadAssignment, Eqs, TargetMemory, Perception, UtilityDecision
```

`UtilityDecision` is highest priority and is shed last.

### Test Results

`Passed! — Failed: 0, Passed: 16, Skipped: 0, Total: 16`

### Test Coverage

| Test | Scenario |
|------|----------|
| `UtilityDecision_NoDebugState_EmitsZero` | No DebugState component — skip entity |
| `UtilityDecision_FlagSet_TracePresent_EmitsAtLeastOne` | Flag + component with winner record → emits |
| `UtilityDecision_FlagAbsent_EmitsZero` | Wrong flag bit set — no emission |
| `UtilityDecision_FlagSet_ComponentAbsent_EmitsZero_NoThrow` | Flag set but component missing — no throw, no emit |
| `Perception_FlagAbsent_EmitsZero` | Wrong flag — no emit |
| `Perception_FlagSet_ComponentAbsent_EmitsZero_NoThrow` | Missing component — no throw |
| `Perception_FlagAndComponentPresent_EmitsAtLeastOne` | Both present → emits |
| `TargetMemory_FlagAbsent_EmitsZero` | Wrong flag — no emit |
| `TargetMemory_FlagSet_ComponentAbsent_EmitsZero_NoThrow` | Missing component — no throw |
| `TargetMemory_FlagAndComponentPresent_EmitsAtLeastOne` | Both present, one target seeded → emits sphere |
| `Eqs_FlagAbsent_EmitsZero` | Wrong flag — no emit |
| `Eqs_FlagSet_ComponentAbsent_EmitsZero_NoThrow` | Missing component — no throw |
| `Eqs_FlagAndComponentPresent_EmitsAtLeastOne` | Both present → emits |
| `SquadAssignment_FlagAbsent_EmitsZero` | Wrong flag — no emit |
| `SquadAssignment_FlagSet_ComponentAbsent_EmitsZero_NoThrow` | Missing component — no throw |
| `BudgetArbiter_ShedsChannels_KeepsUtilityDecision` | Budget exceeded by Channels recording → Channels shed, UtilityDecision kept |

---

## Part D — Build & Solution

### Solution Update

`IOS-IG-SimHost.sln` updated with:
- Two new `Project(...)` entries for `Hrot.Diagnostics.Overlays` and `Hrot.Diagnostics.Overlays.Tests`
- Full `Debug|Any CPU`, `Debug|x64`, `Debug|x86`, `Release|Any CPU`, `Release|x64`, `Release|x86` configuration entries for both projects
- Both projects nested under the existing `Diagnostics` solution folder (`{5E4C52BA-6213-E083-B735-5DDE0CCE6DA3}`)

### Build Result

```
Build succeeded.
```

Zero errors. Zero new warnings introduced by BATCH-12 code (pre-existing warnings in Hrot.Blueprints.Tests and other projects are unrelated to this batch).

---

## Files Changed (complete list)

| File | Action |
|------|--------|
| `Hrot/Editor/Hrot.Utility.Editor/Curve/CurveWidget.cs` | Modified |
| `Hrot/Editor/Hrot.Utility.Editor.Tests/CurveWidgetTests.cs` | Modified |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Diagnostics/AiOverlayFlags.cs` | Created |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Diagnostics/DebugState.cs` | Modified |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/Diagnostics/AiOverlayFlagsTests.cs` | Created |
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/Hrot.Diagnostics.Overlays.csproj` | Created |
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/OverlayBudgetArbiter.cs` | Created |
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/PerceptionOverlaySource.cs` | Created |
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/TargetMemoryOverlaySource.cs` | Created |
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/EqsOverlaySource.cs` | Created |
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/UtilityDecisionOverlaySource.cs` | Created |
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/SquadAssignmentOverlaySource.cs` | Created |
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays.Tests/Hrot.Diagnostics.Overlays.Tests.csproj` | Created |
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays.Tests/OverlaySourceTests.cs` | Created |
| `IOS-IG-SimHost.sln` | Modified |
