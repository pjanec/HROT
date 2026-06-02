# BATCH-16 Review (corrective: live animation + replay handle)
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Summary
Fix A: live animation glue (`MannequinAnimationBinder` + `StrideMannequinBlendTreeInstaller`) — loads the `Animations/*` clips, creates `PerEntityBlendTreeBuilder` on the mannequin `AnimationComponent`, pushes the backend's per-entity locomotion+montage state (new tested `TryGetLocomotionBlend`/`TryGetMontageOverlay` accessors). Fix B: bounded `WaitForWriterRelease` barrier so record→replay of the same file no longer throws `IOException`.

## Verification performed
- **Fix B did not regress shared replay code:** I touched `AsyncRecorder`/`RecordingModule`/`EcsRecordReplayController` are shared — ran `Fdp.Toolkits.Tests ~Replay` = 5 failed / 179 passed; the 5 are `ComponentDiffServiceTests`/`RecordingSearchServiceTests`/`SeekToFrameAsync` (diff/search/seek — **none touch RecordingModule/AsyncRecorder/dispose**), i.e. pre-existing, unrelated. Confirmed.
- **`WaitForWriterRelease` is sound + bounded:** 50 attempts × 10 ms = 500 ms cap; probes with `FileShare.Read` (same as `PlaybackController`), returns on success, backs off on `IOException`, bails on other errors. Guarantees "finalize ⟹ file openable" without hanging. Root cause was correctly identified: a release-latency race between the writer's `FileShare.None` close (on the kernel disposal worker) and the playback `FileShare.Read` open.
- **Fix A backend→builder hook** is the clean approach: headless backend stays GPU-type-free; the live installer pumps the builder. Backend accessors unit-tested; builder teardown on unregister. Missing clip fails loud.
- Tests: Animation 41→**48**, Game 70→**81** (incl. the D9 record→replay repro + 8× stress). Baselines unchanged (SimHost 38f / Scenarios 25f / anim-subsystem 195 / Stride.Core 224).

## Issues Found
No blocking issues. GPU skeletal playback (D5–D7 visibly animating) remains human-verified — built on the proven template `AnimationController` pattern.

## Verdict
APPROVED. Hand back to the user to re-run: D5/D6/D7 should now animate (walk/run/jump), and D9 record→replay should no longer crash. (Note: close the running app before rebuilding — the only solution-level failure was a post-build DLL-copy lock from the live app, not a compile error.)

## Commit Message
```
fix(stride): wire live mannequin animation playback + fix record->replay file-handle race (BATCH-16)

Fix A (D5-D7 now animate): MannequinAnimationBinder (headless bind/unbind decision) +
  StrideMannequinBlendTreeInstaller (loads Animations/{Idle,Walk,Run,Jump_*} clips, creates
  PerEntityBlendTreeBuilder on the mannequin AnimationComponent, registers montages); wired into
  EditorStrideSubsystem + StrideHrotGame; backend exposes TryGetLocomotionBlend/TryGetMontageOverlay
  (no GPU types in the headless backend); missing clip fails loud
Fix B (D9 record->replay): bounded WaitForWriterRelease barrier in RecordingModule.Dispose so the
  recorder's FileShare.None handle is verifiably released before PlaybackController opens it
Tests: 48 Animation (+7) / 81 Game (+11). Shared replay baseline unchanged (5 pre-existing
  diff/search/seek failures, unrelated). GPU skeletal playback human-verified.
```
