# BATCH-03 Report

**Batch:** BATCH-03  
**Developer:** AI Agent  
**Date:** 2026-04-01  
**Status:** ✅ COMPLETE

---

## Summary

All five tasks (WM-S301 through WM-S305) implemented and passing. Test suite grew from 96 to **135 tests** (39 new), zero failures.

---

## Tasks Completed

| Task | File(s) Created/Modified | Status |
|------|--------------------------|--------|
| WM-S301 — `GlobalMenuRegistry` trie | `WindowManager/GlobalMenuRegistry.cs` | ✅ Done |
| WM-S302 — `WindowManager` registry + programmatic API | `WindowManager/WindowManager.cs` | ✅ Done |
| WM-S303 — `WindowManager.Render()` + Windows pulldown + auto-pin | `WindowManager/WindowManager.cs` | ✅ Done |
| WM-S304 — Perspective switcher | `WindowManager/WindowManager.cs` | ✅ Done |
| WM-S305 — Help / Debug menu | `WindowManager/WindowManager.cs` | ✅ Done |

---

## Test Results

```
dotnet test FDP/Toolkits/FDP.Toolkit.ImGui.Tests/FDP.Toolkit.ImGui.Tests.csproj
Passed! - Failed: 0, Passed: 135, Skipped: 0, Total: 135
```

All 96 prior tests still pass. 39 new tests added.

---

## New Test Files

- `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/WindowManager/GlobalMenuRegistryTests.cs` — 11 tests covering all 9 WM-S301 conditions (conditions 8 and 9 each have a separate test).
- `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/WindowManager/WindowManagerTests.cs` — 28 tests covering WM-S302 (16 conditions), WM-S303 (render count + state logic), WM-S304 (perspective logic), and WM-S305 (Global/PerspectiveBound categorisation + show/hide).

---

## Implementation Notes

### WM-S301 — `GlobalMenuRegistry`

- `MenuItemNode` is a plain data class with public mutable properties and a `Dictionary<string, MenuItemNode> Children`.
- `GlobalMenuRegistry.TraversePath()` splits the path on `'/'`, skips empty segments (handles trailing slash gracefully), and traverses/creates trie nodes.
- Empty/null path throws `ArgumentException` immediately.
- Last-write-wins: re-registration overwrites callbacks on the existing leaf node.

### WM-S302 — `WindowManager`

- Internal storage: `Dictionary<string, ManagedWindow> _windows` keyed by `window.Id`.
- `[MaybeNullWhen(false)]` attribute on `TryGetWindow` requires `using System.Diagnostics.CodeAnalysis`.
- `ShowWindow` auto-pin: only for `PerspectiveBound` windows where `OwningPerspective != CurrentPerspective`.
- `SetWindowPinned` is a no-op for `Global` windows.
- `SwitchPerspective` has an early-return guard (`if (newPerspective == CurrentPerspective) return;`) before updating state or firing the event.
- `CurrentPerspective` initialises to `"Default"`.

### WM-S303 — `Render()`

- `BeginMainMenuBar` return value guards the menu bar content; `EndMainMenuBar` is inside the same `if` block (correct per ImGui docs — EndMainMenuBar only if Begin returned true).
- `RenderGlobalMenu(MenuItemNode node)` iterates `node.Children.Values` recursively. Priority: separator → has-children (sub-menu) → checkable leaf → plain-action leaf.
- `RenderFixedWindowsMenu`: groups `PerspectiveBound` windows with LINQ `GroupBy + OrderBy`. Global windows rendered under a `"Global"` sub-menu (only if any exist).
- `RenderWindowToggleMenuItem` uses the `ref bool` pattern per critical note 6: reads `win.IsOpen` into a local, calls `Gui.MenuItem(win.Title, "", ref isOpen)`, then calls `ShowWindow`/`HideWindow` if the returned bool is `true` (clicks detected).
- Windows with `DrawCount`-based `RenderCountWindow` subclass verified render is called once per open window.

### WM-S304 — `RenderPerspectiveSwitcher`

- Collects `OwningPerspective` from `PerspectiveBound` windows only, `Distinct()`, `OrderBy(p => p)`.
- `Gui.SameLine()` called after every radio button except the last (index-based loop).
- Calls `SwitchPerspective(p)` on click (which includes no-op guard).

### WM-S305 — `RenderFixedHelpMenu`

- Fixed structure: `Help → Debug` (all `Global` windows), then `About` (no-op).
- Global windows use same `ref bool isOpen` pattern as Windows menu.
- No auto-pin logic for Global windows (they have no owning perspective).

---

## Issues / Deviations

### Naming conflict: `WindowManager` class in `WindowManager` namespace

The class `FDP.Toolkit.ImGui.WindowManager.WindowManager` conflicts with its own namespace in the test project. The test file uses a `using` alias (`using WM = FDP.Toolkit.ImGui.WindowManager.WindowManager;`) to disambiguate. The production code is unaffected.

### `BeginMainMenuBar` / `EndMainMenuBar` guard

Per ImGui documentation, `EndMainMenuBar` should only be called if `BeginMainMenuBar` returned `true` — unlike `Gui.End()` which must always be matched. The implementation wraps both the content and `EndMainMenuBar` inside the `if` block.

---

## Files Created

| File | Lines |
|------|-------|
| `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/GlobalMenuRegistry.cs` | 107 |
| `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/WindowManager.cs` | 267 |
| `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/WindowManager/GlobalMenuRegistryTests.cs` | 145 |
| `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/WindowManager/WindowManagerTests.cs` | 310 |

---

## Git Commit

```
feat(window-manager): BATCH-03 complete - GlobalMenuRegistry trie + full WindowManager (WM-S301-S305)
```
