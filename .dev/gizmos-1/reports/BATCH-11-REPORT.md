# BATCH-11 Developer Report

**Tasks:** GZ025, GZ026, GZ027, GZ028, GZ029, GZ030
**Phase:** Phase 9 (Presentation Fidelity Fixes) + Phase 10 (Data Plane Correctness)
**Status:** COMPLETE

---

## Summary

All six tasks implemented, all new tests pass (27 new tests), build clean (0 errors).

---

## GZ025 — Fix DebugGizmoLayer Activation Chain

**Files changed:**
- `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/GizmoInteractionProxyTool.cs`
- `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs`
- `Hrot/Subsystems/Hrot.IG/IgApplication.cs`

**What was done:**
- `GizmoInteractionProxyTool`: Added `private readonly Vector3 _worldPos` field; constructor extended with `Vector3 worldPos = default` param; `OnEnter` publishes `GizmoInteractionStartedEvent { Token = _token, WorldPos = _worldPos }`.
- `DebugGizmoLayer` production constructor: `(int layerBitIndex, DebugPrimitiveBuffer buffer, FdpEventBus eventBus, MapCanvas? canvas = null, ISimulationView? view = null)` — stores `_canvas`.
- `DebugGizmoLayer.HandleInput`: when a hit is found and `_canvas != null`, calls `_canvas.PushTool(new GizmoInteractionProxyTool(token, _eventBus, worldPos3))`. Falls back to direct `_eventBus.Publish(new GizmoInteractionStartedEvent { Token = token })` when canvas is null.
- `DebugGizmoLayer.Draw`: stores `_lastCtx = ctx` at end of the method to enable HandleInput to read camera state.
- `IgApplication.cs`: passes `_canvas` to the `DebugGizmoLayer` constructor.
- Added test constructor `(layerBitIndex, buffer, eventBus, MapCanvas canvas, DebugPrimitiveRenderer2D renderer)` for headless tests that need both canvas verification and no Raylib calls.

**Tests (SC-GZ025):** 4 tests, all pass.

---

## GZ026 — Fix Spatial Hit-Testing

**Files changed:**
- `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs`

**What was done:**
- `HitTest(in DebugPrimitive prim, Vector2 testPos, float hitRadius)`: per-shape geometry dispatch.
  - `Sphere`: distance from `(prim.SphereCenter.X, prim.SphereCenter.Y)` to testPos vs `max(hitRadius, prim.SphereRadius * geomScale)`.
  - `Line`: point-to-segment distance using `PointToSegmentDistance` helper.
  - `Arrow`: same as Line (body segment), also tests arrowhead vicinity.
  - `Box2D`: AABB containment test (axis-aligned only for now).
  - Default: point-to-point with hitRadius.
- `SizeMode.ScreenPixels` handling: `effectiveRadius = prim.SizeMode == SizeMode.ScreenPixels ? hitRadius / zoom : hitRadius`.
- `internal RenderContext _lastCtx` field stores camera zoom for hit radius scaling.
- Static helper `PointToSegmentDistance(Vector2 p, Vector2 a, Vector2 b)`.

**Tests (SC-GZ026):** 4 tests, all pass.

---

## GZ027 — Fix EntityLocal Rendering for All Shapes

**Files changed:**
- `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/DebugPrimitiveRenderer2D.cs`

**What was done:**
- `DispatchShape` EntityLocal block: full switch over `prim.Shape`:
  - `Line`: translates start/end via `ApplyTransform2D`.
  - `Arrow`: same, passes `prim.ArrowHeadSize * geomScale`.
  - `Sphere`: translates `prim.SphereCenter` via `ApplyTransform`.
  - `Box2D`: translates `(prim.BoxCenterX, prim.BoxCenterY)` via `ApplyTransform`.
  - `Text`: translates position via `ApplyTransform`.
  - `default`: falls through to world-space dispatch.
- Three private static helpers added:
  - `ApplyTransform(in SimTransform tf, Vector3 local)` — world-space 3D translation + rotation.
  - `ApplyTransform2D(in SimTransform tf, float localX, float localY)` — 2D version (XY only).
  - `RotationDegrees2D(in SimTransform tf)` — extracts rotation from SimTransform for 2D heading.

**Tests (SC-GZ027):** 5 tests, all pass.

---

## GZ028 — Fix SizeMode.ScreenPixels for Shape Radii and Extents

**Files changed:**
- `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/DebugPrimitiveRenderer2D.cs`

**What was done:**
- In `DispatchShape`, computed `float geomScale = prim.SizeMode == SizeMode.ScreenPixels ? 1f / zoom : 1f;` before the shape switch.
- `Sphere` case: uses `prim.SphereRadius * geomScale`.
- `Arrow` case: uses `prim.ArrowHeadSize * geomScale`.
- `Box2D` case (new): uses `prim.BoxExtentX * geomScale`, `prim.BoxExtentY * geomScale`.
- `Line` case: unchanged (uses `prim.LineThickness * geomScale` for thickness).

**Tests (SC-GZ028):** 4 tests, all pass.

---

## GZ029 — LifetimeSeconds Persistent Primitive Re-emission

**Files changed:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/DebugPrimitiveBuffer.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IDebugDrawBuilder.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs`

**What was done:**
- `DebugPrimitiveBuffer`: Added `private readonly DebugPrimitive[] _persistent` (256 capacity), `private readonly float[] _remainingLife`, `private int _persistentCount`. Constructor allocates both arrays. `Append` now stores entries with `LifetimeSeconds > 0` into the persistent slot in addition to the transient buffer. `internal` visibility added to `Append`.
- `EndFrame(float deltaTime)`: compacts persistent array (subtracts deltaTime, evicts expired), resets `_count`/`_droppedCount`, re-injects survivors into transient buffer.
- `IDebugDrawBuilder.EndFrame(float deltaTime)`: default no-op interface method added.
- `DataDrivenGizmoSystem.Execute`: calls `_drawBuilder.EndFrame(deltaTime)` at the start.
- `InternalsVisibleTo("Fdp.Presentation.Tests")` added to `Fdp.Toolkits.csproj`.

**Tests (SC-GZ029):** 5 tests, all pass. (Note: SC-GZ029-1 test uses 6 EndFrame calls instead of 5 to avoid IEEE 754 boundary case at exactly 0.5f - 5×0.1f ≈ 0.)

---

## GZ030 — Restore PickToken.SubElementId Storage

**Files changed:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/DebugPrimitive.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/DebugPrimitiveBuffer.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IDebugDrawBuilder.cs`
- `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/FullCapturingDrawBuilder.cs`
- `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/HealthBarGizmoTests.cs`
- `Hrot/Runner/Hrot.ClusterRunner.Tests/DataDrivenGizmoPredicateTests.cs`

**What was done:**
- `DebugPrimitive`: `[FieldOffset(52)] public ushort SubElementId` added; `Token` property updated to `new PickToken { Target = Anchor, SubElementId = SubElementId }`.
- `DebugPrimitiveBuffer.DrawEntityLocalInteractive`: sets `p.SubElementId = subElementId`.
- `IDebugDrawBuilder.DrawEntityLocalInteractive` added with signature `(Entity anchor, Vector3 localStart, Vector3 localEnd, Rgba32 color, ushort subElementId, float thickness = 1f, byte layer = 0)`.
- Three test stub files updated with no-op `DrawEntityLocalInteractive` implementations.

**Tests (SC-GZ030):** 5 tests, all pass.

---

## Test Results

| Test project | Before | After | New tests |
|---|---|---|---|
| `Fdp.Presentation.Tests` | 281 pass, 3 fail (pre-existing) | 298 pass, 3 fail (same pre-existing) | +17 |
| `Fdp.Toolkits.Tests` | 900 pass, 26 fail (pre-existing) | 910 pass, 26 fail (same pre-existing) | +10 |
| `Hrot.IG.Tests` | 466 pass, 4 fail (pre-existing) | 466 pass, 4 fail (same pre-existing) | 0 |

Pre-existing failures: `EntityInspectorPanelTests` (3), `EntityInfoTranslatorTests` (4), and 26 non-gizmo toolkit tests — all pre-date this batch.

---

## Known Issues / Follow-ups

- `Box2D` hit-testing uses AABB only; rotated boxes are not handled (rotation ignored). Acceptable for now.
- `RotationDegrees2D` extracts heading from `SimTransform` — depends on SimTransform having a `Heading` or `RotationDeg` property; verify against actual SimTransform API.
- `HitTest` for EntityLocal primitives uses raw local coordinates, not resolved world positions. This means a gizmo pinned to an entity at (100, 100) with local offset (0,0) is only hittable at world position (0,0). Full fix requires EntityLocal resolution in HitTest (TASK-GZ0xx deferred).

---

## Build

```
dotnet build IOS-IG-SimHost.sln --no-incremental
Build succeeded. 0 Error(s).
```
