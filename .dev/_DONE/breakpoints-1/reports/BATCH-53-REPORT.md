# BATCH-53 Report

**Batch:** BATCH-53  
**Tasks:** P11T7, P12T1–P12T4  
**Status:** COMPLETE

---

## Summary of Changes

### P11T7 — CompoundPredicateHelper + DrawPredicateEditor

**New file:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/CompoundPredicateHelper.cs`

- Added `public static class CompoundPredicateHelper` with one method:
  `public static bool IsChildReadOnly(CompoundPredicateDto dto, int childIndex)`
  returns `dto.ReadOnlyChildIndices?.Contains(childIndex) == true`.
- Placed in `Hrot.Diagnostics.Breakpoints` namespace so both the panel and the test
  project can reference it without a new project dependency.

**Modified:** `Hrot/Engine/Hrot.Presentation/Panels/Breakpoints/DataBreakpointManagerPanel.cs`

- Added `using System.Linq;` at the top.
- Added `DrawPredicateEditor()` private method that:
  - Returns early if no valid `_selectedId`.
  - Calls `_manager.AllBreakpoints.FirstOrDefault(b => b.Id == _selectedId)`.
  - For `CompoundPredicateDto` conditions: renders each child with
    `ImGuiApi.BeginDisabled()` / `EndDisabled()` wrapping when
    `CompoundPredicateHelper.IsChildReadOnly(compound, i)` returns true.
  - For non-compound conditions: renders a simple summary line.
- Updated `DrawContent()` to call `DrawPredicateEditor()` between `DrawGrid()` and
  `DrawBanner()`.

**Deviation from instructions:** The instructions showed `IsChildReadOnly` as both
`internal static` on `DataBreakpointManagerPanel` AND as a method in `CompoundPredicateHelper`.
Followed the "Preferred approach": only `CompoundPredicateHelper` carries the method;
`DataBreakpointManagerPanel.DrawPredicateEditor()` calls `CompoundPredicateHelper.IsChildReadOnly`.
The test file was also adapted to call `CompoundPredicateHelper.IsChildReadOnly` directly,
matching the preferred approach. This avoids making `DataBreakpointManagerPanel` a public
API surface for a pure-logic helper.

---

### P12T1–T4 — Wired integration tests (tests 21–25)

**Modified:** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/BreakpointSubsystemWiringTests.cs`

Added 5 new tests appended after test 20:

| # | Method | What it tests |
|---|--------|---------------|
| 21 | `E2E_Wired_ActiveViewSwitchesToPreTickDuringPause` | ActiveView object changes to pre-tick snapshot after `OnHit` |
| 22 | `E2E_Wired_DeferredMutationQueued_StepDrainsECB` | `RequestStep()` un-pauses and leaves `PendingMutationsCount == 0` |
| 23 | `Wired_Performance_ArmedBP_100Ticks_WellUnderBudget` | 100 ticks with an armed ExternalHitTag BP finish in < 10 s |
| 24 | `Wired_FlightRecorder_PauseStepResume_KernelAdvancesTick` | World.GlobalVersion progresses monotonically through pause/step/resume |
| 25 | `MultiSubsystem_TwoManagers_PausingOneDoesNotAffectOther` | Two independent EditorSubsystem instances do not cross-pause |

No `ComponentTypeRegistry.Clear()` calls were introduced.

**Fix applied during implementation:** The batch instructions referenced `Fdp.Core.ISimulationView`
but the interface is actually in `Fdp.ModuleHost.Abstractions`. Used
`Fdp.ModuleHost.Abstractions.ISimulationView` in test 21.

---

## New Test Files

### `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/PredicateBuilderP11T7Tests.cs`

| Class | Method |
|-------|--------|
| `PredicateBuilderP11T7Tests` | `IsChildReadOnly_IndexInList_ReturnsTrue` |
| `PredicateBuilderP11T7Tests` | `IsChildReadOnly_IndexNotInList_ReturnsFalse` |
| `PredicateBuilderP11T7Tests` | `IsChildReadOnly_EmptyList_ReturnsFalse` |
| `PredicateBuilderP11T7Tests` | `IsChildReadOnly_NullList_ReturnsFalse` |

### New methods in `BreakpointSubsystemWiringTests.cs`

| Method |
|--------|
| `E2E_Wired_ActiveViewSwitchesToPreTickDuringPause` |
| `E2E_Wired_DeferredMutationQueued_StepDrainsECB` |
| `Wired_Performance_ArmedBP_100Ticks_WellUnderBudget` |
| `Wired_FlightRecorder_PauseStepResume_KernelAdvancesTick` |
| `MultiSubsystem_TwoManagers_PausingOneDoesNotAffectOther` |

---

## Test Results

```
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/...
  Passed!  - Failed: 0, Passed: 128, Skipped: 0, Total: 128

dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/... --filter BreakpointSubsystemWiring
  Passed!  - Failed: 0, Passed: 25, Skipped: 0, Total: 25

dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/...
  Passed!  - Failed: 0, Passed: 167, Skipped: 0, Total: 167

dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/...
  Passed!  - Failed: 0, Passed: 192, Skipped: 0, Total: 192
```

---

## Build Output

```
Build succeeded.
    1 Warning(s)   [pre-existing CS0618 in DataBreakpointManagerTests.cs:679]
    0 Error(s)
Time Elapsed 00:00:30.96
```

Zero new warnings introduced. The single warning is a pre-existing `[Obsolete]`
annotation on `IBlueprintTimeController` in an existing test file.

---

## Deviations from Instructions

1. **`IsChildReadOnly` location** — Instructions suggested two options and marked
   `CompoundPredicateHelper` as preferred. Followed the preferred approach.
   Test calls `CompoundPredicateHelper.IsChildReadOnly` instead of
   `DataBreakpointManagerPanel.IsChildReadOnly`.

2. **`Fdp.Core.ISimulationView` reference** — Instructions contained an incorrect
   fully-qualified name. `ISimulationView` lives in `Fdp.ModuleHost.Abstractions`.
   Corrected to `Fdp.ModuleHost.Abstractions.ISimulationView`.

---

## Checklist

- [x] `CompoundPredicateHelper.IsChildReadOnly` added in `Hrot.Diagnostics.Breakpoints`
- [x] `DataBreakpointManagerPanel.DrawPredicateEditor()` added, calls `CompoundPredicateHelper.IsChildReadOnly`
- [x] `DrawContent()` calls `DrawPredicateEditor()` after `DrawGrid()`
- [x] `PredicateBuilderP11T7Tests.cs` created (4 tests, no ImGui context required)
- [x] Tests 21–25 added to `BreakpointSubsystemWiringTests.cs`
- [x] No `ComponentTypeRegistry.Clear()` calls in the new integration tests
- [x] Build: 0 errors, 0 new warnings
- [x] 128 unit tests pass (124 existing + 4 new P11T7)
- [x] 25 integration tests pass (20 existing + 5 new P12)
