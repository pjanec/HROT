# BATCH-S2-Y — Mannequin faces its movement direction (owned kinematic character yaw)

## Problem (root cause already established)
A walking mannequin keeps a constant facing. Owned (editor) entities' VISUAL rotation is the
Bullet body's Stride entity transform (`entry.StrideEntity.Transform.Rotation`), NOT the
StrideVisualFactory.UpdatePose path (that's ghosts only — so the earlier −90° UpdatePose fix S2-M
is dead code for owned mannequins). The CharacterComponent (kinematic capsule) controls POSITION
only; nothing ever sets its yaw, so it never turns. `GetBodyState` reads
`entry.StrideEntity.Transform.Rotation`, and `BulletReverseSyncSystem` writes that to
`SimTransform.Rotation` — so if we set the entity rotation in the motor it flows through cleanly.

Vehicles already face correctly (dynamic body steered via SetYawRate) — this is CHARACTER-ONLY.

## Fix — turn the character entity to face its horizontal velocity, each frame, smoothed.

### File 1: `Stride/Hrot.Stride.Core/IPhysicsBodyService.cs` — add a DEFAULT interface method
Add, in the "Character motor" region (after `SetCharacterVelocity`), a **default** interface method
so existing implementers/test fakes need NO change (only the real service overrides it):
```csharp
/// <summary>
/// Sets the world-space ORIENTATION of a Bullet <c>CharacterComponent</c> body so the
/// rendered mannequin faces a chosen direction (BATCH-S2-Y). The kinematic character
/// controller owns POSITION only; its yaw is free, so the motor turns it to face travel.
/// In the concrete service this sets <c>entry.StrideEntity.Transform.Rotation</c> — which
/// <c>GetBodyState</c> reads back and <c>BulletReverseSyncSystem</c> writes to SimTransform.
/// Default no-op so headless fakes are unaffected.
/// </summary>
/// <param name="bodyHandle">Handle of a body created with <c>CollisionShapeKind.Capsule</c>.</param>
/// <param name="strideRotation">Desired orientation in Stride world space (Y-up, left-handed).</param>
void SetCharacterFacing(object bodyHandle, SMath.Quaternion strideRotation) { }
```
(Default-method body `{ }` requires nothing else; `SMath` alias already in this file.)

### File 2: `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs` — implement it
Add (near `SetCharacterVelocity`, ~line 662). Only act on CharacterComponent bodies; set the entity
rotation and push it into the native transform so it sticks (mirror the reposition path which uses
`UpdatePhysicsTransformation(true)` for characters):
```csharp
/// <inheritdoc/>
public void SetCharacterFacing(object bodyHandle, SMath.Quaternion strideRotation)
{
    if (bodyHandle is SkippedBodyHandle) return;
    if (!_bodies.TryGetValue(bodyHandle, out var entry)) return;
    if (entry.PhysicsComponent is CharacterComponent ch)
    {
        entry.StrideEntity.Transform.Rotation = strideRotation;
        entry.StrideEntity.Transform.UpdateWorldMatrix();
        try { ch.UpdatePhysicsTransformation(true); } // push rotation into the native ghost
        catch (Exception) { /* controller not yet initialised — entity transform is set */ }
    }
}
```
ALSO forward it in the deferred wrapper `BulletPhysicsBodyServiceDeferred` (~line 1513, next to its
`SetCharacterVelocity` forward):
```csharp
public void SetCharacterFacing(object bodyHandle, SMath.Quaternion strideRotation)
    => Inner.SetCharacterFacing(bodyHandle, strideRotation);
```

### File 3: `Stride/Hrot.Stride.Core/BulletCharacterMotor.cs` — turn toward velocity
In `Execute`, in the simRunning drive path, AFTER `SetCharacterVelocity` is called, turn the body to
face the horizontal movement direction — but only when it is actually moving, and slerp for smoothness
so it doesn't snap. Add tunable fields and the logic.

Add fields near the multipliers:
```csharp
/// <summary>Min horizontal FDP speed (m/s) before the character turns to face travel.
/// Below this it keeps its current facing (so a stopped mannequin doesn't snap to a default).</summary>
public float FacingMinSpeed { get; set; } = 0.10f;

/// <summary>Model-forward yaw correction (degrees), added to the travel heading. The mannequin
/// model's local forward is not FDP +East at yaw 0; this aligns it. Matches the known mannequin
/// correction (StrideVisualFactory.MannequinYawCorrectionDeg = −90). Tune if facing is off.</summary>
public float FacingYawOffsetDeg { get; set; } = -90f;

/// <summary>Per-frame slerp factor toward the target facing (0..1). ~0.20 gives a smooth turn.</summary>
public float FacingTurnLerp { get; set; } = 0.20f;
```

After the `_bodyService.SetCharacterVelocity(bodyRef.BodyHandle, strideVelocity);` line (and before /
after the PostCollision channel write — order doesn't matter), add:
```csharp
// ── Face the direction of travel (BATCH-S2-Y) ─────────────────────────────
// Owned mannequin visual rotation = the body entity transform; the kinematic controller
// never turns on its own. Turn it to face horizontal velocity, smoothed, when moving.
// FDP horizontal plane is X=East, Y=North (Z=up). Heading yaw is about FDP up (Z).
float hx = scaledFdpVelocity.X, hy = scaledFdpVelocity.Y;
float horizSpeed = MathF.Sqrt(hx * hx + hy * hy);
if (horizSpeed >= FacingMinSpeed)
{
    float headingRad = MathF.Atan2(hy, hx);                  // FDP yaw about Z (up)
    float yawRad     = headingRad + FacingYawOffsetDeg * (MathF.PI / 180f);
    var fdpFacing    = System.Numerics.Quaternion.CreateFromAxisAngle(
                           new Vector3(0f, 0f, 1f), yawRad); // FDP Z = up
    var targetStride = FdpStrideTransform.ToStrideRotation(fdpFacing);

    // Slerp from the current body orientation for a smooth turn.
    var curStride = _bodyService.GetBodyState(bodyRef.BodyHandle).Rotation;
    var nextStride = SMath.Quaternion.Slerp(curStride, targetStride, FacingTurnLerp);
    _bodyService.SetCharacterFacing(bodyRef.BodyHandle, nextStride);
}
```
Notes:
- `Vector3` here is `System.Numerics.Vector3` (already `using System.Numerics;` at top). `scaledFdpVelocity`
  is `System.Numerics.Vector3`. `SMath` = `Stride.Core.Mathematics` (already aliased). `SMath.Quaternion.Slerp`
  exists in Stride.Core.Mathematics. `FdpStrideTransform.ToStrideRotation` takes a `System.Numerics.Quaternion`.
- Do NOT add facing in the `!simRunning` (frozen) branch — a paused mannequin must not turn.
- Keep everything else (velocity, stance, PostCollision channel, jump) unchanged.

## Constraints
- THREE files exactly: IPhysicsBodyService.cs (default method), BulletPhysicsBodyService.cs (impl +
  deferred forward), BulletCharacterMotor.cs (facing logic). Do NOT touch the reverse-sync, reposition,
  vehicle paths, or test fakes.
- The default interface method means NoOpPhysicsBodyService and all recording test fakes compile
  unchanged — verify you did NOT add the method to any fake.
- Verify `SMath.Quaternion.Slerp(Quaternion, Quaternion, float)` is the correct signature in
  Stride.Core.Mathematics (it is static). If the static is named differently, use the correct one;
  do NOT invent an API.

## Acceptance
- Builds clean (`Stride/HrotStrideApp.sln`).
- (User, time RUNNING) RMB-move a mannequin → it TURNS to face its walking direction and walks facing
  forward; it turns smoothly (no snap) and keeps its facing when it stops. If the facing is rotated a
  constant amount (e.g. 90° or 180° off), that's a one-line `FacingYawOffsetDeg` tune — report the
  observed offset.
- Vehicle facing/movement unchanged.
