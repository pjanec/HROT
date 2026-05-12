# BATCH-28 Review

**Batch:** BATCH-28  
**Reviewer:** Development Lead  
**Date:** 2026-05-12  
**Status:** ⚠️ NEEDS FIXES

---

## Summary

Phase 5 (StandardInteractionTool eradication) is architecturally solid — the god-class is gone, `SelectionInteractionSystem` and `EntityDragGizmo` exist and are wired. However, **3 of the 71 tests actually fail** despite the report claiming 71/71 passed. This is a P1 credibility issue that must be fixed before merge.

---

## Issues Found

### Issue 1: 3 Tests Fail (P1)

The developer reported "71/71 passed" but running `dotnet test Hrot/Engine/Hrot.Presentation.Tests/` produces:

```
Failed:  3, Passed: 68, Total: 71
```

**Failing tests:**

**EDG-001: `UpdateAndDraw_EmitsSphereWithValidPickToken`**  
`EntityDragGizmoTests.cs` line 56  
The test searches for a `DebugPrimitiveShape.Sphere` with a valid entity pick token, but `EntityDragGizmo.UpdateAndDraw` emits a `Box2D` primitive (not a Sphere). Test looks for the wrong shape.  
**Fix:** Update the test to search for `Box2D` instead of `Sphere` (the implementation choice of Box2D for 2D map hit-testing is valid).

**SIS-002: `GizmoInteractionStartedEvent_WithNullEntity_ClearsSelection`**  
`SelectionInteractionSystemTests.cs`  
The test publishes a null-entity `GizmoInteractionStartedEvent` and expects selection to be immediately cleared. The implementation instead starts rubber-band selection (sets `_isBoxSelecting = true`) without clearing selection. The `Assert.False(state.IsSelected)` fails.

**SIS-008: `OnSelectionChanged_FiresWithNull_OnEmptySpaceClick`**  
`SelectionInteractionSystemTests.cs`  
Same root cause as SIS-002. The implementation does not invoke `OnSelectionChanged` on a null entity click (it starts rubber-band instead). The callback sentinel remains non-null.

**Fix for SIS-002 and SIS-008:** The rubber-band implementation is a legitimate design improvement (keep it). Update both tests to match the actual behavior:
- SIS-002: After a null-entity click (rubber-band start), selection is NOT immediately cleared. Test should verify that `_isBoxSelecting` or equivalent state started (or just remove the "clear" assertion and test the commit path instead).
- SIS-008: Update to verify that `OnSelectionChanged` is NOT invoked on rubber-band start, but IS invoked on tiny-drag commit.

---

## Test Quality Assessment

The 65 passing tests (SIS-001, 003-007, EDG-002-006, etc.) are behaviorally correct and verify actual values. The 3 failing tests are not inherently bad tests — they test the right things — they just don't match the implementation after the rubber-band deviation from spec.

---

## Corrective Actions Required

1. Fix `EntityDragGizmoTests.UpdateAndDraw_EmitsSphereWithValidPickToken`: change `DebugPrimitiveShape.Sphere` search to `DebugPrimitiveShape.Box2D` (test must pass).
2. Fix `SelectionInteractionSystemTests.GizmoInteractionStartedEvent_WithNullEntity_ClearsSelection` (SIS-002): adapt to rubber-band behavior — verify `_isBoxSelecting` is true and selection is NOT cleared yet. Or if internal state is private, test the commit path (`GizmoInteractionCommitEvent` with tiny drag → selection clears).
3. Fix `SelectionInteractionSystemTests.OnSelectionChanged_FiresWithNull_OnEmptySpaceClick` (SIS-008): adapt to rubber-band behavior — verify `OnSelectionChanged` fires after a tiny-drag commit (null entity → no drag → commit → clears + fires callback).
4. All 71+ tests must pass before proceeding to Phase 22 tasks.

---

**Next Batch:** BATCH-29 — Corrective fixes (P1) + Phase 22: Composite Gizmo Identity (TASK-GZ064 through TASK-GZ067)
