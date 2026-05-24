# BATCH-11 Review

**Batch:** BATCH-11
**Reviewer:** Development Lead
**Date:** 2026-05-07
**Status:** ✅ APPROVED

---

## Summary

All six tasks (GZ025–GZ030) implemented. 27 new tests added across Fdp.Presentation.Tests (+17)
and Fdp.Toolkits.Tests (+10). Build clean (0 errors). No new failures introduced.

---

## Issues Found

### Issue 1: EntityLocal HitTest uses raw local coordinates (known limitation)

**File:** `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs`
**Problem:** HitTest for EntityLocal primitives uses raw local offsets, not resolved world
positions. A gizmo pinned to entity at (100, 100) with local offset (0,0) is only hittable at
world position (0,0). Developer noted this in the report.
**Impact:** Low — EntityLocal hit testing is untestable without a simulation view in headless
tests anyway. Full fix requires camera-to-world resolution and is deferred.
**Severity:** P3 (technical debt, not a blocker)

### Issue 2: Box2D hit-testing ignores rotation (known limitation)

**File:** `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs`
**Problem:** Rotated Box2D primitives use AABB containment, ignoring rotation.
**Impact:** Negligible — no rotated Box2D gizmos currently in use.
**Severity:** P3

---

## Test Quality Assessment

Tests are behaviorally sound:
- SC-GZ025: Checks `canvas.ActiveTool is GizmoInteractionProxyTool` and that `GizmoInteractionStartedEvent`
  is published with correct `Token.Target` — not just that event count > 0.
- SC-GZ026: Tests actual geometry: line midpoint hit (50,0) vs 10 units beyond endpoint (110,0);
  sphere center at (20,30) with 8-unit radius; zoom-scaled ScreenPixels hit radius.
- SC-GZ029: Verifies persistence by counting frames (6 EndFrame(0.1f) calls to clear 0.5s lifetime,
  avoiding IEEE 754 boundary case). SC-GZ029-3 tests DroppedCount overflow.
- SC-GZ030: PickToken.SubElementId round-trip through DebugPrimitive bytes tested.

---

## 📝 Commit Message

```
feat: presentation fidelity + data plane correctness (BATCH-11)

Completes TASK-GZ025, TASK-GZ026, TASK-GZ027, TASK-GZ028, TASK-GZ029, TASK-GZ030

GZ025: DebugGizmoLayer pushes GizmoInteractionProxyTool via MapCanvas on hit;
  GizmoInteractionProxyTool publishes GizmoInteractionStartedEvent in OnEnter.
  Falls back to direct publish when canvas is null.

GZ026: Geometry-aware hit-testing: Line uses point-to-segment distance,
  Sphere uses radius check, Box2D uses AABB, Arrow tests body segment.
  SizeMode.ScreenPixels hit radius scaled by 1/zoom.

GZ027: EntityLocal rendering dispatches all shapes (Line, Arrow, Sphere, Box2D,
  Text) through ApplyTransform2D; ApplyTransform helper added.

GZ028: SizeMode.ScreenPixels geomScale applied to sphere radius, arrowhead
  size, and box extents.

GZ029: DebugPrimitiveBuffer persistent primitive re-emission: primitives with
  LifetimeSeconds > 0 stored in _persistent[] (256 capacity); EndFrame(dt)
  compacts and re-injects survivors.

GZ030: PickToken.SubElementId restored at FieldOffset(52); Token property
  now includes SubElementId; DrawEntityLocalInteractive adds subElementId param.

Tests: 27 new tests (17 Presentation.Tests + 10 Toolkits.Tests)
```

---

**Next Batch:** BATCH-12 (already completed)
