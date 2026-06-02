# BATCH-12: In-app Stride test harness (manual visual-test UI)
**Tasks:** STR-TEST-1 (cross-phase test-harness infrastructure + initial P0–P3 test cases)   **Phase:** tooling / glue   **Est:** ~5–7h
**Dependencies:** BATCH-10/11 (live app boots `editor_stride`, renders, logs via NLog).

**Purpose.** There is no editor (raylib/ImGui) window yet (that's P5), and almost all behavior is GPU-only and can't be tested headlessly. The user needs an **in-app way to trigger test scenarios and see results** during visual testing. Build a reusable **Stride test-harness overlay** — clickable buttons + on-screen status — driven by an **extensible test-case registry**, so every subsequent phase (P4 animation, P5 gizmos, P6 networking) adds its manual test cases here. This batch builds the harness and seeds it with test cases that exercise the **already-working P0–P3** behavior (so you also validate the harness itself).

**Reality of verification.** This renders in the GPU window and **cannot be verified headlessly** — build it correctly against the verified Stride.UI / input / DebugText APIs, compile clean, keep tests green, and document the exact on-screen layout + controls. The human validates by running. Route all harness status through NLog too (BATCH-11) so the log file records what was triggered.

No Corrective Task 0.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — working contract.
2. The live wiring you build on: `Stride/HrotStrideApp.Game/StrideHrotGame.cs` (boots `EditorStrideSubsystem`, has the demo spawns + diag log), `EditorStrideSubsystem.cs` (exposes `World`, `ScenarioSource`, `VisualBindingSystem`, `Tick`), `StrideVisualFactory.cs`, `StrideLogging.cs` (NLog).
3. `reviews/BATCH-10-REVIEW.md`, `reviews/BATCH-09-REVIEW.md`, `DEBT-TRACKER.md`.

Use the **codebase-memory MCP first** (project `D-Work-IOS-IG-SimHost-FDP`).

### Verified facts & exact references
- Stride.UI is referenced by `HrotStrideApp.Game`. There is a `Stride/Assets/GraphicsCompositor.sdgfxcomp`. **[VERIFY]** whether the compositor includes a **UI render stage** — if Stride.UI buttons won't render without one, either add the UI render feature/stage to the compositor, or render the harness UI via a `UIComponent` on an entity that the scene renderer draws. **[VERIFY]** the Stride 4.2.1.2487 UI API: `UIComponent`, `UIPage`/`RootElement`, `Canvas`/`StackPanel`, `Button`, `TextBlock`, a default `SpriteFont` (you may need to provide/[VERIFY] a font for text/buttons).
- **Always-available fallbacks (use these so the harness works even if UI input is finicky):** Stride's `DebugTextSystem` (`Game.DebugText`/`this.DebugText.Print(text, position)`) renders on-screen text with no compositor UI setup — use it for the status/test-case list. Keyboard input via `Input.IsKeyPressed(Keys.D1..D9)` to trigger cases — bind number keys to the same actions as the buttons. [VERIFY] `DebugTextSystem` access on `Game`.
- `EditorStrideSubsystem` surface: `World` (`EntityRepository`), `ScenarioSource.Enqueue(EntityCreationRequest)`, `VisualBindingSystem` (`.Visuals`), `Tick`. Spawn pattern (from BATCH-10): `EntityCreationRequest { OwnerAppInstanceId=0, TkbType=2002|2001, InitialComponents={ new SimTransform{...}, new TkbIdentity{TkbType=...} } }`. `FdpStrideTransform` for FDP↔Stride.

**Work autonomously; build to a clean compile + green tests.** Only stop on a genuine breaking design flaw.

---

## Task: Stride test harness + initial P0–P3 test cases (STR-TEST-1)

1. **Test-case registry (extensible — the key deliverable).** Create a small, reusable registry in `Hrot.Stride.Core` (engine-agnostic part) and/or `HrotStrideApp.Game` (UI part):
   - `sealed record VisualTestCase(string Label, string Description, Action<TestHarnessContext> Run)` (or an `IVisualTestCase` interface — your call).
   - `TestHarnessContext` exposing what cases need: the `EditorStrideSubsystem` (World/ScenarioSource/VisualBindingSystem), the active `Scene`, the camera entity, a `Log(string)` that writes via NLog (+ optional on-screen echo), and a small per-frame `Update(dt)` hook so a case can run continuous behavior (e.g. an orbiting entity) until toggled off.
   - A `TestHarnessRegistry` that other code calls `Register(VisualTestCase)` on. **Document the one-line pattern** a future phase batch uses to add a case.
2. **Harness UI/overlay** in `HrotStrideApp.Game` (e.g. `StrideTestHarness` + a `UIComponent` or a script):
   - Render a **clickable button per registered test case** (Stride.UI `Button` in a `StackPanel`/`Canvas`), label = case label; clicking runs the case.
   - Render an **on-screen status area** showing: the list of cases with their keyboard shortcut, the last action triggered, and a few lines of recent harness log. Prefer `DebugTextSystem` for this (robust, no compositor dependency).
   - **Also bind keyboard shortcuts** (D1–D9) to the first 9 cases — a guaranteed-working trigger path if UI button hit-testing needs more wiring than expected. (Both paths call the same `VisualTestCase.Run`.)
   - Wire into `StrideHrotGame`: construct the harness after `BootEditorSubsystem`, register the initial cases, and drive its per-frame update + input from `Update(GameTime)`.
3. **Initial P0–P3 test cases** (all visible/meaningful in the current app — physics is still NoOp, so pick cases that don't need Bullet):
   - **"Spawn Infantry"** — enqueue an InfantrySoldier (2002) spawn at an incrementing position in view; a new mannequin appears.
   - **"Spawn Vehicle"** — enqueue a MilitaryAPC (2001); a box appears.
   - **"Clear All"** — destroy all live FDP entities (`World`), and confirm the visuals disappear next tick (validates the §7 death/teardown reconciliation **live**).
   - **"Spawn Orbiting Ghost"** — create a **non-owned** entity (so the `SplitAuthorityStrideSyncScript` Pass-B forward-sync drives its visual) and move its `SimTransform` in a slow circle each frame via the context `Update` hook; its mannequin visual should visibly orbit. This validates the forward-sync→visual path **live** (the one bit of motion possible without Bullet). [VERIFY] how to create a `.WithoutOwned<SimTransform>()` entity in Mode-1 (e.g. create directly via `World` without the spawn-path authority grant); document the approach. If a true ghost is impractical, fall back to: spawn normally + directly drive its visual via the registry context, and clearly label it a forward-sync demo.

## Success Criteria
- [ ] Reusable `TestHarnessRegistry` + `VisualTestCase` + `TestHarnessContext` with a documented one-line registration pattern for future phases.
- [ ] In-app harness renders clickable buttons + on-screen status (DebugText), with D1–D9 keyboard shortcuts as a fallback; wired into `StrideHrotGame`; actions + status logged via NLog.
- [ ] The 4 initial cases work (spawn infantry/vehicle, clear-all with visible despawn, orbiting ghost with visible forward-synced movement).
- [ ] `Stride/HrotStrideApp.sln` builds clean (0 errors); Stride test projects green (215 Core / 33 Game / 4 Animation); report written.

## Report Requirements (`reports/BATCH-12-REPORT.md`)
Answer: whether the GraphicsCompositor needed a UI render stage (and what you did) ([VERIFY] result); the Stride.UI + DebugText + input APIs used; the registry/context design + **the exact one-line pattern a future phase uses to register a test case** (so I can put it in later batch instructions); how the "Clear All" and "Orbiting Ghost" cases validate reconciliation + forward-sync live; the on-screen layout + controls (buttons + D1–D9) for the human; what you could/couldn't verify (can't run GPU); suggested commit message. Report actual test counts. Do NOT claim the UI renders — only that it compiles and is wired.
