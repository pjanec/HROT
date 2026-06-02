# BATCH-08 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Summary
`DotRecastDtCrowdProvider` (T3, real DtCrowd), `CrowdAgentUpdateSystem` velocity-only refactor (T4, **resolves STR-D12**), and road-graph/`Auto`-selection verification (T5). **Phase 2 complete.** Verified: read the refactored shared system + provider, ran the Stride suite (171 Core green), and — because T4 edits **shared FDP engine code** — performed a cross-node regression check.

## Cross-node regression verification (the key review for this batch)
T4 removed the `tf.Position += velocity*dt` integration from `CrowdAgentUpdateSystem`, which is shared (used by the non-Stride SimHost node too). The only production consumer of `CrowdMotorIntent` is `BulletCharacterMotor` (Stride), so I checked whether SimHost crowd movement regressed:
- `Hrot.SimHost.Tests` at current HEAD+working-tree: **38 failed / 573 passed / 3 skipped**.
- `Hrot.SimHost.Tests` at the **pre-stride-1 baseline `6bb3153d`** (isolated git worktree, full rebuild): **identical 38 failed / 573 passed / 3 skipped**.
- Conclusion: stride-1 (incl. `CrowdMotorIntent` ComponentId 265 registration **and** the T4 refactor) introduced **zero** new SimHost failures. The 38 are pre-existing (e.g. `HullDownAttackParams_Is40Bytes` fails 56≠40 — a struct drift unrelated to navigation; the heavier ones resemble the pre-existing STR-D6 class). Recorded as STR-D6-adjacent; **not stride-1's doing**.

## Verification performed
- `DotRecastDtCrowdProvider`: real `DtCrowd` over a baked `DtNavMesh`; agent add/remove/target/update; coordinate swizzle FDP→crowd `(X,Z,Y)` matching the navmesh convention; `GetAgentVelocity` harvested from `agent.vel`. Steering-toward-target asserted (target due East → velocity.X>0 over 10 ticks, magnitude ≤ MaxSpeed). 11 tests.
- `CrowdAgentUpdateSystem`: now writes **only** `CrowdMotorIntent.Velocity`; no `SimTransform`/`SimVelocity` writes; skips `AwaitingTraversal`. The old position-integration unit test was replaced with a position-**unchanged** assertion; an integration test proves crowd-velocity → `CrowdMotorIntent` → `BulletCharacterMotor`.
- T5: `PathfindingSolverSystem.SelectBackend` already implemented `Auto` (prior nav work) — T5 verifies it with real `DotRecastNavmeshProvider` + `ZoneEnvironmentData` singletons (5 integration tests: RoadGraph near road nodes / Navmesh off-road / Hybrid mixed, by the threshold). No duplication of selection logic.
- Stride Core 171 (was 153, +18); FDP Navigation 295 green.

## Issues Found
No blocking issues. One architectural note recorded (STR-D14): the shared `CrowdAgentUpdateSystem` is now velocity-only **for all nodes**; any non-Stride consumer that relied on its self-integration would need a `CrowdMotorIntent` integrator. No test regressed (SimHost is the fake being replaced; the design §5.3 mandates this), so it's acceptable, but flagged so it isn't forgotten.

## Verdict
APPROVED. **Phase 2 complete.** Proceed to Phase 3 — Perception via Stride raycasts (STR-P3-T1..T3). Note: raycasts go through Stride's `Simulation.Raycast` (same `internal`/PhysicsProcessor constraint as bodies — likely a seam + concrete-at-GPU pattern again).

## Commit Message
```
feat(stride): DotRecastDtCrowdProvider + CrowdAgentUpdateSystem velocity-only + Auto selection — Phase 2 complete (BATCH-08)

Completes STR-P2-T3, STR-P2-T4, STR-P2-T5
- DotRecastDtCrowdProvider: real DtCrowd over baked navmesh (IDtCrowdProvider drop-in); steering
  velocity toward target, magnitude<=MaxSpeed; FDP<->crowd swizzle (X,Z,Y)
- CrowdAgentUpdateSystem: writes ONLY CrowdMotorIntent.Velocity, no SimTransform/SimVelocity
  (resolves STR-D12); consumed by BulletCharacterMotor — closes the crowd->motor loop
- PathfindingSolverSystem Auto selection verified with DotRecast + ZoneEnvironmentData singletons
  (RoadGraph/Navmesh/Hybrid by threshold)
Tests: 171 Stride Core (+18) + 295 FDP Navigation, all green. Verified no SimHost regression vs
  pre-stride-1 baseline (38 pre-existing failures identical at 6bb3153d).
```
