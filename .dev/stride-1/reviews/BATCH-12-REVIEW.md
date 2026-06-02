# BATCH-12 Review (in-app test harness, STR-TEST-1)
**Status:** ✅ APPROVED (compiles + wired; UI render is human-verifiable)   **Date:** 2026-06-03

## Summary
Extensible in-app test harness: `VisualTestCase` + `TestHarnessRegistry` + `TestHarnessContext` (in `Hrot.Stride.Core/TestHarness/`), `StrideTestHarness` UI (Stride.UI buttons + `DebugTextSystem` status + D1–D9 keyboard fallback), wired into `StrideHrotGame` after boot, with 4 P0–P3 cases. Verified what's verifiable headlessly; UI render is the human's to confirm.

## Verification performed
- Wiring confirmed: `StrideHrotGame.BuildTestHarness` builds the context (World/ScenarioSource/VisualBindingSystem/scene/camera + NLog `Log`), registers the cases, constructs the harness; driven from `Update`.
- Read the 4 cases — all real and meaningful in the current (NoOp-physics) app:
  - **Spawn Infantry/Vehicle** — enqueue via `ScenarioSource`, incrementing layout.
  - **Clear All** — snapshots the entity list then destroys (correct: no mutation-during-iteration), stops continuous hooks; validates §7 teardown reconciliation **live**.
  - **Orbiting Ghost** — creates a genuine **non-owned** entity (`World.CreateEntity` + `AddComponent`, no authority grant → `.WithoutOwned<SimTransform>()`), adds `TkbIdentity`(2002)+`SimTransform`, registers a per-frame orbit hook; visual moves via Pass-B forward-sync — validates the forward-sync→visual path **live**. Hook self-cancels when the entity dies.
- Registry pattern documented + clean: `registry.Register(new VisualTestCase("Label","Desc", ctx => {...}));` — I'll require future phase batches to add their cases this way.
- Key finding: `GraphicsCompositor.sdgfxcomp` already has a `UIRenderFeature` (no compositor change); no `SpriteFont` ships, so buttons are tinted rects + `DebugText` labels (the guaranteed-render channel).
- Build: solution 0 errors. Tests: 215 Core / 4 Animation / **48 Game** (+15) green.

## What is NOT verified
The on-screen rendering / button hit-testing requires the GPU window — human-verifiable only. The D1–D9 keyboard path + `DebugText` status are the robust fallbacks if button input needs more wiring.

## Verdict
APPROVED. The harness is the manual-test surface for all remaining phases. **Going forward, each phase batch must register its test cases here** (one line per case) — I will bake that into P4–P6 instructions.

## Commit Message
```
feat(stride): in-app Stride test harness for manual visual testing (BATCH-12)

- Extensible registry: VisualTestCase + TestHarnessRegistry + TestHarnessContext (Hrot.Stride.Core)
- StrideTestHarness UI: Stride.UI buttons + DebugTextSystem status + D1-D9 keyboard fallback;
  wired into StrideHrotGame; actions logged via NLog
- 4 P0-P3 cases: Spawn Infantry, Spawn Vehicle, Clear All (validates teardown reconciliation live),
  Spawn Orbiting Ghost (non-owned entity → Pass-B forward-sync moves the visual, live)
- Compositor already had a UIRenderFeature; no SpriteFont ships so buttons use DebugText labels
Tests: 215 Core / 4 Animation / 48 Game (+15). UI render is human-verifiable (needs GPU).
```
