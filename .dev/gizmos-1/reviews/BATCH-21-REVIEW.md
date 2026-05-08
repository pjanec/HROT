# BATCH-21 Review

**Reviewer:** Dev Lead
**Date:** 2025-08-02
**Status:** APPROVED (after dev-lead corrections)

---

## Build & Functional Verification

- Build: **0 errors** (0 new warnings introduced)
- SimHost gizmo tests: 4/4 passed
- IG gizmo tests: 51/51 passed (49 pre-existing + 2 added during review)
- Contracts tests: 17/17 passed

Implementation quality is high. The gizmo logic correctly mirrors the legacy render layers
(coordinate conventions, loop handling, culling gate, conditionMask formula).
The pre-existing bug fixes (`SC_GZ015_2`, registrar tests) are correct.

---

## Corrections Applied During Review (dev-lead self-corrected)

### C-1: SC_GZ057_3 — added `CoordinateSpace.EntityLocal` assertion (CRITICAL)

The `SemanticShape → SpatialAnchor` linking mechanism depends on `Space == EntityLocal`.
Added `Assert.Equal(CoordinateSpace.EntityLocal, semantic.Space)` to
`SC_GZ057_3_Draw_EmitsSemanticShapeWithMatchingAnchorIndex` in `SimHostEntityPresentationGizmoTests`.

### C-2: SC_GZ057_7 — new test for IgEntityPresentationGizmo conditionMask (HIGH)

`SC_GZ057_7_IgGizmo_WithHighDamage_SetsDamagedConditionMask` added to `PresentationGizmoTests`.
Creates entity with `IgHealthState.Damage = 75f`, verifies Damaged bit set, Immobile bit not set.

### C-3: SC_GZ058_5 — new MapOverlayGizmo test (HIGH)

`SC_GZ058_5_MapOverlayGizmo_EmitsLinesForOpenPolyline` added to `PresentationGizmoTests`.
Creates entity with `EditablePolyline` (3 points, IsClosed=false), verifies 2 segments emitted.

All 3 corrections verified to pass (51/51 IG gizmo tests green).

---

## Non-Blocking Observations (deferred)

### NB-1: SC_GZ058_3 does not verify waypoint coordinates

The RouteGizmo test verifies line count but not the actual coordinate values.
Given the critical `Z=North` coordinate convention, coordinate assertions would
increase confidence. Deferred — acceptable for this batch.

### NB-2: SC_GZ058_2 does not verify SizeMode

`FullCapturingDrawBuilder.LineCalls` does not capture `SizeMode`. Extending the helper
to track it is a lower-priority improvement. Deferred.

### NB-3: CS8602 nullable warnings in CgfSubsystem

Pre-existing pattern; not introduced by this batch.

---

## Approved (all production code — no changes needed)

- `IDebugDrawBuilder` default method additions
- `DebugPrimitiveBuffer.DrawSpatialAnchor` / `DrawSemanticShape` implementations
- `SimHostEntityPresentationGizmo`, `CgfEntityPresentationGizmo`, `IgEntityPresentationGizmo`
- `EffectPresentationGizmo`, `RouteGizmo`, `MapOverlayGizmo`, `MissionPresentationGizmo`
- All composition root wiring (SimHostApp, IgApplication, CgfSubsystem, GizmoRegistrar)
- Pre-existing bug fixes (`SC_GZ015_2`, registrar test component registrations)


---

## Build & Functional Verification

- Build: **0 errors** (0 new warnings introduced)
- SimHost gizmo tests: 4/4 passed
- IG gizmo tests: 49/49 passed
- Contracts tests: 17/17 passed

Implementation quality is high. The gizmo logic correctly mirrors the legacy render layers
(coordinate conventions, loop handling, culling gate, conditionMask formula).
The pre-existing bug fixes (`SC_GZ015_2`, registrar tests) are correct.

---

## Required Changes

### RQ-1: SC_GZ057_3 must verify `CoordinateSpace.EntityLocal` (CRITICAL)

The `SemanticShape → SpatialAnchor` linking mechanism works ONLY when
`SemanticShape.Space == CoordinateSpace.EntityLocal`. The `DebugPrimitiveRenderer2D`
resolves entity-local coordinates by keying on this field. The instructions
explicitly required this assertion:

> "assert `Shape == SemanticShape`, `Space == EntityLocal`, `AnchorIndex == 42`"

The current test only checks `Shape` and `AnchorIndex`. Add:

```csharp
Assert.Equal(CoordinateSpace.EntityLocal, semantic.Space);
```

to `SC_GZ057_3_Draw_EmitsSemanticShapeWithMatchingAnchorIndex`.

### RQ-2: Add test for IgEntityPresentationGizmo conditionMask computation (HIGH)

The DESIGN spec (GZ057 execution step 4) says: "Evaluate condition masks locally
(e.g., `IgHealthState` or `ActorCapabilityState`) and pack the result into the
`ConditionMask` payload."

The implementation is correct (Damage >= 50f → `Damaged`, Damage >= 90f → `Immobile`),
but it is not tested. Add a test to `PresentationGizmoTests.cs`:

```
SC_GZ057_7_IgGizmo_WithHighDamage_SetsDamagedConditionMask
```

Create an entity with `CullingState { IsVisible = true }`, `NetworkIdentity(1L)`,
`SimTransform`, `IgHealthState { Damage = 75f }`, draw via `DebugPrimitiveBuffer`,
check that `frame[1].ConditionMask` has the Damaged bit set and not the Immobile bit.

### RQ-3: Add MapOverlayGizmo test (HIGH)

Success condition SC-GZ058-1 says: "All tactical graphics, routes, and effects render
successfully through the DebugPrimitiveBuffer stream."

Routes are tested (SC_GZ058_3), effects are tested (SC_GZ058_1 and _2), but
MapOverlayGizmo has zero test coverage. Add at minimum:

```
SC_GZ058_5_MapOverlayGizmo_EmitsLinesForPolyline
```

Create an entity with `SimTransform`, `MapOverlayStyle`, `EditablePolyline` with
3 points, draw via `FullCapturingDrawBuilder`, assert 2 line segments emitted
(IsClosed=false → N-1 segments for N points).

---

## Non-Blocking Observations (fix-if-easy, else defer)

### NB-1: SC_GZ058_3 does not verify waypoint coordinates

The RouteGizmo test verifies line count but not the actual coordinate values.
Given the critical `Z=North` coordinate convention, a coordinate assertion would
substantially increase confidence. Consider adding:

```csharp
Assert.Equal(0f,  draw.LineCalls[0].Start.X);
Assert.Equal(0f,  draw.LineCalls[0].Start.Y);  // Z=North maps to canvas Y
Assert.Equal(10f, draw.LineCalls[0].End.X);
Assert.Equal(20f, draw.LineCalls[0].End.Y);    // waypoint[1].Position.Z
```

### NB-2: SC_GZ058_2 does not verify SizeMode

`FullCapturingDrawBuilder.LineCalls` stores `(Start, End, Color)` but not `SizeMode`.
The tracer correctly uses `SizeMode.ScreenPixels` in the implementation, but it is
not asserted. Since `FullCapturingDrawBuilder` would need extending to capture
`SizeMode`, this is a lower-priority fix — defer or handle in a follow-up batch.

### NB-3: CS8602 nullable warnings in CgfSubsystem

Noted in the report; pre-existing pattern. Not introduced by this batch.

---

## Approved Parts

The following are fully correct and approved without changes:

- `IDebugDrawBuilder` default method additions (default no-op bodies, correct signatures)
- `DebugPrimitiveBuffer.DrawSpatialAnchor` / `DrawSemanticShape` implementations
- `SimHostEntityPresentationGizmo` (quaternion heading, VehicleParams optional)
- `CgfEntityPresentationGizmo` (NetworkTransform fallback)
- `IgEntityPresentationGizmo` (CullingState gate, conditionMask formula)
- `EffectPresentationGizmo` (Explosion → Sphere, Tracer → Line with ScreenPixels)
- `RouteGizmo` (TkbType filter, Z=North convention, loop handling)
- `MapOverlayGizmo` (origin offset, IsClosed polygon closing)
- `MissionPresentationGizmo` (no [GizmoProjector], constructor injection, JSON parsing)
- All composition root wiring (SimHostApp, IgApplication, CgfSubsystem, GizmoRegistrar)
- Pre-existing bug fixes (SC_GZ015_2 size assertion, registrar test component registrations)
- SC_GZ057_1 through SC_GZ057_2, SC_GZ057_4 through SC_GZ057_6
- SC_GZ058_1, SC_GZ058_2, SC_GZ058_3, SC_GZ058_4

---

## Summary

The required changes are targeted and minimal:
1. Add one `Assert.Equal(CoordinateSpace.EntityLocal, ...)` line to SC_GZ057_3.
2. Add one new test for conditionMask (SC_GZ057_7).
3. Add one new test for MapOverlayGizmo (SC_GZ058_5).

No production code changes are required — all changes are in test files only.
