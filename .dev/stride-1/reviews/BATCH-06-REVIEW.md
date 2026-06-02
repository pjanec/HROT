# BATCH-06 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Summary
`BulletReverseSyncSystem` in a `TogglablePostSimulationGroup` (T5, resolves STR-D5), `SplitAuthorityStrideSyncScript` (T6), and reverse-sync-before-kernel ordering + motor/reverse-sync wiring into `editor_stride` (T7). **Phase 1 logic is complete.** Verified: read the reverse-sync + tests, ran the suite (Core 126, Animation 4, Game 33 = 163 green).

## Verification performed
- `BulletReverseSyncSystem`: owned-only query; writes `SimTransform` via `ToFdpPosition/ToFdpRotation`; **velocity invariant** — dynamic bodies use `GetBodyState` lin/ang (zero-on-arrest flows straight from the solver), kinematic bodies use `PhysicsBodyReference.PostCollision*` (already zeroed on full block by the motor). Writes `SimVelocity` every frame for every owned body → never stale.
- Confirmed the zero-on-arrest test is real (`CollisionArrest_..._NoStale`: non-zero velocity frame 1, zero frame 2, asserts all 6 `SimVelocity` components exactly 0) and the kinematic-blocked test asserts exact zero. Severability (`Enabled=false` → no writes) covered (T5-SC5).
- `SplitAuthorityStrideSyncScript`: Pass A existence-only reconciliation (new `StrideVisualBindingSystem.SyncExistenceOnly`), Pass B forward-sync for `.WithoutOwned<SimTransform>()` only; owned entities skipped. Replaces the P0 forward-sync.
- T7: `EditorStrideSubsystem.Tick` order = motors → `ReverseSyncGroup.Execute` (before `Kernel.Update()`) → `SplitSync.Sync`. Same-frame post-physics read proven by a `ProbeCaptureSystem` integration test (+ a negative test documenting the one-frame-lag failure for wrong order). `NoOpPhysicsBodyService` wired in editor_stride (documented STR-D11 placeholder). Fixed timestep set in `GameSettings.sdgamesettings` (`PhysicsSettings.FixedTimeStep = 1/60`).
- Ran the suite myself; counts match.

## Issues Found
No blocking issues.

## Notes carried forward (for the concrete bring-up, STR-D11)
- **Owned visual ↔ physics body unification (new — STR-D13).** With T6, owned entities' visuals are no longer pose-updated by the sync (correct — they must follow the Bullet body). But the current concrete `StrideVisualFactory` creates the visual as a *separate* entity from the physics body; the design (§6.2) requires the visual to **be** the physics body (or a child of it) so Bullet motion moves the visual. The concrete `BulletPhysicsBodyService` + factory must unify them at GPU bring-up, else owned entities render frozen.
- All of Phase 1's actual physics behavior (body creation, character/vehicle motion, collision response, velocity readback, the reverse-sync against real bodies) is **seam-tested only**. The concrete `BulletPhysicsBodyService` + a real-engine (PhysicsProcessor/GPU) validation pass is the single deferred milestone that retires STR-D4/D11/D13. Strongly recommend running it on a GPU machine before/early in P2–P3.

## Verdict
APPROVED. **Phase 1 complete** (logic). Proceed to Phase 2 — DotRecast navigation (STR-P2-T1..T5). Note: DotRecast is pure managed .NET, so P2's navmesh bake + pathfinding **can be validated headlessly** (no GPU) — a phase where real fidelity returns.

## Commit Message
```
feat(stride): reverse-sync + split-authority sync + timestep ordering — Phase 1 complete (BATCH-06)

Completes STR-P1-T5, STR-P1-T6, STR-P1-T7
- BulletReverseSyncSystem: owned bodies' Bullet pose+velocity -> SimTransform/SimVelocity via
  FdpStrideTransform; velocity invariant (dynamic from GetBodyState incl. zero-on-arrest, kinematic
  from motor's PostCollision channel); wrapped in TogglablePostSimulationGroup (Enabled=false severs
  for replay — resolves STR-D5)
- IPhysicsBodyService.GetBodyState added (pose + lin/ang velocity + IsKinematic)
- SplitAuthorityStrideSyncScript: Pass A existence reconcile, Pass B forward-sync WithoutOwned only
- EditorStrideSubsystem: motors -> reverse-sync (before Kernel.Update) -> split-sync; same-frame
  post-physics read proven; NoOpPhysicsBodyService placeholder (concrete impl = STR-D11);
  fixed timestep 1/60 in GameSettings
Tests: 163 (126 Core, 4 Animation, 33 Game). Phase-1 physics behavior remains seam-tested (STR-D11).
```
