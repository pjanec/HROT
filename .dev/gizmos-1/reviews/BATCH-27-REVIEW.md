# BATCH-27 Review

**Status: APPROVED**

## Pass Condition Verification

| Condition | Verified |
|-----------|----------|
| `Hrot.Editor.Tools.LocationPickerTool` physically deleted | YES |
| `Hrot.Editor.Tools.ModalBoxSelectionTool` physically deleted | YES |
| `Hrot.Editor.Tools.EntityPickerTool` physically deleted | YES |
| `LocationPickerGizmo` implements `IEntityStatefulGizmo`, `RequiresExclusiveFocus = true` | YES |
| `ModalBoxSelectionGizmo` implements `IEntityStatefulGizmo`, `RequiresExclusiveFocus = true` | YES |
| `EditorMapPickAdapter.PickLocationAsync` uses bridge+gizmo | YES |
| `EditorMapPickAdapter.PickAreaEntitiesAsync` uses bridge+gizmo | YES |
| `EditorMapPickAdapter.PickEntityAsync` unchanged (FDP's EntityPickerTool) | YES |
| `CanvasMapPickAdapter` unchanged | YES |
| Solution builds 0 errors | YES |
| `Hrot.Editor.Tests` all pass | YES — 95/95 |
| `Hrot.IG.Tests` no new failures vs 68-failure baseline | YES |
| `Hrot.Presentation.Tests` all pass | YES — 57/57 |

## Code Quality

`LocationPickerGizmo` — correct. Crosshair constants copied from deleted `LocationPickerTool`. Geo conversion via `_geoTransform.ToGeodetic`. Left-click fires delegate then removes; right/Escape cancels.

`ModalBoxSelectionGizmo` — correct. No visual. Left-click fires `_onSelectionComplete(Array.Empty<int>())` then removes.

`EditorMapPickAdapter` — correct. `PlacementCanvasBridge? bridge = null` + closure pattern is established and correct. CancellationToken registration guards `if (_canvas.ActiveTool == bridge)` before requesting pop.

Bonus fix: `EditorSpawnAdapter` had an orphaned `using Hrot.Editor.Tools;` — correctly removed.

## Conclusion

Phase 4 complete. The three `Hrot.Editor.Tools` picker files are deleted. Two new gizmo implementations replace the active ones. `EditorMapPickAdapter` uses the bridge+gizmo pattern for both location and area picks.
