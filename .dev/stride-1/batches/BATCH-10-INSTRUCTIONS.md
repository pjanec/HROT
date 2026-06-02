# BATCH-10: Live bring-up — make `editor_stride` actually run & render (small batch)
**Tasks:** STR-LIVE-1 (live wiring of the existing P0–P3 work into the runnable app)   **Phase:** P0 follow-up / glue   **Est:** ~4–6h
**Dependencies:** Phases 0–3 (all the pieces exist; this batch only *connects* them to the process entry point).

**Purpose.** Today, running `HrotStrideApp` shows the stock 3rd-person-platformer template — because the Windows head still launches a plain `new Game()`. All stride-1 code is exercised only by headless tests. This batch flips the live entry point so the app boots `StrideHrotGame` → `EditorStrideSubsystem`, spawns the UrbanCombat demo entities through the Brain path, and **renders them as Stride models** (via the concrete `StrideVisualFactory`) at swizzled positions. This is the first **visually testable** payoff and the real exercise of the deferred GPU/asset milestone (STR-D4).

**Reality of verification.** This code path needs a `GraphicsDevice` + SDL window, which **cannot be run or verified headlessly** in CI. You (the coder) **build it correctly against the verified Stride 4.2.1.2487 APIs and make it compile**, then write precise *"how to run / what you should see / what to check if it's wrong"* instructions for the human to validate on a GPU machine. **Do not fake a "it renders" claim.** Movement will be static (Bullet physics is still the `NoOpPhysicsBodyService` until the concrete `BulletPhysicsBodyService` lands) — that is expected and fine; the goal is **visible spawned models**.

No Corrective Task 0.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — working contract.
2. `.dev/stride-1/Stride-Integration_v0_3.md` §8.3 (host loop), §6.5 (visual binding), §12 (scenario/assets — the template static arena stays; the template player/scripts are throwaway), §14 step 0 (this is the live version of that smoke).
3. The existing pieces you are connecting (read them): `Stride/HrotStrideApp.Game/StrideHrotGame.cs`, `EditorStrideSubsystem.cs`, `StrideVisualFactory.cs`; `Stride/HrotStrideApp.Windows/HrotStrideAppApp.cs` (the entry point to change).
4. `reviews/BATCH-09-REVIEW.md` + `DEBT-TRACKER.md` (STR-D4, STR-D9, STR-D10, STR-D11, STR-D13).

Use the **codebase-memory MCP first** (project `D-Work-IOS-IG-SimHost-FDP`).

### Verified facts & exact references
- **Entry point** = [HrotStrideApp.Windows/HrotStrideAppApp.cs](../../../Stride/HrotStrideApp.Windows/HrotStrideAppApp.cs): currently `using var game = new Game(); game.Run();`. Change it to launch `StrideHrotGame`.
- **`StrideHrotGame`** (subclass of `Stride.Engine.Game`, BATCH-02): throttler disabled (`WindowMinimumUpdateRate=0`); has a `StrideHostLoopDriver` + `Tick(float wallDelta)`. **[VERIFY]** its current surface and choose the simplest single-window integration: override `Update(GameTime)` (Stride's normal `Run()` loop) to drive `EditorStrideSubsystem.Tick(dt)` via the loop driver. (The fully-external SDL pump is only needed for the P5 raylib second window — do **not** build that here.)
- **`EditorStrideSubsystem`** (BATCH-02/03/04/06): `Initialize(IStrideVisualFactory? visualFactory = null)`, `Tick(float dt)`, `ScenarioSource.Enqueue(EntityCreationRequest)`, `World`, `VisualBindingSystem`. It already registers the UrbanCombat TKB templates (1001–2003, each with a `StrideRenderModelDefDto`). Spawn exactly as the integration tests do: `EntityCreationRequest { OwnerAppInstanceId = 0, TkbType = 2002 /*InfantrySoldier*/, InitialComponents = { new SimTransform { Position = ... }, new TkbIdentity { TkbType = 2002 } } }`.
- **`StrideVisualFactory(Game game, Scene scene)`** (BATCH-03, concrete): `Content.Load<Model>` + `ModelComponent` + scene add; model path is real, procedural path is mesh-less (STR-D9 — not exercised by the demo, which uses model refs). Asset URLs: `Models/mannequinModel` (infantry, has skeleton) and `Models/Box2x1x1` (vehicles).
- **Assets:** the MainScene + `Models/mannequinModel`/`Models/Box2x1x1` were seeded by the template (§12). The static arena, camera, and lighting come from the loaded scene.

**Work autonomously; build to a clean compile.** Only stop on a genuine breaking design flaw.

---

## Task: Live-wire `editor_stride` into the runnable app (STR-LIVE-1)

1. **Entry point.** Change `HrotStrideApp.Windows/HrotStrideAppApp.cs` to `new StrideHrotGame()` (+ `.Run()`), so the process boots our game.
2. **Boot the subsystem in `StrideHrotGame`.** On the appropriate Stride lifecycle hook (after the graphics/scene are available — e.g. override `BeginRun`/`LoadContent`/first `Update`, [VERIFY] the right hook so `Content`/`SceneSystem`/the active `Scene` are valid):
   - Get the active `Scene` (the loaded MainScene — [VERIFY] `SceneSystem.SceneInstance.RootScene` or similar).
   - Construct `new StrideVisualFactory(this, scene)`.
   - Construct `EditorStrideSubsystem`, call `Initialize(visualFactory)`.
   - Enqueue a handful (e.g. 4–6) of UrbanCombat spawn requests at positions **in front of the scene camera** (pick FDP positions that, after `FdpStrideTransform.ToStride`, sit in the camera's view — document the positions and the reasoning). Mix infantry (`mannequinModel`) and a vehicle (`Box2x1x1`).
   - Drive `EditorStrideSubsystem.Tick(dt)` each frame from `Update(GameTime)` via the `StrideHostLoopDriver` (fixed dt).
3. **Camera/visibility.** Ensure there is a working camera that can see the spawn area and a light. The template's camera followed the now-removed player — [VERIFY] and either (a) repurpose it to a fixed/free position looking at the spawn area, or (b) add a simple camera. Do **not** leave the camera dependent on a template player entity that may not exist. If the template `PlayerController`/`PlayerInput`/`ThirdPersonCamera`/`AnimationController` scripts or player entity cause errors at boot, neutralize them (remove the player entity from the scene or disable the scripts) — keep the **static arena** (design §12). Document what you changed in the scene.
4. **Make asset-load loud (resolve STR-D10).** In `StrideVisualFactory.CreateModelVisual`, replace the silent `Content.Load` catch-and-placeholder with a **loud failure** (throw or log-error-and-visibly-mark) so a missing/miscompiled `mannequinModel`/`Box2x1x1` is obvious when the human runs it, instead of silently showing nothing. (Mark STR-D10 resolved.)

## Success Criteria
- [ ] `Stride/HrotStrideApp.sln` builds clean (Game + Windows head), 0 errors; all existing tests still green (215 Core / 33 Game / 4 Animation).
- [ ] Entry point launches `StrideHrotGame`; on a GPU machine the app boots `editor_stride`, spawns the UrbanCombat entities, and renders them as Stride models at swizzled positions (human-verified — you document the procedure).
- [ ] Camera sees the spawn area; no boot errors from leftover template player scripts.
- [ ] STR-D10 resolved (asset-load is loud).
- [ ] Report written.

## Report Requirements (`reports/BATCH-10-REPORT.md`)
Answer: the exact Stride lifecycle hook you used to boot the subsystem and why (where `Scene`/`Content` are valid); the camera/scene changes you made (and how you neutralized the template player without losing the static arena); the spawn positions chosen and why they should be in view; **a precise "how to run and what you should see" section for the human** (build/run command, expected: N mannequin + box models standing in the arena at the swizzled positions; static — no movement yet, since physics is NoOp; what an asset-load failure now looks like); exactly what you could and could **not** verify yourself (you can't run the GPU path — say so plainly); any [VERIFY] results; suggested one-line commit message. Report actual test counts. Do NOT claim the render works — only that it compiles and is wired per the APIs.
