# BATCH-27 Report: Phase 4 — Migrate Hrot Editor Picker Tools to IEntityStatefulGizmo

## Pass Condition Table

| Condition | Status |
|-----------|--------|
| `Hrot.Editor.Tools.LocationPickerTool` physically deleted | PASS |
| `Hrot.Editor.Tools.ModalBoxSelectionTool` physically deleted | PASS |
| `Hrot.Editor.Tools.EntityPickerTool` physically deleted | PASS |
| `LocationPickerGizmo` implements `IEntityStatefulGizmo`, `RequiresExclusiveFocus = true` | PASS |
| `ModalBoxSelectionGizmo` implements `IEntityStatefulGizmo`, `RequiresExclusiveFocus = true` | PASS |
| `EditorMapPickAdapter.PickLocationAsync` uses bridge+gizmo | PASS |
| `EditorMapPickAdapter.PickAreaEntitiesAsync` uses bridge+gizmo | PASS |
| `EditorMapPickAdapter.PickEntityAsync` unchanged (still uses FDP's EntityPickerTool) | PASS |
| `CanvasMapPickAdapter` unchanged | PASS |
| Solution builds 0 errors | PASS |
| `Hrot.Editor.Tests` all pass | PASS |
| `Hrot.IG.Tests` no new failures vs 68-failure baseline | PASS |
| `Hrot.Presentation.Tests` all pass | PASS |

## Test Results

| Project | Passed | Failed | Total |
|---------|--------|--------|-------|
| Hrot.Editor.Tests | 95 | 0 | 95 |
| Hrot.Presentation.Tests | 57 | 0 | 57 |
| Hrot.IG.Tests | 315 | 68 (pre-existing) | 383 |

## Files Created

- `Hrot/Subsystems/Hrot.Editor/Gizmos/LocationPickerGizmo.cs` (new)
- `Hrot/Subsystems/Hrot.Editor/Gizmos/ModalBoxSelectionGizmo.cs` (new)

## Files Modified

- `Hrot/Subsystems/Hrot.Editor/Adapters/EditorMapPickAdapter.cs`
  - Removed `using Hrot.Editor.Tools;`
  - Added `using Hrot.Editor.Gizmos;`, `using Hrot.ScenarioEditor.Gizmos;`
  - Replaced `PickLocationAsync` — now uses `LocationPickerGizmo` + `PlacementCanvasBridge`
  - Replaced `PickAreaEntitiesAsync` — now uses `ModalBoxSelectionGizmo` + `PlacementCanvasBridge`
  - `PickEntityAsync` left unchanged (still uses FDP's `EntityPickerTool`)
- `Hrot/Subsystems/Hrot.Editor/Adapters/EditorSpawnAdapter.cs`
  - Removed orphaned `using Hrot.Editor.Tools;` (leftover from before Phase 3)
- `Hrot/Subsystems/Hrot.Editor.Tests/Adapters/AdapterTests.cs`
  - Removed `using Hrot.Editor.Tools;`
  - Updated A004 `PickLocationAsync_ToolFires_TaskCompletesWithGeoPoint` — uses `PlacementCanvasBridge.HandleClick` instead of direct `LocationPickerTool` callback invocation
  - Updated A004 `PickLocationAsync_CancellationToken_TaskCancelled` — asserts `PlacementCanvasBridge` is active tool before cancellation
  - Updated A004 `PickAreaEntitiesAsync_ToolFires_TaskCompletesWithList` — uses `PlacementCanvasBridge.HandleClick` instead of direct `ModalBoxSelectionTool` callback invocation

## Files Deleted

- `Hrot/Subsystems/Hrot.Editor/Tools/LocationPickerTool.cs`
- `Hrot/Subsystems/Hrot.Editor/Tools/ModalBoxSelectionTool.cs`
- `Hrot/Subsystems/Hrot.Editor/Tools/EntityPickerTool.cs`

## Issues Encountered and Resolved

**Issue 1: `EditorSpawnAdapter.cs` had orphaned `using Hrot.Editor.Tools;`**

After deleting the three picker tool files, the build failed with CS0234 in `EditorSpawnAdapter.cs` (line 9) because it had a `using Hrot.Editor.Tools;` that was not used by any code in the file (it was a leftover from an earlier phase). Removed the unused using directive. Build then succeeded with 0 errors.
