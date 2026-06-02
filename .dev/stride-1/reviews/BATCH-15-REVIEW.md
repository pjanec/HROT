# BATCH-15 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Summary
`DebugPrimitiveRenderer3D` (two-pass anchor→shape resolution + swizzle, bound to the live `DebugPrimitive` struct) (T1), and record/replay wired via the togglable reverse-sync group + `ReferenceReplayLoadHandler`/`PlaybackTickSystem` (T4, **fully resolves STR-D5**). Verified: ran suites (Core 224, Game 70), baselines unchanged.

## Verification performed
- T1 two-pass: tests assert real Stride-space values — e.g. anchor (10,20,3)+heading 90° + local (2,0,0) → world (10,22,3) → Stride (10,3,22) (correct `(X,Z,Y)` swizzle). Bound to the live 64-byte struct by named fields (no hardcoded offsets).
- T4: `EcsRecordReplayController` + `ReferenceReplayLoadHandler` wired with the BATCH-06 `BulletReverseSyncSystem` `TogglablePostSimulationGroup`; `PrepareReplay`→`Enabled=false` (severed, no writes), `PlaybackTickSystem` drives `SimTransform` from keyframes, `FinalizeReplay`/`PrepareLive`→`Enabled=true`; recording grows per tick. 6 headless tests. Severing suffices in Mode-1 (NoOp physics). **STR-D5 fully resolved.**
- Baselines re-checked: SimHost 38f / Scenarios 25f / anim 195 — unchanged (the one shared change, `ReverseSyncGroup` now always non-null, had its baseline test updated; low risk, factory path unchanged).
- Harness cases registered ("Draw Test Gizmo", "Record 3s / Replay").

## Issues Found (non-blocking — recorded as debt)
- **Stride 4.2 has no immediate-mode debug-shape API**, so the actual gizmo **draw is deferred** behind `IDebugDrawSink3D`; the only impls are a test capturer + a `LoggingDebugDrawSink3D` (logs, doesn't draw). So "Draw Test Gizmo" will *log* but **not yet visibly render** until a concrete GPU sink (compositor `DebugRenderer` render-stage or dynamic mesh) is built → **STR-D16**. The two-pass resolution/swizzle logic (the hard part) is done + tested; only the GPU emit remains.

## Verdict
APPROVED. Proceed to BATCH-16: P5-T2 (raylib/ImGui editor second window) + P5-T3 (shared selection + `CenterOnEntityCommand`).

## Commit Message
```
feat(stride): 3D gizmo renderer (two-pass) + record/replay togglable reverse-sync (BATCH-15)

Completes STR-P5-T1, STR-P5-T4 (resolves remainder of STR-D5)
- DebugPrimitiveRenderer3D: two-pass anchor->shape resolution against the live DebugPrimitive struct,
  swizzled via FdpStrideTransform, emitted to IDebugDrawSink3D (concrete GPU sink deferred = STR-D16;
  Stride 4.2 has no immediate debug-shape API)
- Record/replay: EcsRecordReplayController + ReferenceReplayLoadHandler wired in editor_stride with the
  BulletReverseSyncSystem TogglablePostSimulationGroup; PrepareReplay severs reverse-sync + PlaybackTickSystem
  drives SimTransform; FinalizeReplay/PrepareLive restores. Recording grows per tick.
- Harness: Draw Test Gizmo + Record/Replay cases
Tests: 224 Core (+9) / 70 Game (+5). Baselines unchanged (SimHost 38f / Scenarios 25f / anim 195).
```
