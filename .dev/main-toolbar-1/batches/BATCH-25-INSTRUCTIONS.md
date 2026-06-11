# BATCH-25: Relocate the main toolbar into the menu bar + shrink icons to frame height
**Tasks:** UX fix (post-completion)   **Est:** ~5h
**Dependencies:** BATCH-24 (toolbar groups wired).

## Problem (from interactive testing)
The toolbar is a separate full-width band at **64px** tall — it eats too much vertical space, and the
16px famfamfam silk icons look blurry upscaled to 64px. **Goal:** render the toolbar **inside the main
menu bar, to the right of the menus, at menu-bar (frame) height**, with icons sized to that height —
removing the separate band and its dockspace inset (reclaiming the vertical space).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md`.
2. `.dev/main-toolbar-1/DESIGN.md` §4.1 (note: §4.1.2's separate-band placement is being revised here).
3. Current code:
   - `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/MainToolbarManager.cs` — `Render(...)` opens its
     own `##MainToolbar` window (L184-234, pos=WorkPos, size=WorkSize.X×height).
   - `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs` — menu bar at
     `BeginMainMenuBar()` (~L397-407: RenderGlobalMenu + RenderPerspectiveMenu); the toolbar band is
     rendered separately at `_mainToolbar.Render(CurrentPerspective)` (~L438).
   - Icon sizes (all 64): `PerspectiveToolbarSection.DefaultIconSize`, `ToolbarCommandAdapter.DefaultSize`,
     `MainToolbarTimeControlSection` (`const float iconSize = 64f`).
   - `Hrot/Runner/Hrot.ClusterRunner/Program.cs` — dockspace inset uses `MainToolbar.Height`
     (`DockspaceLayout.CentralPos/CentralSize`).

## Scope
### 1. Inline render inside the menu bar
- Add `MainToolbarManager.RenderInline(string currentPerspective = "")` that draws the registered
  entries/separators **left-to-right within the CURRENT window** (no `Begin`/`End`, no `SetNextWindow*`)
  — same ordering/perspective-filter/separator logic as `Render`, but assuming it's called inside an
  active menu bar. Use `Gui.SameLine()` between items as today.
- In `WindowManager.Render`, call `_mainToolbar.RenderInline(CurrentPerspective)` **inside**
  `BeginMainMenuBar()` (after `RenderPerspectiveMenu()`, before `EndMainMenuBar()`), and **remove** the
  separate band call `_mainToolbar.Render(CurrentPerspective)` (~L438). Keep `_statusBar.Render(...)`.
  (Optionally add a small leading separator/spacing so the toolbar is visually distinct from the menus.)
- You may keep the old `Render` (band) method for compat or remove it if unused — document which.

### 2. Icon size = menu-bar / frame height
- Introduce a small toolbar icon size based on the menu-bar height — use `ImGui.GetFrameHeight()`
  (the menu bar is exactly frame height) for the icon button size (square). Replace the hardcoded `64f`
  in the three sections with this frame-height size so icons fill the menu-bar row crisply (the silk
  source art is ~16px; at ~frame height ~18-22px it renders sharp, not upscaled).
- The `declaredHeight` passed to `RegisterEntry` should likewise be frame-height (not 64) — but since
  the toolbar is now in the menu bar, `Height` no longer drives a dockspace inset (see #3).

### 3. Drop the dockspace toolbar inset
- The toolbar now lives in the menu bar (which ImGui already excludes from the viewport work area), so
  the central dockspace must NOT reserve extra toolbar height. In `Program.cs`, set the toolbar inset
  to **0** (stop subtracting `MainToolbar.Height` for the top inset — keep the status-bar bottom inset).
  Leave the pure `DockspaceLayout` helper as-is (it's still correct math; just pass toolbarHeight 0),
  or pass 0 at the call site. Net effect: no empty gap under the menu bar.

## Tests
- Update `MainToolbarManagerTests` / `WindowManagerMainToolbarTests` for the inline-render path
  (the recording-delegate "Render invokes entries" test should drive `RenderInline`; ordering/
  perspective/separator assertions unchanged). Keep the headless `GetVisibleItemPlan` tests.
- The BATCH-24 guardrail `EditorSubsystem_RegisterWindows_PopulatesMainToolbar` must still pass
  (entries still registered; Height may now be frame-height instead of 64).
- `DockspaceLayoutTests` stay green (pure math). If you change the Program.cs inset, no test covers
  Program.cs directly — that's fine.

## Hard constraints
- No scope creep beyond relocation + icon sizing + dropping the toolbar dockspace inset. Keep the
  toolbar groups/behavior (perspective radio, AI-debug, time control) intact — only placement + size change.
- Do NOT delete/modify legacy code. Do NOT weaken/skip tests; zero new warnings (TreatWarningsAsErrors).

## Definition of done
- Library projects compile cleanly (if the editor is running you'll see MSB3027/3021 file-LOCK copy
  errors into `Hrot.ClusterRunner/bin` — environmental, not compile errors; confirm compilation).
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. Updated tests pass. `Fdp.Presentation.Tests` toolbar/
  windowmanager classes (by class filter — PRE-2 full-suite deadlock) 0-failed; `Hrot.Editor.Tests`,
  `Fdp.Toolkits.Tests`, `Hrot.SimHost.Tests` 0-failed; `Hrot.Blueprints.Tests` (Stability filter) stays
  at exactly the 9 PRE-1 failures.
- Write `.dev/main-toolbar-1/reports/BATCH-25-REPORT.md`: the inline-render approach, the frame-height
  icon size, the Program.cs inset change, tests updated, test-run summaries.

If something cannot be done as specified, stop and report why rather than stubbing it.
