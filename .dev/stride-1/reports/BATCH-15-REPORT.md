# BATCH-15 Report — 3D gizmos + record/replay (Phase 5, part A)

**Tasks:** STR-P5-T1 (DebugPrimitiveRenderer3D), STR-P5-T4 (record/replay togglable reverse-sync).
**Status:** both implemented, headless-tested; GPU draw deferred + documented.

## Implementation Summary

### STR-P5-T1 — `DebugPrimitiveRenderer3D`
New file `Stride/Hrot.Stride.Core/DebugPrimitiveRenderer3D.cs`. Mirrors the raylib
`DebugPrimitiveRenderer2D` two-pass scheme but resolves into **3-D** world coords and swizzles
through `FdpStrideTransform`:

- **Pass 1 — anchors.** Sweep the span; cache every `DebugPrimitiveShape.SpatialAnchor` by its
  `NetworkId` into a reusable `Dictionary<long, SpatialAnchor3D>` (world X/Y/Z + yaw/pitch/roll in
  radians). Cleared each `Render` call (no stale-anchor leak across frames — tested).
- **Pass 2 — shapes.** For each drawable primitive: if `Space == EntityLocal`, look up its anchor
  by `AnchorIndex` (the renderer keys EntityLocal primitives by `AnchorIndex`, exactly like the 2-D
  renderer); resolve the local payload into absolute FDP world coords **in-place** (heading rotation
  about FDP-Up/Z + anchor translation; `SemanticShape` stamps the `Resolved*` spare-payload fields
  like the 2-D renderer). Then swizzle FDP→Stride via `FdpStrideTransform` and emit a
  `DebugDrawShape3D` (sphere/box) or `DebugDrawLine3D` to an `IDebugDrawSink3D`. Anchors and the
  non-visual meta-primitives (ContextMenuBinding / InputCaptureBinding / MainMenuBinding /
  LayerControlMask) are never drawn; dangling anchor references are skipped. Returns the emitted count.

Bound to the **live** `DebugPrimitive` struct by named-field access only (no hardcoded offsets); a
test asserts `Marshal.SizeOf<DebugPrimitive>() == 64` and reflects the field names the renderer reads.

### STR-P5-T4 — record/replay togglable reverse-sync
Wired into `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs`:
- `EcsRecordReplayController` (the same factory `EditorSubsystem`/`SimHostSubsystem` use) constructed
  with `(Kernel, nodeId=0, World)`. `PrepareRecordingAsync` installs a `RecordingModule`
  (`RecorderTickSystem` captures SimTransform each PostSimulation tick); `PrepareReplayAsync` installs
  a `ReplayModule` (registers `PlaybackTickSystem` outside any togglable group).
- `ReferenceReplayLoadHandler` constructed with the `BulletReverseSyncSystem`'s
  `TogglablePostSimulationGroup` (BATCH-06) as `postSimGroup` (input/sim/lifecycle groups null —
  editor_stride has none separately; `bypassLifecycleToggle` null — no GhostCreationSystem).
- `ReverseSyncGroup` is now created **unconditionally** (shared-system change, see below) so the
  handler always has a real group to toggle, including headless.

## Design Decisions
- **`IDebugDrawSink3D` boundary.** The renderer's contract ends at a swizzled sink call
  (`DrawLine`/`DrawShape`). The headless default is a Trace-logging sink (`LoggingDebugDrawSink3D` in
  `EditorStrideSubsystem`); the GPU sink is deferred. This keeps the two-pass + swizzle fully
  headless-testable (capturing sink) and isolates the GPU half.
- **ProducerBuffer = `GizmoPrimitiveBuffer`.** Created on `EditorStrideSubsystem.ProducerBuffer`;
  `Tick()` step 6 sweeps it with `GizmoRenderer3D.Render(ProducerBuffer.GetFrame())` then
  `EndFrame(dt)` (persistence clock + transient reset). Local gizmo producers write here.
- **Replay harness is async-aware.** Kernel module installs only go live on a subsequent kernel
  `Update()`; the "Record/Replay" case is a per-frame state machine that polls the async Tasks while
  the hook keeps the kernel ticking, never blocking the game thread.

## [VERIFY] Results

### Stride 4.2.1.2487 debug-draw API
**There is no immediate-mode `Stride.DebugRendering` shape API in 4.2.1.2487.** Probing
`Stride.Rendering.dll` / `Stride.Engine.dll` shows only:
- `Stride.Profiling.DebugTextSystem` (text only — already used by the BATCH-12 harness overlay),
- `Stride.Rendering.Compositing.DebugRenderer` (a compositor **render feature**, not an immediate
  shape API) + `DebugRenderStages`,
- `GeometricPrimitive` / `PrimitiveType` (dynamic mesh building blocks).

No `ImmediateDebugRenderSystem` / `DebugShapes` / `DrawSphere`/`DrawLine` immediate helper exists.
**Therefore the actual 3-D draw is GPU-deferred** per design §11 fallbacks: implement
`IDebugDrawSink3D` either by adding a `DebugRenderer` render-stage to the `GraphicsCompositor`, or by
emitting dynamic `GeometricPrimitive` meshes (sphere/cube line-lists) tinted per primitive. The
two-pass resolution + anchor application + `FdpStrideTransform` swizzle is the headless-tested half.

### ProducerBuffer location + gizmo producers
editor_stride had **no** gizmo buffer wired before this batch. Per the batch instruction ("find/
confirm … or create/expose one") I created `EditorStrideSubsystem.ProducerBuffer`
(`GizmoPrimitiveBuffer`, namespace `Fdp.Toolkit.Diagnostics.Gizmos`, from `GizmoMap.Contracts` —
already referenced transitively via `Fdp.Toolkits`). Producers: none are auto-wired yet (Mode-1 has
no data-driven gizmo systems in this composition); the "Draw Test Gizmo" harness case is the current
producer. Mode-2's `ConsumerBuffer` sweep (§13.5) is future work.

### IRecordReplayController transitions + group wiring + sever-suffices
- Transition method names (from `IRecordReplayController`): `PrepareRecordingAsync`,
  `FinalizeRecordingAsync`, `PrepareReplayAsync`, `SeekToTimeAsync`, `TeardownReplayAsync`,
  `ProcessPlaybackTick`, plus `IsReplayActive` / `GetCurrentReplayTime` / `ActiveMaxNetworkId` /
  `ActiveReplayDurationSeconds` / `ActiveRecordingStartWallTicks`.
- `NodeOpType` operations the handler reacts to: `PrepareReplay` (=11) → `Commit` sets the post-sim
  group `Enabled=false`; `FinalizeReplay` (=12) and `PrepareLive` (=9) → `Enabled=true`.
- The reverse-sync group is passed as the `postSimGroup` ctor arg of `ReferenceReplayLoadHandler`.
- **Sever suffices (Mode 1).** In editor_stride physics is `NoOpPhysicsBodyService` (no Bullet step),
  so there is no owned-body integration to pause — severing the reverse-sync group is sufficient
  (the manually-driven `ReverseSyncGroup.Execute` early-outs on `!Enabled`, and `PlaybackTickSystem`,
  registered outside the group, drives `SimTransform` from keyframes). At GPU bring-up with a real
  `Stride.Physics.Simulation` (STR-D11), the same lifecycle transition should additionally pause the
  Bullet step (`Simulation.Enabled=false`/skip the processor) — bind it to the same transition, not a
  hand-rolled flag.

## Harness cases + controls
New file `Stride/HrotStrideApp.Game/StrideGizmoReplayHarnessCases.cs`, registered in
`StrideHrotGame.BuildTestHarness` after the BATCH-12/14 cases (they get the next D-keys):
- **"Draw Test Gizmo"** — writes a known World-space line (vertical, 3 m, FDP +Z) + sphere (r=0.75 m,
  2 m up) at FDP (0,6,0) into `ProducerBuffer` (`LifetimeSeconds=5`), then renders once and logs the
  emitted shape count (swizzled to Stride; GPU draw deferred). NLog via the harness `Log`.
- **"Record 3s / Replay"** — ensures an orbiting ghost (BATCH-12) exists, starts a recording,
  records ~3 s, finalizes, prepares replay (`ReferenceReplayLoadHandler.PrepareAsync`+`Commit` →
  reverse-sync severed; `PlaybackTickSystem` drives `SimTransform`), runs ~3 s of playback, then
  finalizes replay (reverse-sync restored). Each phase logs via NLog.

## Shared-system change + baseline check
- **`EditorStrideSubsystem.ReverseSyncGroup` is now created unconditionally** (was guarded by
  `PhysicsBodyLifecycle != null`, i.e. only when a visual factory was supplied). Headless it is an
  **empty** `TogglablePostSimulationGroup` (no inner systems) whose `Enabled` flag is still the
  replay sever/restore switch; with a factory it wraps the real `BulletReverseSyncSystem` exactly as
  before. WHY: the replay handler needs a non-null group in every composition (incl. headless tests)
  to fulfil STR-D5. RISK: low — an empty enabled group is a no-op each `Tick`; the live (factory)
  path is byte-for-byte unchanged.
- No other shared system was modified. Baselines re-verified (see Test Results).

## GPU-deferred bits
- The actual 3-D debug draw (`IDebugDrawSink3D` GPU implementation: compositor `DebugRenderer`
  render-stage or dynamic `GeometricPrimitive` mesh). Headless sink logs at Trace.
- On-screen visibility of "Draw Test Gizmo" and the replayed ghost motion (needs the running GPU app).

## Test Results
- **`Hrot.Stride.Core.Tests`: 224 passed / 0 failed** (includes the 9 new
  `DebugPrimitiveRenderer3DTests`: live-struct binding + size==64; anchor+sphere absolute-world
  resolve then swizzle to Stride (10,3,22) etc.; world sphere/line direct swizzle; both line
  endpoints resolve+swizzle; gradient colours; shape-listed-before-anchor still resolves (Pass-1
  caches first); dangling anchor skipped; anchors/meta never drawn; no stale-anchor leak across
  frames).
- **`HrotStrideApp.Game.Tests`: 70 passed / 0 failed** (includes the 6 new `RecordReplayWiringTests`:
  PrepareReplay severs the group / Finalize+PrepareLive restore it; severed empty group is a no-op
  Tick; recording captures frames each tick → replay opened from it has TotalFrames>0; replay drives
  SimTransform from the recording over the corrupted sentinel while reverse-sync is severed; plus the
  updated headless-group baseline test). _Note: each new IO round-trip test verified to terminate
  (no `.GetAwaiter().GetResult()` deadlock — async kernel installs/uninstalls are awaited while
  ticking the kernel)._
- **Baselines re-verified, no new failures:**
  - `Hrot.SimHost.Tests`: 573 passed / **38 failed** (matches documented baseline "SimHost 38f").
  - `Fdp.Examples.Scenarios.Tests`: 43 passed / **25 failed** (matches baseline "Scenarios 25f").
  - `Hrot.MuscleCharacter.Animation.Tests`: **195 passed / 0 failed** (matches baseline "anim 195").
- **`Stride/HrotStrideApp.sln` builds clean: 0 errors** (`dotnet build -c Debug`).

## Known Issues
- `SemanticShape` 3-D body: the 64-byte payload's `Resolved*` fields have no Z slot, so the renderer
  carries the resolved altitude alongside (from the anchor) at emit time; the box extents mapping
  (length→Stride Z, width→Stride X, thin height) is a reasonable default, not authored per-profile.
- No live gizmo producer systems are wired in Mode 1 yet (only the harness case produces primitives).

## Suggested Commit Message
`feat(stride): DebugPrimitiveRenderer3D (two-pass+swizzle) + editor_stride record/replay togglable reverse-sync (BATCH-15, STR-P5-T1/T4)`
