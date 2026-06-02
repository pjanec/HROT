# BATCH-04 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Summary
`StrideKinematicsModule` (kept systems, FDP integrators omitted, `DeadReckoningSyncSystem` `DriveFromNetwork=false`) wired into `editor_stride` (T1), and `PhysicsBodyReference` + `PhysicsBodyLifecycleSystem` reactive on the authority bit via an `IPhysicsBodyService` seam (T2). Verified independently: read the sources/tests, ran the full suite (Core 88/88, Animation 4/4, Game 24/24 = 116 green).

## Headline finding (shapes the rest of Phase 1)
**Headless Bullet is not feasible.** `Stride.Physics.Simulation`'s constructor and `Add/RemoveRigidBody`/`Add/RemoveCharacter`/`Simulate` are `internal`, owned by `PhysicsProcessor`; a `Simulation` cannot be created/stepped without a running `Scene`+`Game`. The coder correctly responded with the `IPhysicsBodyService` seam (mirroring BATCH-03's visual factory) — lifecycle/authority logic is fully unit-tested with a recording fake; the concrete `BulletPhysicsBodyService` is deferred to a real-engine bring-up (STR-D11). This is the right engineering call, but it means **the entire Bullet-authority core of Phase 1 (T2–T5) can only be behaviorally validated against a running `PhysicsProcessor`** — see the accumulating-risk note below.

## Verification performed
- `StrideKinematicsModule`: registers exactly the 5 kept systems + `DeadReckoningSyncSystem(driveFromNetwork:false)`; `CarKinematicsSystem`/`LinearKinematicsSystem`/terrain-query trio absent. Membership tests use real `is T` checks.
- **"Integrators off" integration test is genuinely meaningful**: owned entity, `SimVelocity.Linear=10 m/s`, 10 frames → `SimTransform.X` unchanged at precision 2 (LinearKinematicsSystem would move it ~1.67 m). Proves topological removal, not a mock.
- `EditorStrideSubsystem` recomposed to register `StrideKinematicsModule` + Combat/DamageAssessment/nav-bridge individually instead of the whole `SimHostCoreLogicPack` — integrators gone, rest intact.
- `PhysicsBodyLifecycleSystem`: destruction(`DestructionOrder.Entity`)→revocation(`WithoutOwned`+body)→creation(`WithOwned`+no body+visual-ref present) order; idempotent; destroyed-this-frame guard prevents re-create of a deferred-destroy; shape read from `StrideVisualReference` (no descriptor re-resolution); seam-tested for create/revoke/destroy/capsule-vs-box/idempotency.
- Ran all three test projects myself; counts match the report.

## Issues Found
No blocking issues. Recorded debt: STR-D11 (concrete `BulletPhysicsBodyService` deferred to GPU bring-up), STR-D12 (`CrowdAgentUpdateSystem` still writes `SimTransform` in P1 — correctly deferred to the P2-T4 refactor; it would fight Bullet for crowd entities, but crowd isn't wired until P2).

## Accumulating-risk note (carry to user at the P1 checkpoint)
With headless Bullet impossible, the un-validated-against-real-engine surface now spans STR-D4 (visual render), STR-D9/D10 (visual factory gaps), STR-D11 (physics bodies), and will grow through T3–T5. The seam-tested logic is correct and the wiring is provable, but **a single "concrete-impl + GPU/PhysicsProcessor bring-up + manual validation" milestone is now on the critical path to validating any actual Bullet behavior** (collision response, velocity readback, the velocity invariant). Recommend scheduling that bring-up at the end of Phase 1 on a GPU-capable machine.

## Verdict
APPROVED. Proceed to BATCH-05 (STR-P1-T3 `BulletCharacterMotor` + STR-P1-T4 `KinematicVehicleMotor`, creating `CrowdMotorIntent`), extending the `IPhysicsBodyService` seam for velocity apply/readback and post-collision velocity computation.

## Commit Message
```
feat(stride): StrideKinematicsModule (integrators off) + PhysicsBodyLifecycleSystem (BATCH-04)

Completes STR-P1-T1, STR-P1-T2
- StrideKinematicsModule: SpatialHash/FormationTarget/VehicleCommand/NavigationExecution/
  CrowdAgentUpdate + DeadReckoningSyncSystem(DriveFromNetwork=false); omits CarKinematicsSystem,
  LinearKinematicsSystem, and the terrain-query pipeline (topological exclusion only)
- EditorStrideSubsystem recomposed: StrideKinematicsModule + Combat/DamageAssessment/nav-bridge
  replace SimHostCoreLogicPack's kinematics; FDP integrators no longer move owned entities
- PhysicsBodyReference + PhysicsBodyLifecycleSystem: create/revoke/destroy bodies on the
  authority bit (.WithOwned/.WithoutOwned + DestructionOrder), shape from StrideVisualReference,
  via IPhysicsBodyService seam (headless Bullet infeasible — Simulation is internal; STR-D11)
Tests: 116 (88 Core, 4 Animation, 24 Game). Headless-Bullet finding documented in report.
```
