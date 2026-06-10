# BATCH-04: WindowManager.MainToolbar + dockspace top inset
**Tasks:** MTB-P1-T3   **Phase:** 1 — Toolbar & Icon Infrastructure   **Est:** ~5h
**Dependencies:** BATCH-03 (`MainToolbarManager` exists in `Fdp.Presentation.WindowManager`). Completes Phase 1.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/main-toolbar-1/DESIGN.md` §4.1.2 (Window placement & dockspace integration).
3. `.dev/main-toolbar-1/TASK-DETAIL.md` → MTB-P1-T3.
4. Existing wiring to mirror:
   - `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs` — `StatusBar` property
     (~L159–165) and the `_statusBar.Render(CurrentPerspective)` call in `Render` (~L361).
   - `Hrot/Runner/Hrot.ClusterRunner/Program.cs` — the dockspace setup (~L296–322) where
     `statusBarHeight` already insets the **bottom**.

## Scope — do ONLY this
### 1. `WindowManager.MainToolbar` (engine) — mirror `StatusBar`
In `WindowManager.cs`:
- Add `private readonly MainToolbarManager _mainToolbar = new();` and
  `public MainToolbarManager MainToolbar => _mainToolbar;` (mirror the `StatusBar` member/property).
- In `Render(...)`, call `_mainToolbar.Render(CurrentPerspective)` alongside the existing
  `_statusBar.Render(CurrentPerspective)` (the toolbar draws its own top-anchored band window).

### 2. Testable dockspace-inset helper (engine)
Add a small pure static helper in `Fdp.Presentation` (e.g.
`FDP/Engine/Fdp.Presentation/ImGui/WindowManager/DockspaceLayout.cs`) so the host's inset math is
unit-testable headlessly:
```csharp
public static class DockspaceLayout
{
    /// Central dockspace size given the work area and the top toolbar + bottom status-bar insets.
    /// Width = workWidth. Height = workHeight - toolbarHeight - statusBarHeight, clamped to >= 0.
    public static Vector2 CentralSize(float workWidth, float workHeight, float toolbarHeight, float statusBarHeight);
    /// Top-left of the central dockspace: workPos + (0, toolbarHeight).
    public static Vector2 CentralPos(Vector2 workPos, float toolbarHeight);
}
```

### 3. Wire the helper into `Program.cs` (Hrot.ClusterRunner) — §4.1.2
Replace the current bottom-only inset (~L315–321) so the dockspace is inset at the **top** by the
toolbar and at the **bottom** by the status bar:
- `toolbarHeight = windowCtrl.WindowManager?.MainToolbar.Height ?? 0f`
- dockspace window pos = `DockspaceLayout.CentralPos(viewport.WorkPos, toolbarHeight)` (use it for
  `SetNextWindowPos` so the dock region starts below the toolbar band)
- dockspace size = `DockspaceLayout.CentralSize(viewport.WorkSize.X, viewport.WorkSize.Y, toolbarHeight, statusBarHeight)`
- Keep the existing `WindowManager.Render()` call (which now also renders the toolbar band).
Match §4.1.2 exactly: `pos = WorkPos + (0, toolbarHeight)`,
`size = (WorkSize.X, WorkSize.Y - toolbarHeight - statusBarHeight)`. Do NOT change unrelated
dockspace flags/styles.

## Tests required
File: extend/add in `FDP/Engine/Fdp.Presentation.Tests/ImGui/WindowManager/`.
- **`WindowManagerMainToolbarTests`** (or add to existing WindowManager tests):
  - `MainToolbar_PropertyResolves` — `wm.MainToolbar` is non-null and is the same instance across calls.
  - `Render_InvokesMainToolbar` — register a recording entry on `wm.MainToolbar`; call `wm.Render(...)`
    inside the headless ImGui fixture (mirror how `StatusBar` rendering is exercised); assert the
    entry's render delegate was invoked. (If the existing WindowManager render test harness already
    drives `StatusBar`, reuse it.)
- **`DockspaceLayoutTests`** (NEW) — pure, no ImGui:
  - `CentralSize_SubtractsToolbarAndStatusBar` — `CentralSize(1920, 1080, 64, 24).Y == 1080-64-24`
    and `.X == 1920`.
  - `CentralSize_ClampsToZero_WhenInsetsExceedWork` — large insets → `.Y == 0` (never negative).
  - `CentralPos_OffsetsTopByToolbarHeight` — `CentralPos((10,20), 64) == (10, 84)`.

## Hard constraints
- Do NOT delete/modify legacy/assembly-loading code. No scope creep beyond the three items above.
- Keep public APIs of existing types intact (only ADD `MainToolbar`).
- Do NOT alter `MainToolbarManager` itself (done in BATCH-03) beyond using it.
- Do NOT weaken/skip/auto-pass tests or add a Stability trait to dodge a failure.

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings).
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New tests pass UNFILTERED. `Fdp.Presentation.Tests`
  0-failed for the toolbar/dockspace/WindowManager tests with
  `--filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"`.
  NOTE: the full `Fdp.Presentation.Tests` suite has ~19 PRE-EXISTING failures (Vis2D NRE +
  EntityInspector/EventBrowser) and can DEADLOCK if Vis2D NRE tests run together — to get a clean
  signal, run your new tests by class/namespace filter (e.g.
  `--filter "FullyQualifiedName~DockspaceLayout|FullyQualifiedName~MainToolbar|FullyQualifiedName~WindowManager"`),
  and do NOT try to "fix" the pre-existing Vis2D/inspector failures (out of scope; recorded as PRE-2).
- Write `.dev/main-toolbar-1/reports/BATCH-04-REPORT.md`: files changed, the new tests + assertions,
  paste actual test-run summaries, and answer the insight questions.

If something cannot be done as specified, stop and report why rather than stubbing it.
