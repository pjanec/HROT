# BATCH-13 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Summary
Real `StrideAnimationBackend : IAnimationBackend` (full 17-member contract) + `PerEntityBlendTreeBuilder` (T1), and mannequin `CharacterAnimationDefDto` demo content attached to InfantrySoldier/Insurgent (T2). Verified: read the testable blend logic, ran the suites, and — because T2 touched shared `UrbanCombatNewScenario` — isolated BATCH-13's effect on the scenario tests.

## Verification performed
- Testable seam: blend-weight-from-speed logic lives in a pure `LocomotionBlend` (no GraphicsDevice); the Stride `AnimationComponent` application in `PerEntityBlendTreeBuilder`/backend is the GPU-bound part (documented). No `NotImplementedException` left — full contract implemented.
- Animation tests 4→**28** (+24: idle/walk/run weight thresholds, montage slot state, register/unregister); Game 48→**58**; Core 215. All green.
- T2 content wired in `UrbanCombatNewScenario.cs`; `CharacterAnimationDefDto` bakes to `CharacterAnimationDefRuntime`; attached to InfantrySoldier(2002)/Insurgent(2003). Editor `UrbanCombat` registration tests: 18/18 green.
- **Shared-code regression check** (T2 edits `Fdp.Examples.Scenarios`): `Fdp.Examples.Scenarios.Tests` = 25 failed / 43 passed with BATCH-13; **identical 25 failed / 43 passed with BATCH-13 stashed (at BATCH-12 HEAD)** → BATCH-13 added **zero** failures. The 25 are pre-existing (heavy end-to-end scenario suite — DistributedTank/Ballistics/SensorGrid/UrbanCombat/ComponentDamage RunToCompletion); recorded as STR-D15. Not stride-13's doing.

## Issues Found
No blocking issues. The 25 pre-existing `Fdp.Examples.Scenarios.Tests` failures are unrelated baseline state (STR-D15).

## Verdict
APPROVED. Proceed to BATCH-14: STR-P4-T3 (locomotion bridge — `AnimationRuntimeBridgeSystem` reads physics-sourced `SimVelocity` → `UpdateLocomotionInputs`) + STR-P4-T4 (montage dispatch via `OffMeshLinkDetectionSystem`), wiring the backend into `editor_stride` and adding the visible **Walk/Run/Jump harness test cases** (per the standing requirement to register test cases each phase).

## Commit Message
```
feat(stride): real StrideAnimationBackend + PerEntityBlendTreeBuilder + mannequin anim content (BATCH-13)

Completes STR-P4-T1, STR-P4-T2
- StrideAnimationBackend: full IAnimationBackend (modeled on FakeAnimationBackend); pure LocomotionBlend
  computes idle/walk/run weights from speed (headless-tested); Stride AnimationComponent application is
  the GPU-bound part; root-motion hooks left unimplemented (DD-1 §19)
- PerEntityBlendTreeBuilder : IBlendTreeBuilder (modeled on template AnimationController)
- CharacterAnimationDefDto for the mannequin (Idle/Walk/Run + Jump_Start/Loop/End -> Animations/*),
  attached to InfantrySoldier(2002)/Insurgent(2003); bakes to CharacterAnimationDefRuntime
Tests: 28 Animation (+24) / 58 Game / 215 Core green. No regression vs baseline in Fdp.Examples.Scenarios
  (25 pre-existing failures unchanged with/without this batch).
```
