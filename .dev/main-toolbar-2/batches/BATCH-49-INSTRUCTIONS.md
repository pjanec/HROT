# BATCH-49 — BUG-A14 fix-up: picker Tree folders must default to COLLAPSED

**Model: pro (Zoo).** This layers on top of the UNCOMMITTED BATCH-48 changes already in the working tree
(`PickerState.cs`, `TreeLayout.cs`, `PickerWindow.cs`, `PickerTreeBuilderTests.cs`). Do NOT use codebase-memory tooling.
**Repo root:** `D:\Work\IOS-IG-SimHost-FDP`.

## Defect
BATCH-48 tracks `CollapsedFolders` (default **empty**). Because `SetNextItemOpen(!CollapsedFolders.Contains(path),
Always)` is used, an empty set means **every folder is forced OPEN on first render** — i.e. folders still
auto-expand by default. The user explicitly rejected auto-expand: folders must **default to COLLAPSED**, expandable
by keyboard (←/→) or mouse. (Auto-expand ONLY while searching stays.)

## Fix — invert the model to `ExpandedFolders` (default empty = collapsed)
Rename/replace the `CollapsedFolders` concept with `ExpandedFolders` everywhere, flipping every membership test so
the DEFAULT (folder not in the set) is **collapsed**:

1. **`PickerState.cs`:** rename `HashSet<string> CollapsedFolders` → `HashSet<string> ExpandedFolders`. Clear it in
   `Reset()` (unchanged behavior — empty = all collapsed). Keep `VisualRows`, `TreeRow`, `TreeFocusRow` as-is.

2. **`TreeLayout.cs`** (implicit + explicit folder nodes):
   - Open state: `ImGui.SetNextItemOpen(state.ExpandedFolders.Contains(fullPath), ImGuiCond.Always);`
     (default false → collapsed). For the IMPLICIT tree keep the `if (!isSearching)` guard so that **while
     searching** you DON'T call SetNextItemOpen and the `DefaultOpen` flag auto-expands matches (unchanged).
   - Mouse/keyboard sync after `TreeNodeEx`: `if (open) state.ExpandedFolders.Add(fullPath); else
     state.ExpandedFolders.Remove(fullPath);` (replaces the inverted CollapsedFolders add/remove).

3. **`PickerWindow.cs`:**
   - `HandleTreeKeyboardNavigation`: **→** = `state.ExpandedFolders.Add(row.FolderPath)` (expand);
     **←** = `state.ExpandedFolders.Remove(row.FolderPath)` (collapse).
   - Enter-on-folder guard (in `DrawFooter`) + the `Confirm()` folder guard: "expand if not already expanded" →
     `if (!state.ExpandedFolders.Contains(path)) state.ExpandedFolders.Add(path);` then suppress confirm / return.

4. **`PickerTreeBuilderTests.cs`:** the `FlattenVisualRows` helper currently takes a `collapsedFolders` set
   (children hidden when the folder IS in the set). Invert it to take an `expandedFolders` set: **children are shown
   only when the folder IS in `expandedFolders`** (default = collapsed). Update the 3 tests accordingly:
   - `FlattenVisualRows_AllExpanded_ProducesDfsOrder`: pass an `expandedFolders` set containing ALL folder paths
     ("A","A/B","C", and for the deep test "A","A/B","A/B/C") so the full DFS order is produced — assertions on
     order/depth unchanged.
   - `FlattenVisualRows_CollapsedFolder_HidesDescendants`: with folder "A" NOT in `expandedFolders` (but "C" in it),
     A's descendants are hidden — keep the same expected rows (A folder, C folder, C/L3 leaf).
   - `FlattenVisualRows_DeeplyNested_*`: "all expanded" passes {"A","A/B","A/B/C"}; "collapse A/B" passes
     {"A"} (so A's children show, A/B's hidden); "collapse A" passes {} (only A shows). Keep the expected counts/paths.
   Keep the tests meaningful (they must still assert DFS order + that collapsed/!expanded folders hide descendants).

## Rules
- Touch ONLY those four files. No other behavior change. No new auto-expand. Keep search-time auto-expand intact.
- No test weakening/deletion/stubs. Build 0 warnings.

## Build & test (no BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/NodeEditor.UI.csproj
dotnet test  FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj
```
`Failed: 0`; build 0 warnings.

## Definition of done
- Picker Tree folders are **collapsed by default**; ←/→ collapse/expand the focused folder; ↑/↓ visual-order nav
  still works; search still auto-expands. Tests updated to the `ExpandedFolders` (default-collapsed) semantics + green.
- Append a short note to `.dev/main-toolbar-2/reports/BATCH-48-REPORT.md` (or a new BATCH-49 note) describing the
  inversion.

If something cannot be done as specified, STOP and report why rather than stubbing.
