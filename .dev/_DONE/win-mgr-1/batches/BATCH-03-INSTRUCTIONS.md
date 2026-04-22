# BATCH-03: Phase 3 — Window Manager: Menu Registry & Orchestrator

**Batch Number:** BATCH-03  
**Tasks:** WM-S301, WM-S302, WM-S303, WM-S304, WM-S305  
**Phase:** Phase 3 — Window Manager: Menu Registry & Orchestrator  
**Estimated Effort:** 12–15 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (Icons), BATCH-02 (ManagedWindow base)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch creates `GlobalMenuRegistry` (trie-based menu registration) and the full `WindowManager` class — the central orchestrator for all windows, perspectives, and global menu rendering. This is the most complex batch to date. Work in order: WM-S301 → WM-S302 → WM-S303 → WM-S304 → WM-S305.

### Required Reading (IN ORDER)

1. **Design Document (focus §4.3–4.4):** `.dev/win-mgr-1/DESIGN.md`
2. **Task Details (Phase 3):** `.dev/win-mgr-1/TASK-DETAIL.md` — WM-S301 through WM-S305.
3. **Previous Reviews:** `.dev/win-mgr-1/reviews/BATCH-02-REVIEW.md`
4. **Icon + ManagedWindow APIs:** Read `Icons/IconAtlas.cs`, `Icons/IconWidgets.cs`, `WindowManager/ManagedWindow.cs`
5. **Test Fixture:** `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/ImGuiTestFixture.cs`

### Source Code Location

- **New files:** `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/GlobalMenuRegistry.cs` and `WindowManager.cs`
- **Tests:** `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/WindowManager/GlobalMenuRegistryTests.cs` and `WindowManagerTests.cs`

### Report Submission

**When done, write your report to:** `.dev/win-mgr-1/reports/BATCH-03-REPORT.md`  
**If you have questions, create:** `.dev/win-mgr-1/questions/BATCH-03-QUESTIONS.md`

---

## 🎯 Tasks

### Task WM-S301: `GlobalMenuRegistry` — Trie Data Structure + Registration API

See full details: [TASK-DETAIL.md §WM-S301](../../win-mgr-1/TASK-DETAIL.md#wm-s301-globalmenuregistry--trie-data-structure--registration-api)

**File to create:** `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/GlobalMenuRegistry.cs`

```csharp
namespace FDP.Toolkit.ImGui.WindowManager;

public class MenuItemNode
{
    public string Name { get; set; } = "";
    public Action? OnClick { get; set; }
    public Func<bool>? GetCheckedState { get; set; }
    public Action<bool>? OnCheckedChanged { get; set; }
    public bool IsSeparator { get; set; }
    public Dictionary<string, MenuItemNode> Children { get; } = new();
}

public class GlobalMenuRegistry
{
    public MenuItemNode Root { get; } = new();

    public void RegisterItem(string path, Action onClick)
    public void RegisterCheckableItem(string path, Func<bool> getChecked, Action<bool> onChanged)
    public void RegisterSeparator(string path)
}
```

Path registration: split on `'/'`, traverse/create trie from Root. Empty path throws `ArgumentException`. Last-write-wins on re-registration.

**Tests:** All 9 WM-S301 success conditions — pure unit tests, no ImGui needed.

---

### Task WM-S302: `WindowManager` — Registry + Programmatic API

See full details: [TASK-DETAIL.md §WM-S302](../../win-mgr-1/TASK-DETAIL.md#wm-s302-windowmanager--registry--programmatic-api)

**File to create:** `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/WindowManager.cs`

```csharp
namespace FDP.Toolkit.ImGui.WindowManager;

public class WindowManager
{
    public WindowManager(Icons.IconAtlas atlas)

    // Registration
    public void RegisterWindow(ManagedWindow window)
    public bool TryGetWindow(string id, [MaybeNullWhen(false)] out ManagedWindow window)

    // Programmatic API
    public void ShowWindow(string id)
    public void HideWindow(string id)
    public void SetWindowPinned(string id, bool isPinned)
    public void FocusWindow(string id)

    // Perspective
    public string CurrentPerspective { get; private set; } = "Default";
    public event Action<string, string>? OnPerspectiveChanged;
    public void SwitchPerspective(string newPerspective)

    // Menu
    public GlobalMenuRegistry GlobalMenu { get; } = new();

    // Render (stub — implemented fully in WM-S303-305)
    public void Render() { /* implement in WM-S303 */ }
}
```

**Programmatic API rules** (from DESIGN.md §4.4.5):
- `ShowWindow(id)`: `IsOpen = true`. If `PerspectiveBound` and `OwningPerspective != CurrentPerspective` → `IsPinned = true`. Unknown id: silent no-op.
- `HideWindow(id)`: `IsOpen = false`, `IsPinned = false`. Unknown id: silent no-op.
- `SetWindowPinned(id, bool)`: updates `IsPinned` only for `PerspectiveBound` windows. No-op for `Global`. Unknown id: silent.
- `FocusWindow(id)`: applies `ShowWindow` logic then calls `window.RequestFocus()`.
- `SwitchPerspective(p)`: updates `CurrentPerspective`, fires `OnPerspectiveChanged(old, new)`. No-op and no event if same value.

**Tests:** All 16 WM-S302 success conditions. Pure unit tests (no ImGui needed for API logic).

---

### Task WM-S303: `WindowManager.Render()` — Global Menu + Windows Pulldown + Auto-Pin

See full details: [TASK-DETAIL.md §WM-S303](../../win-mgr-1/TASK-DETAIL.md#wm-s303-windowmanagerrender--global-menu--windows-pulldown--auto-pin)

**File to modify:** `WindowManager.cs`

Implement `Render()` skeleton:
```
Gui.BeginMainMenuBar()
  RenderGlobalMenu(GlobalMenu.Root)
  RenderFixedWindowsMenu()
  RenderPerspectiveSwitcher()   // WM-S304
  RenderFixedHelpMenu()         // WM-S305
Gui.EndMainMenuBar()
foreach window in _windows.Values:
    window.Render(CurrentPerspective, _atlas)
```

**`RenderGlobalMenu(MenuItemNode node)`:**
- If `node.Children.Count > 0` → `Gui.BeginMenu(node.Name)` → recurse children → `Gui.EndMenu()`.
- If separator → `Gui.Separator()`.
- If leaf with `OnClick` → `Gui.MenuItem(node.Name)`, call `OnClick` if returned true.
- If leaf with `GetCheckedState`/`OnCheckedChanged` → checked MenuItem; update state on selection.

**`RenderFixedWindowsMenu()`:**
- `Gui.BeginMenu("Windows")`.
- Group `PerspectiveBound` windows by `OwningPerspective` as sub-menus (alphabetical sort).
- `Global` windows under `"Global"` sub-menu.
- Each entry: checkable `MenuItem` reflecting `IsOpen`. On `IsOpen = true`: apply auto-pin if cross-perspective. On `IsOpen = false`: also `IsPinned = false`.
- `Gui.EndMenu()`.

**Tests (WM-S303):** Integration tests using `ImGuiTestFixture`. Verify: `OnClick` invoked, checkable state update, all windows rendered (call count via stub). 9 conditions.

---

### Task WM-S304: Perspective Switcher

See full details: [TASK-DETAIL.md §WM-S304](../../win-mgr-1/TASK-DETAIL.md#wm-s304-perspective-switcher--switchperspective--onperspectivechanged)

**File to modify:** `WindowManager.cs`

Implement `RenderPerspectiveSwitcher()`:
- Collect distinct `OwningPerspective` values from `PerspectiveBound` windows. Sort alphabetically.
- For each, render `Gui.RadioButton(p, CurrentPerspective == p)`.
- If clicked → `SwitchPerspective(p)`.
- Call `Gui.SameLine()` between radio buttons (not after the last one).

**Tests:** 6 WM-S304 conditions. Test via ImGui headless fixture.

---

### Task WM-S305: Help / Debug Menu

See full details: [TASK-DETAIL.md §WM-S305](../../win-mgr-1/TASK-DETAIL.md#wm-s305-windowmanagerrender--help--debug-menu)

**File to modify:** `WindowManager.cs`

Implement `RenderFixedHelpMenu()`:
```
Gui.BeginMenu("Help")
  Gui.BeginMenu("Debug")
    foreach Global window:
      Gui.MenuItem(win.Title, "", ref isOpen) // mirrors IsOpen
      if changed: apply show/hide logic (no auto-pin for Global)
  Gui.EndMenu() // Debug
  Gui.MenuItem("About") // no-op
Gui.EndMenu() // Help
```

**Tests:** 6 WM-S305 success conditions.

---

## 🧪 Test-Driven Task Progression

**This section is mandatory. Follow it exactly:**

```
For each task:
    1. READ the task description in TASK-DETAIL.md thoroughly.
    2. WRITE the unit/integration tests first.
    3. RUN tests — confirm FAIL (red).
    4. IMPLEMENT until all tests PASS (green).
    5. RUN tests — confirm passing.
    6. Only then move to the next task.
```

**Final check:**
```
dotnet build FDP/Toolkits/FDP.Toolkit.ImGui/FDP.Toolkit.ImGui.csproj
dotnet test FDP/Toolkits/FDP.Toolkit.ImGui.Tests/FDP.Toolkit.ImGui.Tests.csproj
```
Zero errors, zero failures. All previous 96 tests must still pass.

---

## 🧱 Critical Implementation Notes

1. **`WindowManager` stores windows in `Dictionary<string, ManagedWindow> _windows`** keyed by `window.Id`.

2. **`_atlas` field:** Store the `IconAtlas` passed to the constructor. Pass it to every `window.Render()` call.

3. **`MaybeNullWhen(false)` attribute:** Import `System.Diagnostics.CodeAnalysis` for the `[MaybeNullWhen(false)]` attribute on `TryGetWindow`.

4. **`SwitchPerspective` no-op guard:** `if (newPerspective == CurrentPerspective) return;` before updating.

5. **Auto-pin logic in `ShowWindow`:** Only applies when `Scope == WindowScope.PerspectiveBound`. Check `window.OwningPerspective != CurrentPerspective`.

6. **`RenderGlobalMenu` recursion:** The root `node` represents the tree root (not a menu item itself). Iterate `root.Children.Values` for top-level items. Each child that has children → `BeginMenu/EndMenu`. Leaf nodes → `MenuItem`.

7. **`RenderFixedWindowsMenu` — ordering:** Use `OrderBy(p => p)` for perspective name sort. Group using `ILookup` or `GroupBy`.

8. **Windows menu `IsOpen` toggle:** The `ref bool` in `Gui.MenuItem(name, "", ref isOpen)` is a local copy — you must write it back to `window.IsOpen` and apply pin logic conditionally:
   ```csharp
   bool isOpen = win.IsOpen;
   if (Gui.MenuItem(win.Title, "", ref isOpen))
   {
       if (isOpen) ShowWindow(win.Id);
       else HideWindow(win.Id);
   }
   ```

9. **Start `Render()` with `BeginMainMenuBar` — only render menu bar content if `BeginMainMenuBar()` returns true.** Always call `EndMainMenuBar()` regardless.

10. **WM-S303 Integration tests:** It is difficult to test the actual ImGui menu rendering in a headless context. Focus tests on:
    - The programmatic API (WM-S302 tests handle this well).
    - Verifying `window.Render()` is called for each registered window (use a `RenderCallCountWindow` subclass that increments a counter in `DrawClientArea()`). 
    - Other integration test ideas: verify that `ShowWindow` of a cross-perspective window sets `IsPinned = true`, and that `HideWindow` clears both flags. These are pure state tests not requiring rendering.
