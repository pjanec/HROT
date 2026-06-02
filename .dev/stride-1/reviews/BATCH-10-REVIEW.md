# BATCH-10 Review (live bring-up, STR-LIVE-1)
**Status:** ✅ APPROVED (compiles + wired; render is human-verifiable only)   **Date:** 2026-06-03

## Summary
The runnable app now boots `StrideHrotGame` → `EditorStrideSubsystem` → concrete `StrideVisualFactory`, spawns 6 UrbanCombat entities, and is set up to render them. Verified what's verifiable headlessly; flagged + fixed the most likely black-screen defect.

## Verification performed
- Entry point `HrotStrideApp.Windows/HrotStrideAppApp.cs` now launches `new StrideHrotGame()`. Windows head + full solution build clean (0 errors).
- `StrideHrotGame.BeginRun` boots the subsystem on a sound hook (scene+content valid, scripts not yet Started — documented); removes the template `PlayerCharacter` (+children) so `ThirdPersonCamera`/`PlayerInput` can't error; adds `DemoCamera` + `DemoDirectionalLight`; enqueues 6 spawns; `Update` drives `EditorStrideSubsystem.Tick` via the fixed-step driver. Spawn positions documented with the FDP→Stride swizzle and placed in the camera's view.
- **Caught + fixed a likely black-screen bug:** the new `DemoCamera` wasn't bound to the `GraphicsCompositor` camera slot (the template's bound camera was removed with the player). Sent a focused correction — the camera is now bound via `SceneSystem.GraphicsCompositor.Cameras[0].ToSlotId() → CameraComponent.Slot`, with loud fallbacks for null-compositor/zero-slots.
- STR-D10 resolved: `StrideVisualFactory.CreateModelVisual` now fails loud (logs + throws) on `Content.Load` failure; the silent placeholder + dead `CreatePlaceholderEntity` removed.
- Tests: 215 Core / 33 Game / 4 Animation — all green.

## What is NOT verified (honest)
The actual render (window opens, models appear) **requires a GraphicsDevice + SDL2 window and cannot be run here**. The coder correctly did not claim it renders. Remaining live-run risks the human may hit: (a) asset URLs `Models/mannequinModel`/`Models/Box2x1x1` must be compiled into the content DB (they're template assets, expected present); (b) the GraphicsCompositor must actually expose a camera slot (fallback logs if not); (c) directional-light intensity/exposure may need tuning for visibility. None are headlessly checkable.

## Verdict
APPROVED. This is the first human-visually-testable build and the live exercise of STR-D4 (now downgraded to "pending the human's GPU run"). Movement is static (physics = `NoOpPhysicsBodyService`, STR-D11) — expected.

## Commit Message
```
feat(stride): live bring-up — boot editor_stride + render UrbanCombat models in the app (BATCH-10)

- HrotStrideApp.Windows entry point now launches StrideHrotGame (was plain Game)
- StrideHrotGame.BeginRun boots EditorStrideSubsystem + concrete StrideVisualFactory on the
  loaded MainScene; removes the template PlayerCharacter; adds a fixed overview DemoCamera
  (bound to the GraphicsCompositor camera slot) + DemoDirectionalLight; enqueues 6 UrbanCombat
  spawns (4 InfantrySoldier + 2 MilitaryAPC); Update drives EditorStrideSubsystem.Tick (fixed step)
- StrideVisualFactory: Content.Load failures now fail loud (resolves STR-D10); silent placeholder removed
Build: solution 0 errors. Tests: 215 Core / 33 Game / 4 Animation green. Render is human-verifiable
  only (needs GPU); movement static until concrete BulletPhysicsBodyService (STR-D11).
```
