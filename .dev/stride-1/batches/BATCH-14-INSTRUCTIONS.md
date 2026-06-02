# BATCH-14: Locomotion bridge + montage dispatch — Phase 4 complete
**Tasks:** STR-P4-T3, STR-P4-T4   **Phase:** P4 (Animation)   **Est:** ~8–10h
**Dependencies:** BATCH-13 (`StrideAnimationBackend` + `PerEntityBlendTreeBuilder` + mannequin `CharacterAnimationDefDto`), BATCH-12 (test harness), BATCH-10 (live app + visual binding attaches `AnimationComponent` for skinned models).

Goal — finish animation: (T3) wire `AnimationRuntimeBridgeSystem` to read physics-sourced `SimTransform`+`SimVelocity` → `UpdateLocomotionInputs` so a moving mannequin blends walk→run by speed; (T4) `OffMeshLinkDetectionSystem` → `AnimationChannel.PlayMontage` → Jump montage. Wire the `StrideAnimationBackend` + bridge into `editor_stride` so the spawned mannequins actually animate, and add **visible Walk/Run/Jump harness test cases**. Bridge/dispatch *logic* is testable headlessly; the visible skeletal animation is human-verified on GPU.

No Corrective Task 0.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — working contract.
2. `.dev/stride-1/Stride-Integration_v0_3.md` §6.4 (animation), §6.2 (animation entity composes on the physics transform).
3. `.dev/anim-ctrl/DD-1_MuscleCharacterRuntime_v1_2.md` §10 (`AnimationRuntimeBridgeSystem`).
4. `.dev/stride-1/TASK-DETAIL.md` — STR-P4-T3, STR-P4-T4. + `reviews/BATCH-13-REVIEW.md`.

Use the **codebase-memory MCP first** (project `D-Work-IOS-IG-SimHost-FDP`).

### Verified facts & exact references
- **Locomotion bridge** = `AnimationRuntimeBridgeSystem` ([AnimationRuntimeBridgeSystem.cs](../../../Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Systems/AnimationRuntimeBridgeSystem.cs)) — reads `SimTransform`+`SimVelocity`, calls `IAnimationBackend.UpdateLocomotionInputs`. On the Stride node, `SimVelocity` is physics-sourced (BATCH-06 reverse-sync) — in the current NoOp-physics app it's ~0, so the harness will drive it directly to exercise the bridge.
- **Backend ↔ AnimationComponent connection:** the `StrideVisualFactory` (BATCH-03) attaches an `AnimationComponent` to skinned (mannequin) visuals; `StrideVisualReference` links the FDP entity ↔ Stride visual. `StrideAnimationBackend.RegisterEntity` must be connected to that entity's `AnimationComponent` so the backend drives the right skeleton. **[VERIFY]** the cleanest connection (e.g. the backend resolves the Stride visual entity via the visual binding / a per-entity registration hook on appear). Document it.
- **Montage dispatch (T4):** `OffMeshLinkDetectionSystem` ([OffMeshLinkDetectionSystem.cs](../../../FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/OffMeshLinkDetectionSystem.cs)) signals traversal; route it to `IAnimationBackend.PlayMontageOnSlot` (the `AnimationChannel.PlayMontage` path, §6.4) → Jump montage (Start/Loop/End). **[VERIFY]** the existing animation-channel/montage dispatch seam (how montage requests reach the backend today — there may be a montage-dispatch system in `Hrot.MuscleCharacter.Animation`).
- **Animation module wiring:** [VERIFY] how the animation backend + `AnimationRuntimeBridgeSystem` are registered (an `AnimationMuscleModule` / the bootstrapper's systems registrar, §6.4) and register them in `EditorStrideSubsystem` with the real `StrideAnimationBackend` as the `IAnimationBackend` (replacing any fake).
- **Harness:** register cases via `registry.Register(new VisualTestCase("Label","Desc", ctx => {...}))` (BATCH-12). `TestHarnessContext` exposes World/ScenarioSource/VisualBindingSystem + a per-frame `RegisterUpdate` hook.

**Complete tasks in sequence (T3 → T4); do NOT start T4 until T3 is implemented, tested, and ALL tests (incl. prior batches') pass.** If you change a shared `Fdp.Toolkits`/`Hrot.MuscleCharacter.Animation` system, **re-verify no new failures** vs baseline (SimHost 38, Fdp.Examples.Scenarios 25, anim-subsystem). Work autonomously. Only stop on a genuine breaking design flaw.

---

## Task 1: Locomotion bridge wired into editor_stride (STR-P4-T3)
**Files:** wire into `EditorStrideSubsystem` + connect the backend to per-entity `AnimationComponent`s; minimal glue in `Hrot.Stride.Core`/`Hrot.Stride.Animation`/Game as needed. Spec: design §6.4, DD-1 §10.
Register the `StrideAnimationBackend` (as `IAnimationBackend`) + `AnimationRuntimeBridgeSystem` in `editor_stride`; connect `RegisterEntity`/`UnregisterEntity` to the spawned mannequins' `AnimationComponent`s (via the visual binding). The bridge drives `UpdateLocomotionInputs` from `SimTransform`+`SimVelocity` each frame; the backend blends idle→walk→run.

**Tests required** (headless — bridge/registration logic):
- A moving entity (nonzero `SimVelocity` at walk speed) → the bridge calls `UpdateLocomotionInputs` and the backend's computed blend favors **walk**; at run speed → **run**; at rest → **idle** (assert via the backend's blend weights / `LocomotionBlend`, building on BATCH-13's tested logic — now driven through the bridge from `SimVelocity`).
- Backend register/unregister is wired to entity appear/death (a mannequin entity gets registered with the backend on spawn, unregistered on death) — assert via the backend's active-entity set.

## Task 2: Montage dispatch (STR-P4-T4)
**Files:** the dispatch glue (`OffMeshLinkDetectionSystem` → backend montage) + harness. Spec: design §6.4.
Route an off-mesh-link traversal (the `OffMeshLinkDetectionSystem` signal) to `IAnimationBackend.PlayMontageOnSlot` for the Jump montage (Start/Loop/End on the correct slot).

**Tests required** (headless):
- A simulated off-mesh-link traversal trigger → `PlayMontageOnSlot` is called with the Jump montage on the correct slot; the backend reports that slot active (`IsAnySlotActive`).
- Montage start/loop/end sequencing reflected in the backend slot state.

## Task 3: Visible harness test cases (required this phase)
Register in the test harness (BATCH-12), driving behavior that's **visible on the live mannequins** (physics is NoOp, so the cases drive `SimVelocity`/`SimTransform` directly):
- **"Walk Mannequin"** — spawn/select a mannequin and drive it forward at walk speed (set `SimVelocity` + advance `SimTransform` each frame via `RegisterUpdate`), so the locomotion blend plays the **walk** animation while it moves.
- **"Run Mannequin"** — same at run speed → **run** blend.
- **"Trigger Jump"** — fire the montage path on a mannequin → Jump montage plays.
- Each case logs via NLog what it triggered. Document the controls (button + D-key).

## Success Criteria
- [ ] STR-P4-T3: backend + `AnimationRuntimeBridgeSystem` wired into `editor_stride`, connected to mannequin `AnimationComponent`s; bridge drives walk/run/idle blend from `SimVelocity` (logic headless-tested); register/unregister on appear/death.
- [ ] STR-P4-T4: off-mesh-link traversal → Jump montage via the backend (headless-tested slot state).
- [ ] Walk/Run/Jump harness test cases registered + documented (human-visible on GPU).
- [ ] Full test suite green (all prior batches + this); no new failures vs the SimHost/Scenarios/anim baselines; Stride solution builds clean; report submitted.

## Report Requirements (`reports/BATCH-14-REPORT.md`)
Answer: the backend↔`AnimationComponent` connection mechanism you used ([VERIFY] result); how the animation module/backend is registered in `editor_stride`; the montage-dispatch seam (how `OffMeshLinkDetectionSystem` reaches `PlayMontageOnSlot`); how the bridge walk/run/idle selection is tested via `SimVelocity`; the Walk/Run/Jump harness cases + controls (button + D-key); any shared-system change + its baseline-regression check result; what's GPU-deferred (actual skeletal playback); test counts; suggested commit message. Do NOT claim skeletal playback works.
