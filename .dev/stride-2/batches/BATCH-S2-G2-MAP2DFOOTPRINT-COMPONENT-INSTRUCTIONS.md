# BATCH-S2-G2 — Map2DFootprint component, populated from TKB shape dims (all shapes)

## Goal
Give EVERY entity a neutral 2D footprint (length, width, shape category) so the 2D gizmo renderer can
draw a correctly-sized, correctly-shaped marker WITHOUT touching 3D/physics types and WITHOUT depending
on DIS-type template data (the TKB templates currently don't set DisType, so the DIS-type path yields the
magenta fallback for everything). The category is derived from the physics `CollisionShapeKind`
(Capsule→Humanoid, OrientedBox→GroundVehicle), which IS set per template.

This is the data layer only. G3 makes the renderer consume it.

## Part 1 — the component + enum
Add a small, dependency-light component. PLACEMENT IS A DECISION — verify and choose the assembly that
BOTH (a) the translator project `Fdp.Toolkits` (CarKinem) AND (b) the gizmo emitter
`Hrot/Engine/Hrot.Presentation` (EntityPresentationGizmoShared) already reference, to avoid new/circular
deps. `VehicleParams` lives in CarKinem.Core and IS read by the gizmo emitter today — so the same
assembly as `VehicleParams` (or a lower shared one like `Fdp.Core`/`Fdp.Toolkits.Spatial`) is a safe home.
Confirm by checking the existing references before placing it. Report the chosen assembly + why.

```csharp
/// <summary>Shape category for the 2D map gizmo (BATCH-S2-G2). Independent of 3D — drives which
/// symbolic profile the 2D renderer draws. Derived from the physics CollisionShapeKind (and/or DIS
/// type when available).</summary>
public enum GizmoShapeCategory : byte { Unknown = 0, Humanoid = 1, GroundVehicle = 2, FixedWing = 3, RotaryWing = 4 }

/// <summary>Neutral 2D map footprint (BATCH-S2-G2): real-world length/width in METERS plus a shape
/// category. Written by the TKB translator from the entity's shape dims; read by the 2D gizmo renderer.
/// Carries NO 3D/physics types — pure data, so the generic renderer stays 3D-agnostic.</summary>
public struct Map2DFootprint
{
    public float LengthM;            // meters, along the entity's forward (X) extent
    public float WidthM;             // meters, lateral (Y) extent
    public GizmoShapeCategory Shape; // symbolic profile selector
}
```
Register the component type wherever the other gameplay components are registered for the editor world
(grep how `VehicleParams`/`CrowdMotorIntent` get `IsComponentTypeRegistered` true — match that path).

## Part 2 — populate it in the TKB translator
`FDP/Toolkits/Fdp.Toolkits/CarKinem/Tkb/VehicleKinematicsTkbTranslator.cs` — in `Inject(...)` (~line 45),
which already reads `VehicleParametersDto` AND `StrideRenderModelDefDto`. Compute and attach
`Map2DFootprint` for ALL entities it processes (NOT just OrientedBox). Mirror the proven "0→default"
resolution in `StrideVisualBindingSystem.ResolveShapeDims` (Stride/Hrot.Stride.Core/StrideVisualBindingSystem.cs:282):

```
shapeKind = def.ShapeKind
if OrientedBox:
    length = 2 * (def.BoxHalfX != 0 ? def.BoxHalfX : vehicleDto.Length / 2)
    width  = 2 * (def.BoxHalfY != 0 ? def.BoxHalfY : vehicleDto.Width  / 2)
    category = GroundVehicle
if Capsule:
    r = def.ShapeRadius != 0 ? def.ShapeRadius : (PhysicsCollider.Radius if present else 0.3f)
    length = width = 2 * r
    category = Humanoid
else (Sphere/other):
    use radius-equivalent; category = Unknown
```
- VERIFY the exact field names on `StrideRenderModelDefDto` (ShapeKind/ShapeRadius/ShapeHeight/BoxHalfX/Y/Z)
  and `CollisionShapeKind` enum values. If the translator doesn't already fetch `StrideRenderModelDefDto`
  from the template, fetch it the same way it fetches `VehicleParametersDto` (and skip footprint if absent,
  defaulting category from any available signal).
- Only WRITE the component (AddComponent/SetComponent) if its type is registered, mirroring the guards
  other translators use (e.g. `if (repo.IsComponentTypeRegistered<Map2DFootprint>()) ...`).
- This translator is gated on consuming `VehicleParametersDto` — confirm BOTH current entities (TKB 2001
  vehicle, 2002 infantry) carry it (memory says 2002 does). If an entity type lacks it, the footprint is
  simply not written (acceptable for now; G3 falls back to a category default). Note this limitation in the
  report.

## Constraints
- Component + enum (Part 1) and the translator population (Part 2) ONLY. Do NOT change the renderer/gizmo
  emitter (that's G3). Do NOT change the spawn pipeline.
- No 3D/physics/Stride types in the component. `CollisionShapeKind` is a TKB-domain enum (Fdp.Toolkit.Tkb.Domain)
  — fine to READ in the translator, but the component stores only the resulting `GizmoShapeCategory` + floats.
- Build the full solution that covers both Fdp.Toolkits and HrotStrideApp.

## Acceptance
- Builds clean.
- (Verified by G3 / a quick log) Spawned infantry (2002) and vehicles (2001) carry a `Map2DFootprint` with
  sensible LengthM/WidthM and category Humanoid / GroundVehicle respectively.
- Report: chosen assembly for the component + why, the StrideRenderModelDefDto field names used, and whether
  both TKB types got the footprint.
