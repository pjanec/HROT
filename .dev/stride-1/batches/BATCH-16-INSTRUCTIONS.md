# BATCH-16: Fix live animation playback + record→replay file handle (corrective)
**Tasks:** Fix A (STR-P4 live animation playback), Fix B (STR-P5-T4 record→replay file handle)   **Phase:** corrective / GPU bring-up   **Est:** ~6–9h
**Dependencies:** BATCH-13/14 (anim backend + bridge + `PerEntityBlendTreeBuilder`), BATCH-15 (record/replay), BATCH-10/12 (live app + harness).

**Context (from a real GPU run by the user):** D1–D4 work. **D5–D7: mannequins move but do NOT animate** — the harness/log shows correct blend weights (`speed=1.5 → Walk=1.00`, Jump slot active), so the headless backend logic is right, but **nothing in the live path attaches a `PerEntityBlendTreeBuilder` to the mannequin's `AnimationComponent` / loads the clips / pushes the backend's weights** — so the skeleton never plays. **D9: record→replay crashes** with `IOException: node_0.fdp … being used by another process` at `ReplayModule.RegisterSystems → new PlaybackController(filePath)` — the recording's file handle isn't released before playback opens it.

This batch makes the animation actually play and fixes the replay handle. Both are **GPU/runtime-verified by the human** (you build correctly + compile + keep tests green; the user confirms on a GPU run).

No Corrective Task 0 beyond these two fixes.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md`.
2. `.dev/stride-1/Stride-Integration_v0_3.md` §6.4 (animation), §9 (record/replay).
3. The code: `Stride/Hrot.Stride.Animation/PerEntityBlendTreeBuilder.cs` (the **complete** GPU builder — already implements `BuildBlendTree`/evaluators/blend; it just needs to be instantiated + fed in the live path), `StrideAnimationBackend.cs`, `StrideAnimationBridge.cs` (computes weights; comment notes builder attachment is "done separately by the visual binding"), `StrideVisualFactory.cs` (creates the mannequin `AnimationComponent`), the template `Stride/HrotStrideApp.Game/Player/AnimationController.cs` (the **clip-loading + builder + per-frame weight-push reference** — proven Stride code), `EditorStrideSubsystem.cs` (record/replay wiring), and the replay stack (`RecordingModule.cs`, `ReplayModule.cs`, `PlaybackController` ctor, `EcsRecordReplayController`).
4. `reviews/BATCH-15-REVIEW.md`, `DEBT-TRACKER.md`.

Use the **codebase-memory MCP first** (project `D-Work-IOS-IG-SimHost-FDP`).

**Work autonomously; build clean + keep tests green. If you change shared FDP replay code, re-verify no new failures vs baselines (SimHost 38 / Scenarios 25 / anim 195 / the editor & cluster replay tests).** Only stop on a genuine breaking design flaw.

---

## Fix A: Wire live animation playback (D5–D7)
**Goal:** when a mannequin is registered with the backend AND its Stride visual (`AnimationComponent`) exists, **load the locomotion clips + jump montages, create the `PerEntityBlendTreeBuilder`, attach it to that `AnimationComponent`, and push the backend's per-frame locomotion-blend + montage state into the builder** so the skeleton actually plays Idle/Walk/Run + Jump.
- Model the clip loading + builder usage on the template `AnimationController` (it loads `Animations/*` clips, creates evaluators via the `AnimationComponent.Blender`, and pushes weights every frame — exactly the proven pattern). [VERIFY] `Content.Load<Stride.Animations.AnimationClip>("Animations/Idle"|"Walk"|"Run"|"Jump_Start"|"Jump_Loop"|"Jump_End")` and that the mannequin's `AnimationComponent` has a valid `Blender` at the wiring point.
- Connect the **backend's computed per-entity locomotion blend + montage state** (already correct — see the log) to `builder.SetLocomotion(...)` / `builder.SetMontage(...)` each frame. [VERIFY] the cleanest backend→builder hook: the backend may already expose per-entity blend state, or add a minimal accessor (`TryGetLocomotionBlend(handle, out weights, out normalizedTime)` + montage state) so the live glue can pump the builder. Keep the headless backend logic unit-testable (don't move GPU types into it).
- Place the live glue where the `AnimationComponent` + backend handle are both known — likely the visual binding / a Game-project system that runs after `StrideAnimationBridge` registers the entity. Tear down the builder (`ReleaseEvaluators`) on unregister/death.

**Tests required** (headless where possible):
- The backend exposes per-entity locomotion blend + montage state that the builder consumes (assert the accessor returns the same weights the backend computed for a given speed — building on BATCH-13/14's tested logic).
- A unit/integration test of the live-glue wiring decision (entity registered + has visual → builder created; unregister → `ReleaseEvaluators` called) using a fake/abstracted clip-loader + a fake `AnimationComponent` seam if a real one can't be constructed headlessly. (The actual skeletal playback is GPU-verified by the human.)
- Existing anim tests stay green.

**Human-verify note for the report:** after this, pressing D5/D6 should show the mannequin's legs cycling (walk/run) and D7 the jump montage. Document exactly what the human should now see + any asset-URL assumption that could still fail loudly (clips must be compiled — a missing clip should fail loud, not silently no-op).

## Fix B: Release the recording file handle before replay (D9)
**Goal:** `record → replay` of the same exercise in one session must not throw `IOException` (file in use).
- Diagnose: after `FinalizeRecordingAsync`, is the `RecordingModule`/`FlightRecorder` writer (the `node_0.fdp` `FileStream`) actually closed/disposed and the module **uninstalled** before `ReplayModule` (`PlaybackController`) opens the same file for read? The stack shows it is NOT.
- Fix at the correct lifecycle point: ensure the recorder's file handle is **released** (writer disposed / `RecordingModule` uninstalled) as part of finalize **before** the replay module installs. Prefer the clean lifecycle fix over a `FileShare` band-aid; if you do relax sharing, ensure the file is fully flushed first. If the gap is shared FDP code (`EcsRecordReplayController.FinalizeRecordingAsync` / `RecordingModule` not closing the stream), fix it there **carefully** and re-verify the editor/cluster replay tests don't regress. If it's harness-flow-specific (needs an explicit stop-recording/uninstall step between finalize and replay), fix it in the harness/controller sequencing.

**Tests required:**
- A headless test of the record→finalize→replay sequence on the same exercise file that currently reproduces the handle conflict (drive the kernel ticks as BATCH-15's replay tests do) → asserts no `IOException` and that playback opens the file successfully.
- Existing replay/record tests + the shared-baseline replay tests stay green.

## Success Criteria
- [ ] Fix A: live path loads clips + creates/attaches `PerEntityBlendTreeBuilder` + pushes backend weights; backend exposes the per-entity blend state (tested); builder torn down on unregister. (Skeletal playback human-verified.)
- [ ] Fix B: record→replay of the same exercise no longer throws; file handle released before playback opens it; covered by a headless test.
- [ ] Full suite green; no new failures vs baselines; Stride solution builds clean; report submitted.

## Report Requirements (`reports/BATCH-16-REPORT.md`)
Answer: where you placed the live animation glue + the backend→builder hook you used; the `Content.Load<AnimationClip>` URLs + how a missing clip fails loud; exactly what the human should now see for D5/D6/D7; the root cause of the replay `IOException` (was the recorder stream/module not released? where?) + the precise fix (lifecycle vs FileShare) + whether it touched shared FDP replay code + the baseline-regression check result; test counts; suggested commit message. Be explicit about what you could not verify (GPU skeletal playback).
