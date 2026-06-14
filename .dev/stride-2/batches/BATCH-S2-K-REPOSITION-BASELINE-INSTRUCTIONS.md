# BATCH-S2-K — Reposition baseline fix (vehicle frozen by reposition vs physics-step phase mismatch)

## The bug (root cause, high confidence — proven via the autonomous harness)
The vehicle is commanded correctly (motor sets velocity 2.63 m/s, body reports it, yaw 100%)
but its position NEVER changes (`distMoved=0.00`, frozen exactly at the last reposition target).

Why: the external-reposition pass (`SyncBodyToExternalPose` in `BulletPhysicsBodyService`)
compares the body's **live** Stride position against `SimTransform`, assuming they "stay in sync
during normal physics motion." They do NOT, because of a one-step phase mismatch:

- `BulletReverseSyncSystem` runs **PRE-kernel** (in `StridePhysicsBracket.RunPreKernelStep`,
  before Stride's external loop steps Bullet that frame). So it reads the body pose from the
  PREVIOUS frame's physics step and writes it to `SimTransform`. `SimTransform` therefore always
  **lags the live body by exactly one physics step**.
- The reposition pass runs at the start of the next pre-kernel step. It sees the live body one
  step ahead of the stale `SimTransform`, declares a false "external drag," **teleports the body
  back to the stale `SimTransform` AND zeroes its velocity** (BulletPhysicsBodyService.cs ~1110-1112).
- The motor then re-sets velocity, Bullet steps ~0.04 m, next frame yanks it back. Net: frozen.

## The fix
The reposition pass must compare `SimTransform` against **the pose reverse-sync last wrote**
(the muscle's own last output), NOT the live body pose. Then:
- Normal physics motion: `SimTransform` still equals the muscle's last output → no divergence → skip.
- A genuine external write (operator drag sets `SimTransform` from outside the muscle): `SimTransform`
  differs from the muscle's last output → divergence → teleport (existing behavior).

This is correct regardless of the one-step physics lag because the baseline and the trigger are
both "muscle-authored `SimTransform`" values, not the live body pose.

## Scope — THREE FILES

### File 1: `Stride/Hrot.Stride.Core/PhysicsBodyLifecycleSystem.cs`
Add one method to the `IBodyRepositionService` interface (right after `SyncBodyToExternalPose`):

```csharp
/// <summary>
/// Records the FDP pose that the reverse-sync just wrote into <see cref="SimTransform"/> for
/// this body. This becomes the baseline that <see cref="SyncBodyToExternalPose"/> compares
/// against to distinguish muscle-authored motion (skip) from an external write / operator
/// drag (teleport). Must be called by the reverse-sync each frame after it writes SimTransform.
/// </summary>
void RecordReverseSyncedPose(object bodyHandle, in SimTransform simTf);
```

Do NOT change the reposition-pass call site (line ~240) or anything else in this file.

### File 2: `Stride/Hrot.Stride.Core/BulletReverseSyncSystem.cs`
1. Add a private field for the downcast, set in the constructor:
   ```csharp
   private readonly IBodyRepositionService? _repositionService;
   ```
   In the constructor body, after the existing assignments:
   ```csharp
   _repositionService = bodyService as IBodyRepositionService;
   ```
   (`IBodyRepositionService` is in the same namespace `Hrot.Stride.Core` — no new using needed.)

2. In `Execute`, IMMEDIATELY after the existing `repo.SetComponent(entity, newTransform);`
   (the line that writes the pose; ~line 161), record the baseline:
   ```csharp
   // Record the muscle-authored pose as the reposition baseline (BATCH-S2-K).
   // SyncBodyToExternalPose compares SimTransform against THIS, not the live body pose,
   // so physics-step motion is never mistaken for an external drag.
   _repositionService?.RecordReverseSyncedPose(bodyRef.BodyHandle, in newTransform);
   ```
   Change nothing else (the SimVelocity logic, capsule branch, diagnostics all stay).

### File 3: `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs`

**3a. `BodyEntry` class** — add two fields (near `InitialPoseApplied`):
```csharp
/// <summary>
/// The FDP-space position the reverse-sync last wrote into SimTransform for this body
/// (BATCH-S2-K). This is the baseline SyncBodyToExternalPose compares the incoming
/// SimTransform against — divergence means an EXTERNAL writer (operator drag) changed
/// SimTransform, not the muscle's own physics motion.
/// </summary>
public System.Numerics.Vector3 LastReverseSyncedFdpPos { get; set; }

/// <summary>False until RecordReverseSyncedPose has run at least once for this body.</summary>
public bool HasReverseSyncBaseline { get; set; }
```
(Match the exact type of `SimTransform.Position` — it is `System.Numerics.Vector3`. If the
codebase aliases it, use the same alias.)

**3b. Implement `RecordReverseSyncedPose`** on `BulletPhysicsBodyService` (the concrete class —
near `SyncBodyToExternalPose`, ~line 1039):
```csharp
/// <inheritdoc cref="IBodyRepositionService.RecordReverseSyncedPose"/>
public void RecordReverseSyncedPose(object bodyHandle, in SimTransform simTf)
{
    if (bodyHandle is SkippedBodyHandle) return;
    if (!_bodies.TryGetValue(bodyHandle, out var entry)) return;
    entry.LastReverseSyncedFdpPos = simTf.Position;
    entry.HasReverseSyncBaseline  = true;
}
```

**3c. Change the divergence DETECTION in `SyncBodyToExternalPose`** (BulletPhysicsBodyService.cs
~line 1054-1059). Replace the current "live body pos vs target Stride pos" check:

```csharp
var currentBodyPos = entry.StrideEntity.Transform.Position;
float dx = targetStridePos.X - currentBodyPos.X;
float dz = targetStridePos.Z - currentBodyPos.Z;
float distSqXZ = dx * dx + dz * dz;

if (distSqXZ <= RepositionEpsilonM * RepositionEpsilonM) return; // normal physics motion
```

with a comparison of the INCOMING SimTransform against the reverse-sync baseline, in FDP
horizontal axes (FDP X=East, Y=North; Z=up — horizontal is X,Y):

```csharp
// BATCH-S2-K: detect an EXTERNAL write by comparing the incoming SimTransform against the
// muscle's own last reverse-synced pose (NOT the live body pose). The live body leads
// SimTransform by one physics step (reverse-sync runs pre-step), so comparing against the
// live body produced a false divergence every frame and froze the vehicle.
//
// No baseline yet (reverse-sync hasn't run for this body): the initial-pose slam owns
// placement — skip external-reposition until we have a baseline.
if (!entry.HasReverseSyncBaseline) return;

// Horizontal (FDP X,Y) divergence between the externally-visible SimTransform and what the
// muscle last authored. <= epsilon => muscle-authored motion (or no change) => skip.
float dXf = simTf.Position.X - entry.LastReverseSyncedFdpPos.X;
float dYf = simTf.Position.Y - entry.LastReverseSyncedFdpPos.Y;
float distSqFdpXY = dXf * dXf + dYf * dYf;
if (distSqFdpXY <= RepositionEpsilonM * RepositionEpsilonM) return; // not externally moved

// External reposition detected. (Below: keep the existing teleport — read the live body pos
// for Y-preservation and teleport in Stride XZ.)
var currentBodyPos = entry.StrideEntity.Transform.Position;
```

Then KEEP the rest of the method unchanged from `var newPos = new SMath.Vector3(...)` onward:
the Stride-XZ teleport, the Character/Rigidbody branches, the Y-preservation, the velocity zero,
and the `ExternalReposition` log lines all stay exactly as they are.

NOTE: `distSqXZ` is no longer computed before the branches. The two existing log lines reference
`MathF.Sqrt(distSqXZ)`. Replace those two `distSqXZ` references with `distSqFdpXY` so the logs
still compile and report the detected horizontal divergence.

**3d. Deferred wrapper** (`BulletPhysicsBodyServiceDeferred`, ~line 1543) — forward the new method,
same null-guard pattern as `SyncBodyToExternalPose`:
```csharp
/// <inheritdoc/>
public void RecordReverseSyncedPose(object bodyHandle, in SimTransform simTf)
{
    if (_inner != null)
        _inner.RecordReverseSyncedPose(bodyHandle, in simTf);
}
```
(If the deferred wrapper implements `IBodyRepositionService`, this satisfies the new interface
member. Make sure the wrapper still compiles against the extended interface.)

### File 4 (tests): `Stride/HrotStrideApp.Game.Tests/DynamicBodyInitialPoseTests.cs`
The existing "no-false-fire" / reposition tests assert the OLD body-vs-SimTransform detection.
Update them to the new baseline-driven contract:
- Establish a baseline first: call `RecordReverseSyncedPose(handle, simTfAtBodyPose)`.
- Assert that a subsequent `SyncBodyToExternalPose` with a `SimTransform` EQUAL to the baseline
  (i.e. simulating physics drift where the live body moved but SimTransform matches the last
  muscle output) does NOT teleport.
- Assert that a `SyncBodyToExternalPose` with a `SimTransform` that DIFFERS from the baseline by
  more than the epsilon (simulating an operator drag) DOES teleport.
- Keep the initial-pose and basic reposition coverage; adapt as needed so they call
  RecordReverseSyncedPose to establish the baseline before expecting a reposition.

## Constraints
- Do not move reverse-sync to post-kernel, do not touch the bracket ordering, do not touch
  TimeController / preview / edit-mode anything (position-authority constraint stands).
- Keep all existing readiness guards (InitialPoseApplied, Simulation==null, NativeBodyNotReady).
- The teleport itself (Stride XZ, Y-preserve, velocity zero, Character vs Rigidbody) is unchanged.

## Acceptance (lead verifies via the autonomous harness)
- Builds clean (whole `Stride/HrotStrideApp.sln`).
- `STRIDE_SELFTEST=1` run: DRIVE phase shows the body POSITION advancing from (-7,5) toward
  (4,11) (`distMoved` >> 0, `driveErrToDest` shrinks), `navResult=InProgress`, no false Arrived.
  `initialHold=PASS repos=PASS` must STILL pass (the reposition for the SPAWN→A and A→B legs
  must still work — those are genuine external writes via the harness).
- No `ExternalReposition(vehicle)` log spam during the drive (it must fire only on the harness's
  explicit REPOSITION step, not every frame).
