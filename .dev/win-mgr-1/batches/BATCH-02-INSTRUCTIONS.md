# BATCH-02: Phase 2 — Window Manager: ManagedWindow Base

**Batch Number:** BATCH-02  
**Tasks:** WM-S201, WM-S202, WM-S203  
**Phase:** Phase 2 — Window Manager: ManagedWindow Base  
**Estimated Effort:** 8–12 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (IconAtlas + IconWidgets must exist)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch implements the `ManagedWindow` abstract base class and all its supporting types. This is the core abstraction that every window in the FDP Window Manager will inherit from. The focus is on: visibility gating logic, focus management, the custom title bar (pin + close icons), and an optional local menu bar.

All three tasks build on the same file; complete them in order (WM-S201 → WM-S202 → WM-S203).

### Required Reading (IN ORDER)

1. **Design Document (focus §4.1–4.2):** `.dev/win-mgr-1/DESIGN.md`
2. **Task Details (Phase 2):** `.dev/win-mgr-1/TASK-DETAIL.md` — WM-S201, WM-S202, WM-S203.
3. **Previous Batch Review:** `.dev/win-mgr-1/reviews/BATCH-01-REVIEW.md`
4. **Icon API you will use:** `FDP/Toolkits/FDP.Toolkit.ImGui/Icons/IconAtlas.cs` and `IconWidgets.cs`
5. **Test Fixture (headless ImGui):** `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/ImGuiTestFixture.cs`

### Source Code Location

- **Primary Work Area:** `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/` _(create this folder)_
- **Test Project:** `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/WindowManager/` _(create this folder)_

### Report Submission

**When done, write your report to:** `.dev/win-mgr-1/reports/BATCH-02-REPORT.md`  
**If you have questions, create:** `.dev/win-mgr-1/questions/BATCH-02-QUESTIONS.md`

---

## 🎯 Tasks

### Task WM-S201: `WindowScope` enum + `ManagedWindow` Abstract Base

See full details: [TASK-DETAIL.md §WM-S201](../../win-mgr-1/TASK-DETAIL.md#wm-s201-windowscope--managedwindow-abstract-base)

**Files to create:**
- `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/WindowScope.cs`
- `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/ManagedWindow.cs`

**`WindowScope` enum:**
```csharp
namespace FDP.Toolkit.ImGui.WindowManager;
public enum WindowScope { PerspectiveBound, Global }
```

**`ManagedWindow` abstract base — key implementation points:**

```csharp
namespace FDP.Toolkit.ImGui.WindowManager;
public abstract class ManagedWindow
{
    public string      Id                { get; }
    public string      Title             { get; protected set; }
    public string      OwningPerspective { get; }
    public WindowScope Scope             { get; }
    public bool        IsOpen            { get; set; }
    public bool        IsPinned          { get; set; }

    protected virtual bool HasMenuBar => false;

    protected ManagedWindow(string id, string title, string owningPerspective, WindowScope scope)

    public void Render(string currentPerspective, Icons.IconAtlas atlas)
    internal void RequestFocus()

    protected virtual  void DrawLocalMenuBar()  { }
    protected abstract void DrawClientArea();
}
```

**Render lifecycle** (implement in the given order):
1. If `!IsOpen` → return early.
2. Compute `isVisible`: `Scope == Global` **or** `IsPinned` **or** `OwningPerspective == currentPerspective`.
3. If `!isVisible` → return early.
4. If `_focusRequested` → call `Gui.SetWindowFocus(windowInternalName)` and set `_focusRequested = false`.
5. `windowInternalName = $"{Title}###{Id}"`
6. Call `Gui.Begin(windowInternalName, ref _isOpen, flags)` where `_isOpen` is the backing field for `IsOpen`.
7. Call `DrawCustomTitleBarControls(currentPerspective, perspectiveActive, atlas)` (private method — implement in WM-S202).
8. If `HasMenuBar` and `Gui.BeginMenuBar()` → `DrawLocalMenuBar()` → `Gui.EndMenuBar()`.
9. `DrawClientArea()`.
10. `Gui.End()`.

**Testing guidance:** The visibility logic (steps 1–3) is pure boolean — test without any ImGui context. Create a concrete `TestWindow : ManagedWindow` subclass in the test file. Test all 10 WM-S201 success conditions.

For items requiring ImGui calls (Begin/End, SetWindowFocus), use `ImGuiTestFixture` to create a headless context. Tests that verify `_focusRequested` is cleared after `Render()` need to call `Render()` inside a frame.

---

### Task WM-S202: `ManagedWindow` Custom Title Bar Controls

See full details: [TASK-DETAIL.md §WM-S202](../../win-mgr-1/TASK-DETAIL.md#wm-s202-managedwindow-custom-title-bar-controls)

**Implement the private method `DrawCustomTitleBarControls` called from `Render()` immediately after `Gui.Begin()` succeeds.**

**Pin icon** (only for `PerspectiveBound` scope windows):
```csharp
// Position at right side of title bar
Gui.SameLine(Gui.GetWindowWidth() - offset);
bool pinChanged = IconWidgets.AlternatingFaceToggleIcon(
    atlas, "##pin", "pin_on", "pin_off", ref _isPinned);
if (pinChanged && !IsPinned && !perspectiveActive && Gui.IsItemHovered())
    Gui.SetTooltip("Unpinning will hide this window in the current perspective.");
```

**Close icon** (all windows):
```csharp
Gui.SameLine(Gui.GetWindowWidth() - closeOffset);
if (IconWidgets.IconButton(atlas, "##close", "cross"))
{
    IsOpen = false;
    IsPinned = false;
}
```

Where `perspectiveActive = (OwningPerspective == currentPerspective)` is determined at call site.

**Testing:** Tests that verify icon rendering require a headless frame. The assertions in WM-S202 are mostly behavioral (no pin for Global, close sets IsOpen=false etc). Some can be verified by inspecting state after Render() while others are specification-level (visual positioning).

---

### Task WM-S203: `ManagedWindow` Optional Local Menu Bar

See full details: [TASK-DETAIL.md §WM-S203](../../win-mgr-1/TASK-DETAIL.md#wm-s203-managedwindow-optional-local-menu-bar)

**Already partially implemented in WM-S201 (step 8 of Render lifecycle).**

Complete the implementation:
- `HasMenuBar` default is `false` (already a virtual property).
- When `HasMenuBar = true`, pass `ImGuiWindowFlags.MenuBar` to `Gui.Begin()`.
- After `DrawCustomTitleBarControls`, if `HasMenuBar` → `Gui.BeginMenuBar()` → `DrawLocalMenuBar()` → `Gui.EndMenuBar()`.
- `DrawLocalMenuBar()` has a `protected virtual` default empty implementation.

**Testing:** 5 WM-S203 success conditions. Test that `HasMenuBar = false` doesn't include the flag, `HasMenuBar = true` does. Subclass override of `DrawLocalMenuBar` works.

---

## 🧪 Test-Driven Task Progression

**This section is mandatory. Follow it exactly:**

```
For each task:
    1. READ the task description in TASK-DETAIL.md thoroughly.
    2. WRITE the unit/integration tests first (stub the implementation to fail).
    3. RUN: dotnet test FDP/Toolkits/FDP.Toolkit.ImGui.Tests/ — confirm tests FAIL (red).
    4. IMPLEMENT the feature until all tests PASS (green).
    5. RUN: dotnet test FDP/Toolkits/FDP.Toolkit.ImGui.Tests/ — confirm passing.
    6. Only then move to the next task.
```

**Final check before submitting report:**
```
dotnet build FDP/Toolkits/FDP.Toolkit.ImGui/FDP.Toolkit.ImGui.csproj
dotnet test FDP/Toolkits/FDP.Toolkit.ImGui.Tests/FDP.Toolkit.ImGui.Tests.csproj
```
Both must succeed with zero errors and zero test failures.

---

## 🧱 Critical Implementation Notes

1. **`internal void RequestFocus()`** — accessibility: `WindowManager` (in the same assembly) must be able to call this. Verify with `InternalsVisibleTo` already set in the csproj (it is).

2. **`_isOpen` backing field vs `IsOpen` property:** `Gui.Begin` requires a `ref bool`. Pass the backing field `ref _isOpen`, not the property.

3. **`DrawCustomTitleBarControls` signature:** declare as `private void DrawCustomTitleBarControls(string currentPerspective, bool perspectiveActive, Icons.IconAtlas atlas)`. Compute `perspectiveActive = OwningPerspective == currentPerspective` in `Render()` before calling.

4. **Icon coordinate strings `"pin_on"`, `"pin_off"`, `"cross"`:** These are placeholder atlas coordinate strings. Since the actual atlas is caller-provided, they just need to be valid strings that `atlas.GetUvCoordinates()` will process (even if returning fallback UVs in tests). No hard-coding of actual pixel positions.

5. **Offset for right-aligned title bar icons:** A sensible default is `2 * (atlas.IconSizeVec.X + 8f)` for pin+close. The exact value can be adjusted. Document your choice in the report.

6. **Concrete test subclass:** Create `private class TestWindow : ManagedWindow` inside the test file with minimal `DrawClientArea()` implementation (e.g., `Gui.Text("test")`). This is standard practice for testing abstract classes.

7. **`Title` property:** per the window name format `$"{Title}###{Id}"`, `Title` can be `protected set` so subclasses can update it dynamically (useful for live-updating window titles). The spec leaves `Title` as a constructor parameter but doesn't restrict updates. Make it `protected set`.

---

## 📋 Report Format

Submit to `.dev/win-mgr-1/reports/BATCH-02-REPORT.md`. Include:

| Task ID | Status | Notes |
|---------|--------|-------|
| WM-S201 | | |
| WM-S202 | | |
| WM-S203 | | |

Answer all Developer Insights questions (Q1–Q5). Include the final `dotnet test` output summary.
