# BATCH-15: 3D gizmos + record/replay (Phase 5, part A)
**Tasks:** STR-P5-T1, STR-P5-T4   **Phase:** P5   **Est:** ~8–10h
**Dependencies:** BATCH-06 (`BulletReverseSyncSystem` already in a `TogglablePostSimulationGroup`), BATCH-12 (test harness), BATCH-10 (live app).

Goal: (T1) `DebugPrimitiveRenderer3D` sweeps the `ProducerBuffer` → Stride debug draw (two-pass anchors→shapes, swizzled); (T4) record/replay via the togglable reverse-sync group + `PlaybackTickSystem`. Both have testable logic (two-pass resolution/swizzle; group-toggle + playback) + a GPU/render part (the actual debug draw) that's human-verified. Add harness cases (draw a gizmo; record→replay).

No Corrective Task 0. (P5-T2 editor dual-window + T3 shared selection are the NEXT batch.)

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md`.
2. `.dev/stride-1/Stride-Integration_v0_3.md` §11 (3D gizmos — spec for T1; **do NOT hardcode `DebugPrimitive` offsets — read the live struct**), §9 (record/replay — spec for T4).
3. `.dev/stride-1/TASK-DETAIL.md` — STR-P5-T1, STR-P5-T4. + `reviews/BATCH-14-REVIEW.md`.

Use the **codebase-memory MCP first** (project `D-Work-IOS-IG-SimHost-FDP`).

### Verified facts & exact references
- **`DebugPrimitive` struct (source of truth — read it, bind to it, no hardcoded offsets):** [GizmoMap.Contracts/Primitives/DebugPrimitive.cs](../../../FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/Primitives/DebugPrimitive.cs). **2D renderer to mirror (two-pass):** [GizmoMap.Presentation/Rendering/DebugPrimitiveRenderer2D.cs](../../../FDP/ExtDeps/GizmoMap/GizmoMap.Presentation/Rendering/DebugPrimitiveRenderer2D.cs) (Pass 1 cache anchors by network id; Pass 2 resolve shapes/lines against their anchor → absolute world transform, write resolved transform into the primitive's spare payload, swizzle, emit). **Buffer:** [GizmoMap.Contracts/Primitives/DebugPrimitiveBuffer.cs](../../../FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/Primitives/DebugPrimitiveBuffer.cs).
- **[VERIFY] in editor_stride:** where the `ProducerBuffer` lives and which systems write gizmos (the mock exposed `ProducerBuffer` on `StrideNodeBootstrapper`; `EditorStrideSubsystem` uses direct kernel wiring — find/confirm the `ProducerBuffer` instance the gizmo producers write to, or create/expose one).
- **[VERIFY] Stride 4.2.1.2487 debug-draw API:** the immediate-mode `Stride.DebugRendering` entry point (e.g. a `DebugTextSystem`-like debug-shapes system, `ImmediateDebugRenderFeature`, or a `DebugRenderer` render stage in the `GraphicsCompositor`). Design §11 fallbacks: add a `DebugRenderer` render-stage to the compositor, or draw via a dynamic `Mesh`. Pick what works; document it. The two-pass + swizzle + anchor-resolution is testable headlessly against a synthetic `ProducerBuffer`; the actual draw is GPU.
- **Replay stack:** `PlaybackTickSystem` ([Replay/PlaybackTickSystem.cs](../../../FDP/Toolkits/Fdp.Toolkits/Replay/PlaybackTickSystem.cs)), `RecordingModule` ([Replay/RecordingModule.cs](../../../FDP/Toolkits/Fdp.Toolkits/Replay/RecordingModule.cs)), `ReplayModule` ([Replay/ReplayModule.cs](../../../FDP/Toolkits/Fdp.Toolkits/Replay/ReplayModule.cs)), `IRecordReplayController` ([Orchestration/IRecordReplayController.cs](../../../FDP/Engine/Fdp.Core/Orchestration/IRecordReplayController.cs) — transition names), `ReferenceReplayLoadHandler` ([Orchestration/Handlers/ReferenceReplayLoadHandler.cs](../../../FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs)). The `BulletReverseSyncSystem`'s `TogglablePostSimulationGroup` (BATCH-06) is the group to pass to `ReferenceReplayLoadHandler` (resolves the remaining half of STR-D5).
- `FdpStrideTransform` for all swizzles. Harness registration: `registry.Register(new VisualTestCase(...))` (BATCH-12).

**Complete tasks in sequence (T1 → T4); ALL tests green before moving on. If you change a shared system, re-verify no new failures vs baselines (SimHost 38 / Scenarios 25 / anim 195).** Work autonomously. Only stop on a genuine breaking design flaw.

---

## Task 1: `DebugPrimitiveRenderer3D` (STR-P5-T1)
**File:** `Stride/Hrot.Stride.Core/DebugPrimitiveRenderer3D.cs` (NEW). Spec: design §11.
Two-pass sweep of the `ProducerBuffer` mirroring the 2D renderer: Pass 1 cache anchors by network id; Pass 2 resolve each shape/line against its anchor into absolute world coords, swizzle via `FdpStrideTransform`, emit through the Stride debug-draw API. Bind to the **live** `DebugPrimitive` struct (no hardcoded layout).

**Tests required** (headless — two-pass resolution + swizzle):
- Anchor + shape: a shape primitive resolved against a cached anchor yields the correct **absolute world transform**, and after `FdpStrideTransform` the Stride position/orientation matches the expected swizzle (assert numeric values against a synthetic `ProducerBuffer`).
- Binds to the live `DebugPrimitive` struct (reflection/compile-time, not hardcoded offsets).
- A line primitive's endpoints resolve + swizzle correctly.
(The actual `Stride.DebugRendering` draw is GPU — document it.)

## Task 2: Record/replay togglable reverse-sync (STR-P5-T4)
**Files:** wire `RecordingModule`/`ReplayModule`/`PlaybackTickSystem` + `ReferenceReplayLoadHandler` into `editor_stride`. Spec: design §9. **Resolves the rest of STR-D5.**
Pass the `BulletReverseSyncSystem`'s `TogglablePostSimulationGroup` to `ReferenceReplayLoadHandler`. On `PrepareReplay` → group `Enabled=false` (reverse-sync severed); `PlaybackTickSystem` drives `SimTransform` from recorded keyframes; on `FinalizeReplay`/`PrepareLive` → group `Enabled=true`. Ensure Bullet doesn't advance owned bodies during replay ([VERIFY] whether severing the group suffices or the sim must be paused — in the current NoOp app it's moot, but wire it per §9).

**Tests required** (headless):
- `PrepareReplay` → the reverse-sync group `Enabled=false` (no writes); `PlaybackTickSystem` drives `SimTransform` from keyframes (assert a replayed entity's `SimTransform` comes from the recording, not the reverse-sync).
- `FinalizeReplay`/`PrepareLive` → group `Enabled=true` (reverse-sync restored).
- Recording captures the node's `SimTransform` each tick (assert a recording grows with ticks).

## Task 3: Harness test cases (required)
- **"Draw Test Gizmo"** — write a known `DebugPrimitive` (a line + a sphere/box at a known FDP position) into the `ProducerBuffer`; `DebugPrimitiveRenderer3D` should render it (human-visible). Log via NLog.
- **"Record 3s / Replay"** — start recording, let the **orbiting ghost** (BATCH-12 case) move for a few seconds, stop, then replay → the ghost's recorded motion plays back (visible, driven by `PlaybackTickSystem` while reverse-sync is severed). Document the steps.

## Success Criteria
- [ ] STR-P5-T1: `DebugPrimitiveRenderer3D` two-pass resolution + swizzle (bound to the live struct) headless-tested; Stride debug-draw documented as GPU.
- [ ] STR-P5-T4: record/replay wired; reverse-sync group toggled by `PrepareReplay`/`FinalizeReplay`; `PlaybackTickSystem` drives `SimTransform`; recording grows per tick (STR-D5 fully resolved).
- [ ] "Draw Test Gizmo" + "Record/Replay" harness cases registered + documented.
- [ ] Full suite green (no new failures vs baselines); Stride solution builds clean; report submitted.

## Report Requirements (`reports/BATCH-15-REPORT.md`)
Answer: the live `DebugPrimitive` struct shape + how the two-pass resolves/swizzles; the Stride 4.2 debug-draw API you used (or compositor render-stage added) ([VERIFY] result); where the `ProducerBuffer` lives in editor_stride + who produces gizmos; the `IRecordReplayController` transition names + how the reverse-sync group is passed to `ReferenceReplayLoadHandler`; whether severing the group suffices for replay (or sim-pause needed); the harness cases + controls; any shared-system change + baseline check; what's GPU-deferred; test counts; suggested commit message.
