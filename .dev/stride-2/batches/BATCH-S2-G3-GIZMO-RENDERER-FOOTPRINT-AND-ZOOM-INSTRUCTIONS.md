# BATCH-S2-G3 — 2D gizmo renderer: consume Map2DFootprint (size + shape) + 1A zoom floor

## Goal
Make the 2D map gizmo use the `Map2DFootprint` component (BATCH-S2-G2) so every entity draws at its
real footprint with the correct symbolic shape, and apply the agreed 1A size rule so it stays visible
when zoomed out. This removes the 5 m magenta fallback as the common case.

## Background (verify before editing)
- `Map2DFootprint { float LengthM; float WidthM; GizmoShapeCategory Shape }` is in `CarKinem.Core`
  (Fdp.Toolkits), readable by the gizmo emitter (Hrot.Presentation already references it).
- Today the emitter `EntityPresentationGizmoShared.DrawSemanticShape`
  (Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/EntityPresentationGizmoShared.cs) gets length/width
  from `TryGetVehicleDimensions` (VehicleParams — vehicle-only → 0 for infantry) and the shape PROFILE from
  `ResolveProfileId` (DIS type — currently 0 → `_fallback` → magenta). The renderer
  `GizmoMap.Presentation/Rendering/DebugPrimitiveRenderer2D.DispatchShape` SemanticShape branch
  (~lines 335-371) draws via `PerspectiveShapeRenderer.RenderShape(...)` with `zoom` already in scope.

## Part 1 — emitter reads Map2DFootprint for SIZE + SHAPE
File: `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/EntityPresentationGizmoShared.cs`
(and confirm which emitter the EDITOR 2D map actually uses — if the editor uses `SimHostEntityPresentationGizmo`
or another caller of `DrawSemanticShape`, make the shared change so both benefit; report the call graph).

1. When the entity has a `Map2DFootprint`, use it as the authoritative source:
   - `lengthMeters = footprint.LengthM`, `widthMeters = footprint.WidthM` (fall back to
     `TryGetVehicleDimensions` then the existing default ONLY when no footprint).
   - shape PROFILE from `footprint.Shape` → profile name: Humanoid→"humanoid", GroundVehicle→"ground_vehicle",
     FixedWing→"fixed_wing", RotaryWing→"rotary_wing", Unknown→(keep DIS-type/default resolution).
2. VERIFY exactly how the profile reaches the renderer: does `DrawSemanticShape` pass a profile NAME / id on
   the `SemanticShape` primitive (DebugPrimitive), or does the renderer resolve it later from DIS type? Trace
   `MakeSemanticShape` / the primitive fields and `DefaultEntityShapeLibrary.GetShape(shapeName, disType)`.
   Set the profile name from `footprint.Shape` so `GetShape` returns the humanoid/vehicle profile (NOT
   `_fallback`). Keep the DIS-type path as the fallback when there is no footprint.
3. Guard with `IsComponentTypeRegistered<Map2DFootprint>()` + `HasComponent`. When absent, behavior is
   exactly as today (no regression for entities without a footprint).

## Part 2 — renderer applies the 1A zoom floor
File: `FDP/ExtDeps/GizmoMap/GizmoMap.Presentation/Rendering/DebugPrimitiveRenderer2D.cs`, the
`case DebugPrimitiveShape.SemanticShape:` branch (~lines 335-371), where `prim.LengthMeters`,
`prim.WidthMeters` and `zoom` (pixels/meter) are in scope.

Apply: clamp the ON-SCREEN size to a minimum pixel floor, then convert back to meters for the renderer
(which draws in world meters under the Camera2D zoom):
```csharp
const float MinPixelFloor = 12f; // readable minimum on screen
float lenPx = prim.LengthMeters * zoom;
float widPx = prim.WidthMeters  * zoom;
float scale = 1f;
float maxPx = MathF.Max(lenPx, widPx);
if (maxPx > 0f && maxPx < MinPixelFloor) scale = MinPixelFloor / maxPx; // uniform up-scale to the floor
float drawLen = prim.LengthMeters * scale;
float drawWid = prim.WidthMeters  * scale;
```
Pass `drawLen`/`drawWid` to `PerspectiveShapeRenderer.RenderShape(...)` instead of the raw meters. Uniform
`scale` preserves aspect ratio and the perspective distortion. Above the floor (`scale==1`) it's exactly
true-to-scale. Keep the existing 5 m fallback ONLY for primitives with zero/again-missing dims.
- VERIFY the exact param names the SemanticShape branch passes to RenderShape (the earlier investigation
  named `len`/`wid` from `prim.LengthMeters>0?...:5f`). Apply the floor to those, keeping the `>0?...:5f`
  guard as the no-data fallback.

## Part 3 (optional, low-risk) — min click radius
If the entity pick/selection hit-test in the 2D map uses the gizmo footprint, ensure a minimum pickable
size (the existing pick box is `MakeBox2D(... 8f,8f ...)` per prior investigation — confirm it already
provides a minimum; if it scales with the footprint and could go sub-pixel, clamp it to a min meters
value equivalent to ~16 px at current zoom). If the pick box is already a fixed size, do nothing here and
note it.

## Constraints
- Emitter (Part 1) + renderer floor (Part 2) [+ optional Part 3]. Do NOT change the component/translator
  (G2) or the spawn pipeline. Keep current `PerspectiveShapeRenderer` + profiles (no new shapes/MIL-STD).
- No regression for entities without `Map2DFootprint` (fall back to today's behavior).
- Build the full solution (Hrot.Presentation + GizmoMap + HrotStrideApp).

## Acceptance
- Builds clean.
- (User) Infantry render as a small HUMANOID profile at its real ~0.6 m footprint; vehicles as a
  vehicle-box at ~7 m — no more 5 m magenta boxes, no 90% overlap for adjacent units. Zooming OUT, markers
  shrink to a readable floor and stop (stay visible). Zooming IN, they're true-to-scale.
- Report the emitter call graph (which gizmo the editor uses), how the profile name flows to the renderer,
  and the exact RenderShape params changed.
