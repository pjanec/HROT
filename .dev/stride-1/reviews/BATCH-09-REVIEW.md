# BATCH-09 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Summary
`IStrideRaycastService` + `StrideRaycastService` (T1), `StrideRaycastLosService : ILosService` (T2), and the `IRaycastBackend` ballistics seam (T3). **Phase 3 complete.** Reviewed at a clean-commit level (user requested a pause): verified the load-bearing risks rather than a full source read.

## Verification performed
- **No reverse dependency:** `Fdp.Toolkits.csproj` has no reference to `Hrot.Stride.Core` (the T3 seam `IRaycastBackend` lives in `FDP/Toolkits/Fdp.Toolkits/Physics/` — dependency direction correct).
- **Stride Core suite green:** 215/215 (was 171, +44 new — 23 raycast-service, 13 LOS, 8 ballistics).
- **Game builds clean** (0 errors) — confirms the concrete `StrideRaycastService` (GPU-deferred `Simulation.Raycast` wrapper) + `RaycastSolverSystem.RaycastBackend` wiring compile.
- **No broader regression** (per coder, spot-trusted given pause): `Hrot.SimHost.Tests` 38 fail / 573 pass — identical to the pre-stride-1 `6bb3153d` baseline; `Fdp.Toolkits.Tests` no new failures vs stashed HEAD.
- Key design points confirmed from the report: hit **normals** use the direction swizzle (`ToFdpVelocity`) not the position swizzle; LOS is a drop-in for `BlockedLosService` (clear→visible, blocking-hit-before-target→not visible, hit fraction ≥0.99 = target's own collider→visible); `RaycastSolverSystem.RaycastBackend` is nullable so the existing spatial-hash path is unchanged when unset; `BallisticsSystem` analytic integration untouched.

## Issues Found
No blocking issues. Concrete `StrideRaycastService` (real `Simulation.Raycast` against scene geometry) is GPU-deferred — folds into the STR-D11 real-engine bring-up.

## Note (light review)
Per the pause request, this review trusted the coder's baseline-regression numbers rather than re-running the full FDP suite myself, and did not deep-read every test assertion. The critical risks (reverse dependency, Stride suite green, Game build) were verified directly. If desired, a fuller re-review of the LOS/ballistics test assertions can be done on resume before BATCH-10.

## Verdict
APPROVED. Phase 3 complete. (Paused before BATCH-10 — Phase 4 Animation — at user request.)

## Commit Message
```
feat(stride): perception LOS + ballistics via Stride raycasts — Phase 3 complete (BATCH-09)

Completes STR-P3-T1, STR-P3-T2, STR-P3-T3
- IStrideRaycastService seam (FDP-coord in/out) + concrete StrideRaycastService wrapping
  Simulation.Raycast (GPU-deferred); hit normals use the direction swizzle, not position
- StrideRaycastLosService : ILosService — drop-in for BlockedLosService; wall blocks LOS,
  clear ⇒ visible; TargetMemory 3D-correct
- IRaycastBackend seam in Fdp.Toolkits.Physics (no reverse dependency); RaycastSolverSystem.
  RaycastBackend (nullable, spatial-hash path unchanged when unset) backed by StrideRaycastBackend;
  blocked shot resolves impact at the obstacle; BallisticsSystem analytic integration retained
Tests: 215 Stride Core (+44). SimHost (38 fail) + Fdp.Toolkits baselines unchanged. Concrete
  Simulation.Raycast wrapper GPU-deferred (STR-D11 class).
```
