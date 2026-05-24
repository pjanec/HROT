# BATCH-25 Review

**Batch:** BATCH-25
**Reviewer:** Development Lead
**Date:** 2025-07-17
**Status:** APPROVED

---

## Summary

All 4 legacy tool files deleted, 6 new gizmo files created, ECS wiring in `IgApplication` and `EditorSubsystem` updated correctly. Build: 0 errors. All 9 new gizmo tests pass. No regressions in `Hrot.IG.Tests` (68 pre-existing failures confirmed identical to HEAD baseline).

---

## Issues Found

### Issue 1: Stale XML doc in `IgApplication.cs`

**File:** `Hrot/Subsystems/Hrot.IG/IgApplication.cs` (Lines 3114-3119)
**Problem:** Comment block and XML `<summary>` doc still say "EditTool" and "activates the EditTool". The method body no longer does this -- it calls `ActivateAreaEditingTool()` which uses the gizmo marker approach.
**Priority:** P3
**Fix:** Update the comment header and XML summary to describe the current behavior (activates `VertexEditGizmo` via `ActiveVertexEditRequest` marker).

---

## Test Quality Assessment

Tests are meaningful and verify actual behavior:

- **VEG-001/VEG-002**: Read back `EditablePolyline.Points` from ECS after commit and check specific index values and total count -- actual correctness verified.
- **VEG-003**: Verifies ECS is unchanged after drag+cancel; implicitly validates OnCancel does not call `WriteBackAndPublish` with dragged data.
- **VEG-004/VEG-005**: Verify menu-action-triggered insert/delete by checking `Points.Count` against ECS.
- **RWG-001/RWG-002/RWG-003**: Verify `SelectedVertexIndex`, ECS position (X and Z with precision), and revert behavior.
- **RWG-004**: Verifies singleton `Current` lifecycle.
- **WaypointEditorPanelTests**: Tests caching logic with `ReferenceEquals` check (buffer not re-allocated when index unchanged) and transitions on null state.

Minor note (P3, not a blocker): VEG-003 and RWG-003 `OnCancel` tests verify ECS unchanged (correct), but cannot directly assert in-memory revert since ECS is only written on commit. Coverage is sufficient for phase-2 scope.

---

## Verdict

**Status: APPROVED**

All requirements met. Ready to commit.

---

## Commit Message

```
refactor: replace EditTool + RouteEditTool with VertexEditGizmo + RouteWaypointGizmo (BATCH-25)

Completes Phase 2 of the gizmo migration initiative.

- Deleted EditTool, EditToolConstants, RouteEditTool, RouteEditToolConstants
- Deleted EditToolTests, RouteEditToolTests
- Added ActiveVertexEditRequest (id=187) and ActiveRouteEditRequest (id=188) marker components
- Added VertexEditGizmo, VertexEditGizmoDefinition (non-exclusive focus, SubElementId-based drag)
- Added RouteWaypointGizmo, RouteWaypointGizmoDefinition (non-exclusive focus; exposes Current singleton)
- Added IRouteWaypointEditorState interface; WaypointEditorPanel now takes Func<IRouteWaypointEditorState?>
- Wired both gizmos into EditorSubsystem and IgApplication (marker registration, gizmo registry, DataDrivenGizmoSystem)
- Rewrote ActivateAreaEditingTool() and TestHook_ActivateRouteEditToolForNetworkId to use ECS marker toggle

Tests: 9 new tests (VEG-001..005, RWG-001..004); Hrot.Presentation.Tests 51/51 pass; no regressions.
```

---

**Next Batch:** BATCH-26 (Phase 3 — migrate CreationTool, AreaPlacementTool, RoutePlacementTool, ObstaclePlacementTool to IEntityStatefulGizmo with exclusive focus)
