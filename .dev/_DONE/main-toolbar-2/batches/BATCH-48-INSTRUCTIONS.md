# BATCH-48 — BUG-A14 (P1): picker Tree keyboard navigation (visual-order, folder-reachable, ←/→ expand)

**Bug:** BUG-A14 — in the picker's **Tree** layout, ↑/↓ traverse the flat `Filtered` list (NOT the visual tree
order → chaotic jumps), and folder nodes are unreachable (not in `Filtered`) so they can't be expanded/collapsed by
keyboard. The earlier "default-open all folders" fix (BATCH-45) was WRONG — the user never asked for it and it is
unusable at scale → **revert it**.
**Model: pro (Zoo).** Focus-model rework in a shared NodeEdit component — follow the design below precisely; if any
step cannot be done as written, STOP and report rather than guessing. Do NOT use codebase-memory tooling.
**Repo root:** `D:\Work\IOS-IG-SimHost-FDP`.

## Desired behavior (Tree layout only)
- ↑/↓ move a keyboard focus through the **visible rows in VISUAL (rendered) order** — folders AND leaves,
  respecting current expand/collapse state.
- **→** expands the focused folder (if collapsed); **←** collapses it (if expanded). (On a leaf, ←/→ may no-op or
  move to/from the parent — keep it simple: no-op on leaves is acceptable.)
- **Enter** confirms when the focused row is a leaf (selects it + closes), as today.
- Folders are **NOT auto-expanded** by default (revert BATCH-45). Default collapsed; **except while searching**,
  keep the existing auto-expand so matches are visible.
- The focused row (folder or leaf) shows a clear focus highlight and scrolls into view.
- **Other layouts (Standard/Compact/Wide/Grid) are UNCHANGED** — they are flat lists where Filtered order = visual
  order; keep their existing `KeyboardFocusIndex` nav.

## Files
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/Layouts/TreeLayout.cs` (build + render the visual rows; revert
  the always-DefaultOpen change; control expand state explicitly).
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerState.cs` (hold the tree expand state + visual-row model +
  tree focus index).
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerWindow.cs` (`HandleKeyboardNavigation`: branch to tree nav
  when `_layout == PickerLayout.Tree`).
- Touch ONLY these three (+ their test files). No `Hrot.*` changes.

## Recommended design (refine as needed, keep it correct + testable)
1. **PickerState additions:**
   - `HashSet<string> CollapsedFolders` (folder full-paths that are collapsed). Cleared on `Reset`.
   - A per-frame visual-row list, e.g. `List<TreeRow> VisualRows` where
     `readonly record struct TreeRow(bool IsFolder, string FolderPath, int FilteredIndex, int Depth)`
     (`FilteredIndex` = -1 for folders). Rebuilt by TreeLayout each frame in render order.
   - `int TreeFocusRow` (index into `VisualRows`, default 0/clamped).
2. **TreeLayout (implicit + explicit):**
   - Revert: folders are `DefaultOpen` ONLY when searching (as before BATCH-45). For non-search, drive each folder's
     open state with `ImGui.SetNextItemOpen(!state.CollapsedFolders.Contains(fullPath), ImGuiCond.Always)`.
   - Compute a stable folder **full-path** per node (parent path + "/" + name) and use it for collapse state + IDs.
   - As each row is rendered (folder header, then leaves of expanded folders, DFS), append a `TreeRow` to
     `state.VisualRows` in that exact order. Recurse into a folder's children only when expanded.
   - Highlight the row whose index == `state.TreeFocusRow` (folder OR leaf); `SetScrollHereY` on it. Keep the
     existing leaf selection/confirm visuals; when the focused row is a leaf, mirror its `FilteredIndex` into
     `state.KeyboardFocusIndex` so the existing leaf highlight + default-confirm logic still works (set it to -1 when
     a folder is focused).
   - Mouse arrow-click expand/collapse must stay in sync with `CollapsedFolders` (add/remove on the rendered open
     state, mirroring the same idiom used in `SaveAsBrowserDialog.DrawFolderNode`).
3. **PickerWindow.HandleKeyboardNavigation:** when `_layout == PickerLayout.Tree` and `VisualRows.Count > 0`:
   - ↑/↓ : `TreeFocusRow = Clamp(TreeFocusRow ± 1, 0, VisualRows.Count-1)`. Home/End/PageUp/PageDown analogous.
   - → : if the focused row is a folder in `CollapsedFolders`, remove it (expand).
   - ← : if the focused row is a folder NOT collapsed, add it (collapse).
   - After moving, if the focused row is a leaf set `KeyboardFocusIndex = row.FilteredIndex` and the Single-select
     selection to it; if a folder, clear the leaf focus. Enter (already handled in DrawFooter) confirms the focused
     leaf — ensure `Confirm()` uses the focused leaf (guard: if a folder is focused, Enter expands/does nothing
     rather than confirming a wrong item).
   - Keep the non-Tree path exactly as today.

## Hard requirements
- Inspect the source; explain the visual-row build + nav in the report. Live GUI check is the user's (it's ImGui).
- Do NOT regress: search auto-expand still works; mouse expand/collapse + click-select + double-click confirm still
  work; non-Tree layouts unchanged; multi-select pickers unaffected. No test weakening/skips/stubs. Build 0 warnings.
- Add a focused unit test where tractable: e.g. a pure helper that flattens a `PickerTreeBuilder` tree + a
  `CollapsedFolders` set into the expected `VisualRows` order (folders+leaves, DFS, collapsed folders hide children).
  Assert the order + that collapsing a folder removes its descendants. Put it in the NodeEditor.UI test project.

## Build & test (no BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/NodeEditor.UI.csproj
dotnet test  <the NodeEditor.UI / NodeEditor.Core test project(s)>
```
All `Failed: 0`; build 0 warnings.

## Definition of done
- Tree picker: ↑/↓ walk folders+leaves in visual order; →/← expand/collapse the focused folder; Enter confirms a
  focused leaf; folders default-collapsed (auto-expand only while searching). Other layouts unchanged.
- New flatten/order unit test added + green; build 0 warnings; UI/Core suites `Failed: 0`.
- Write `.dev/_DONE/main-toolbar-2/reports/BATCH-48-REPORT.md`: the visual-row model, the nav branch, the revert, files/
  tests, summary.

If something cannot be done as specified, STOP and report why rather than stubbing.
