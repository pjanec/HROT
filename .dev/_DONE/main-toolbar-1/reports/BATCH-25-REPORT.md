# BATCH-25 Report

## Implementation Summary

**Goal:** Relocate the main toolbar into the menu bar at menu-bar (frame) height, removing the separate 64px band and recovering vertical space.

### 1. Inline render inside the menu bar (`MainToolbarManager.RenderInline`)

Added `RenderInline(string currentPerspective = "")` to `MainToolbarManager` that draws registered entries and separators left-to-right **within the current ImGui window** — no `Begin`/`End`, no `SetNextWindow*`. The render loop was extracted into a private `RenderEntries()` shared by both `Render` (standalone band, kept for backward compat) and `RenderInline` (new production path).

In `WindowManager.Render`, `_mainToolbar.RenderInline(CurrentPerspective)` is now called inside `BeginMainMenuBar()`, after the existing menus and gizmo items, with a leading `Gui.Separator()` to visually separate the toolbar from the last menu. The guard `_mainToolbar.Height > 0f` ensures no empty separator when no entries are registered. The standalone `_mainToolbar.Render(CurrentPerspective)` call at ~L438 was removed.

**Files:**
- `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/MainToolbarManager.cs` — added `RenderInline` + extracted `RenderEntries`
- `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs` — inline call inside `BeginMainMenuBar`, removed standalone band call

### 2. Icon size = menu-bar / frame height

Replaced hardcoded `64f` icon sizes with `ImGui.GetFrameHeight()` (menu-bar frame height, typically ~20px) across all three sections:

- **`PerspectiveToolbarSection.cs`**: Changed `DefaultIconSize` from `static readonly Vector2 new(64f, 64f)` to `static Vector2 IconSize => new(Gui.GetFrameHeight(), Gui.GetFrameHeight())`. Render methods use `IconSize`; `declaredHeight` uses `MainToolbarManager.DefaultEntryHeight` (a safe wrapper that falls back to 20f when no ImGui context exists — e.g. headless tests).
- **`ToolbarCommandAdapter.cs`**: Changed `DefaultSize` from `static readonly Vector2 new(64f, 64f)` to `static Vector2 DefaultSize => new(Gui.GetFrameHeight(), Gui.GetFrameHeight())`. `declaredHeight` uses `MainToolbarManager.DefaultEntryHeight`.
- **`MainToolbarTimeControlSection.cs`**: Changed `const float iconSize = 64f` to `float iconSize = ImGui.GetFrameHeight()` (safe — Render always has an ImGui context).
- **`EditorSubsystem.cs`**: Changed `declaredHeight: 64f` for the TimeControlGroup entry to `declaredHeight: MainToolbarManager.DefaultEntryHeight`.
- **`MainToolbarManager.cs`**: Added `public static float DefaultEntryHeight` property — returns `ImGui.GetFrameHeight()` when a context exists, `20f` fallback otherwise.

### 3. Drop the dockspace toolbar inset

In `Program.cs`, set `toolbarHeight = 0f` (was `windowCtrl.WindowManager?.MainToolbar.Height ?? 0f`). The toolbar now lives in the menu bar, which ImGui already excludes from the viewport work area. The status-bar bottom inset is unchanged.

## Design Decisions

1. **`RenderEntries` extraction**: Both `Render` (old standalone band) and `RenderInline` share the same render logic via a private helper. This avoids code duplication and keeps the old path functional for any potential external callers.

2. **`MainToolbarManager.DefaultEntryHeight` fallback**: `ImGui.GetFrameHeight()` crashes when called without an active ImGui context (null `GImGui` pointer). The `DefaultEntryHeight` property guards with `ImGui.GetCurrentContext() != IntPtr.Zero` and falls back to 20f. This allows `RegisterEntry` calls in subsystem initialisation and headless tests to succeed while using the real frame height in production.

3. **`PerspectiveToolbarSection` and `ToolbarCommandAdapter` icon size split**: The render-time icon size uses `Gui.GetFrameHeight()` directly (always has a context). The `declaredHeight` passed at registration time uses `MainToolbarManager.DefaultEntryHeight` (safe wrapper). While `declaredHeight` no longer drives the dockspace inset (see #3), it still determines `MainToolbar.Height` which is checked by the BATCH-24 guardrail test and the inline-render guard in `WindowManager`.

4. **Separator rendering**: The `DrawSeparator` method uses `Gui.GetCursorScreenPos()` and `Gui.GetWindowDrawList()` — both work correctly inside a menu bar window (created by `BeginMainMenuBar`), so no changes were needed.

## Deviations

None. All three scope items implemented exactly as specified.

## Test Results

All tests run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`.

### Fdp.Presentation.Tests — toolbar/windowmanager classes (class filter)
```
Passed!  Failed: 0, Passed: 35, Skipped: 0, Total: 35
```
Classes covered: `MainToolbarManagerTests` (8), `WindowManagerMainToolbarTests` (3), `PerspectiveToolbarTests` (9), `ToolbarCommandAdapterTests` (7), `DockspaceLayoutTests` (8).

Key tests changed from `Render()` to `RenderInline()`:
- `RegisterEntry_DuplicateId_LastWriteWins` — drives `RenderInline` inside `BeginMainMenuBar`
- `Entries_RenderInAscendingSortOrder` — same
- `PerspectiveFilter_NullIsGlobal_NamedOnlyWhenMatch` — both perspective cases use inline path
- `Height_IsMaxDeclaredOverAllRegistered_RegardlessOfCurrentPerspective` — render path updated
- Headless `GetVisibleItemPlan` tests (separator ordering/perspective) — unchanged, all pass

### Hrot.Editor.Tests
```
Passed!  Failed: 0, Passed: 176, Skipped: 0, Total: 176
```

### Hrot.Presentation.Tests — MainToolbarTimeControlTests
```
Passed!  Failed: 0, Passed: 10, Skipped: 0, Total: 10
```
Full suite has pre-existing deadlock (Vis2D-related); time-control subset is clean.

### Fdp.Toolkits.Tests
```
Failed!  Failed: 22, Passed: 1859, Skipped: 0, Total: 1881
```
22 pre-existing failures (orchestration path resolution, unrelated to toolbar). No new failures.

### Hrot.SimHost.Tests
```
Failed!  Failed: 38, Passed: 604, Skipped: 3, Total: 645
```
38 pre-existing failures (sim host initialization, unrelated to toolbar). No new failures.

### Hrot.Blueprints.Tests (Stability filter)
```
Failed!  Failed: 9, Passed: 1854, Skipped: 8, Total: 1871
```
Exactly 9 pre-existing failures — matches the "EXACTLY the 9 PRE-1 failures" requirement:
1. `AiPrimitive_EmitMatchesGoldenSource("MoveToAndFire")` — golden source snapshot
2. `AiPrimitive_EmitMatchesGoldenSource("HasVisibleTarget")` — golden source snapshot
3. `Stage8_PdbContainsEmbeddedSource` — Stage8 PDB
4. `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb` — Stage8 compiler
5. `TickFrame_1000Frames_AllocatesZeroBytes` — allocation benchmark
6. `MoveToAndFire_GeneratedSource_Snapshot` — demo snapshot
7. `CF2_EndToEnd_DelayBreakpointPauses` — debugger end-to-end
8. `SetBreakpoint_TriggersAutoInstrument_ThenPauses` — debugger end-to-end
9. `WhenNode_ZeroAllocOnHotPath` — benchmark

**BATCH-24 guardrail `EditorSubsystem_RegisterWindows_PopulatesMainToolbar`**: PASSED (confirmed with focused run).

### Compilation
All library projects compile cleanly: 0 warnings, 0 errors in `Fdp.Presentation`, `Hrot.Presentation`, `Hrot.Editor`, `Hrot.ClusterRunner`. Test projects also compile (pre-existing warnings only).

## Developer Insights

- **Pre-existing deadlocks**: `Fdp.Presentation.Tests` full suite and `Hrot.Presentation.Tests` full suite both deadlock on pre-existing Vis2D tests. Class-level filters successfully isolate the toolbar-related tests.

- **`BeginMainMenuBar` in headless tests**: Works correctly with `ImGuiTestFixture` (which sets up a proper context with display size). No special setup needed beyond `NewFrame()`.

- **`Gui` namespace conflict in test project**: The `Fdp.Presentation.Tests/ImGui/` folder creates a namespace `Fdp.Presentation.Tests.ImGui` that shadows `ImGuiNET.ImGui`. Test files that need ImGui API calls must use fully qualified `ImGuiNET.ImGui.BeginMainMenuBar()`. The production code uses `global using Gui = ImGuiNET.ImGui` and is unaffected.

- **Icon size at frame height**: With the default ImGui style (font ~13px + frame padding ~7px), `GetFrameHeight()` returns ~20px. The 16px silk icons render at near-native resolution with minimal upscaling — dramatically sharper than the old 64px ×4 upscale.

## Known Issues

None. The old `Render` (standalone band) method is kept for backward compatibility; it is no longer called by `WindowManager` but could be removed in a future cleanup pass.

## Suggested Commit Message

```
feat(main-toolbar): relocate toolbar into menu bar, shrink icons to frame height (BATCH-25)

- Add MainToolbarManager.RenderInline — draws entries/separators in the current
  window (no Begin/End), called inside WindowManager.BeginMainMenuBar
- Replace 64px icon sizes with ImGui.GetFrameHeight() in PerspectiveToolbarSection,
  ToolbarCommandAdapter, and MainToolbarTimeControlSection
- Drop toolbar dockspace inset in Program.cs (toolbarHeight = 0)
- Update MainToolbarManagerTests to drive RenderInline inside BeginMainMenuBar
- Keep BATCH-24 guardrail green; DockspaceLayoutTests unchanged

Co-Authored-By: Claude <noreply@anthropic.com>
```
