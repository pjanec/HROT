# BATCH-S2-I Report

## Implementation Summary

### Task 1 — Fix capsule (CharacterComponent) reposition in `SyncBodyToExternalPose`

**File:** `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs`

Replaced the blanket `if (entry.IsKinematic) return;` early-return with a two-branch dispatch on component type:

- **`CharacterComponent ch` (capsule / mannequin):**
  - Readiness guard: `if (ch.Simulation == null) return;`
  - Sets `entry.StrideEntity.Transform.Position = newPos` (XZ from request, Y preserved), sets Rotation, calls `UpdateWorldMatrix()`.
  - Then calls `ch.Teleport(newPos)` in a try/catch (crash-safe).
  - Log: `[BulletPhysicsBodyService] ExternalReposition(character) '{name}': distXZ=.. → Stride (x,y,z).`
  - No `InitialPoseApplied` gate — capsule bodies never go through `ApplyDynamicConfigIfReady`, so that flag stays `false` for them forever.

- **`RigidbodyComponent rb` (dynamic vehicle, `!rb.IsKinematic`):**
  - Existing path preserved exactly — `InitialPoseApplied` gate, `rb.Simulation != null` guard, `NativeBodyNotReady` guard, `UpdatePhysicsTransformation(true)`, zero `LinearVelocity`/`AngularVelocity`.
  - Log: `[BulletPhysicsBodyService] ExternalReposition(vehicle) '{name}': distXZ=.. → Stride (x,y,z), zeroed velocity.`

- Other body kinds (kinematic `RigidbodyComponent`, unknown) fall through silently — they are moved by their motor.

**Exact capsule teleport call:**
```csharp
ch.Teleport(newPos);
// where newPos = new SMath.Vector3(targetStridePos.X, currentBodyPos.Y, targetStridePos.Z)
```
Signature verified from `Stride.Physics.xml` in the 4.2.1.2487 nupkg:
`M:Stride.Physics.CharacterComponent.Teleport(Stride.Core.Mathematics.Vector3)` — takes one `Vector3 targetPosition`.

---

### Task 2 — DRIVE phase in `StrideSelfTest.cs`

**File:** `Stride/HrotStrideApp.Game/StrideSelfTest.cs`

Added `using Fdp.Toolkit.Navigation;` (provides `NavigationIntent`, `NavigationMode`, `NavigationStatus`, `NavigationResult`).

Extended the state machine with three new phases after `CheckB`:

#### Phase `DriveIssue`
Finds the entity, adds `NavigationStatus` if absent, then writes a `NavigationIntent`:

| Field | Value | Source |
|-------|-------|--------|
| `Mode` | `NavigationMode.DirectPoint` | `NavigationMode` enum, value 1 |
| `FinalDestination` | `(4f, 11f, 0f)` (`PosD`) | FDP Cartesian |
| `TargetSpeed` | `5.0f` | per spec |
| `ArrivalRadius` | `2.0f` | per spec |
| `IntentId` | `1` | per spec |
| `ReverseAllowed` | `0` | per spec |

Set via `world.SetComponent` (if `NavigationIntent` already present) or `world.AddComponent` (if component type is registered but not present). If `NavigationIntent` is not registered in the composition, the phase runs without issuing the intent — vehicle won't move, `CHECK_DRIVE` records FAIL capturing the diagnostic.

Log: `[SELFTEST] DRIVE_ISSUE intent → D=(4,11) IntentId=1`

#### Phase `DrivingSettle` (~240 frames)
Every 30 frames logs:
```
[SELFTEST] drive frame=N pos=(x,y) navResult=<NavigationResult> navIntentId=<uint>
```
Reads `NavigationStatus.Result` and `NavigationStatus.IntentId` (tolerates absence — returns `InProgress`/0).

#### Phase `CheckDrive`
Reads final position. Computes:
- `distMoved = distance(endXY, B.xy)` (from `PosB = (-7,5)`)
- `errToDest = distance(endXY, D.xy)` (to `PosD = (4,11)`)

Verdict: PASS if `errToDest <= 3.0` (arrived near D) OR `distMoved >= 3.0` (real progress from B).

Log:
```
[SELFTEST] CHECK_DRIVE end=(x,y) distMoved=.. errToDest=.. navResult=<..> -> PASS/FAIL
```

#### Extended RESULT line
```
[SELFTEST] RESULT initialHold=../repos=../drive=.. errA=.. errB=.. driveDistMoved=.. driveErrToDest=..
(A=(6,8) endA=(x,y) B=(-7,5) endB=(x,y) D=(4,11) endDrive=(x,y))
```

#### Timeout constant extended
`TotalTimeoutFrames` raised from 1200 → 1800 to accommodate the 240-frame drive phase (30W + 150A + 1check + 120B + 1check + 1issue + 240D + 1check = ~544 frames typical path; 1800 is a safe ceiling).

---

## Design Decisions

1. **No `InitialPoseApplied` gate for capsules.** The capsule body goes through `CreateBody` → `strideEntity.Add(character)` but never through `ApplyDynamicConfigIfReady` (that path is rigidbody-only). `InitialPoseApplied` stays `false` forever for capsule entries. Gating on it would prevent all capsule repositions permanently.

2. **`ch.Simulation != null` readiness guard for capsules.** `PhysicsComponent.Simulation` is set by the `PhysicsProcessor` on the first step after the entity is added. It is the canonical "body is in the simulation" signal — safe and symmetrical with the rigidbody guard.

3. **NavigationIntent write guards.** In `TickDriveIssue`, `NavigationIntent` may not be registered in the composition if the vehicle type doesn't have it wired. The guard `world.IsComponentTypeRegistered<NavigationIntent>()` prevents a crash. If unregistered, the diagnostic still captures "vehicle didn't move" cleanly.

4. **`NavigationStatus` component added proactively.** The muscle needs somewhere to write its status. Matching the pattern used in `StridePhysicsHarnessCases.cs` (lines 1695, 2053, 2311).

---

## Deviations

| What | Why | Benefit | Risk |
|------|-----|---------|------|
| `TotalTimeoutFrames` 1200 → 1800 | Drive phase adds ~240+ frames | Process always exits | None — value is a hard ceiling, not a timing assumption |
| Drive phase writes `NavigationStatus` if absent | Muscle needs the component to write feedback | Captures `navResult` in logs | None — idempotent add, matches existing harness pattern |

---

## Test Results

No automated tests exist for `StrideSelfTest.cs` or `SyncBodyToExternalPose` capsule path (tests are GPU-run by the Lead). The build is the verification gate for this batch.

**Build results:**
- `HrotStrideApp.Game`: Build succeeded — **0 errors, 0 new warnings** (5 pre-existing: 4× NU1608 NuGet version noise + 1× CS0108 in `StrideHrotGame.cs`, not touched by this batch).
- `HrotStrideApp.Windows`: Build succeeded — **0 errors** (80 pre-existing asset-compiler + HLSL shader warnings, unchanged from baseline).

---

## Developer Insights

- **`CharacterComponent.Teleport` confirmed** in `Stride.Physics` 4.2.1.2487 XML docs: `M:Stride.Physics.CharacterComponent.Teleport(Stride.Core.Mathematics.Vector3)`. Note: the SourceServer-cached `CharacterComponent.cs` found locally is the Bepu (Stride.BepuPhysics) version, not the classic Bullet one — confirmed the correct API from the nupkg XML.

- **`NavigationStatus.RouteHandle` field absent.** The struct has `IntentId`, `Result`, `ProgressS`, `Phase`, `CurrentTraversalKind`, `LastFailureReason`, `ReplanCount` — no RouteHandle in the status (RouteHandle is on the intent side). The drive logging uses `IntentId` + `Result` as specified.

- **`NavigationIntent.FinalDestination` is `System.Numerics.Vector3`** (not `Stride.Core.Mathematics.Vector3`) — confirmed from the struct definition. `PosD` is already declared as `System.Numerics.Vector3` so the assignment compiles directly.

- The capsule-teleport Y-preserve strategy (keep `currentBodyPos.Y`) is correct: Bullet's CharacterController tracks its own internal Y via gravity. Setting Y to 0 (from FDP ground-plane) would bury the capsule into the floor on first teleport.

---

## Known Issues

- The DRIVE phase result depends on whether the vehicle's navigation composition has `NavigationIntent` + muscle ticked. If the muscle is not registered for this entity kind, `drive=FAIL` with `navResult=InProgress` is the expected diagnostic output — this is by design (bug-C diagnosis).
- `CharacterComponent.Teleport` success cannot be confirmed headlessly — only verifiable in the live GPU run.

---

## Suggested Commit Message

fix(physics): capsule teleport via CharacterComponent.Teleport + selftest DRIVE phase for nav muscle diagnosis
