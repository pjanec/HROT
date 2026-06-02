# BATCH-16 Report — Fix live animation playback + record→replay file handle (corrective)

## Implementation Summary

### Fix A — Wire live animation playback (D5–D7)

The headless half was already complete and correct before this batch:
- `StrideAnimationBridge` registers each mannequin with `StrideAnimationBackend` and pumps `SimVelocity` → idle/walk/run blend + jump-montage slot state every frame.
- `StrideAnimationBackend.Tick()` **already** pushes that per-entity blend + montage state into a `PerEntityBlendTreeBuilder` *iff one is attached* (`AttachBlendTreeBuilder`).
- `PerEntityBlendTreeBuilder` is a complete Stride `IBlendTreeBuilder`.

What was missing: **nothing in the live path created the builder, loaded the clips, or attached it to the mannequin's `AnimationComponent`** — so `backend.Builder` stayed null and the skeleton never moved. This batch adds that glue.

**Where the live anim glue lives:** new file `Stride/HrotStrideApp.Game/MannequinAnimationBinder.cs`.
- `MannequinAnimationBinder` (engine-agnostic decision logic): each frame it reconciles the set of *bound* mannequins against the live visual set. **Bind** when a mannequin has both (a) a Stride visual with a valid `AnimationComponent` and (b) a backend handle (the bridge registered it). **Unbind/release** when the visual disappears.
- `IMannequinBlendTreeInstaller` (GPU-bound seam) + its live implementation `StrideMannequinBlendTreeInstaller` do the GPU work: load the six clips via `Content.Load<Stride.Animations.AnimationClip>`, construct the `PerEntityBlendTreeBuilder` on the entity's `AnimationComponent` (creating evaluators from its `Blender`), register the three jump montage clips (keyed by the same `StableIdHasher.ComputeMontageAssetId` hashes the bridge uses), and call `backend.AttachBlendTreeBuilder(handle, builder)`.

**Wiring into the runtime:**
- `EditorStrideSubsystem.Initialize(visualFactory, blendTreeInstaller)` now takes an optional installer; it creates `AnimationBinder` only when both a visual binding system and an installer are present. Driven from `EditorStrideSubsystem.Tick()` as **Step 5b** (`AnimationBinder?.Reconcile()`), after the bridge has registered handles (Step 4b) and the visual sync has created `AnimationComponent`s (Step 5). Released in `Dispose()`.
- `StrideHrotGame.BootEditorSubsystem()` constructs `new StrideMannequinBlendTreeInstaller(Content)` and passes it to `Initialize`.

**The backend→builder hook (clean, tested accessor):** added two minimal per-entity accessors on `StrideAnimationBackend` returning exactly what `Tick()` pushes to an attached builder, so the GPU skeleton is driven by the proven headless blend logic and the hook is unit-testable without GPU types:
- `TryGetLocomotionBlend(handle, out LocomotionBlendWeights weights, out double normalizedTime)`
- `TryGetMontageOverlay(handle, out int montageHash, out float weight, out double normalizedTime)`

These keep the headless backend free of Stride types. The live push in `Tick()` is unchanged behaviorally — the accessors expose the same values.

**`Content.Load` URLs + loud failure:** the six clip URLs (matching the mannequin's `CharacterAnimationDefDto`, DD-1 §12) are:
`Animations/Idle`, `Animations/Walk`, `Animations/Run`, `Animations/Jump_Start`, `Animations/Jump_Loop`, `Animations/Jump_End`.
`StrideMannequinBlendTreeInstaller.Load` wraps `Content.Load<AnimationClip>` in try/catch and, on failure, logs the failing URL to Debug + stderr + NLog and **rethrows `InvalidOperationException`** (STR-D10 parity, mirroring `StrideVisualFactory`). A missing/un-compiled clip crashes loud — it never spawns a silently-static mannequin.

**What the human should now see on a GPU run:**
- **D5 (Walk Mannequin):** the mannequin's legs cycle in a walk gait while it moves forward at ~1.5 m/s (blend → Walk), returning to idle when the drive stops.
- **D6 (Run Mannequin):** a run gait at ~4 m/s (blend → Run).
- **D7 (Trigger Jump):** the Jump_Start→Jump_Loop→Jump_End montage plays on the full-body slot, overlaid on the locomotion blend.
- Log line `[anim] Bound PerEntityBlendTreeBuilder to mannequin visual '…'` confirms each bind.

**Could NOT verify:** actual GPU skeletal playback (no GraphicsDevice / compiled asset pipeline in CI). The clip URLs assume the `Animations/*` assets are compiled in the HrotStrideApp pipeline; if any is missing it fails loud (above), not silently.

### Fix B — Release the recording file handle before replay (D9)

**Root cause.** `EcsRecordReplayController.FinalizeRecordingAsync` → `kernel.UninstallModuleAsync(RecordingModule)` already removes `RecorderTickSystem` from the topology, drains in-flight writes, and disposes the module (closing the `AsyncRecorder` `FileStream`, opened `FileShare.None`). That close happens on the kernel's **background disposal worker**, and `PlaybackController` opens the same `.fdp` with `FileShare.Read` — which forbids any concurrent writer. There is a handle-release latency window between `FileStream.Dispose()` and the OS dropping the exclusive lock during which the `ReplayModule` install (`new PlaybackController(filePath)`) loses the race → `IOException("…node_0.fdp… used by another process")`. The recorder stream/module *was* being released, but not provably-before the next opener under real GPU wall-clock timing.

**Precise fix (clean lifecycle, not a FileShare band-aid).** Disposal stays in the correct place (the kernel drain — disposing earlier on the finalize thread would race the still-live `RecorderTickSystem`; I tried that first and it caused `ObjectDisposedException` in the concurrent-loop SimHost tests, then reverted it). Instead, `RecordingModule.Dispose()` now, **after** `AsyncRecorder.Dispose()` closes the writer, blocks until the handle is verifiably released via a new private `WaitForWriterRelease(filePath)` — it probes-opens the file with `FileShare.Read` (the same mode `PlaybackController` uses), retrying briefly (≤50×10 ms) until it succeeds. This makes the contract "**`FinalizeRecordingAsync` completed ⟹ the file is openable for replay**" hold on every OS/timing, eliminating the D9 race without relaxing the reader's share mode. `EcsRecordReplayController.FinalizeRecordingAsync` clears `_activeRecordingModule` before the await and documents the ordering. `AsyncRecorder.CaptureFrame/CaptureKeyframe` also gained a defensive `if (_disposed) return;` guard (safe-after-close).

**Shared FDP replay code touched:** YES.
- `FDP/Engine/Fdp.Core/FlightRecorder/AsyncRecorder.cs` — `_disposed` guards on capture (defensive).
- `FDP/Toolkits/Fdp.Toolkits/Replay/RecordingModule.cs` — `Dispose()` adds `WaitForWriterRelease` barrier.
- `Hrot/Subsystems/Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs` — `FinalizeRecordingAsync` ordering + comments.

**Baseline-regression check (re-verified vs the documented baselines):**
| Suite | Baseline | With Fix | Verdict |
|---|---|---|---|
| `Hrot.SimHost.Tests` (full) | 38 fail / 573 pass | 38 fail / 573 pass — **identical failing set** (verified by sorted diff) | no regression |
| `Hrot.SimHost.Integration.Tests` (RecordReplay) | 2 fail / 2 pass | 2 fail / 2 pass (same pre-existing `node_1.fdp` path-mismatch tests) | no regression |
| `Fdp.Toolkits.Tests` (Replay/Recording) | 5 fail / 179 pass | 5 fail / 179 pass (pre-existing: SeekOffMainThread, ReplayBrowser diff/search) | no regression |
| `Fdp.Examples.Scenarios.Tests` | 25 fail / 43 pass | 25 fail / 43 pass | no regression |
| `Hrot.MuscleCharacter.Animation.Tests` (anim 195) | 195 pass | 195 pass | green |

The 38/25/5/2 pre-existing failures were each confirmed by stashing the three shared-code edits and re-running on the clean baseline (identical results). They are unrelated to this batch (test-side `.fdp` path mismatches missing the `exercises/` subdir, a threading-assertion flake, ReplayBrowser diff/search, scenario fixtures).

## Test Results (new + touched suites, with the fix)

- `Hrot.Stride.Animation.Tests`: **48 passed** (was 41; +7 `BackendBuilderHookTests` for the backend→builder accessors — assert `TryGetLocomotionBlend`/`TryGetMontageOverlay` equal `LocomotionBlend.FromSpeed` and `QuerySlotState` for walk/run/idle/montage, and false for stale handles).
- `HrotStrideApp.Game.Tests`: **81 passed** (was 70; +7 `MannequinAnimationBinderTests` wiring-decision + +4 `RecordReplayWiringTests` Fix-B repro/stress).

## Record/Replay re-entrancy guard (BATCH-16 follow-up)

**Problem (confirmed from a GPU run + `editor_stride.log`).** Pressing **D9** ("Record 3s / Replay", `StrideGizmoReplayHarnessCases.RecordThenReplay`) starts a ~6 s record→finalize→replay state machine driven by a per-frame harness hook. Fix B above made a *single* sequence drain and dispose cleanly. But if D9 is triggered **again while a sequence is still in flight** — a second key press, or the UI button `Click` and keyboard `D9` both firing in one frame (`StrideTestHarness.TriggerCase` is called from both paths on the same game-update thread) — a **second concurrent sequence** starts. The log showed multiple `Recording_<guid>` modules "installed and live" within milliseconds and multiple "Record 3s / Replay: starting" lines; the two sequences raced on the shared `RecordReplayController` + the reverse-sync `TogglablePostSimulationGroup` and crashed (IOException / topology churn). Fix B does not cover this — it is a harness-level concurrency problem, not an FDP infrastructure one.

**[VERIFY] trigger edge-behavior.** Confirmed the keyboard trigger is **edge-triggered**: `StrideTestHarness.PollKeyboard` uses `input.IsKeyPressed(Keys.D1+i)` (true only on the frame the key transitions down), so holding D9 does not re-fire. Both trigger paths — `button.Click += … TriggerCase(index,"click")` and the keyboard poll's `TriggerCase(i,"key")` — funnel into the same `_registry.Trigger(index, _context)` synchronously on the Stride game-update thread. So the realistic re-entrancy is (a) two separate presses while a 6 s sequence runs, or (b) a button-click and a key-press landing in the same frame. The guard handles all of these uniformly.

**Fix (harness-only; FDP replay infrastructure untouched).** Added a process-wide re-entrancy guard to the static case class:
- `private static bool s_recordReplayInProgress;` (the case class is `static`; the harness drives all triggers on one thread, so no locking is needed).
- At the top of `RecordThenReplay`, if the flag is already set: log `"Record/Replay already in progress — ignored."` via the existing `ctx.Log` (NLog-backed harness logger) and **return immediately** — before `EnsureOrbitingGhost`, before any async op, and crucially before the second `ctx.RegisterUpdate` hook. No second sequence, no second continuous hook.
- The flag is set `true` exactly once when a sequence starts (right after the guard check, before the "starting" log line).

**All guard-reset paths (every terminal path clears the flag, so a faulted sequence can't wedge it true):**
1. **Normal completion** — phase 4 "complete — reverse-sync restored" branch sets `s_recordReplayInProgress = false` before `return false`.
2. **Recording-install FAILED** — phase 1 `IsFaulted` branch (`return false`).
3. **Finalize FAILED** — phase 2 `IsFaulted` branch (`return false`).
4. **Replay-prepare FAILED** — phase 3 `IsFaulted` branch (`return false`).
5. **`default` branch** — unreachable phase value (`return false`).
6. **Catch-all** — the entire `switch` body is wrapped in `try { … } catch (Exception ex) { ctx.Log("…hook faulted — clearing in-progress guard…"); s_recordReplayInProgress = false; return false; }`, so any unexpected throw inside the hook (e.g. topology churn) also clears the guard and removes the hook. This is the robust "clear-on-every-terminal-path" pattern the spec asked for.

**Headless tests added** (`Stride/HrotStrideApp.Game.Tests/TestHarnessTests.cs`, both order-independent via a reflection reset of the static field in `try/finally`):
- `RecordReplay_SecondTriggerWhileInFlight_DoesNotStartSecondSequence` — first trigger registers 2 hooks (spawned orbit hook + phase machine); the guarded second trigger registers **0** additional hooks, logs `"already in progress"`, and produces exactly **one** `"…starting"` line.
- `RecordReplay_AfterCompletion_CanBeTriggeredAgain` — drives the phase machine to a terminal path (completion or fault — both clear the guard) against a real headless `EditorStrideSubsystem`, then re-triggers and asserts a fresh `"…starting"` line with no `"already in progress"`, proving the guard is not wedged after a sequence ends.

**Build / test results:**
- `dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug` → **0 errors** (9 pre-existing NU1608/CS0108 warnings, unrelated).
- `Hrot.Stride.Animation.Tests`: **48 passed** · `HrotStrideApp.Game.Tests`: **83 passed** (81 baseline + 2 new guard tests) · `Hrot.Stride.Core.Tests`: **224 passed**. All green.

**Other continuous-hook cases (Walk/Run/Orbiting Ghost) — noted, not changed.** These also register per-frame hooks and can stack (repeated presses add multiple hooks), but they only move local `SimTransform`/visuals and share no exclusive external resource (no `RecordReplayController`, no severed reverse-sync group, no exclusive file handle). Stacking them is harmless/idempotent-ish, not crash-prone, and "Clear All" calls `ctx.ClearUpdates()` to drop them all. The crash is specific to Record/Replay's shared mutable infrastructure, so per the task scope they were intentionally **not** guarded (avoid over-engineering).
  - Binder: spawn→bind installs exactly once (idempotent), death releases the builder (no leaked tokens), visual-without-AnimationComponent does not bind, `ReleaseAll` releases all, subsystem creates the binder only with an installer and binds through `Tick()`.
  - Record→replay: `RecordThenReplaySameFile_DoesNotThrowIOException_AndOpensPlayback` (+ a probe-open with `FileShare.Read`), `HarnessStyleRecordThenReplay_PhaseMachine_DoesNotThrow` (reproduces the D9 harness phase machine), `RecordFinalizeReplay_RepeatedOnSameDir_NeverThrowsIOException` (1× and 8× stress).
- `Hrot.Stride.Core.Tests`: 224 passed. `Hrot.MuscleCharacter.Animation.{Fake,Stride}.Tests`: 15 / 31 passed.
- `Fdp.Core.Tests` recorder/playback subset (touching `AsyncRecorder`): **123 passed** in isolation. Full suite shows 3 unrelated failures (`FastPathBenchmarks`, 2× `CheckpointIOWorkerTests` ComponentId collision — test pollution, not recorder/playback, independent of this batch).

## Design Decisions

- **Backend→builder hook = read-only accessors, not new push logic.** `Tick()` already pushes to the builder; I added `TryGetLocomotionBlend`/`TryGetMontageOverlay` that return the same values so the hook is unit-testable and the headless backend stays GPU-type-free. The binder relies on `Tick()` for the per-frame push (single source of truth); the accessors exist for the contract test.
- **Binder split into decision logic + GPU installer seam.** `PerEntityBlendTreeBuilder` and `AnimationComponent` can't be built headlessly, so the binder owns only which-entity-to-bind logic (tested with a fake installer), and `StrideMannequinBlendTreeInstaller` owns clip-load + builder-create + attach (GPU-verified by the human). Mirrors the existing `IStrideVisualFactory` testable-seam pattern.
- **Fix B disposal stays in the kernel drain.** The only race-free place to close the writer is after `RecorderTickSystem` is removed from the topology. The handle-release barrier (`WaitForWriterRelease`) is the minimal addition that closes the OS-latency window without a `FileShare` band-aid on the reader.

## Deviations

- **Initial Fix-B attempt (reverted).** First implemented an eager `RecordingModule.FlushAndClose()` called from `FinalizeRecordingAsync` *before* uninstall. WHY reverted: it disposes the recorder while `RecorderTickSystem` is still live on the concurrent kernel loop, causing `ObjectDisposedException` in 3 SimHost tests (`PrepareRecordingAsync_InstallsRecordingModule`, `FinalizeReplay_ReEnablesAllFourGroups`, `TeardownReplay_PreservesEntityRepositoryState`, etc.). Replaced with the in-`Dispose` `WaitForWriterRelease` barrier. RISK: none — the barrier is bounded (≤500 ms), never throws, and only runs at finalize.

## Developer Insights

- The headless record→replay-same-file path is already correct under deterministic single-thread ticking — none of the four new repro tests (incl. an 8× stress) reproduce the IOException in CI. The D9 crash is a real-GPU wall-clock handle-release race; the barrier makes the lifecycle robust regardless.
- Pre-existing test debt worth flagging to the Lead: several record/replay tests (`FullBranchPipelineTests`, `RecordReplayIntegrationTests` ×2) assert a recording path **missing the `exercises/` subdir** that `OrchestrationConstants` actually uses — they fail on a clean baseline. Not fixed here (out of scope; would be a test-only correction).

## Known Issues

- GPU skeletal playback is **not** verified here (no GraphicsDevice in CI) — built per the proven template `AnimationController` pattern; human-verified on a GPU run.
- The `Animations/*` clip URLs must be compiled in the HrotStrideApp asset pipeline; a missing clip fails loud at first bind.
- The 38/25/5/2/3 pre-existing failures across SimHost/Scenarios/Toolkits/Core remain (unrelated to this batch; confirmed identical to baseline).

## Suggested Commit Message

`fix(stride): wire live mannequin animation glue + release recording handle before replay (BATCH-16, STR-P4/P5-T4)`

---

## D9 visible-replay rework (BATCH-16 follow-up 2)

### Problem

The D9 "Record 3s / Replay" harness case ran cleanly end-to-end (editor_stride.log confirmed the full cycle: `recorded ~3.0s` -> recorder `fully drained and disposed` -> `replay live - reverse-sync severed (Enabled=False); PlaybackTickSystem drives SimTransform` -> `Replay fully drained and disposed` -> `complete - reverse-sync restored`). The replay infrastructure WORKS. But the replay had **no visible effect**: the recorded entity was the shared BATCH-12 orbiting ghost, moved every frame by a SEPARATE live orbit hook (`EnsureOrbitingGhost` -> `ctx.RegisterUpdate`) that KEPT RUNNING during replay. Playback (restoring the same recorded orbit) was indistinguishable from the continuing live motion. `ctx.ClearUpdates()` is all-or-nothing, so the orbit could not be selectively stopped without also killing the case's own phase machine.

### Rework (harness case only; FDP replay infra untouched)

1. **Dedicated, case-owned ghost.** The case now creates its own non-owned ghost (`World.CreateEntity` + `AddComponent(SimTransform)` + `AddComponent(TkbIdentity{TkbType=2002})`), modeled on `EnsureOrbitingGhost`. Non-owned -> Mode-1 ghost -> gets a mannequin visual and is matched by Pass-B's `.WithoutOwned<SimTransform>()` forward-sync selector. The old shared-ghost reuse and the separate orbit hook were removed (`EnsureOrbitingGhost` deleted).
2. **Record-drives-live.** During the RECORD phase the phase-machine hook itself writes the ghost's `SimTransform` every frame (a clear circle, radius 2.5 around center (0,8,1)), so the case fully controls when live-driving stops. Records ~3 s.
3. **Replay-stops-live + snap-to-start.** At replay start the case SNAPS the ghost to the origin (obviously different pose) and then STOPS writing `SimTransform` entirely - the phase machine only ticks the kernel and logs. From there PlaybackTickSystem is the ONLY driver, so the ghost visibly re-traces the recorded circle from the snapped origin. Each replay frame the case logs the ghost's `SimTransform.Position` throttled to ~3/sec, including the per-sample delta; if the position never changes across the whole replay window it logs an explicit `WARNING - ghost SimTransform NEVER changed ... PlaybackTickSystem did not drive it` (silent-failure detection), otherwise it logs a confirmation.
4. **Restore.** After replay finalize the dedicated ghost is destroyed and the re-entrancy guard is cleared. The guard (`s_recordReplayInProgress`) and phase state machine are preserved; a shared `FailCleanup()` local clears the guard AND tears down the ghost on every early-return/fault terminal path. The ghost is kept ALIVE across record->replay (destroyed only at the very end / on failure) so PlaybackTickSystem drives the same entity id.

### What the human should now SEE

- **Record (~3 s):** a single mannequin ghost orbiting a clear circle.
- **Replay start:** the ghost SNAPS to the origin (jump cut), then with no live hook driving it, re-traces the recorded circle purely under PlaybackTickSystem.
- **Log:** throttled `[playback t=Xs] ghost SimTransform=(...)` lines with non-zero per-sample movement, ending in the "confirmed playback drove the ghost" line. If movement is zero throughout, the explicit WARNING fires instead.

### SchemaManifest warning note

editor_stride.log shows `[PlaybackController] WARNING: Recording has no SchemaManifest. Playback may fail silently`. The per-frame replay position logging added here is exactly the probe to confirm whether this is benign: for a same-build record/replay the manifest is implicit, so playback is expected to drive `SimTransform` and the position log should show movement. If a GPU run shows the position NOT changing (WARNING line fires), that warning is the lead suspect and playback is genuinely not applying the recorded component stream - to be confirmed on the GPU app (cannot be run in this environment).

### Build / test result

- `dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug` -> **0 errors** (pre-existing NU1608 / CS0108 warnings only).
- Stride tests green: **Animation 48 / Game 83 / Core 224**, all passing.
- Updated the two BATCH-16 guard tests in `TestHarnessTests.cs` for the changed testable seam: the first trigger now registers exactly **1** hook (phase machine; ghost driven from within it) and creates exactly one ghost entity; the second guarded trigger creates no second ghost. The reenter test's terminal-signal comment was corrected (hook count drops to 0, no orbit hook remains).
