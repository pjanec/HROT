# Stride‑2 Onboarding — Full Editor UI in the Stride Second Window

**For:** my future self continuing this work in a fresh thread.
**Written:** 2026‑06‑05, at the end of the `stride-1` effort (Mode‑1 integration complete & GPU‑verified).
**Branch:** `blueprint-integ-1`.

---

## 0. Read these first (in order)

1. `.dev/.guides/DEV-LEAD-GUIDE_claude.md` — the Dev Lead operating model (Plan → Delegate → Review → Commit → Repeat).
2. `.dev/.guides/DEV-GUIDE_claude.md` — coding/test conventions for delegated coders.
3. `.dev/stride-1/TASK-TRACKER.md` — what's done (Phases 0–5, all GPU‑verified) + the batch history (BATCH‑01…27).
4. `.dev/stride-1/DEBT-TRACKER.md` — open/resolved debt (STR‑D1…D21). **Still OPEN and relevant: STR‑D2, D3, D7, D17, D18.**
5. `.dev/stride-1/reports/BATCH-22-REPORT.md` (dual‑window) and `BATCH-23-REPORT.md` (selection) — the second‑window mechanism you're extending.
6. **`CLAUDE.md`** project rule: use the **Codebase Memory MCP** (`mcp_codebase-memo_*`) FIRST for exploration before reading files.

---

## 1. THE TASK (what the user wants)

> "Extend the secondary window to show the **full content of what the ClusterRunner editor subsystem is showing**."

**My understanding (confirmed with the user):** The Stride app's optional second window (today a minimal read‑only stub) should host the **complete existing Hrot editor UI** — all the panels the real editor renders (entity tree/orbat, full component inspector, scenario browser, spawner, mission/config, cluster diagnostics, event browser, blueprint/BT/HSM windows, asset browser, timeline/time‑controls, logs, etc.) — running in that second window, on the host thread, over the **same live FDP world** the Stride 3D view is simulating. It becomes the *real* editor next to the Stride view, not a toy inspector.

This is **NOT Mode 2 (P6)**. Mode 2 comes after, and is gated on STR‑D18.

---

## 2. The single most important fact

`Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs` was deliberately built to **mirror the simulation + orchestration core of `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (lines 449–1092) WITHOUT its Raylib/WinForms/ImGui UI** (see the class XML‑doc at `EditorStrideSubsystem.cs:49‑82`).

**The "full content" the user wants = exactly the UI half that was omitted.** So the job is: bring `EditorSubsystem`'s UI (its panels + the Window Manager) into the Stride second window, **bound to the live world `EditorStrideSubsystem` already simulates** — do **NOT** boot a second simulation. This sim/UI split is the crux of the whole task.

---

## 3. Code map (verified file:line — re‑confirm, code drifts)

### The editor UI to port
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (2279 lines) — `ISubsystem`, the full editor.
  - `Initialize(SubsystemConfig config)` — line **449** (sim + UI setup; EditorStrideSubsystem mirrors only the sim part).
  - `Update(float deltaTime)` — line **1332**.
  - **`DrawUI()` — line 1434** ← the per‑frame UI render (renders all registered panels). **This is the entry point to pump in the second window.**
  - **`RegisterWindows(WindowManager windowManager)` — line 1552** ← registers every editor panel/window with the Window Manager. Must run once at setup.
  - Panel fields (lines ~199–280): `ScenarioBrowserPanel`, `EditorToolbarPanel`, `EditorOrbatPanel`, `SpawnerPanel`, `MissionPanel`, `ConfigPanel`, `SharedOrbatPanel`, `PreviewPanel`, `ZoneEditorPanel`, `FdpEntityInspectorPanel`, `FdpEventBrowserPanel`, `ClusterScenarioPanel`, `ClusterDiagnosticsPanel`, `AssetBrowserWindow`, Blueprint/BT/HSM windows, etc.
- `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs`
  - `class WindowManager` (line 16); **`Render(...)` — line 316** ← draws all registered windows. `DrawUI()` ultimately drives this.
- Panels live under `Fdp.Presentation.Panels`, `Hrot.Presentation.*`, `Hrot.Editor.AiShared.Windows`, `Hrot.Orchestrator.Panels`, `Hrot.UI.Common.Panels`, `Hrot.Blueprints.Editor.Windows`. (Projects under `Hrot/Editor/…`, `Hrot/Engine/Hrot.Presentation`, `FDP/Engine/Fdp.Presentation`.)

### The second window (what you're extending)
- `Stride/HrotStrideApp.Game/StrideInspectorWindow.cs`
  - `StrideInspectorWindowConfig` — env flag `STRIDE_EDITOR_WINDOW=1` enables the window.
  - `StrideInspectorWindow` (class ~448) — uses `rlImGui_cs` + `ImGuiNET`. Lifecycle proven in BATCH‑22:
    - `Open()` — creates the GLFW/OpenGL window via `Raylib.InitWindow` + `rlImGui` init.
    - **`PumpFrame()` — called once per frame from `StrideHrotGame.Update`**; `BeginDrawing()/rlImGui.Begin()` → render ImGui → `rlImGui.End()/EndDrawing()`. **This is where you currently draw the stub panels and where you'll instead drive the full editor `DrawUI()`.**
    - `Close()/Dispose()`.
  - `StrideInspectorViewModel` (pure, headless‑testable) + `EntityRow`/`InspectorField` — the current stub's data mapping (entity list + a few fields). Likely **replaced/retired** once the real panels are in.
  - `EditorSelectionState` (line ~128) — shared selection (BATCH‑23): `Select/Clear/ClearIfDead/Version` + one‑shot `RequestCenter/ConsumeCenter`. Reused by the Stride view for the highlight + `C` center. **Keep this; the real editor panels should drive the same selection so selection stays shared between the two windows.**

### The Stride host (wiring point)
- `Stride/HrotStrideApp.Game/StrideHrotGame.cs`
  - `BootEditorSubsystem` — constructs `EditorStrideSubsystem`, the `StrideInspectorWindow` (`.Open()` when enabled), the selection state, navmesh bake, etc.
  - `Update(GameTime)` — host loop: drives `EditorStrideSubsystem.Tick(dt)` then `_inspectorWindow?.PumpFrame()`. **Both windows pumped on one thread (Option A, proven).**
  - `EndRun()` — disposes the inspector window.
- `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs` — the live sim/world. Exposes `World` (the `EntityRepository`), `SelectionState`, `PhysicsBodyLifecycle`, nav systems, etc. **This is the world the editor panels must bind to.**

---

## 4. Integration approach (recommended; refine in a Plan)

The win is **reuse, not reimplement**. Two viable shapes — research which is cleaner:

**Option A — host the real `EditorSubsystem` UI half over the existing world.**
Instantiate `EditorSubsystem` (or just its panel/WindowManager machinery) in UI‑only mode: call `RegisterWindows(windowManager)` once, then `DrawUI()` each `PumpFrame`, but wire its world/services/selection to the **live** ones from `EditorStrideSubsystem` rather than letting it boot its own sim. The hard part: `EditorSubsystem.Initialize(SubsystemConfig)` does a lot of sim+service setup intertwined with UI setup — you must separate "UI needs" (WindowManager, panels, selection store, the `SubsystemConfig`/services panels read from) from "sim needs" (already provided by `EditorStrideSubsystem`). Map each panel's data source to the live world/services.

**Option B — stand up the Window Manager + the specific panels directly.**
Create a `WindowManager` in the second window, register the panels you want (incrementally — start with the full entity inspector + orbat + scenario + diagnostics), each bound to the live `EditorStrideSubsystem.World`/services + the shared `EditorSelectionState`. Pump `WindowManager.Render(...)` in `PumpFrame`. More incremental/controllable; less "all or nothing".

**Recommendation:** start with **Option B incrementally** (get the full FDP entity inspector + a couple of high‑value panels rendering against the live world first — that's already "much more than the stub"), then pull in more panels / converge toward the whole `EditorSubsystem.DrawUI()` set. Validate the **sim/UI split** early — confirm panels render the live entities and selection is shared, with **no second simulation**.

**First concrete step for the next thread:** a short research/Plan pass — open `EditorSubsystem.Initialize` (449) + `RegisterWindows` (1552) + `DrawUI` (1434), and the `WindowManager`, and enumerate (a) what each panel needs (world/services/`SubsystemConfig`/selection), (b) which of those the Stride app already has live vs must construct, (c) the minimal set to render the inspector+orbat+scenario panels. Then delegate implementation.

---

## 5. Gotchas & constraints

- **Host‑thread dual pump is PROVEN (BATCH‑22, "Option A").** Stride = DirectX, raylib = GLFW/OpenGL — independent device contexts, safe to pump sequentially on the host thread. Do **not** call `FdpApplication.Run()` (it has a blocking loop); use the `Open()/PumpFrame()/Close()` lifecycle.
- **STR‑D7:** `OfflineNetworkFactory` (Hrot.Editor) already drags Raylib/WinForms/ImGui into `HrotStrideApp.Game`, so the editor UI assemblies are reachable — but pulling in the full editor will widen the dependency surface; expect to add project references to the panel/WindowManager assemblies. Watch for WinForms‑only bits that don't belong in the raylib window.
- **STR‑D3 footgun:** `Stride.Engine.Entity` vs `Fdp.Core.Entity` name collision — qualify with `global::Stride.Engine.Entity` where both are in scope.
- **Selection must stay shared:** the real editor panels should read/write the same `EditorSelectionState` so clicking in the editor highlights in the Stride view (and `C` centers). Reconcile the editor's own selection store with `EditorSelectionState` (bridge or replace).
- **Coordinate seam:** `FdpStrideTransform` (FDP X=East,Y=North,Z=Up ↔ Stride Y‑up LH). Panels show FDP values; the Stride view swizzles. Don't double‑convert.
- **Windowing/sizing:** the second window already opens; you may want it larger / dockable (the WindowManager does ImGui docking). Confirm ImGui docking branch is available in the `ImGuiNET` build used.

---

## 6. Working agreements (how I run this — keep doing it)

- **I am Dev Lead.** Plan → delegate implementation to **sonnet** sub‑agents (`Agent` tool, `model: sonnet`) → review hard → build+test → commit each batch. **User directive: ALL development/fix/test‑iteration work goes to sonnet to save Opus limits. Opus only for research/design/review/orchestration.**
- **GPU is user‑verified only.** `StrideHrotGame.Run()` needs SDL2+DirectX; there's no headless GPU. Build it correct + headless‑test the pure logic; the user runs the app and reports. Read `Stride/Bin/Windows/Debug/win-x64/logs/editor_stride.log` (NLog) for diagnostics.
- **HARD‑WON LESSON (cost ~6 GPU rounds on F6/F7): "headless‑green ≠ live‑works."** Unit tests of systems in isolation passed while the assembled live system failed (component stamping, system‑query overlap, tick order, channel arbitration, nearest‑entity resolution). **Build/extend the assembled‑subsystem integration tests** (`Stride/HrotStrideApp.Game.Tests/AssembledNavIntegrationTests.cs`) that tick the REAL system set/order with the REAL entity types — and make them faithful (right entity type, right tick order) or they'll lie. For UI work this matters less (UI is GPU‑verified anyway), but the principle stands: test what the live path does.
- **Commit discipline:** branch is `blueprint-integ-1`; commit each reviewed batch. End commit messages with `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`. **Quirk:** a stray deletion of `.dev/stride-1/Stride-Integration_v0_1.md` keeps appearing in `git add -A`; run `git checkout HEAD -- .dev/stride-1/Stride-Integration_v0_1.md` before every commit to keep commits clean.
- **Windows host.** Use portable `git`/`dotnet` commands (PowerShell or the Bash tool/git‑bash). Avoid Unix‑only tools like `find`.
- **Shared‑code regression rule:** if you touch shared `FDP/**` or `Hrot/**` (not just `Stride/**`), A/B vs HEAD with `git stash` and confirm **no NEW** test failures. **Current branch baselines** (NOT the stale STR‑D15 "25"): `Fdp.Examples.Scenarios.Tests` = **30 failed** (pre‑existing, branch divergence from `main` missing commit `b200cd14`); `Fdp.Toolkits.Tests` ≈ **44 failed** at HEAD (flaky, ranges ~26–44). `Hrot.StrideMock.Tests` ~10 pre‑existing fails (STR‑D6). These are pre‑existing; just don't ADD failures.

---

## 7. Build & test commands (Windows)

```
# Build the Stride app (0 errors expected)
dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug

# Stride test suites (all must stay green) — current counts ~:
#   Core 327 / Animation 48 / Game 210
dotnet test Stride/Hrot.Stride.Core.Tests/Hrot.Stride.Core.Tests.csproj -c Debug
dotnet test Stride/Hrot.Stride.Animation.Tests/Hrot.Stride.Animation.Tests.csproj -c Debug
dotnet test Stride/HrotStrideApp.Game.Tests/HrotStrideApp.Game.Tests.csproj -c Debug

# Shared-suite regression A/B (only if you touch FDP/** or Hrot/**):
#   git stash → run the suite → git stash pop → run again → diff failures
dotnet test Fdp/Examples/Fdp.Examples.Scenarios.Tests/Fdp.Examples.Scenarios.Tests.csproj -c Debug
dotnet test Fdp/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj -c Debug
```

**Run the app (user does this):** launch `HrotStrideApp.Windows`; set env `STRIDE_EDITOR_WINDOW=1` to open the second window. Harness keys in the Stride window: D0 drop, F1 walk, F2 drive, F3 steer‑to‑point, F4 vehicle navmesh, F5 char navmesh, F6 char NavigationIntent, F7 vehicle NavigationIntent, D8 gizmo test; **C** = center camera on selection.

---

## 8. Current state of the Stride integration (context)

**Mode 1 (Phases 0–5) COMPLETE & GPU‑verified (2026‑06‑05):** Stride/Bullet is the real backend behind FDP for physics (gravity/collision/resting), character movement+locomotion animation, a dynamic‑rigidbody vehicle (drives/turns/collides), navigation for BOTH characters and vehicles over a runtime‑baked DotRecast navmesh **through the production `NavigationIntent` interface** (F6/F7), plus editor‑facing: gizmo GPU draw (`PooledEntityDebugDrawSink3D`), the dual‑window inspector stub, shared selection + center camera.

**Batch history:** BATCH‑17 physics; 18 vehicle navmesh; 19 char crowd; 20 production‑interface nav; 21 gizmo sink; 22 dual‑window; 23 selection+center; 24–27 the F6/F7 "live‑broken" saga (channel arbitration, translator `VehicleState` footgun, nearest‑entity resolver, etc.). Reports in `.dev/stride-1/reports/BATCH-*-REPORT.md`.

**Open debt to keep in mind:**
- **STR‑D18** — the vehicle is a Bullet **dynamic** rigidbody (Bullet owns its motion), a deviation from the kinematic design. **Must be settled before Mode 2** (egress/dead‑reckoning assumed kinematic). Not relevant to the editor‑window task, but it's the gate for P6.
- **STR‑D17** — vehicle uses placeholder `Box2x1x1` model (content).
- **STR‑D2** — `FdpStrideTransform.ScreenRayToFdp` untested; relevant if you add **picking** (click an entity in the Stride view to select it) — a natural companion to the full editor window.
- **STR‑D7 / STR‑D3** — see §5.

**After this task:** Mode 2 (P6) — networked Stride node over DDS — settling STR‑D18 first.

---

## 9. Definition of done (this task)

- The second window (`STRIDE_EDITOR_WINDOW=1`) shows the real editor panels (at minimum: full entity/component inspector + orbat/scenario + diagnostics), rendering the **live** FDP world `EditorStrideSubsystem` simulates — **no second sim**.
- Selecting in the editor window highlights/centers in the Stride view (shared `EditorSelectionState`), and vice‑versa where feasible.
- Stride suites stay green; no new failures in shared suites (A/B vs HEAD).
- Both windows still pump on the host thread without blocking; all harness keys still work.
- A `BATCH-NN-REPORT.md` under `.dev/stride-2/reports/` + tracker updates; user GPU‑verifies.
