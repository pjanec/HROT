# BATCH-S2-AH — Make a manually-set character orientation STICK (continuous facing hold)

## Problem
Rotating a stationary mannequin from the editor (BATCH-S2-Z applies it once via SyncBodyToExternalPose)
"jumps to the right orientation then snaps back". A one-shot rotation does not persist for a kinematic
CharacterComponent — the next reverse-sync reads the body rotation and overwrites SimTransform.Rotation
back (and re-baselines it), so the divergence detector never fires again. The PROVEN-working mechanism
is CONTINUOUS re-application: BATCH-S2-Y's `SetCharacterFacing` visibly holds a moving mannequin's facing
precisely because the motor re-applies it every frame. A manual orientation must be held the same way.

## Fix — ONE FILE, all self-contained in BulletPhysicsBodyService
`Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs`

Model: a per-character "manual facing hold". When an external rotation is detected for a character, store
it and RE-APPLY it every frame (rotation only, no teleport). When the locomotion motor actively faces the
character to its travel direction (calls `SetCharacterFacing`), CLEAR the hold so movement supersedes the
manual orientation (and it doesn't snap back to the old manual heading when the unit stops).

### 1. Add a hold field to the body entry (next to LastReverseSyncedFdpRot, ~line 226)
```csharp
/// <summary>
/// Operator/brain-set manual facing to HOLD each frame for a kinematic character (BATCH-S2-AH),
/// in Stride world space. Set when SyncBodyToExternalPose detects an external rotation on a
/// CharacterComponent; re-applied every frame so it sticks (a one-shot does not, the controller's
/// rotation is not self-preserving). Cleared by SetCharacterFacing (locomotion facing supersedes it).
/// null = no manual hold active.
/// </summary>
public SMath.Quaternion? ManualFacingHoldStride { get; set; }
```

### 2. SetCharacterFacing (the S2-Y method, ~line 672): clear the hold
The motor calls this every frame WHILE MOVING to face travel. An active locomotion facing supersedes a
manual hold, so clear it here (prevents the unit snapping back to the old manual heading after walking):
```csharp
public void SetCharacterFacing(object bodyHandle, SMath.Quaternion strideRotation)
{
    if (bodyHandle is SkippedBodyHandle) return;
    if (!_bodies.TryGetValue(bodyHandle, out var entry)) return;
    if (entry.PhysicsComponent is CharacterComponent ch)
    {
        entry.ManualFacingHoldStride = null; // BATCH-S2-AH: locomotion facing supersedes a manual hold
        entry.StrideEntity.Transform.Rotation = strideRotation;
        entry.StrideEntity.Transform.UpdateWorldMatrix();
        try { ch.UpdatePhysicsTransformation(true); }
        catch (Exception) { }
    }
}
```
(Keep the existing body otherwise; just add the clear line + confirm the rest matches what S2-Y wrote.)

### 3. SyncBodyToExternalPose (~line 1086): set + continuously hold the manual facing for characters
Currently it early-returns when neither position nor rotation diverged. Change so a character with an
active hold KEEPS re-applying its rotation each frame. Concretely, after computing `posMoved`/`rotChanged`
(BATCH-S2-Z), and BEFORE the `if (!posMoved && !rotChanged) return;`:
```csharp
bool isCharacter = entry.PhysicsComponent is CharacterComponent;

// BATCH-S2-AH: an external rotation on a character becomes a manual facing HOLD.
if (rotChanged && isCharacter)
    entry.ManualFacingHoldStride = targetStrideRot;

bool holdFacing = isCharacter && entry.ManualFacingHoldStride.HasValue;

// Nothing external changed AND no manual hold to maintain → done.
if (!posMoved && !rotChanged && !holdFacing) return;
```
Then in the CharacterComponent apply branch:
- For the divergence case (posMoved || rotChanged) keep the existing teleport+rotation, BUT set the
  rotation AFTER `ch.Teleport(newPos)` (Teleport can reset orientation), i.e. order:
  `UpdatePhysicsTransformation(true); ch.Teleport(newPos); Transform.Rotation = targetStrideRot; UpdateWorldMatrix(); ch.UpdatePhysicsTransformation(true);`
- When ONLY holding (no divergence this frame: `!posMoved && !rotChanged && holdFacing`), do a
  rotation-ONLY re-apply (no teleport, no position change):
```csharp
// BATCH-S2-AH: re-apply the held manual facing each frame so it persists against reverse-sync.
entry.StrideEntity.Transform.Rotation = entry.ManualFacingHoldStride!.Value;
entry.StrideEntity.Transform.UpdateWorldMatrix();
try { ch.UpdatePhysicsTransformation(true); } catch (Exception) { }
return;
```
  Structure this so the rotation-only hold path runs WITHOUT falling through to the position-teleport
  logic (it must not teleport when nothing moved). Put the hold-only re-apply early in the character
  branch (guarded by `!posMoved && !rotChanged`), then `return`. Keep the divergence path below it.

  Be careful: the method's existing flow computes `newPos` from `targetStridePos`/current Y and the
  `RigidbodyComponent` (vehicle) branch must be UNCHANGED. Only the CharacterComponent branch gains the
  hold logic. Vehicles (dynamic) preserve rotation natively — do NOT add a hold for them.

### 4. Verify the reverse-sync interaction (no code change, just confirm reasoning)
With the hold re-applied each frame, BulletReverseSyncSystem reads the body rotation (= held value) and
writes SimTransform.Rotation = held, re-baselining to the held value. So next frame `rotChanged` is false
but `holdFacing` is true → re-apply continues → rotation sticks. A fresh manual rotate updates the hold.
A move order → motor calls SetCharacterFacing → hold cleared → travel-facing takes over.

## Constraints
- ONE file. CharacterComponent branch + SetCharacterFacing + the new field only. Vehicle branch and the
  BATCH-S2-Z divergence detection (posMoved/rotChanged, RepositionRotDotEpsilon) stay intact.
- No new ECS components, no cross-assembly state, no changes to BulletCharacterMotor/reverse-sync.
- Build the Stride solution; kill HrotStrideApp + rebuild on file lock.

## Acceptance
- Builds clean.
- (User) Rotate a STATIONARY mannequin from the 2D editor → it turns to the requested heading and HOLDS
  (no snap-back). Issue a move → it walks facing its travel direction (S2-Y), and does NOT snap back to
  the old manual heading when it stops. Rotating a vehicle still works (unchanged).
