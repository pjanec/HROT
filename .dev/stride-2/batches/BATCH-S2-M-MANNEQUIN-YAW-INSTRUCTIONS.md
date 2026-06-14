# BATCH-S2-M — Mannequin 3D yaw correction (~90° off)

## Problem
Mannequin (capsule + `Models/mannequinModel`) entities render rotated ~90° to the right in the 3D
Stride view vs the 2D map. Vehicles (OrientedBox primitive) are correct. Root cause: the FDP→Stride
rotation swizzle (`FdpStrideTransform.ToStrideRotation`) is correct and uniform, but the mannequin
MODEL ASSET's authored forward axis differs ~90° from the primitive box's. Needs a per-model visual
yaw correction applied ONLY to the mannequin mesh — NOT to vehicles, NOT to the physics body.

## Scope — ONE FILE
`Stride/HrotStrideApp.Game/StrideVisualFactory.cs`

Do NOT change the `IStrideVisualFactory` interface (it has ~15 test implementors) and do NOT touch
the swizzle, the physics body, or the animation code.

## Change
Apply a per-entity local yaw offset, detected by model ref at creation, composed into the rotation
in BOTH `ApplyPose` (creation) and `UpdatePose` (per-frame). The offset is a single tunable constant.

1. Add fields/consts to `StrideVisualFactory`:
```csharp
// BATCH-S2-M: the mannequin model asset's authored forward axis is ~90° off from the
// primitive box's, so it renders yawed in 3D. Apply a per-model local yaw correction to the
// mannequin VISUAL only (not the physics body, not vehicles).
// TUNE: if the mannequin faces the wrong way after this, flip the sign (e.g. +90) or adjust.
private const float MannequinYawCorrectionDeg = -90f;

// Per-visual-entity local rotation offset (model-forward correction), applied on top of the
// swizzled world rotation. Empty for entities with no correction (e.g. vehicles).
private readonly System.Collections.Generic.Dictionary<global::Stride.Engine.Entity, Stride.Core.Mathematics.Quaternion> _rotationOffsets = new();
```

2. In `CreateModelVisual`, AFTER `var entity = new ...Entity(...)` and BEFORE `ApplyPose(...)`,
   register the offset when the model is the mannequin:
```csharp
// Per-model visual yaw correction (BATCH-S2-M). Mannequin mesh forward is ~90° off.
if (modelRef.IndexOf("mannequin", StringComparison.OrdinalIgnoreCase) >= 0)
{
    _rotationOffsets[entity] = Stride.Core.Mathematics.Quaternion.RotationY(
        Stride.Core.Mathematics.MathUtil.DegreesToRadians(MannequinYawCorrectionDeg));
}
```
   (Add `using System;` if not present — it is, for `Exception`. `Stride.Core.Mathematics` is
   already used.) `CreateProceduralVisual` needs NO change — procedural capsules have no mesh.

3. Make `ApplyPose` an INSTANCE method (remove `static`) so it can read `_rotationOffsets`, and
   compose the offset:
```csharp
private void ApplyPose(
    global::Stride.Engine.Entity entity,
    float scale,
    System.Numerics.Vector3 offsetFdp,
    in SimTransform pose)
{
    var stridePos  = FdpStrideTransform.ToStridePosition(pose.Position);
    var strideOff  = FdpStrideTransform.ToStridePosition(offsetFdp);
    var strideRot  = FdpStrideTransform.ToStrideRotation(pose.Rotation);

    entity.Transform.Position = stridePos + strideOff;
    entity.Transform.Rotation = ComposeWithOffset(entity, strideRot);
}
```

4. Update `UpdatePose` to compose the same offset:
```csharp
entity.Transform.Position = FdpStrideTransform.ToStridePosition(pose.Position);
entity.Transform.Rotation = ComposeWithOffset(entity, FdpStrideTransform.ToStrideRotation(pose.Rotation));
```

5. Add the helper:
```csharp
/// <summary>
/// Composes the swizzled world rotation with this entity's per-model local yaw correction
/// (BATCH-S2-M). Returns <paramref name="worldRot"/> unchanged when no offset is registered.
/// </summary>
private Stride.Core.Mathematics.Quaternion ComposeWithOffset(
    global::Stride.Engine.Entity entity, Stride.Core.Mathematics.Quaternion worldRot)
{
    if (_rotationOffsets.TryGetValue(entity, out var offset))
        return worldRot * offset;   // apply local model-forward correction, then world orientation
    return worldRot;
}
```
   NOTE on composition order: `worldRot * offset` applies the local correction in the model's own
   frame first, then the world rotation — the intended semantics for a fixed mesh-forward fix. If
   visual testing shows the correction rotates with the world incorrectly, switch to `offset * worldRot`.

6. In `Destroy`, remove the entry to avoid leaking entries:
```csharp
_rotationOffsets.Remove(entity);
```
   (place it after the `if (visualHandle is not ... entity) return;` guard, before/after the scene
   removal — order doesn't matter as long as it runs for the entity).

## Constraints
- One file. No interface change. No swizzle/physics/animation change.
- Vehicles and all non-mannequin models must be completely unaffected (offset dict empty for them →
  `ComposeWithOffset` returns the world rotation unchanged).

## Acceptance
- Builds clean (`Stride/HrotStrideApp.sln`).
- (Lead/user verifies visually — the harness cannot validate 3D yaw.) The mannequin should face the
  same direction the 2D map shows; vehicles unchanged. Sign/magnitude tunable via
  `MannequinYawCorrectionDeg`.
