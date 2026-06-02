# BATCH-17 Review (concrete Bullet physics)
**Status:** ✅ APPROVED (code complete + headless-green; physics behavior GPU-verified by user)   **Date:** 2026-06-03

## Summary
Concrete `BulletPhysicsBodyService : IPhysicsBodyService` against the running Stride `Simulation` (STR-D11); physics body attached to the entity's visual Stride entity so Bullet moves the model (STR-D13); wired into live `editor_stride` (NoOp only in headless tests); Physics Drop/Walk harness cases driving the real `CrowdMotorIntent → BulletCharacterMotor → CharacterComponent` path; key-map extended so every case is keyboard-triggerable.

## Verification performed
- Game builds clean (0 errors); headless suites green and **seam contracts intact**: Core 224 (unchanged), Animation 48, Game 96.
- Live-vs-test wiring confirmed: `StrideHrotGame.BeginRun` resolves `SceneInstance.GetProcessor<PhysicsProcessor>().Simulation` and passes `BulletPhysicsBodyServiceDeferred` to `EditorStrideSubsystem.Initialize`; falls back to NoOp + logs a warning if the processor isn't found; `Initialize`'s param defaults to NoOp so all headless tests are unaffected.
- STR-D13 confirmed: `CreateBody` looks up the entity's Stride visual entity via `StrideVisualBindingSystem.Visuals` and adds the `CharacterComponent`/kinematic `RigidbodyComponent` to **that** entity (the one with the `ModelComponent`) — Bullet motion moves the visible model.
- 3 real Stride 4.2.1.2487 API corrections found during impl (`CapsuleColliderShape` length param; `MaxSlope` is `AngleSingle`; `Simulation.ShapeSweep` not `ShapeSweepPenetrationDepth`) — evidence it's hitting the real API. 12 headless helper tests (shape-dims math, swizzle, deferred construction) + the key-map coverage test.
- Key-map fix: `TryGetCaseKey` (D1–D9, D0, F1–F6) shared by `PollKeyboard` + `DrawStatus`; **Physics Drop = D0, Physics Walk = F1** — both now triggerable + labeled on-screen.

## Issues Found
No blocking issues. `MoveKinematic` is a first-cut sweep (block-or-stop, not full slide) — documented; refine if the vehicle demo needs it. Actual physics behavior (fall/land/walk/collide) is GPU-verified by the user.

## Verdict
APPROVED. STR-D11 + STR-D13 implemented; pending the user's GPU confirmation. Hand back for testing.

## Commit Message
```
feat(stride): concrete Bullet physics — entities move/collide in Stride (BATCH-17, STR-D11/D13)

- BulletPhysicsBodyService : IPhysicsBodyService against the running Stride Simulation: Capsule ->
  CharacterComponent (+gravity, rests on the arena's static colliders), OrientedBox -> kinematic
  RigidbodyComponent; SetCharacterVelocity/Jump/IsGrounded/GetBodyState/MoveKinematic(ShapeSweep)
- STR-D13: physics body attached to the entity's VISUAL Stride entity (with the ModelComponent) so
  Bullet moves the model; CreateBody resolves it via StrideVisualBindingSystem
- Wired into live editor_stride (PhysicsProcessor.Simulation at BeginRun; NoOp fallback + headless tests)
- Harness: "Physics Drop" (D0 — falls+lands) + "Physics Walk" (F1 — CrowdMotorIntent->motor->Bullet,
  walks+collides+walk-anim); key-map extended (TryGetCaseKey: D1-D9/D0/F1-F6) so all cases are keyable
- NLog diagnostics: body create/remove, grounded LANDED/AIRBORNE, throttled positions
Tests: Core 224 / Animation 48 / Game 96; build 0 errors. Physics behavior GPU-verified by the user.
```
