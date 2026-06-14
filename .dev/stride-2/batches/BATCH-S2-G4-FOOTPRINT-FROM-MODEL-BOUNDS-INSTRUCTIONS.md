# BATCH-S2-G4 — 2D gizmo footprint from the MODEL/physics-body bounds (not kinematic length)

## Problem (confirmed)
Vehicle gizmos are ~3.5× too big. The footprint `Map2DFootprint` (BATCH-S2-G2) is populated by the TKB
translator from `VehicleParametersDto.Length/Width` (APC = 7×3.5 m, the KINEMATIC size) because the TKB
templates set no `BoxHalfX/Y`. But the actual 3D body AND visible model are derived from the MODEL
bounding box in `BulletPhysicsBodyService.CreateBody` (OrientedBox branch, `bboxParams` →
`useHalfX/useHalfY/useHalfZ`), e.g. "Box2x1x1" ≈ 2×1×1 m. So the gizmo (7×3.5) doesn't match the visible
body (2×1). The footprint must follow the **model-derived box** — the user's "scaled to 3D / physics body
bounds" intent. (Capsule/infantry already uses the 0.3 m radius → correct; only OrientedBox is wrong.)

## Fix — expose the resolved model box and write it into Map2DFootprint

### File 1: `Stride/Hrot.Stride.Core/IPhysicsBodyService.cs` — default-interface accessor (no fake churn)
Add (near the other body-state methods):
```csharp
/// <summary>
/// Returns the resolved 2D footprint (FDP-space meters) the muscle actually built the body with —
/// for OrientedBox bodies this is the MODEL bounding-box extents (BATCH-S2-G4), which match the
/// visible mesh, unlike the kinematic VehicleParametersDto length. Returns false when no resolved
/// footprint is known (e.g. capsule, headless fallback, or a fake). Default no-op for fakes.
/// </summary>
bool TryGetResolvedFootprintMeters(object bodyHandle, out float lengthM, out float widthM)
{
    lengthM = 0f; widthM = 0f; return false;
}
```

### File 2: `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs`
1. Add a per-handle store (near `_bodies`):
```csharp
// BATCH-S2-G4: resolved 2D footprint (FDP meters) for OrientedBox bodies, from the model bbox.
private readonly Dictionary<object, (float lengthM, float widthM)> _resolvedFootprintMeters = new();
```
2. In `CreateBody`, OrientedBox branch, in the `if (bboxParams.HasValue)` (model-derived) path — AFTER the
   body handle is created and stored — record the footprint. Per the SAME FDP↔Stride swizzle the fallback
   documents (Stride.X = FDP.X = length/East; Stride.Z = FDP.Y = width/North; Stride.Y = FDP.Z = up):
```csharp
// BATCH-S2-G4: footprint = model XZ extents (Stride X→FDP length, Stride Z→FDP width).
_resolvedFootprintMeters[handle] = (lengthM: useHalfX * 2f, widthM: useHalfZ * 2f);
Log.Info("[Footprint] entity #{0} model-box footprint L×W = {1:F2}×{2:F2} m (Stride half X={3:F2} Z={4:F2}).",
    entity.Index, useHalfX * 2f, useHalfZ * 2f, useHalfX, useHalfZ);
```
   (Find the local name of the created handle/body in that branch — the method returns a handle; store
   under that exact handle object so `TryGetResolvedFootprintMeters` can look it up. If the handle is
   created at the end of the box branch, place this right after it. Do NOT record for the ShapeDims
   fallback path — only the model-derived path.)
3. Implement the accessor + clean up on RemoveBody:
```csharp
public bool TryGetResolvedFootprintMeters(object bodyHandle, out float lengthM, out float widthM)
{
    if (_resolvedFootprintMeters.TryGetValue(bodyHandle, out var fp)) { lengthM = fp.lengthM; widthM = fp.widthM; return true; }
    lengthM = 0f; widthM = 0f; return false;
}
```
   In `RemoveBody`, also `_resolvedFootprintMeters.Remove(bodyHandle);` (mirror the other per-handle cleanup).
4. In `BulletPhysicsBodyServiceDeferred`, forward it:
```csharp
public bool TryGetResolvedFootprintMeters(object bodyHandle, out float lengthM, out float widthM)
    => Inner.TryGetResolvedFootprintMeters(bodyHandle, out lengthM, out widthM);
```

### File 3: `Stride/Hrot.Stride.Core/PhysicsBodyLifecycleSystem.cs`
In `Execute`, right AFTER `_bodies[entity] = new PhysicsBodyReference(handle, visualRef.ShapeKind, visualRef.Dims);`
(~line 222), overwrite the entity's `Map2DFootprint` length/width from the resolved model box:
```csharp
// BATCH-S2-G4: if the muscle resolved a model-derived footprint (OrientedBox), write it into
// Map2DFootprint so the 2D gizmo matches the visible body (not the kinematic VehicleParams length).
if (_bodyService.TryGetResolvedFootprintMeters(handle, out float fpLen, out float fpWid)
    && view is EntityRepository wrepo
    && wrepo.IsComponentTypeRegistered<Map2DFootprint>())
{
    var shape = wrepo.HasComponent<Map2DFootprint>(entity)
        ? wrepo.GetComponentRO<Map2DFootprint>(entity).Shape
        : GizmoShapeCategory.GroundVehicle; // OrientedBox ⇒ vehicle
    wrepo.SetComponent(entity, new Map2DFootprint { LengthM = fpLen, WidthM = fpWid, Shape = shape });
}
```
- VERIFY: `Execute`'s `view` can be cast to `EntityRepository` for SetComponent (other writes in this file /
  the reverse-sync do `view is not EntityRepository repo` — confirm the pattern and reuse it; if Execute
  already holds a writable repo, use that). Add `using CarKinem.Core;` for `Map2DFootprint`/`GizmoShapeCategory`
  (Hrot.Stride.Core already references CarKinem.Core for VehicleState).

## Constraints
- THREE files. Only the OrientedBox model-derived path produces a resolved footprint; capsule unchanged.
- Default-interface method ⇒ NoOp service + test fakes compile unchanged. Don't add it to fakes.
- The diagnostic `[Footprint] ...` log stays (one line per body create) so we can confirm the L×W values.
- Build the Stride solution; kill HrotStrideApp + rebuild on file lock.

## Acceptance
- Builds clean.
- (User) Vehicle gizmos now match the visible 3D box footprint (~2×1 for the placeholder Box2x1x1), not
  ~7×3.5; adjacent units no longer overlap massively. Infantry unchanged (already correct). The
  `[Footprint]` log prints the L×W used per vehicle (so we can confirm/adjust the length/width axis if
  rotated 90°).
