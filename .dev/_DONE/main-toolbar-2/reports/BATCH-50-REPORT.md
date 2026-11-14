# BATCH-50 REPORT — picker Tree: mouse↔keyboard focus sync, dbl-click open/expand, native open-state memory

**Date:** 2026-06-12 | **Model:** pro (Zoo) | **Status:** ✅ Done

## Bugs resolved

| Bug | Description | Root |
|-----|-------------|------|
| **A17** | Mouse clicks snap selection back to row 0 | `SyncTreeFocusToLeaf()` ran every frame but `TreeFocusRow` was never updated by mouse clicks |
| **A18** | Double-click leaf opens the wrong item | `Confirmed = true` targeted row where `KeyboardFocusIndex` pointed, which was stale after snap-back |
| **A15** | New recipe Tree picker opens the wrong perspective | Same root cause: selection fell on a folder (row 0), not the clicked recipe |
| **A16** | Folder open-state forced every frame via `ExpandedFolders` set | `ImGui.SetNextItemOpen(…, ImGuiCond.Always)` overrode ImGui's native tree persistence |
| **A19** | Double-clicking a folder row did nothing | No double-click handler existed on folder rows |

## Fix 1 — Mouse drives `TreeFocusRow` (A17/A18/A15)

**Files:** `TreeLayout.cs`

- **`DrawLeafItem`:** Captured `int visualRowIndex = state.VisualRows.Count;` before the `VisualRows.Add`. On `actualMouseClicked`, set `state.TreeFocusRow = visualRowIndex;` alongside the existing selection set. `SyncTreeFocusToLeaf()` now reads the correct row → double-click `Confirmed = true` fires with the right leaf focused.

- **`DrawImplicitFolderNode` & `DrawExplicitFolderNode`:** After `TreeNodeEx`, on `ImGui.IsItemClicked()`, set `state.TreeFocusRow = visualRowIndex;` (the folder row index, already computed as `state.VisualRows.Count` before the Add). Mouse selection now consistently follows clicks.

`SyncTreeFocusToLeaf` is kept as-is — it is now correct because `TreeFocusRow` reflects the latest mouse OR keyboard focus.

## Fix 2 — Double-click folder toggles expand/collapse (A19)

**File:** `TreeLayout.cs`

In both `DrawImplicitFolderNode` and `DrawExplicitFolderNode`, after `TreeNodeEx`:
```csharp
if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
{
    state.PendingToggleFolderPath = fullPath;
    state.PendingToggleOpen = !open;
}
```
Uses the new one-shot toggle request (see Fix 3) — the folder toggles on the next frame.

## Fix 3 — ImGui native open-state memory (A16)

**Files:** `PickerState.cs`, `TreeLayout.cs`, `PickerWindow.cs`

### PickerState.cs
- **REMOVED** `HashSet<string> ExpandedFolders` (force-source-of-truth for open state).
- **ADDED** `string? PendingToggleFolderPath` and `bool PendingToggleOpen` — one-shot toggle request consumed on the next frame.
- `Reset()` clears both new fields instead of `ExpandedFolders`.

### TreeLayout.cs
- **Implicit tree (`DrawImplicitFolderNode`):** Replaced `SetNextItemOpen(ExpandedFolders.Contains(fullPath), Always)` with a guarded one-shot:
  ```csharp
  if (!isSearching && state.PendingToggleFolderPath == fullPath)
  {
      ImGui.SetNextItemOpen(state.PendingToggleOpen, ImGuiCond.Always);
      state.PendingToggleFolderPath = null;
  }
  ```
  When NOT searching: no `SetNextItemOpen` → ImGui remembers this node's open/closed state natively across frames. When searching: `DefaultOpen` flag on `treeFlags` handles auto-expand; pending toggle is NOT applied (avoids redundant override).

- **Explicit tree (`DrawExplicitFolderNode`):** Same one-shot pattern, unconditional (no search mode in explicit tree).

- **REMOVED** the old `if (open) ExpandedFolders.Add … else … ExpandedFolders.Remove` sync blocks in both folder draws. ImGui now owns the open-state lifecycle.

- **REMOVED** the `SetNextItemOpen(ExpandedFolders.Contains(fullPath), Always)` force-override in both folder draws.

### PickerWindow.cs
- **`HandleTreeKeyboardNavigation`:**
  - `→` (expand): `_state.PendingToggleFolderPath = row.FolderPath; _state.PendingToggleOpen = true;`
  - `←` (collapse): `_state.PendingToggleFolderPath = row.FolderPath; _state.PendingToggleOpen = false;`
- **`DrawFooter` (Enter-on-folder):** Replaced `ExpandedFolders.Add` with `PendingToggleFolderPath = …; PendingToggleOpen = true;`
- **`Confirm()` folder-guard:** Same replacement — uses pending toggle instead of direct `ExpandedFolders.Add`.

## Files changed

| File | Changes |
|------|---------|
| `PickerState.cs` | Removed `ExpandedFolders`; added `PendingToggleFolderPath`/`PendingToggleOpen`; updated `Reset()` |
| `TreeLayout.cs` | Fix 1: mouse→focus in folder+leaf draws. Fix 2: dbl-click toggle in both folder draws. Fix 3: one-shot `SetNextItemOpen` replaces `ExpandedFolders` force-override; removed old sync blocks |
| `PickerWindow.cs` | Fix 3: keyboard →/←, Enter-on-folder, Confirm guard all use pending toggle |

## Test results

```
NodeEditor.UI build:  0 Warning(s), 0 Error(s)
NodeEditor.UI.Tests:  Passed! Failed: 0, Passed: 59, Skipped: 0
NodeEditor.Core.Tests: Passed! Failed: 0, Passed: 181, Skipped: 0
```

All `PickerTreeBuilderTests` pass unchanged — the test-local `expandedFolders` parameter is independent of the removed production field. DFS order + descendant-hiding assertions remain intact.

## Definition of done met

- [x] Single-click a folder/leaf → selection + focus moves there (no snap-back to row 0)
- [x] Double-click a leaf → opens (Open-Asset + New recipe) — `Confirmed = true` now fires on the correct row
- [x] Double-click a folder → toggles expand/collapse via one-shot request
- [x] Folder open-state remembered by ImGui natively (not forced collapsed every frame)
- [x] ←/→ + Enter-on-folder still expand/collapse via one-shot request
- [x] New recipe picker selects the clicked recipe → opens the right perspective (A15)
- [x] Other layouts unchanged
- [x] Build 0 warnings; UI/Core suites `Failed: 0`
