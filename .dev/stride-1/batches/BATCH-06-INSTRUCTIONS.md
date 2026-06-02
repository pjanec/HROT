# BATCH-06: Reverse-sync + split-authority sync + timestep ordering (Phase 1 capstone)
**Tasks:** STR-P1-T5, STR-P1-T6, STR-P1-T7   **Phase:** P1   **Est:** ~10–12h
**Dependencies:** BATCH-04 (body lifecycle, `IPhysicsBodyService`, `PhysicsBodyReference`), BATCH-05 (motors, `PostCollision*` velocity channel).

Goal — close Phase 1's logic: (T5) `BulletReverseSyncSystem` writes Bullet-resolved pose + velocity into `SimTransform`/`SimVelocity` for owned entities, in a `TogglablePostSimulationGroup` (resolves STR-D5), honoring the velocity invariant; (T6) `SplitAuthorityStrideSyncScript` replaces the P0 forward-sync — Pass A reconciles the visual set, Pass B forward-syncs **non-owned** only; (T7) fixed-timestep + reverse-sync ordering so FDP `Simulation`-phase consumers read post-physics `SimTransform` the same frame. Also wire the BATCH-05 motors + the reverse-sync into `editor_stride`. The concrete `BulletPhysicsBodyService` + real-engine validation remain deferred (STR-D11) — everything here is proven against the seam/fake.

No Corrective Task 0 (BATCH-05 approved). This batch **resolves STR-D5** (togglable groups).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — working contract.
2. `.dev/stride-1/Stride-Integration_v0_3.md` §7 (the split-authority sync — spec for T5+T6), §6.1 (velocity invariant; per-frame post-physics reverse-sync), §8.3 (host-loop ordering — spec for T7), §9 (togglable group + replay severability — why the group).
3. `.dev/stride-1/TASK-DETAIL.md` — STR-P1-T5, STR-P1-T6, STR-P1-T7.
4. `reviews/BATCH-05-REVIEW.md` + `DEBT-TRACKER.md` (STR-D5, STR-D11, STR-D12).

Use the **codebase-memory MCP first** (project `D-Work-IOS-IG-SimHost-FDP`).

### Verified facts & exact references
- **`TogglablePostSimulationGroup`** / **`TogglableSimulationGroup`**: `Fdp.ModuleHost.Scheduling` ([TogglablePostSimulationGroup.cs](../../../FDP/Engine/Fdp.ModuleHost/Scheduling/TogglablePostSimulationGroup.cs)). [VERIFY] the constructor + the `Enabled` flag + how `EditorSubsystem`/`StrideNodeBootstrapper` wrap systems in it (mock exposes `PostSimGroup`). Has tests `TogglablePostSimulationGroup_WhenDisabled_SkipsAllInnerSystems` — model the Enabled=false test on these.
- **Two-pass reconciliation pattern** for T6 = the mock's [SyncFdpToStrideScript.cs](../../../Hrot/Subsystems/Hrot.StrideMock/SyncFdpToStrideScript.cs) (Pass 1 `IsAlive` destructions, Pass 2 query upsert). T6's Pass A delegates visual existence to `StrideVisualBindingSystem` (BATCH-03); Pass B forward-syncs transforms for `.WithoutOwned<SimTransform>()` only.
- **Velocity channel** (BATCH-05): kinematic bodies' post-collision velocity is on `PhysicsBodyReference.PostCollision{Linear,Angular}VelocityFdp`. Dynamic bodies' velocity must come from the body itself — **extend `IPhysicsBodyService`** with a read, e.g. `BodyState GetBodyState(bodyHandle)` returning the Stride-space pose + linear + angular velocity + an `IsKinematic` flag. Concrete impl reads `RigidbodyComponent.LinearVelocity/.AngularVelocity` (deferred, STR-D11); fake returns scripted values.
- Authority queries `.WithOwned<T>()`/`.WithoutOwned<T>()` (Fdp.Core.QueryBuilder). `FdpStrideTransform` for all conversions.
- `EditorStrideSubsystem.Tick` currently: `OrchestrationBus.SwapBuffers(); ClusterMaster.Tick(); TimeController.Step(dt); Kernel.Update();` + (if factory) `VisualBindingSystem.Sync(World)`. The reverse-sync must run **before** `Kernel.Update()` (so FDP Simulation consumers read post-physics `SimTransform`), and the motors must run before the (conceptual) physics step.

**Complete tasks in sequence (T5 → T6 → T7); do NOT start the next until the current is implemented, tested, and ALL tests (incl. prior batches') pass.** Work autonomously. Only stop on a genuine breaking design flaw or unrecoverable blocker.

---

## Task 1: `BulletReverseSyncSystem` in `TogglablePostSimulationGroup` (STR-P1-T5)
**File:** `Stride/Hrot.Stride.Core/BulletReverseSyncSystem.cs` (NEW). Extend `IPhysicsBodyService` with `GetBodyState`. Spec: design §7 (reverse path), §6.1 (velocity invariant), §9 (togglable group).
For each `.WithOwned<SimTransform>()` entity that has a `PhysicsBodyReference`: read the resolved pose + velocity, write `SimTransform.Position`/`.Rotation` (via `FdpStrideTransform.ToFdpPosition`/`ToFdpRotation`) and `SimVelocity` (lin+ang, via `ToFdpVelocity`/`ToFdpAngularVelocity`). **Velocity invariant:** dynamic bodies → use `GetBodyState` lin/ang; kinematic bodies → use `PhysicsBodyReference.PostCollision*`; **a collision-arrested body must write `SimVelocity` = zero that frame** (no stale velocity). The system runs once per frame and is registered inside a `TogglablePostSimulationGroup`.

**Tests required** (headless, scriptable fake `IPhysicsBodyService`):
- Owned dynamic body with a known stride pose → `SimTransform.Position`/`.Rotation` equal the swizzled FDP pose (assert numeric values via `FdpStrideTransform`).
- Dynamic body velocity → `SimVelocity.Linear`/`.Angular` set from the body's lin/ang (correct swizzle).
- **Collision arrest:** fake reports zero velocity this frame → `SimVelocity` written **exactly zero** (not stale from a prior frame — set a non-zero velocity first, then zero, assert it zeroes).
- Kinematic body → `SimVelocity` taken from `PhysicsBodyReference.PostCollision*` (set those, assert they reach `SimVelocity`).
- **Replay severability:** wrap the system in a `TogglablePostSimulationGroup`; with `Enabled=false`, no writes occur (set up an owned body, disable the group, tick, assert `SimTransform`/`SimVelocity` unchanged).

## Task 2: `SplitAuthorityStrideSyncScript` (STR-P1-T6)
**File:** `Stride/Hrot.Stride.Core/SplitAuthorityStrideSyncScript.cs` (NEW). Spec: design §7 (forward path + reconciliation).
Replace the BATCH-03 P0 forward-sync in `EditorStrideSubsystem` with the authority-forked version: **Pass A** reconciles the Stride visual set via `StrideVisualBindingSystem` (existence: appear→create, die→teardown — already its job; this script drives its `Sync`); **Pass B** forward-syncs the visual transform from `SimTransform` (via `FdpStrideTransform`) for `.WithoutOwned<SimTransform>()` entities **only** — owned entities are skipped (their Stride body is physics-driven). Keep it decoupled from the GPU via the existing `IStrideVisualFactory` (`UpdatePose` for non-owned).

**Tests required** (headless, fake factory):
- An **owned** entity is **not** forward-synced (its visual pose is not written from `SimTransform` by Pass B — assert `UpdatePose` is not called for it via the non-owned path, or that an owned entity's manual `SimTransform` change does not propagate through Pass B).
- A **non-owned** entity's visual transform follows `SimTransform` via `FdpStrideTransform` (construct a `.WithoutOwned<SimTransform>()` entity, set its `SimTransform`, tick, assert the swizzled pose reached the factory).
- Appear/disappear reconciliation still spawns/tears down visuals (Pass A).

## Task 3: Fixed timestep + reverse-sync ordering (STR-P1-T7)
**Files:** wire into `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs` + `StrideHrotGame`. Spec: design §6.1, §8.3.
Order the per-frame work so the reverse-sync (T5) runs **before** the FDP kernel tick (`Kernel.Update()`), so `Simulation`-phase consumers (`SpatialHashSystem`, vision broadphase, EQS) read the post-physics `SimTransform` the same frame (no one-frame lag); and the motors (BATCH-05) run before the (conceptual) physics step. Register the motors + reverse-sync into `editor_stride` (against the seam — a fake/no-op `IPhysicsBodyService` is acceptable in `editor_stride` until the concrete service lands at GPU bring-up; document the wiring). Configure the fixed Bullet timestep config location (GameSettings `PhysicsSettings`, [VERIFY]) — note it even if the concrete sim isn't headlessly runnable. The fixed Bullet timestep (via `StrideHostLoopDriver`, BATCH-02) is the sim clock regardless of render rate.

**Tests required** (integration, headless via `EditorStrideSubsystem`):
- **Same-frame post-physics read:** with the reverse-sync ordered before `Kernel.Update()`, a value written by the reverse-sync into `SimTransform` this frame is observed by an FDP `Simulation`-phase reader the **same** frame (no one-frame lag). Drive the fake `IPhysicsBodyService` to report a known pose, tick once, and assert a Simulation-phase consumer (e.g. via `SpatialHashSystem`'s spatial grid, or a probe system) sees the post-reverse-sync position that frame.
- **Fixed clock:** simulation advances on the fixed step independent of render frame count (reuse/extend the `StrideHostLoopDriver` determinism already proven in BATCH-02; assert the sim tick count is governed by the fixed step, not render cadence, in the wired loop).

---

## Success Criteria
- [ ] STR-P1-T5: `BulletReverseSyncSystem` writes owned pose+velocity (swizzled), honors the velocity invariant (zero on arrest), reads kinematic velocity from `PhysicsBodyReference.PostCollision*`, and is severable via `TogglablePostSimulationGroup.Enabled=false`. STR-D5 resolved.
- [ ] STR-P1-T6: `SplitAuthorityStrideSyncScript` forward-syncs non-owned only (owned skipped), with Pass-A reconciliation; replaces the P0 forward-sync.
- [ ] STR-P1-T7: reverse-sync ordered before the kernel tick (same-frame post-physics read proven); motors + reverse-sync wired into `editor_stride`; fixed-clock independence asserted.
- [ ] Full test suite green (all prior batches + this); Stride solution builds clean (no new warnings beyond pre-existing NU1608); report submitted.

## Report Requirements (`reports/BATCH-06-REPORT.md`)
Answer: the `TogglablePostSimulationGroup` constructor/`Enabled` API and how you wrapped the reverse-sync ([VERIFY] result); the `IPhysicsBodyService.GetBodyState` shape you added and how dynamic-vs-kinematic velocity sourcing works; how you proved the velocity invariant (the zero-on-arrest test); how the same-frame post-physics ordering test observes the reverse-synced value; what `IPhysicsBodyService` you wired into `editor_stride` (fake/no-op vs concrete) and why; the GameSettings fixed-timestep config location ([VERIFY]); what remains for the concrete `BulletPhysicsBodyService` + GPU validation (STR-D11) — i.e. the full list of Phase-1 behaviors still only seam-tested; weak points; suggested one-line commit message. Report actual test counts/output. Do NOT ask comprehension questions.
