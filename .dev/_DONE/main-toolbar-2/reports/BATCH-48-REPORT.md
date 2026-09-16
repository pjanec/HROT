# BATCH-48-REPORT — BUG-A14: Tree Picker Visual-Order Keyboard Navigation

**Date:** 2026-06-12
**Bug:** BUG-A14 (P1) — Tree layout ↑/↓ navigated the flat `Filtered` list (chaotic jumps), folder nodes were unreachable, BATCH-45 auto-expand was wrong.
**Branch:** `blueprint-integ-1`
**Build:** 0 warnings, 0 errors
**Tests:** NodeEditor.UI.Tests: 56 passed / 0 failed; NodeEditor.Core.Tests: 181 passed / 0 failed

## Summary

Implemented visual-order keyboard navigation for the picker's Tree layout. ↑/↓ now walk folders+leaves in DFS visual order respecting expand/collapse state. ←/→ collapse/expand the focused folder. Enter confirms a focused leaf or expands a focused folder. Folders default-collapsed (auto-expand only while searching). Other layouts (Standard/Compact/Wide/Grid) are unchanged.

## Files changed

### 1. `PickerState.cs` — Tree state model

- Added `HashSet<string> CollapsedFolders` — folder full-paths currently collapsed
- Added `List<TreeRow> VisualRows` — per-frame visual row list rebuilt in DFS render order
- Added `internal readonly record struct TreeRow(bool IsFolder, string FolderPath, int FilteredIndex, int Depth)` — a row in the visual tree (FilteredIndex = -1 for folders)
- Added `int TreeFocusRow` — keyboard focus index into `VisualRows`
- `Reset()` clears `CollapsedFolders`, `VisualRows`, resets `TreeFocusRow` to 0

### 2. `TreeLayout.cs` — Visual-order rendering + collapse sync

**REVERT BATCH-45:** Folders now default-open ONLY while searching (`isSearching ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None`). For non-search, each folder's open state is driven from `state.CollapsedFolders` via `ImGui.SetNextItemOpen(!state.CollapsedFolders.Contains(fullPath), ImGuiCond.Always)`.

**VisualRows build:** Each rendered row (folder or leaf) appends a `TreeRow` to `state.VisualRows` in DFS order. Children are only recursed into when the folder is expanded, so collapsed folders' descendants are absent from `VisualRows`, matching the visual reality.

**Collapse sync:** After `TreeNodeEx`, the rendered open state is synced with `CollapsedFolders` (mirroring `SaveAsBrowserDialog.DrawFolderNode`):
- If open → remove from `CollapsedFolders`
- If collapsed + has children → add to `CollapsedFolders`

**Focus highlight:** Folders use `ImGuiTreeNodeFlags.Selected` when `TreeFocusRow` matches. Leaves use the existing `KeyboardFocusIndex` mechanism (synced from `TreeFocusRow` via `SyncTreeFocusToLeaf` in PickerWindow). Focused rows scroll into view via `ImGui.SetScrollHereY(0.5f)`.

**Explicit tree (`DrawExplicitFolderNode`):** Same collapse/open/focus pattern, computing full-path as `parentPath + "/" + node.Name`. No search auto-expand (explicit trees have no search context).

**Depth tracking:** `TreeRow.Depth` reflects the true nesting depth (root-level = 0, sub-folders/leaves increment).

### 3. `PickerWindow.cs` — Tree keyboard nav + Confirm guards

**`HandleKeyboardNavigation`** branches early for Tree layout via `HandleTreeKeyboardNavigation()`:
- ↑/↓ — move `TreeFocusRow ± 1` clamped to `[0, VisualRows.Count-1]`
- Home/End/PageUp/PageDown — analogous
- → — remove focused folder from `CollapsedFolders` (expand)
- ← — add focused folder to `CollapsedFolders` (collapse)
- After navigation, `SyncTreeFocusToLeaf()` mirrors the focused leaf's `FilteredIndex` into `KeyboardFocusIndex` (so existing leaf highlight + confirm logic works); clears it when a folder is focused (−1, selection cleared)

**`DrawFooter`** — Enter-on-folder guard: when Enter is pressed and the focused row is a folder, it expands the folder (if collapsed) and suppresses confirmation (`enterPressed = false`).

**`Confirm()`** — OK-button guard: if called while a folder is focused in Tree layout, expands the folder and returns without confirming a wrong item.

Non-Tree layouts (Standard/Compact/Wide/Grid) follow the original `Filtered`-index `MoveFocus`/`SetFocus` path — unchanged.

## Unit test

Added 3 tests to `PickerTreeBuilderTests.cs`:

1. **`FlattenVisualRows_AllExpanded_ProducesDfsOrder`** — Verifies DFS order with all folders expanded: A(folder)→A/L1→A/B(folder)→A/B/L2→C(folder)→C/L3→L4(root leaf). Asserts `IsFolder`, `FolderPath`, `FilteredIndex`, and `Depth` per row.

2. **`FlattenVisualRows_CollapsedFolder_HidesDescendants`** — Collapsing folder "A" removes its children (L1, sub-folder B, L2) from visual rows. Only A, C, C/L3 remain.

3. **`FlattenVisualRows_DeeplyNested_CollapseRespectsHierarchy`** — Tests a 3-level deep tree (A→B→C→L1) with various collapse combinations: all expanded, collapsing A/B (hides C+L1, L2 visible), collapsing A (hides everything under A).

Test helper: `FlattenVisualRows()` is a pure function that takes a `PickerTreeBuilder.Node` + `HashSet<string> CollapsedFolders` and produces the expected `List<TreeRow>` in DFS order — no ImGui dependency.

## Design rationale

- **VisualRows rebuilt per frame** — guarantees the nav model always matches what's on screen, even after mouse expand/collapse or search refilter.
- **CollapsedFolders is the single source of truth** for expand/collapse state. It's mutated by: keyboard ←/→, mouse arrow-click (synced after render), and cleared on `Reset`.
- **TreeFocusRow → KeyboardFocusIndex mirroring** — lets existing leaf highlight (`DrawLeafItem`'s `focus` check) work without modification.
- **Enter/OK guard in both DrawFooter and Confirm** — catches Enter (pre-confirm), OK button (mid-confirm), and double-click → Confirmed (also mid-confirm) paths.
- **Search auto-expand preserved** — when `SearchText` is non-empty, `DefaultOpen` flag forces all folders open so matching leaves are visible below their folders.

---

# BATCH-49 — BUG-A14 fix-up: CollapsedFolders → ExpandedFolders inversion

**Date:** 2026-06-12  
**Layered on:** uncommitted BATCH-48 changes  
**Build:** 0 warnings, 0 errors  
**Tests:** NodeEditor.UI.Tests: 59 passed / 0 failed

## Defect

BATCH-48 used `CollapsedFolders` (default **empty**) with `SetNextItemOpen(!CollapsedFolders.Contains(path), Always)`. An empty set meant every folder was forced OPEN on first render — i.e. folders auto-expanded by default. The user required folders to default COLLAPSED.

## Fix — invert to `ExpandedFolders` (default empty = collapsed)

Renamed/replaced `CollapsedFolders` with `ExpandedFolders` in all four files, flipping every membership test so the DEFAULT (folder not in the set) is **collapsed**:

### `PickerState.cs`
- `CollapsedFolders` → `ExpandedFolders`; `Reset()` clears it (unchanged behavior: empty = all collapsed).

### `TreeLayout.cs`
- `SetNextItemOpen(!CollapsedFolders.Contains(fullPath), Always)` → `SetNextItemOpen(ExpandedFolders.Contains(fullPath), Always)` (default false → collapsed).
- Mouse/keyboard sync after `TreeNodeEx`: `if (open) ExpandedFolders.Add(fullPath); else ExpandedFolders.Remove(fullPath);` (flipped from CollapsedFolders remove/add).
- Implicit tree: `if (!isSearching)` guard unchanged — while searching, `SetNextItemOpen` is NOT called, so `DefaultOpen` flag auto-expands matches (search auto-expand intact).

### `PickerWindow.cs`
- → arrow: `ExpandedFolders.Add(row.FolderPath)` (expand)
- ← arrow: `ExpandedFolders.Remove(row.FolderPath)` (collapse)
- Enter-on-folder / Confirm folder guard: `if (!ExpandedFolders.Contains(path)) ExpandedFolders.Add(path);` → suppress confirm / return.

### `PickerTreeBuilderTests.cs`
- `FlattenVisualRows` helper: parameter inverted from `collapsedFolders` to `expandedFolders`; children are shown only when folder IS in `expandedFolders` (`if (isExpanded)` instead of `if (!isCollapsed)`).
- Removed duplicate `foreach (var leaf in folder.Leaves)` from inside the expanded block — `FlattenVisualRows(folder, ...)` already handles them via its root-level `node.Leaves` iteration (pre-existing BATCH-48 bug).
- Updated 3 tests to pass `expandedFolders` sets: all-expanded passes `{"A","A/B","C"}` (or `{"A","A/B","A/B/C"}` for deep); collapsed-folder passes `{"C"}` (A not expanded); deep-nest tests pass the appropriate subsets.
- Fixed DFS order assertions in `AllExpanded` + `DeeplyNested` tests: correct order is sub-folders-before-leaves (matching `TreeLayout.cs` rendering), not the old leaves-before-sub-folders order.
