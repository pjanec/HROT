# BATCH-50 — picker Tree: mouse↔keyboard focus sync, dbl-click open/expand, native open-state memory (A15/A16/A17/A18)

**Model: pro (Zoo).** Do NOT use codebase-memory tooling. **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`.
Builds on the committed BATCH-48/49 Tree nav. Touch ONLY:
`FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerState.cs`,
`FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/Layouts/TreeLayout.cs`,
`FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerWindow.cs` (+ their test file if a signature must change).

## Root cause (A17 → also A15/A18)
`PickerWindow.SyncTreeFocusToLeaf()` runs every frame and forces `SelectedFilteredIndices`/`KeyboardFocusIndex`
from `TreeFocusRow`. But **mouse clicks never update `TreeFocusRow`**, so any click is overwritten and the selection
snaps back to row 0. Double-click then confirms against row 0 (a folder) → expands it instead of opening the leaf;
the New recipe Tree picker returns the wrong recipe → New opens the wrong/!no perspective (A15).

## Fix 1 (A17/A18/A15) — mouse drives `TreeFocusRow`
- **Leaf** (`DrawLeafItem`): it already appends its `TreeRow`; capture its row index
  `int visualRowIndex = state.VisualRows.Count;` BEFORE the `state.VisualRows.Add(...)`. On `actualMouseClicked`,
  ALSO set `state.TreeFocusRow = visualRowIndex;` (in addition to the existing selection set). The existing
  double-click → `state.Confirmed = true` then works, because `Confirm()`'s folder-guard now sees a leaf focused.
- **Folder** (`DrawImplicitFolderNode` + `DrawExplicitFolderNode`): on a single-click of the folder row
  (`ImGui.IsItemClicked()` after `TreeNodeEx`), set `state.TreeFocusRow = visualRowIndex;` (the folder's own row
  index, already computed as `int visualRowIndex = state.VisualRows.Count;` before the Add). This makes mouse
  selection follow clicks consistently with `SyncTreeFocusToLeaf`.
- Keep `SyncTreeFocusToLeaf` as-is (every frame) — it is now correct because `TreeFocusRow` reflects the latest
  mouse OR keyboard focus.

## Fix 2 (A19-part / dbl-click folder) — double-click a folder row toggles expand/collapse
In both folder draws, after `bool open = ImGui.TreeNodeEx(...)`: if the folder row is double-clicked
(`ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)`), request a toggle:
`state.PendingToggleFolderPath = fullPath; state.PendingToggleOpen = !open;` (see Fix 3 for the field + application).

## Fix 3 (A16) — let ImGui remember open state; drive keyboard/dbl-click via a one-shot request
Stop force-overriding the open state every frame. Replace the `ExpandedFolders` source-of-truth with ImGui's native
per-id tree persistence + a one-shot toggle request:
- **PickerState:** REMOVE `HashSet<string> ExpandedFolders`. ADD `string? PendingToggleFolderPath;` and
  `bool PendingToggleOpen;`. Clear both in `Reset()`.
- **TreeLayout** (both folder draws): replace the current
  `ImGui.SetNextItemOpen(state.ExpandedFolders.Contains(fullPath), ImGuiCond.Always)` with:
  ```csharp
  if (state.PendingToggleFolderPath == fullPath)
  {
      ImGui.SetNextItemOpen(state.PendingToggleOpen, ImGuiCond.Always);
      state.PendingToggleFolderPath = null;
  }
  // else: no SetNextItemOpen → ImGui remembers this node's open/closed state across frames & re-opens.
  ```
  For the IMPLICIT tree, KEEP the search auto-expand: when `isSearching`, the `flags` already carry `DefaultOpen`
  (leave that) — do NOT also force via the pending path. REMOVE the old `if (open) ExpandedFolders.Add … else …`
  sync block entirely (ImGui now owns the state; recursion still uses the `open` bool returned by `TreeNodeEx`).
- **PickerWindow.HandleTreeKeyboardNavigation:** replace the `ExpandedFolders.Add/Remove` for →/← with:
  `→` (expand): `state.PendingToggleFolderPath = row.FolderPath; state.PendingToggleOpen = true;`
  `←` (collapse): `state.PendingToggleFolderPath = row.FolderPath; state.PendingToggleOpen = false;`
  (only when `row.IsFolder`).
- **PickerWindow** Enter-on-folder guard (DrawFooter) + `Confirm()` folder-guard: replace the `ExpandedFolders`
  expand with `state.PendingToggleFolderPath = path; state.PendingToggleOpen = true;` then suppress confirm / return.

Net: folders open/close is remembered by ImGui (no forced "all collapsed"); keyboard ←/→, Enter-on-folder, and
double-click drive it via the one-shot request; mouse single-click selects + moves focus; double-click leaf opens.

## Tests
- The `FlattenVisualRows` tests in `PickerTreeBuilderTests.cs` use a TEST-LOCAL `expandedFolders` param (not the
  production field), so removing `ExpandedFolders` should NOT break them. If anything references the removed field,
  fix the reference; keep the tests asserting DFS order + descendant-hiding. Do not weaken/delete tests.
- Build `NodeEditor.UI` + run `NodeEditor.UI.Tests` + `NodeEditor.Core.Tests`: `Failed: 0`, 0 warnings.

## Definition of done
- Single-click a folder/leaf → selection + focus move there (no snap-back to row 0). Double-click a leaf → opens
  (Open-Asset + New recipe). Double-click a folder → expand/collapse. Folder open-state is remembered (not forced
  collapsed); ←/→ + Enter-on-folder still expand/collapse. New picks the clicked recipe → opens the right perspective
  (A15). Other layouts unchanged. Build 0 warnings; UI/Core suites `Failed: 0`.
- Write `.dev/_DONE/main-toolbar-2/reports/BATCH-50-REPORT.md`: the focus-sync fix, the native-open-state change, files/tests.

If something cannot be done as specified, STOP and report why rather than stubbing.
