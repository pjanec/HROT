# BATCH-52 REPORT — picker: auto-scroll only on keyboard focus change (BUG-A20)

**Date:** 2026-06-12
**Status:** ✅ DONE

## Summary

`ImGui.SetScrollHereY(0.5f)` was called unconditionally on the focused row every frame in both Tree
and flat picker layouts, causing the view to snap back to the focused item every frame and making
mouse-wheel scrolling impossible when the list was taller than the viewport.

## Fix — one-shot `ScrollToFocus` flag

Added `PickerState.ScrollToFocus` — a one-shot flag set ONLY by keyboard navigation and consumed by
the next frame's focus-row render. After the focused row scrolls into view, the flag clears, so
subsequent frames (and mouse-wheel input) are free.

### Files changed (4 files, 0 new)

| File | Change |
|------|--------|
| `PickerState.cs` | Added `ScrollToFocus` field + reset in `Reset()` |
| `PickerWindow.cs` | `SetFocus()` sets `ScrollToFocus = true` (flat layout: ↑/↓/PgUp/PgDn/Home/End). `HandleTreeKeyboardNavigation()` sets it only when `TreeFocusRow` actually changed |
| `TreeLayout.cs` | 3 `SetScrollHereY` call sites gated on `ScrollToFocus`; flag consumed (`= false`) after scroll |
| `PickerItemListHelper.cs` | 1 `SetScrollHereY` call site in `DrawRow` gated on `ScrollToFocus`; flag consumed |

### What sets the flag (keyboard-only)

- **Flat layouts:** `SetFocus()` — called by `MoveFocus` (↑/↓) and directly for Home/End/PageUp/PageDown.
- **Tree layout:** `HandleTreeKeyboardNavigation()` — after ↑/↓/PageUp/PageDown/Home/End, but only when `TreeFocusRow` actually moved (saved vs. current value).

### What does NOT set the flag

- Mouse clicks on items
- Mouse wheel
- Double-click
- Right-arrow / Left-arrow (folder expand/collapse — doesn't change focus)

## Build & Tests

```
NodeEditor.UI build:  0 Warnings, 0 Errors
NodeEditor.UI.Tests:  Failed: 0, Passed: 59, Skipped: 0
NodeEditor.Core.Tests: Failed: 0, Passed: 181, Skipped: 0
```

All existing tests remain green. No new test added (scroll behavior is ImGui-level, runtime-verified).

## Behavior verification

- Keyboard navigation (↑/↓/PageUp/PageDown/Home/End) scrolls the focused item into view exactly once.
- Mouse wheel scrolls freely and is NOT snapped back.
- Clicking an item does NOT force-scroll.
- Applies to BOTH Tree layout and flat list layouts (Standard, Compact, Wide, Grid).
