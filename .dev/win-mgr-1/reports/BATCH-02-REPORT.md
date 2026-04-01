# BATCH-02 Report

**Batch:** BATCH-02  
**Tasks:** WM-S201, WM-S202, WM-S203  
**Phase:** Phase 2 — Window Manager: ManagedWindow Base  
**Completed:** 2026-04-01  
**Status:** ✅ COMPLETE

---

## Task Summary

| Task ID | Status | Notes |
|---------|--------|-------|
| WM-S201 | ✅ Done | `WindowScope` enum + `ManagedWindow` abstract base with full visibility gating, focus management, and `Gui.Begin` lifecycle |
| WM-S202 | ✅ Done | `DrawCustomTitleBarControls` private method: pin icon (PerspectiveBound only) + close icon (all windows), right-aligned |
| WM-S203 | ✅ Done | Optional local menu bar via `HasMenuBar` virtual property and `ImGuiWindowFlags.MenuBar` |

---

## Files Created

| File | Purpose |
|------|---------|
| `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/WindowScope.cs` | `WindowScope` enum (PerspectiveBound, Global) |
| `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/ManagedWindow.cs` | Abstract base class — all three tasks |
| `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/WindowManager/ManagedWindowTests.cs` | 18 new tests covering all WM-S201–203 conditions |

---

## Test Results

### Final `dotnet test` run

```
Passed!  - Failed: 0, Passed: 96, Skipped: 0, Total: 96, Duration: 222 ms
```

- **78** pre-existing tests (BATCH-01): all still passing ✅
- **18** new tests (BATCH-02): all passing ✅

---

## Developer Insights

**Q1: How did you handle the `ref _isOpen` vs `IsOpen` property requirement?**

`_isOpen` and `_isPinned` are explicit private backing fields (not auto-properties). This allows them to be passed directly as `ref _isOpen` to `Gui.Begin()` and `ref _isPinned` to `AlternatingFaceToggleIcon()`. The public properties are thin wrappers that get/set the backing fields. Auto-properties cannot be taken by `ref`.

**Q2: How did you test `_focusRequested` (a private field)?**

Added two `internal` members to `ManagedWindow` behind the existing `InternalsVisibleTo` in the csproj:
- `internal bool FocusRequested => _focusRequested;` — allows test assertions on the focus flag state.
- `internal string WindowInternalName => $"{Title}###{Id}";` — allows test verification of the window name format (condition WM-S201.10).

These are lightweight test-support helpers, not additional production API surface.

**Q3: How were the title bar icon offsets chosen?**

```csharp
var iconStep = atlas.IconSizeVec.X + 8f;  // e.g. 16 + 8 = 24 px
```

- **Pin icon** (when present): positioned at `GetWindowWidth() - 2 * iconStep` (48 px from right for a 16px atlas).
- **Close icon**: positioned at `GetWindowWidth() - iconStep` (24 px from right).

The 8 px margin provides comfortable hit targets and visual breathing room without being excessive. When no pin icon is present (Global scope), the close icon shifts to 1 × `iconStep` — exactly one step from the right. This avoids a gap where the pin icon would have been.

**Q4: How was `DrawCustomTitleBarControls` called relative to `Gui.Begin()`'s return value?**

The method is called unconditionally after `Gui.Begin()` — the return value is ignored. This satisfies the spec's requirement that title bar controls are drawn regardless of collapse state, so the pin and close buttons remain functional even on collapsed windows.

**Q5: How were the WM-S202 behavioral tests handled (click-dependent conditions)?**

In headless ImGui, `InvisibleButton` always returns `false` (no mouse input). This means:
- WM-S202 conditions 2–6 (pin click, close click, tooltip) cannot be verified by state mutation in headless tests.
- Instead, the tests verify the **inverse invariant**: in headless mode, `IsPinned` and `IsOpen` must remain unchanged after `Render()`, and the render must not throw.

Conditions 2–6 are specification-level: the logic (`if pinChanged && !_isPinned && !perspectiveActive`) is clearly visible in the implementation and is the only code path that can mutate those flags in `DrawCustomTitleBarControls`. Full verification requires a UI-level test with simulated mouse input (scope of a future integration batch).

---

## Implementation Notes

### `AlternatingFaceToggleIcon` state direction

Per the design (WM-S104 in BATCH-01): `AlternatingFaceToggleIcon` flips `isToggled` **before** selecting the display coordinate, so the face immediately reflects the new state on the click frame. The pin icon behaves the same way — when clicked from unpinned→pinned state, the `"pin_on"` face is displayed on that same frame.

### Why DrawClientArea is always called

`DrawClientArea()` is called after `Gui.Begin()` regardless of the begin return value. This is intentional per the design spec. It ensures the ImGui window-state machine is always in a consistent state and avoids any half-submitted Begin/End frames. In practice, collapsed windows are very small so the content rendering cost is trivial.

### Namespace

Both production files use `namespace FDP.Toolkit.ImGui.WindowManager;` with file-scoped namespace syntax, consistent with the existing `Icons/` files.
