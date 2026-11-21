# BATCH-52 — picker: auto-scroll only on keyboard focus change (mouse-wheel scrolling must work) (BUG-A20)

**Model: pro (Zoo).** Do NOT use codebase-memory tooling. **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`.
Touch ONLY: `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerState.cs`,
`FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerWindow.cs`,
`FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/Layouts/TreeLayout.cs`,
`FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerItemListHelper.cs` (+ test file if needed).

## Bug
`ImGui.SetScrollHereY(0.5f)` is called on the focused row **every frame** (TreeLayout.cs lines ~146, ~205, ~315 for
folders/leaf; PickerItemListHelper.cs line ~221 for flat layouts). So when there are many items, the view is yanked
back to the selected item every frame — **mouse-wheel scrolling is impossible** (it snaps back instantly). Auto-scroll
should happen ONLY when **keyboard navigation** moved the focus, not every frame and not on mouse interaction.

## Fix — one-shot "scroll to focus" flag set only by keyboard nav
1. **PickerState:** add `public bool ScrollToFocus;`. Reset it to `false` in `Reset()`.
2. **PickerWindow:** set `ScrollToFocus = true` wherever KEYBOARD navigation changes the focus:
   - Flat path: in `SetFocus(...)` (covers ↑/↓/PageUp/PageDown/Home/End via `MoveFocus`/`SetFocus`).
   - Tree path: in `HandleTreeKeyboardNavigation`, after any ↑/↓/PageUp/PageDown/Home/End changes `TreeFocusRow`
     (set `state.ScrollToFocus = true` when the focus row actually moves).
   - Do NOT set it on mouse clicks (the clicked item is already visible) and obviously not on mouse wheel.
3. **Render sites:** gate every `SetScrollHereY` on the flag and CONSUME it (one-shot):
   - TreeLayout (folder draws + `DrawLeafItem`) and `PickerItemListHelper`: change
     `if (focus) ImGui.SetScrollHereY(0.5f);` → `if (focus && state.ScrollToFocus) { ImGui.SetScrollHereY(0.5f);
     state.ScrollToFocus = false; }`. (Clearing it after the focused row scrolls makes it a single shot, so the next
     frame's mouse wheel is free.) Pass `state` into `PickerItemListHelper.DrawRow` if it isn't already available
     there (it renders flat-layout rows) — if threading `state` is awkward, set the flag check via the existing
     `PickerState` the helper already receives; do the minimal plumbing, no behavior change beyond the gate.

Net: ↑/↓/PageUp/Home/etc. scroll the focus into view exactly once; mouse wheel scrolls freely and stays put; clicking
an item does not force-scroll.

## Tests / build
- Build `NodeEditor.UI` + run `NodeEditor.UI.Tests` + `NodeEditor.Core.Tests`: `Failed: 0`, 0 warnings. The scroll
  behavior is ImGui-level (runtime-verified by the lead) — keep existing tests green; add one only if trivially
  tractable (e.g. that keyboard nav sets `ScrollToFocus` and a render-consume clears it, if unit-reachable).

## Definition of done
- Keyboard nav scrolls the focused item into view; mouse-wheel scrolling works and is NOT snapped back; clicking
  doesn't force-scroll. Applies to BOTH the Tree layout and the flat list layouts. Build 0 warnings; suites `Failed: 0`.
- Write `.dev/_DONE/main-toolbar-2/reports/BATCH-52-REPORT.md`.

If something cannot be done as specified, STOP and report why rather than stubbing.
