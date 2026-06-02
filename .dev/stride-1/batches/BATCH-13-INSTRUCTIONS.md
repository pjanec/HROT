# BATCH-13: StrideAnimationBackend + demo animation content (Phase 4 start)
**Tasks:** STR-P4-T1, STR-P4-T2   **Phase:** P4 (Animation)   **Est:** ~10–12h
**Dependencies:** BATCH-01 (the `StrideAnimationBackend` *stub* in `Hrot.Stride.Animation`), the existing `anim-ctrl` infrastructure (real `IAnimationBackend`, `FakeAnimationBackend`, `CharacterAnimationDefDto`/`Runtime`, `AnimationTkbTranslator`, `AnimationRuntimeBridgeSystem`).

Goal: replace the P0 stub with a **real** `StrideAnimationBackend : IAnimationBackend` + `PerEntityBlendTreeBuilder : IBlendTreeBuilder` (idle/walk/run blend + montages), and author the `CharacterAnimationDefDto` demo content for the mannequin. **Same seam reality:** actual Stride skeletal playback needs `AnimationComponent` on a running game (GPU) — so split the **testable blend/montage logic** (weights from locomotion inputs, montage state) from the **GPU-bound Stride playback** (applying weights to `AnimationComponent`). Validate the logic headlessly (mirroring `FakeAnimationBackend`'s tested behavior); the visible walk/run/jump on the mannequin is verified by the human (and lands as harness test cases in BATCH-14).

No Corrective Task 0.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — working contract.
2. `.dev/stride-1/Stride-Integration_v0_3.md` §6.4 (animation — the spec).
3. The `anim-ctrl` design (authoritative for the seam + descriptor): `.dev/anim-ctrl/DD-1_MuscleCharacterRuntime_v1_2.md` (§15 the `IAnimationBackend`/`IBlendTreeBuilder` seam, §10 `AnimationRuntimeBridgeSystem`, §19 root-motion hooks — leave unimplemented), `.dev/anim-ctrl/DD-4_TKB_AnimationDescriptor_v1_2.md` (the `CharacterAnimationDefDto` schema), `.dev/anim-ctrl/DD-Fake_FakeAnimationBackend_v1_1.md` (the reference impl to mirror).
4. `.dev/stride-1/TASK-DETAIL.md` — STR-P4-T1, STR-P4-T2.

Use the **codebase-memory MCP first** (project `D-Work-IOS-IG-SimHost-FDP`).

### Verified facts & exact references
- **The interface** = `IAnimationBackend` ([Contracts/IAnimationBackend.cs](../../../Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Contracts/IAnimationBackend.cs), 17 members) — the BATCH-01 stub `StrideAnimationBackend` in `Stride/Hrot.Stride.Animation/StrideAnimationBackend.cs` currently throws; **implement it for real now.** `IBlendTreeBuilder` is the per-entity blend-tree contract (find it near `IAnimationBackend`).
- **Reference impl** = `FakeAnimationBackend` ([FakeAnimationBackend.cs](../../../Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Fake/FakeAnimationBackend.cs)) — the **contract-complete, deterministic, render-free** implementation. Mirror its semantics (registration, `UpdateLocomotionInputs` → blend weights from speed thresholds, montage slot state, notifies). The real backend differs only in that it drives a Stride `AnimationComponent` instead of a fake.
- **Descriptor** = `CharacterAnimationDefDto` ([CharacterAnimationDefDto.cs](../../../Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Descriptors/CharacterAnimationDefDto.cs)); bakes to `CharacterAnimationDefRuntime` via `AnimationTkbTranslator` ([AnimationTkbTranslator.cs](../../../Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Translators/AnimationTkbTranslator.cs)). `CharacterAnimationDefRuntime.BakeForTest` is the documented test seam.
- **Stride blend-tree reference** = the template `AnimationController` ([Stride/HrotStrideApp.Game/Player/AnimationController.cs](../../../Stride/HrotStrideApp.Game/Player/AnimationController.cs)) — the working Stride idle/walk/run `IBlendTreeBuilder` example on the mannequin (DD-1 §15.3). Model `PerEntityBlendTreeBuilder` on it. **[VERIFY]** the Stride 4.2.1.2487 `AnimationComponent` blend API (`AnimationComponent.Animations`, `Blend`, `PlayingAnimation`, blend trees / `AnimationBlendOperation`).
- **Asset URLs** (§12, template-seeded): `Animations/Idle`, `Animations/Walk`, `Animations/Run`, `Animations/Jump_Start`, `Animations/Jump_Loop`, `Animations/Jump_End`; the mannequin model `Models/mannequinModel` + its skeleton.
- `Hrot.Stride.Animation` references `Stride.Engine` (incl. `Stride.Animations`) + `Hrot.MuscleCharacter.Animation` (BATCH-01).

**Complete tasks in sequence (T1 → T2); do NOT start T2 until T1 is implemented, tested, and ALL tests (incl. prior batches') pass.** Work autonomously. Only stop on a genuine breaking design flaw.

---

## Task 1: `StrideAnimationBackend` + `PerEntityBlendTreeBuilder` (STR-P4-T1)
**Files:** `Stride/Hrot.Stride.Animation/StrideAnimationBackend.cs` (replace the stub), `Stride/Hrot.Stride.Animation/PerEntityBlendTreeBuilder.cs` (NEW). Spec: design §6.4, DD-1 §15.
Implement the full `IAnimationBackend` for real, modeled on `FakeAnimationBackend` (registration/unregistration per entity, locomotion blend, montage slots, notifies, stance) but driving a Stride `AnimationComponent`. `PerEntityBlendTreeBuilder : IBlendTreeBuilder` builds the per-entity idle/walk/run blend tree (modeled on the template `AnimationController`). **Separate the testable logic** — the **blend-weight derivation from locomotion inputs (speed → idle/walk/run weights by threshold)** and the **montage slot state machine** must be unit-testable without a `GraphicsDevice`; the Stride `AnimationComponent` application is the GPU-bound part (document it as such; it's exercised by the human run + BATCH-14). Leave root-motion hooks (DD-1 §19) unimplemented per design.

**Tests required** (headless — assert real values):
- Backend `RegisterEntity`/`UnregisterEntity` per entity (handle valid → invalid after unregister; `TryResolve` reflects it).
- **Idle/Walk/Run blend weights derive from locomotion inputs by speed thresholds** — feed a battery of speeds (0, slow, walk, run) and assert the computed blend weights match the thresholds (this is the core behavioral test; mirror `FakeAnimationBackend`'s tested weight logic).
- Montage slot state: `PlayMontageOnSlot` → slot active; `IsAnySlotActive`/`IsAnySlotInBlendOut`/`StopMontageOnSlot` reflect state; notifies drain.
- The backend satisfies the full `IAnimationBackend` contract at runtime (no `NotImplementedException` left).

## Task 2: `CharacterAnimationDefDto` demo content (STR-P4-T2)
**Files:** author the descriptor content + attach to templates (likely in `UrbanCombatNewScenario` alongside the `StrideRenderModelDefDto`, or the animation content authoring point — [VERIFY] where demo TKB content is authored). Spec: design §6.4, §12; schema per DD-4.
Author a `CharacterAnimationDefDto` for the mannequin class: locomotion clip refs (`Animations/Idle|Walk|Run`) + Jump montages (`Animations/Jump_Start|Jump_Loop|Jump_End`), wired to the Stride asset URLs, with slots/stances per the schema. Attach it to `InfantrySoldier` (2002) and `Insurgent` (2003).

**Tests required:**
- The descriptor **bakes into `CharacterAnimationDefRuntime`** via `AnimationTkbTranslator` (use `BakeForTest` if needed) — assert the runtime carries the locomotion clip refs + montage refs.
- Montage `AssetRef`s resolve to the `Animations/*` URLs; slots/stances validate.
- The `InfantrySoldier`/`Insurgent` templates carry the `CharacterAnimationDefDto` after registration (assert via the TKB DB, mirroring how the `StrideRenderModelDefDto` presence is tested).

## Success Criteria
- [ ] STR-P4-T1: real `StrideAnimationBackend` (full `IAnimationBackend`, no stubs) + `PerEntityBlendTreeBuilder`; blend-weight + montage logic unit-tested headlessly; Stride `AnimationComponent` application documented as the GPU-bound part.
- [ ] STR-P4-T2: mannequin `CharacterAnimationDefDto` authored + attached to InfantrySoldier/Insurgent; bakes to `CharacterAnimationDefRuntime`; clip/montage refs resolve.
- [ ] Full test suite green (all prior batches + this); Stride solution builds clean; report submitted.

## Report Requirements (`reports/BATCH-13-REPORT.md`)
Answer: how you split the testable blend/montage logic from the GPU-bound Stride `AnimationComponent` application (and what exactly is GPU-deferred); the Stride 4.2.1.2487 `AnimationComponent` blend API used ([VERIFY] result, modeled on the template `AnimationController`); where you authored the `CharacterAnimationDefDto` content + how it attaches to InfantrySoldier/Insurgent; the speed→blend-weight thresholds and how they're tested; whether the real backend can be wired into `editor_stride` now or needs BATCH-14's locomotion bridge first (note the dependency for BATCH-14, which adds the visible walk/run/jump harness cases); weak points; suggested one-line commit message. Report actual test counts. Do NOT claim skeletal playback works (GPU).
