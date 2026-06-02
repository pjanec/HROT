# BATCH-12 Report — In-app Stride test harness (STR-TEST-1)

## Implementation Summary

### Task 1 — Extensible test-case registry (the key cross-phase deliverable)
New, engine-agnostic, in `Hrot.Stride.Core/TestHarness/`:

- **`VisualTestCase`** (`VisualTestCase.cs`) — `sealed record VisualTestCase(string Label, string Description, Action<TestHarnessContext> Run)` with an `EnsureValid()` guard (non-empty label, non-null `Run`). Chosen as a record (not an interface) so a case is one line; stateful cases close over locals or register a continuous hook.
- **`TestHarnessContext`** (`TestHarnessContext.cs`) — the single surface a case uses. Exposes `World` (`EntityRepository`), `ScenarioSource` (`ScenarioEntityCreationRequestSource`), `VisualBindingSystem` (`StrideVisualBindingSystem?`, null when headless), `Scene`, `CameraEntity` (Stride `Entity?`), `Log(string)`, and the continuous-case machinery: `RegisterUpdate(Func<float,bool>)`, `ClearUpdates()`, `PumpUpdates(dt)`, `ActiveUpdateHookCount`. Hooks return `true` to keep running / `false` to stop; a hook that throws is caught, logged, and removed (one bad case can't wedge the loop).
- **`TestHarnessRegistry`** (`TestHarnessRegistry.cs`) — ordered `Register(...)` (two overloads) + `Trigger(index, ctx)` used by **both** the button-click handler and the keyboard path (one shared trigger path).

These three types live in `Hrot.Stride.Core` and **do not** reference `HrotStrideApp.Game`, so there is no dependency cycle — the game builds the context from its `EditorStrideSubsystem`.

### Task 2 — In-app harness UI/overlay (`HrotStrideApp.Game`)
- **`StrideTestHarness.cs`** — builds a full-screen `UIComponent` (on a new scene entity `TestHarnessUI`) whose `UIPage.RootElement` is a `Canvas` containing **one clickable `Button` per registered case**, absolutely positioned in a left-hand column. Each `Button.Click += … TriggerCase(i,"click")`. `Update(dt)` (called every frame) polls **keyboard D1–D9** (`Game.Input.IsKeyPressed(Keys.D1+i)` → `TriggerCase(i,"key")`), pumps continuous hooks (`Context.PumpUpdates`), and draws the **on-screen status via `Game.DebugTextSystem.Print`**.
- Wired into **`StrideHrotGame`**: a new `BuildTestHarness(scene)` is called at the end of `BootEditorSubsystem` (after `EnqueueDemoSpawns`); the camera entity is captured in `AddFixedCamera`; `Update(GameTime)` now calls `_testHarness?.Update(wallDt)` each frame. Harness actions log via NLog logger `"StrideTestHarness"` into `logs/editor_stride.log`.

### Task 3 — Initial P0–P3 cases (`StrideTestHarnessCases.cs`)
Registered in this order (→ shortcuts D1–D4):
1. **Spawn Infantry** (D1) — enqueue InfantrySoldier (TKB 2002) at an incrementing slot → mannequin model visual.
2. **Spawn Vehicle** (D2) — enqueue MilitaryAPC (TKB 2001) → box visual.
3. **Clear All** (D3) — `ClearUpdates()` then `World.DestroyEntity` for every entity with `SimTransform`; visuals reconcile away next tick.
4. **Spawn Orbiting Ghost** (D4) — a **non-owned** entity created directly in `World`, orbited each frame via a continuous hook; its visual is forward-synced by Pass-B.

## Design Decisions

- **Buttons are tinted rectangles, not text buttons.** The project ships **no compiled `SpriteFont` (`.sdfnt`) asset** (verified: only `.sdm3d/.sdmat/.sdanim/.sdprefab/...` under `Stride/Assets/`). A `TextBlock`/text `Button.Content` requires a `SpriteFont`, so I render each `Button` as a fixed-size `BackgroundColor` rectangle (`SizeToContent=false`, `Width/Height` set) and put the **human-readable label next to it via DebugText**. DebugText uses Stride's built-in debug font and needs no asset, so labels and full status always render.
- **DebugText is the guaranteed status channel; keyboard is the guaranteed trigger.** Per the batch's "robust fallback" guidance, the DebugText list + D1–D9 path works with zero asset/compositor dependencies. Buttons are the nice-to-have on top.
- **Per-frame drive uses the render-frame wall delta** (not the fixed sim dt) so the orbiting ghost advances smoothly regardless of sim cadence.
- **Continuous-hook model** (`Func<float,bool>`) keeps continuous cases (orbit) and one-shot cases (spawn) under one registry with no special-casing; `Clear All` stops hooks so they can't touch a destroyed entity.

## [VERIFY] Results

### GraphicsCompositor UI render stage — ALREADY PRESENT, no change needed
`Stride/Assets/GraphicsCompositor.sdgfxcomp` already contains a UI render feature:
```
93933ad00d0c357d4915ad462cbfd04c: !Stride.Rendering.UI.UIRenderFeature,Stride.UI
    RenderStageSelectors:
        … SimpleGroupToRenderStageSelector … RenderStage: ref!! <Transparent stage>
```
So `UIComponent`s on scene entities are picked up by the existing `UIRenderFeature` (routed to the Transparent stage) — **I did not need to add a UI stage or modify the compositor**. The buttons render through this feature; the status text renders through `DebugTextSystem` (independent of the compositor's UI feature entirely). I could not visually confirm rendering (no GPU), so this is "wired against the present feature", not "observed".

### Stride 4.2.1.2487 APIs used (verified against the NuGet XML docs / DLLs)
- `Stride.Engine.UIComponent` (in `Stride.UI`): `Page` (`UIPage`), `IsFullScreen`, `IsBillboard`.
- `Stride.Engine.UIPage.RootElement`.
- `Stride.UI.Panels.Canvas`, `Panel.Children` (`UIElementCollection`); positioning via `UIElementExtensions.SetCanvasAbsolutePosition(Vector3)` / `SetCanvasPinOrigin(Vector3)`.
- `Stride.UI.Controls.Button`: `SizeToContent`, `Width/Height`, `BackgroundColor`; `ButtonBase.Click` routed event (`EventHandler<RoutedEventArgs>`, subscribed with `button.Click += (s,e)=>…`).
- `Stride.Profiling.DebugTextSystem.Print(string, Int2, Color4?, TimeSpan?)`, reached via `Game.DebugTextSystem` (Game property — verified).
- `Stride.Input.InputManager.IsKeyPressed(Keys)` via `Game.Input`; `Keys.D1..D9` are contiguous.
- `Stride.Core.Mathematics.Color` → `Color4` via `Color.ToColor4()`.

### How to make a Mode-1 non-owned entity (documented)
Create the entity **directly** via `World.CreateEntity()` + `World.AddComponent(...)`, bypassing the spawn/authority pipeline. Verified in `EntityRepository`: `AddComponent`/`AddUnmanagedComponent` only sets the **component** mask, **never** the `AuthorityMask` bit (authority is granted only by `SetAuthority` or the `localNodeId=0` spawn path). So a directly-created entity with `SimTransform`+`TkbIdentity` is non-owned for `SimTransform` and is matched by `.WithoutOwned<SimTransform>()` — exactly the selector `SplitAuthorityStrideSyncScript` Pass-B uses. A unit test asserts `World.HasAuthority<SimTransform>(ghost) == false` and that the ghost appears in the `.WithoutOwned<SimTransform>()` query. (Direct `World` writes are allowed: the default phase permission outside a kernel tick is `ReadWriteAll`.)

## How Clear-All and Orbiting-Ghost validate reconciliation / forward-sync LIVE

- **Clear All → §7 death/teardown reconciliation.** Destroying the FDP entities leaves dangling keys in `StrideVisualBindingSystem._visuals`. On the next `Tick`, Pass-A (`SyncExistenceOnly`, called from `SplitAuthorityStrideSyncScript`) detects `!World.IsAlive(key)`, calls `factory.Destroy(handle)`, and removes the entry → the visuals disappear. Test `ClearAll_Case_DestroysAllEntities_AndVisualsReconcileAway` asserts `World.EntityCount==0`, `Visuals` empty, and `DestroyCount == prior CreateCount`.
- **Orbiting Ghost → §7 Pass-B forward-sync.** The ghost is non-owned for `SimTransform`, so each `Tick` Pass-B (`.WithoutOwned<SimTransform>()`) reads its `SimTransform` and calls `factory.UpdatePose(...)`. The continuous hook moves the ghost's `SimTransform` in a circle each frame, so the visual visibly orbits — the one motion possible with NoOp physics. Test `OrbitingGhost_Case_ForwardSync_MovesTheVisual_OverFrames` asserts the `SimTransform` moves >0.1 m and that the **visual** pose recorded by the fake factory tracks the moved transform.

## On-screen layout + controls (for the human)

- **Top line:** `== Stride Test Harness (BATCH-12) ==  click a button or press D1-D9` (gold).
- **Left column:** a tinted rectangular **button per case**, with its label drawn to the right as `[D1] Spawn Infantry`, `[D2] Spawn Vehicle`, `[D3] Clear All`, `[D4] Spawn Orbiting Ghost`.
- **Right column (status):** `Last action`, `FDP entities`, `Visuals`, `Live hooks`, then `Recent:` with the last up-to-6 triggered actions.
- **Controls:** click a button **or** press **D1–D9** (first 9 cases). Both paths call the same `VisualTestCase.Run`. Camera free-flight (WASD/Q/E/right-drag) from BATCH-10 is unchanged; fly to the spawns at FDP Y≈5.

## Deviations
- **Buttons have no text labels** (rendered as colored rectangles; labels via DebugText) — WHAT: no `Button.Content` text. WHY: no `SpriteFont` asset compiled in the project. BENEFIT: buttons still render + are clickable with zero new assets; labels still visible. RISK: a user must read the DebugText list to map a button to its case (mitigated by aligning each button row with its `[Dn] Label` line). Adding a `.sdfnt` later is a one-line `Button.Content = new TextBlock{ Font = … }` upgrade.
- **New tests raise the Game count from 33 → 48** (15 new). Not a regression; all green.

## Test Results
- `Hrot.Stride.Core.Tests` — **215 passed**, 0 failed.
- `Hrot.Stride.Animation.Tests` — **4 passed**, 0 failed.
- `HrotStrideApp.Game.Tests` — **48 passed**, 0 failed (33 pre-existing + **15 new** in `TestHarnessTests.cs`).
- `dotnet build Stride/HrotStrideApp.sln -c Debug` → **Build succeeded, 0 errors** (only pre-existing NU1608 NuGet warnings).

New tests cover: registry order/trigger/out-of-range/validation; continuous-hook run-until-false / delta passing / throw-isolation / clear; and the four cases against a real headless `EditorStrideSubsystem` (infantry+vehicle spawn → entity+visual; clear-all → entities gone + visuals reconciled + destroys counted; ghost is non-owned & in the `.WithoutOwned` query; ghost forward-sync moves the visual over frames; ghost hook stops after Clear All).

## What I could / couldn't verify
- **Could (headless, automated):** registry/context behaviour; all four cases' effect on the FDP world and the recording fake factory; the non-owned ghost authority bit and Pass-B forward-sync driving the visual pose; compile of the whole solution; all three test suites green; the compositor already has a `UIRenderFeature`.
- **Couldn't (needs GPU — human validates):** that the `Button` rectangles actually draw/are hit-testable on screen, that `DebugTextSystem` text appears, and that keyboard D1–D9 reach `Input.IsKeyPressed` in the live window. I do **not** claim the UI renders — only that it compiles and is wired against the verified APIs and the present compositor UI feature.

## Developer Insights
- The GraphicsCompositor already shipping a `UIRenderFeature` is the single most load-bearing finding — it removed the only risky [VERIFY] (no compositor edit needed).
- The missing `SpriteFont` asset is the real constraint on rich UI; DebugText neatly sidesteps it and is the robust path the batch anticipated.
- `EntityQuery` is foreach-enumerable but does not expose LINQ extensions (custom enumerator) — collect into a list before LINQ.

## Known Issues
- No `SpriteFont`, so button faces are unlabeled rectangles (labels via DebugText). Add a font asset to upgrade to text buttons later.
- UI button rendering/hit-testing is unverified on GPU (documented above).
- Spawn-cursor layout is a simple row-wrap; many spawns will overlap older rows — fine for manual testing.

## Suggested Commit Message
`feat(stride): in-app test harness — extensible VisualTestCase registry + DebugText/UI overlay + D1-D9, seeded with P0-P3 cases (BATCH-12, STR-TEST-1)`
