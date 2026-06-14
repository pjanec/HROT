# BATCH-S2-V — Make a dragged mannequin STAY where dropped (kinematic character reposition)

## Problem (root cause found)
Dragging a mannequin (kinematic CharacterComponent) and dropping it makes it JUMP BACK to its
original position; the vehicle (dynamic RigidbodyComponent) stays. In
`BulletPhysicsBodyService.SyncBodyToExternalPose`, the vehicle branch calls
`rb.UpdatePhysicsTransformation(true)` (pushes the entity transform into the native Bullet body) and
STICKS. The character branch only sets `entity.Transform` + `ch.Teleport(newPos)`, and
`CharacterComponent.Teleport` does NOT move the kinematic controller's internal position in
Stride.Physics 4.2.1.2487 — so the next reverse-sync reads the un-moved body pose and overwrites
SimTransform back to the original position (snap-back).

`UpdatePhysicsTransformation(bool)` is a method on the BASE `PhysicsComponent` (confirmed present in
Stride.Physics 4.2.1.2487), so the CharacterComponent has it too — it pushes
`entity.Transform` → the native collider/ghost world transform, which is what the kinematic
character controller uses as its position. The existing comment claiming "we cannot use
UpdatePhysicsTransformation" for characters is an untested assumption.

## Fix — ONE FILE
`Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs` — in the CharacterComponent branch of
`SyncBodyToExternalPose` (~lines 1104-1128), ALSO call `ch.UpdatePhysicsTransformation(true)` after
setting the entity transform (in addition to `ch.Teleport(newPos)`), so the native ghost/controller
position is moved like the vehicle's body. Update the misleading comment.

Replace the character branch body (keep the `ch.Simulation == null` guard and the log) so it reads:
```csharp
if (entry.PhysicsComponent is CharacterComponent ch)
{
    // ── CharacterComponent (capsule / mannequin) path ─────────────────
    // Reposition a kinematic character so it STICKS (BATCH-S2-V). Setting entity.Transform +
    // CharacterComponent.Teleport alone does NOT move the controller's internal position in
    // Stride.Physics 4.2.1.2487 (it snaps back). UpdatePhysicsTransformation(true) — the same
    // base-PhysicsComponent call the vehicle uses — pushes the entity transform into the native
    // collider/ghost world transform, which the kinematic controller uses as its position.
    // Readiness guard: only act once the component is in the simulation.
    if (ch.Simulation == null) return;

    entry.StrideEntity.Transform.Position = newPos;
    entry.StrideEntity.Transform.Rotation = targetStrideRot;
    entry.StrideEntity.Transform.UpdateWorldMatrix();
    try
    {
        ch.UpdatePhysicsTransformation(true); // push transform into the native ghost/controller
        ch.Teleport(newPos);                  // belt-and-suspenders (warp the controller too)
    }
    catch (Exception ex)
    {
        // CharacterController not yet fully initialised — safe to skip; entity transform is set.
        Log.Debug("[BulletPhysicsBodyService] SyncBodyToExternalPose(character): reposition call failed for '{0}' ({1}); entity transform set.",
            entry.StrideEntity.Name, ex.GetType().Name);
    }

    Log.Info("[BulletPhysicsBodyService] ExternalReposition(character) '{0}': distXZ={1:F3} → Stride ({2:F2},{3:F2},{4:F2}).",
        entry.StrideEntity.Name, MathF.Sqrt(distSqFdpXY),
        newPos.X, newPos.Y, newPos.Z);
}
```
(Match the existing variable names: `newPos`, `targetStrideRot`, `distSqFdpXY`, `entry`. Keep the
`else if (RigidbodyComponent ...)` vehicle branch and everything else unchanged.)

## Constraints
- One file, character branch only. Vehicle branch unchanged. Do not change the baseline-divergence
  detection (S2-K) or the reverse-sync.
- `ch.UpdatePhysicsTransformation(true)` must compile (base PhysicsComponent method). If it does NOT
  exist on CharacterComponent, STOP and report (do not invent an API).

## Acceptance
- Builds clean (`Stride/HrotStrideApp.sln`).
- (User) Drag a mannequin in the editor and drop it → it STAYS at the dropped location (no snap-back),
  like the vehicle. `[BulletPhysicsBodyService] ExternalReposition(character) ...` logs once per drop.
- Vehicle drag still works.
