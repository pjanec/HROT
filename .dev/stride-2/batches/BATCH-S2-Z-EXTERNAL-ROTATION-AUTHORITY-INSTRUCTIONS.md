# BATCH-S2-Z — Honor brain/operator orientation writes (external-rotation authority)

## Problem (root cause established)
When the brain/operator sets an entity's orientation by writing `SimTransform.Rotation` (e.g. the
operator rotate gizmo `EntityRotatorGizmo.CommitRotation`, or the ImGui Quaternion inspector), an
owned body IGNORES it unless the entity also moved. Cause: `SyncBodyToExternalPose` detects an
external pose change ONLY by horizontal POSITION divergence (`distSqFdpXY`); a rotation-only change
never triggers, and the next reverse-sync overwrites `SimTransform.Rotation` from the (unchanged)
body. So orientation-only requests are silently dropped for owned characters AND vehicles.

This is the exact symmetric gap to the position-authority handoff (BATCH-S2-K). Fix: also track a
baseline ROTATION and trigger the external-reposition when rotation diverges, even if position didn't.

## Interplay with movement-facing (BATCH-S2-Y) — by design, keep it
- Reverse-sync writes `SimTransform.Rotation` from the body AND will now record the baseline rotation,
  so a motor-driven facing change does NOT look like an external write (baseline tracks the body).
- While a character is MOVING, `BulletCharacterMotor` sets facing from velocity each frame (runs after
  reposition in the pre-step), so travel-facing still wins while moving. Brain/operator orientation
  takes effect when the unit is stationary (motor not steering it) — which is the reported case.
- Do NOT change BulletCharacterMotor or the reverse-sync velocity logic in this batch.

## Scope — ONE FILE
`Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs`

### 1. Add a baseline-rotation field on the body entry (next to LastReverseSyncedFdpPos, ~line 224)
```csharp
/// <summary>
/// The last FDP rotation recorded by RecordReverseSyncedPose (BATCH-S2-Z). SyncBodyToExternalPose
/// compares the incoming SimTransform.Rotation against THIS (not the live body) to detect an
/// external orientation write (operator rotate gizmo / inspector) without false-triggering on the
/// muscle's own reverse-synced rotation.
/// </summary>
public System.Numerics.Quaternion LastReverseSyncedFdpRot { get; set; } = System.Numerics.Quaternion.Identity;
```

### 2. Record it in RecordReverseSyncedPose (~line 1058, beside the position write)
```csharp
entry.LastReverseSyncedFdpPos = simTf.Position;
entry.LastReverseSyncedFdpRot = simTf.Rotation;   // BATCH-S2-Z: baseline rotation for external-rotation detect
entry.HasReverseSyncBaseline  = true;
```

### 3. Detect rotation divergence in SyncBodyToExternalPose (~lines 1104-1109)
Replace the position-only early-return:
```csharp
float dXf = simTf.Position.X - entry.LastReverseSyncedFdpPos.X;
float dYf = simTf.Position.Y - entry.LastReverseSyncedFdpPos.Y;
float distSqFdpXY = dXf * dXf + dYf * dYf;
if (distSqFdpXY <= RepositionEpsilonM * RepositionEpsilonM) return; // not externally moved
```
with a position-OR-rotation check:
```csharp
float dXf = simTf.Position.X - entry.LastReverseSyncedFdpPos.X;
float dYf = simTf.Position.Y - entry.LastReverseSyncedFdpPos.Y;
float distSqFdpXY = dXf * dXf + dYf * dYf;
bool posMoved = distSqFdpXY > RepositionEpsilonM * RepositionEpsilonM;

// BATCH-S2-Z: also detect an external ROTATION-only write. The reverse-sync records the baseline
// rotation each frame, so the muscle's own pose never diverges from it; only a brain/operator write
// to SimTransform.Rotation does. Quaternion closeness via |dot| (1 = identical).
float rotDot = System.Numerics.Quaternion.Dot(simTf.Rotation, entry.LastReverseSyncedFdpRot);
bool rotChanged = MathF.Abs(rotDot) < RepositionRotDotEpsilon;

if (!posMoved && !rotChanged) return; // neither position nor orientation externally changed
```
Add the epsilon constant next to `RepositionEpsilonM` (~line 1189):
```csharp
/// <summary>
/// Rotation closeness threshold for SyncBodyToExternalPose (BATCH-S2-Z): treat an incoming
/// SimTransform.Rotation as an external orientation write when |dot| with the baseline rotation
/// is below this. 0.99985 ≈ a ~2° difference — well above float round-trip noise (the baseline
/// stores the exact reverse-synced FDP quaternion, so there is no conversion drift), below any
/// intentional operator rotation.
/// </summary>
public const float RepositionRotDotEpsilon = 0.99985f;
```
The existing apply blocks (character + vehicle) already set
`entry.StrideEntity.Transform.Rotation = targetStrideRot;` — so once we proceed past the guard, a
rotation-only change is applied correctly. `newPos` for a rotation-only change resolves to the current
position (targetStridePos == baseline position, body Y preserved), so no spurious teleport.

OPTIONAL (nice): in the two `Log.Info("[BulletPhysicsBodyService] ExternalReposition(...)"` lines,
append ` rotChanged={n}` using the bool, so logs distinguish a rotate-in-place from a drag. Keep it a
one-arg addition; don't restructure the log.

## Constraints
- ONE production file (BulletPhysicsBodyService.cs). No interface change (RecordReverseSyncedPose /
  SyncBodyToExternalPose signatures unchanged → deferred wrapper + test fakes compile untouched).
- Do NOT modify BulletCharacterMotor, BulletReverseSyncSystem, or the vehicle motor.
- Verify `System.Numerics.Quaternion.Dot` exists (it does) and `MathF.Abs`. Build the Stride solution.

## Acceptance
- Builds clean (`Stride/HrotStrideApp.sln`).
- (User) Select a STATIONARY mannequin (or vehicle), set its orientation via the operator rotate
  gizmo / inspector → the unit visibly turns to the requested heading and HOLDS it (no longer ignored).
- (User) A moving mannequin still faces its travel direction (BATCH-S2-Y unaffected). Dragging still
  repositions. Vehicle drive/steer unchanged.
