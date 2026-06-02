# BATCH-05 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Summary
`CrowdMotorIntent` (new `[ComponentId(265)]` component in `Fdp.Toolkit.Navigation`) + `BulletCharacterMotor` (T3), and `KinematicVehicleMotor` (T4) — both built against the extended `IPhysicsBodyService` seam. Verified: read both motors + their tests, confirmed real components, ran the suite (Core 109, Animation 4, Game 24 = 137 green).

## Verification performed
- `BulletCharacterMotor`: reads `CrowdMotorIntent.Velocity`, applies stance multiplier (Standing/Crouched/Prone, configurable), converts via `FdpStrideTransform.ToStrideVelocity`, calls `SetCharacterVelocity`; jump gated by `IsGrounded`. Stance via injected `Func<Entity,CharacterStance>` resolver (documented map to `StanceStatus.CurrentStance`) — keeps the physics motor decoupled from the animation assembly. Reasonable.
- `KinematicVehicleMotor`: reads `VehicleState.Speed` + `SimTransform.Rotation` (real CarKinem types), bicycle-model yaw from `VehicleParams.WheelBase`/`SteerAngle`, `MoveKinematic` for block-or-slide, derives `linVel=actualDelta/dt` and yaw-only `angVel`, and **zeroes both exactly** when `|actualDelta|² < 1e-10` (velocity invariant, §6.1). Post-collision velocity exposed on `PhysicsBodyReference.PostCollision{Linear,Angular}VelocityFdp` for the T5 reverse-sync.
- Tests use scriptable fake `IPhysicsBodyService` with per-body `MoveKinematic` overrides → real assertions: direction-preserved velocity, per-stance scaled magnitude, jump-only-when-grounded, unobstructed integrates along heading, full-block → exact zero lin+ang, velocity readable on the channel.
- `CrowdMotorIntent` is a genuine registered struct (`[ComponentId(NavigationContractsComponentIds.CrowdMotorIntent)]`, 265); `[MarshalAs(UnmanagedType.I1)] bool Jump` required by the ECS layout validator (documented footgun).
- Ran the suite myself; counts match.

## Issues Found
No blocking issues.

## Notes carried forward
- **Motors not yet registered in `editor_stride`.** They are built + unit-tested but not wired into `EditorStrideSubsystem`'s tick. Integration wiring (motors pre-physics, reverse-sync post-physics) lands with STR-P1-T5/T7 + the concrete `BulletPhysicsBodyService`. Track as part of BATCH-06.
- **Stance resolver not bound to a real component** — the `Func<Entity,CharacterStance>` defaults to Standing; binding it to `StanceStatus.CurrentStance` happens when stance actually drives gameplay (P4 animation era). Minor; documented in the motor.
- Concrete `BulletPhysicsBodyService` (all four new methods + create/remove) remains deferred to GPU bring-up (STR-D11) — the accumulating-risk note from BATCH-04 stands and grows.

## Verdict
APPROVED. Proceed to BATCH-06: STR-P1-T5 (`BulletReverseSyncSystem` in a `TogglablePostSimulationGroup` — addresses STR-D5), STR-P1-T6 (`SplitAuthorityStrideSyncScript`), STR-P1-T7 (fixed timestep + reverse-sync ordering), and wire the motors + reverse-sync into `editor_stride`.

## Commit Message
```
feat(stride): BulletCharacterMotor + KinematicVehicleMotor + CrowdMotorIntent (BATCH-05)

Completes STR-P1-T3, STR-P1-T4
- CrowdMotorIntent: new [ComponentId 265] component in Fdp.Toolkit.Navigation (steering velocity
  + jump flag) — written by CrowdAgentUpdateSystem (P2-T4), read by BulletCharacterMotor
- IPhysicsBodyService extended: SetCharacterVelocity/Jump/IsGrounded + MoveKinematic(KinematicMoveResult)
- BulletCharacterMotor: intent->stance-scaled velocity (FdpStrideTransform), grounded-gated jump
- KinematicVehicleMotor: bicycle-model kinematic move with owned block-or-slide collision response;
  post-collision lin+ang velocity (EXACT zero on full block) exposed on PhysicsBodyReference for reverse-sync
Tests: 137 (109 Core incl. 21 new motor tests via scriptable fake, 4 Animation, 24 Game).
Concrete BulletPhysicsBodyService still deferred (STR-D11).
```
