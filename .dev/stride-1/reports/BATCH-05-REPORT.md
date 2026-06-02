# BATCH-05 Report

**Tasks:** STR-P1-T3 (`CrowdMotorIntent` + `BulletCharacterMotor`), STR-P1-T4 (`KinematicVehicleMotor`)
**Date:** 2026-06-03
**Branch:** `blueprint-integ-1`

---

## Implementation Summary

### STR-P1-T3 — `CrowdMotorIntent` + `BulletCharacterMotor`

**`CrowdMotorIntent`** — new ECS component in `Fdp.Toolkits`:
- File: `FDP/Toolkits/Fdp.Toolkits/Navigation/CrowdMotorIntent.cs`
- Namespace: `Fdp.Toolkit.Navigation` (same assembly as `CrowdAgent`, `NavigationStatus`)
- Fields: `Vector3 Velocity` (FDP-space steering velocity, m/s) + `[MarshalAs(UnmanagedType.I1)] bool Jump`
- ComponentId: `NavigationContractsComponentIds.CrowdMotorIntent = 265` (added to `NavigationContractsComponentIds.cs`)
- The `bool` field required `[MarshalAs(UnmanagedType.I1)]` — enforced by the ECS layout validator at `ComponentType.cs:446` to prevent Flight-Recorder/StructEdit buffer corruption.

**`IPhysicsBodyService`** extended (STR-P1-T3 + T4) with:
- `void SetCharacterVelocity(object bodyHandle, SMath.Vector3 velocity)` — calls `CharacterComponent.SetVelocity` in the concrete impl
- `void Jump(object bodyHandle)` — calls `CharacterComponent.Jump()`
- `bool IsGrounded(object bodyHandle)` — reads `CharacterComponent.IsGrounded`
- `KinematicMoveResult MoveKinematic(object bodyHandle, SMath.Vector3 desiredDelta, SMath.Quaternion desiredRotDelta)` — swept/penetration-tested kinematic move
- New `KinematicMoveResult` record struct carrying `ActualDelta` + `ActualRotDelta` (collision-clamped)

**`PhysicsBodyReference`** extended with post-collision velocity channel:
- `Vector3 PostCollisionLinearVelocityFdp { get; set; }` — written by `KinematicVehicleMotor` each frame; zero on full block
- `Vector3 PostCollisionAngularVelocityFdp { get; set; }` — same; both in FDP space

**`BulletCharacterMotor`** — `Stride/Hrot.Stride.Core/BulletCharacterMotor.cs`:
- Runs at `SystemPhase.Simulation` (pre-physics)
- Queries `CrowdMotorIntent` + `WithOwned<SimTransform>` + body in `PhysicsBodyLifecycleSystem.Bodies`
- Applies configurable stance multiplier (Standing=1.0, Crouched=0.5, Prone=0.25 defaults)
- Converts FDP velocity → Stride via `FdpStrideTransform.ToStrideVelocity`
- Calls `SetCharacterVelocity`; jump gated by `IsGrounded` check
- Stance injected via `Func<Entity, CharacterStance>` (decoupled from animation assembly)

### STR-P1-T4 — `KinematicVehicleMotor`

**`KinematicVehicleMotor`** — `Stride/Hrot.Stride.Core/KinematicVehicleMotor.cs`:
- Runs at `SystemPhase.Simulation` (pre-physics)
- Queries `VehicleState` + `SimTransform` + `WithOwned<SimTransform>` + body in `Bodies`
- Desired position delta: `heading × speed × dt` (heading = X-axis of `SimTransform.Rotation`)
- Desired yaw delta: bicycle model `ω = (v/L) × tan(δ)` from `VehicleState.SteerAngle` + `VehicleParams.WheelBase`
- Converts to Stride space, calls `MoveKinematic`
- Post-collision velocities: `linVel = actualDeltaFdp / dt`; `angVel.Z = extractedYaw / dt`
- **Full block detection**: `|actualDelta|² < 1e-10` → both velocity fields zeroed **exactly** (no divide-by-near-zero artefact)
- Writes results to `PhysicsBodyReference.PostCollisionLinearVelocityFdp` / `PostCollisionAngularVelocityFdp`

---

## Design Decisions

### `CrowdMotorIntent` placement and ComponentId

Placed in `Fdp.Toolkit.Navigation` (same namespace/file area as `CrowdAgent`, `NavigationCorridorMuscle`).
**Why not `Hrot.Stride.Core`:** design §5.3 says "prefer a dedicated `CrowdMotorIntent` over reusing `SimVelocity`"; it is the *steering output* of `CrowdAgentUpdateSystem` (P2-T4 writer, lives in `Fdp.Toolkits`) — placing it in `Hrot.Stride.Core` would create a cycle (`Fdp.Toolkits` → `Hrot.Stride.Core`). In `Fdp.Toolkits` both the writer and reader (`BulletCharacterMotor` in `Hrot.Stride.Core`) can see it without a cycle.

ComponentId 265: the navigation contracts block ends at 261 (`CrowdAgent`); IDs 262-264 are already claimed by `GlobalComponentIds` (DangerAreaSensor/DangerAreaCognitiveBuffer/MovementModeIntent). 265 is the first free slot in the navigation area.

### Stance source ([VERIFY] result)

`StanceStatus.CurrentStance` (type `StanceId`, ComponentId 223) in `Hrot.MuscleCharacter.Animation.Components.ReplicatedComponents` is the live stance on humanoid/crowd entities. However, `Hrot.Stride.Core.csproj` does NOT reference `Hrot.MuscleCharacter.Animation` — adding it would drag the full animation pipeline into the physics motor and introduce an undesirable coupling.

Decision: define `CharacterStance` (byte enum, values 0/1/2 = Standing/Crouched/Prone) locally in `Hrot.Stride.Core`, matching `StanceId` by value. The motor receives a `Func<Entity, CharacterStance>` resolver injected at construction time. The bootstrap/Stride script layer reads `StanceStatus.CurrentStance` from the ECS and casts the byte to `CharacterStance`. This preserves decoupling.

### `VehicleCommandSystem` output component the vehicle motor consumes

`VehicleCommandSystem` manages formation/spawn commands and writes `NavState` + `VehicleState` (speed, steer angle). The kinematic motor reads `VehicleState` (`.Speed`, `.SteerAngle`) from the ECS — the same component that `CarKinematicsSystem.UpdateVehicle` reads to compute vehicle motion. `CarKinematicsSystem` is excluded from `StrideKinematicsModule`; the motor replaces it for owned entities.

### Post-collision velocity channel to the reverse-sync

Channel: `PhysicsBodyReference.PostCollisionLinearVelocityFdp` and `PostCollisionAngularVelocityFdp` (both in FDP world space, on the same object stored in `PhysicsBodyLifecycleSystem.Bodies`). `BulletReverseSyncSystem` (STR-P1-T5) will read these fields each frame for kinematic bodies to populate `SimVelocity.Linear` / `.Angular`. For dynamic rigid bodies it reads `RigidbodyComponent.LinearVelocity` / `.AngularVelocity` directly.

### `KinematicMoveResult` record struct

Added to `IPhysicsBodyService.cs` (same file as the interface). It is a `readonly record struct` — allocation-free and pattern-match-friendly. Contains `ActualDelta` (Stride space) and `ActualRotDelta` (Stride space).

---

## Deviations

| What | Why | Benefit | Risk |
|------|-----|---------|------|
| `CrowdMotorIntent.Jump` uses `[MarshalAs(UnmanagedType.I1)]` on the `bool` field | ECS layout validator (`ComponentType.cs:446`) hard-rejects undecorated `bool` in blittable components | Correct Flight-Recorder/StructEdit layout | None — this is the required pattern |
| `CharacterStance` enum defined locally in `Hrot.Stride.Core` instead of reusing `StanceId` from animation assembly | Avoids cross-project cycle | Clean separation; motor is testable without animation dependencies | Value identity must be maintained manually if `StanceId` values ever change (documented in `CharacterStance` XML doc) |
| `KinematicVehicleMotor` reads `VehicleState.Speed` + heading from `SimTransform.Rotation` (not a separate "motor output component") | `VehicleCommandSystem` is the existing mechanism; it writes `VehicleState` + `NavState`; `CarKinematicsSystem` was the consumer and is now excluded | Reuses the established data path; no new component needed | Tightly coupled to the bicycle-model semantics of `VehicleState` |

---

## IPhysicsBodyService Extension Signatures

```csharp
// STR-P1-T3 — character driving
void SetCharacterVelocity(object bodyHandle, Stride.Core.Mathematics.Vector3 velocity);
void Jump(object bodyHandle);
bool IsGrounded(object bodyHandle);

// STR-P1-T4 — kinematic swept move
KinematicMoveResult MoveKinematic(
    object                           bodyHandle,
    Stride.Core.Mathematics.Vector3  desiredDelta,
    Stride.Core.Mathematics.Quaternion desiredRotDelta);

// Result type
public readonly record struct KinematicMoveResult(
    Stride.Core.Mathematics.Vector3    ActualDelta,
    Stride.Core.Mathematics.Quaternion ActualRotDelta);
```

---

## How the Fake Simulates Block-or-Slide (T4 tests)

`ScriptableFakePhysicsBodyService.MoveOverrides` is a `Dictionary<object, Func<SMath.Vector3, SMath.Quaternion, KinematicMoveResult>>`. Each test registers an override by body handle:

- **Unobstructed (default):** no entry in `MoveOverrides` → returns `(desiredDelta, desiredRotDelta)` pass-through.
- **Fully blocked:** override returns `(SMath.Vector3.Zero, rotDelta)` → motor detects `|actualDelta|² < 1e-10` → zeroes both velocity fields exactly.
- **Partial slide:** override returns `(desired * 0.5f, rotDelta)` → motor computes `vel = 0.5 * desired / dt`.

The scriptable override approach means the collision policy is controlled per-test without any Bullet runtime.

---

## What Remains for Concrete `BulletPhysicsBodyService` (STR-D11)

Still seam-tested only; deferred to GPU bring-up:

| Behavior | Seam tested | Concrete deferred |
|----------|-------------|-------------------|
| `SetCharacterVelocity` → `CharacterComponent.SetVelocity` | ✓ (call recorded by fake) | Needs `PhysicsProcessor` + running `Scene` |
| `Jump` → `CharacterComponent.Jump()` | ✓ (call recorded) | Same |
| `IsGrounded` → `CharacterComponent.IsGrounded` | ✓ (scripted return) | Same |
| `MoveKinematic` → Bullet sweep test / kinematic body update | ✓ (delta pass-through / block script) | Needs Bullet `Simulation`; exact collision response is engine behavior |
| Post-collision velocity == zero on blocked move (invariant) | ✓ (block scripted explicitly) | Motor logic correct; real proof requires Bullet contact |
| Angular velocity from executed rotation delta | ✓ (zero-steer test) | Bicycle model formula correct; real yaw delta from Bullet |

STR-D11 remains OPEN. The concrete `BulletPhysicsBodyService` must implement all four new methods in `HrotStrideApp.Game` at GPU/`PhysicsProcessor` bring-up.

---

## Test Results

**All 137 tests green (0 failures, 0 skips).**

| Project | Prior | New (BATCH-05) | Total |
|---------|-------|----------------|-------|
| `Hrot.Stride.Core.Tests` | 88 | 21 | 109 |
| `Hrot.Stride.Animation.Tests` | 4 | 0 | 4 |
| `HrotStrideApp.Game.Tests` | 24 | 0 | 24 |
| **Total** | **116** | **21** | **137** |

New tests:
- `BulletCharacterMotorTests`: 12 tests — velocity magnitude/direction preserved, FDP.Z→Stride.Y swizzle, stance multipliers (Standing/Crouched/Prone, configurable), jump gated by `IsGrounded` (grounded/not-grounded), no jump intent skips `IsGrounded`, zero intent, entity without body skipped, `GetMultiplier` helper.
- `KinematicVehicleMotorTests`: 9 tests — unobstructed move along East heading, North-facing heading, fully blocked → exactly zero velocity (the invariant), partial slide velocity proportional to delta, post-collision velocity readable on channel, zero steer → zero angular velocity, zero speed → zero velocity, no body ref → skipped, `MoveKinematic` called on correct handle.

Build: `0 errors`; `NU1608` warnings are pre-existing (BATCH-04 baseline).

---

## Developer Insights

1. **ECS `bool` layout rule** is a sharp footgun: the engine's `ComponentTypeRegistry.ValidateUnmanagedLayout` hard-rejects any `bool` field in a `[StructLayout(Sequential)]` component that lacks `[MarshalAs(UnmanagedType.I1)]`. This is not documented except in the error message. Any future component with boolean flags must follow this pattern.

2. **`VehicleCommandSystem` does NOT produce a motor-ready output** — it processes CQRS commands and populates `NavState`/`VehicleState`; the integration was `CarKinematicsSystem`'s job. The batch instructions say "VehicleCommandSystem output component" — the real motor input is `VehicleState` (speed + steer angle), which `VehicleCommandSystem` indirectly populates by processing spawn/formation commands.

3. **`PhysicsBodyReference` as the velocity channel**: storing the post-collision velocity directly on the shadow component is the lightest-weight option (no new ECS component registration, no separate dictionary). The reverse-sync (T5) will simply look up the same `Bodies` dictionary it already consults for body handles.

4. **Bicycle model angular velocity**: the motor derives yaw rate from the bicycle formula. For the `FullyBlockedMove` test the block threshold (`|actualDelta|² < 1e-10`) also zeroes angular velocity, which is correct per the invariant — a body that didn't move also didn't rotate.

5. **Stance resolver decoupling**: the `Func<Entity, CharacterStance>` parameter is nullable and defaults to `_ => CharacterStance.Standing`. This means P1 works with no stance wiring; P2-T4 (when `CrowdAgentUpdateSystem` is refactored) or the bootstrap can inject the real resolver without changing the motor's API.

---

## Known Issues / Open Items

- **STR-D11** (concrete `BulletPhysicsBodyService`) remains OPEN — all four new service methods are seam-tested only. `CharacterComponent.SetVelocity`, `.Jump`, `.IsGrounded`, and the kinematic move must be wired in `HrotStrideApp.Game` at GPU bring-up.
- **STR-D12** (`CrowdAgentUpdateSystem` still mutates `SimTransform`) remains OPEN — deferred to P2-T4 per BATCH-04.
- The `NullVisualFactory` stub in the new motor tests is copy-pasted from the existing `PhysicsBodyLifecycleSystemTests`. Consider extracting it to a shared test helper file at some point (P3 cleanup).

---

## Suggested Commit Message

```
feat(stride): CrowdMotorIntent + BulletCharacterMotor + KinematicVehicleMotor (BATCH-05)

Completes STR-P1-T3, STR-P1-T4
- CrowdMotorIntent: new ECS component in Fdp.Toolkit.Navigation (ID 265);
  Vector3 Velocity (FDP-space) + [MarshalAs(I1)] bool Jump
- IPhysicsBodyService extended: SetCharacterVelocity/Jump/IsGrounded (T3) +
  MoveKinematic returning KinematicMoveResult (T4); all motor-logic unit-tested via
  scriptable recording fake (headless-Bullet finding from BATCH-04 applies)
- BulletCharacterMotor: CrowdMotorIntent → stance-scaled Stride velocity →
  CharacterComponent; grounded-gated jump
- KinematicVehicleMotor: VehicleState → desired delta → MoveKinematic → post-collision
  lin+ang velocity (zero on full block); exposed on PhysicsBodyReference for T5 reverse-sync
- PhysicsBodyReference: PostCollisionLinearVelocityFdp / PostCollisionAngularVelocityFdp
  (FDP-space; velocity invariant §6.1)
Tests: 137 (109 Core, 4 Animation, 24 Game); +21 new motor tests all green
```
