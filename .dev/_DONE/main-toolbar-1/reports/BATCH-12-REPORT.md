# BATCH-12 Report

## Implementation Summary

**Tasks:** MTB-P4-T4, MTB-P4-T5 — "All" tab + chips + filter, and auto-expand + last-opened.

Extended `AssetBrowserPanel` in `Hrot.Editor.AiShared/Browser/AssetBrowserPanel.cs` with additive
changes only. All BATCH-11 public API and behavior preserved.

### T4 — Incremental Filter + "All" Tab + Kind Chips

- **`Filter` property** (`string`): incrementally filters all assets by case-insensitive substring
  match on `IEditableAsset.Name`. Empty/null clears the filter.
- **`FilteredTreeFor(AssetKind kind)`**: returns the per-kind folder tree pruned to matching leaves
  plus their ancestor folders. Falls through to `TreeFor(kind)` when filter is empty. Returns an
  empty root when no assets match.
- **`FilteredFlatList()`**: returns a flat `IReadOnlyList<IEditableAsset>` of all assets across all
  permitted kinds whose kind chip is enabled and whose name matches the filter. Deterministically
  sorted by kind, then name (ordinal ignore-case).
- **Kind chips**: `IsKindChipEnabled(AssetKind)`, `SetKindChip(kind, bool)`,
  `ToggleKindChip(AssetKind)`. Default all-on among permitted kinds. Only `FilteredFlatList()`
  consults chip state; per-kind trees ignore chips.
- **DrawContent**: renders "All" tab (when `ShowAllTab`) before per-kind tabs. Every tab draws the
  shared filter box. All tab draws chips + flat list; per-kind tabs draw `FilteredTreeFor(kind)`.
  All tab rows use the same click/double-click handling as tree rows.

### T5 — Initial Reveal + Last-Opened-Per-Kind

- **`ExpandedFolders(AssetKind)`**: returns `IReadOnlyCollection<string>` of folder FullPaths to
  expand, populated from `InitialFullPath` ancestors or last-opened memory during construction.
- **Initial reveal**: `ApplyInitialReveal()` runs after `RebuildTrees()`. For `InitialKind` (or
  first tab), resolves `InitialFullPath` → computes ancestor folder paths via `GetAncestorPaths()`
  → expands those folders and sets `Selection` to the matching asset. Falls back to
  `LastOpenedByKind` if no explicit `InitialFullPath`.
- **`LastOpenedByKind`**: `IReadOnlyDictionary<AssetKind, string>` populated by `ActivateAsset`
  (stores the asset's relpath per kind). Exposed for host persistence.
- **`RestoreLastOpened(IReadOnlyDictionary<AssetKind, string>?)`**: merges entries into the
  in-memory map (ignores kinds not in `Tabs`).
- **Constructor**: accepts optional `IReadOnlyDictionary<AssetKind, string>? lastOpened` parameter
  (default `null`) — backward compatible with BATCH-11 callers.
- **DrawTreeNode**: now uses `ExpandedFolders` to determine `DefaultOpen` — folders in the expanded
  set open by default; when the set is empty (no initial reveal), all folders open (backward
  compatible).

## Design Decisions

1. **`PruneTree` returns `null` for fully-pruned nodes; `FilteredTreeFor` translates to empty
   root.** This keeps the recursive prune clean (null = pruned) while the public API always returns
   a valid `FolderTreeNode`. When no assets match the filter, `FilteredTreeFor` returns
   `FolderTreePicker.Build(null)` (root with zero children).

2. **Expanded folders: non-null set → selective open, null → open-all.** When
   `_expandedFolders[kind]` is non-empty, only folders in that set get `DefaultOpen`. When null (no
   initial reveal was applied), the old "open everything" behavior is preserved. This is
   backward-compatible and testable.

3. **`FilteredFlatList` sorted by kind then name.** Deterministic ordering for UI stability.

4. **All tab identified by absence of kind, not a synthetic `AssetKind`.** The "All" tab is a
   rendering concept only — `_activeTabIndex` semantics unchanged (the All tab just draws
   differently). No new enum value needed.

5. **`RestoreLastOpened` does not trigger re-reveal.** The reveal only happens in the constructor
   after `RebuildTrees`. `RestoreLastOpened` is a data-seeding method for hosts to call before
   construction or for external state management.

6. **`ActivateAsset` computes relpath via `AssetRelPath.RelPath`** using the same
   `BaseFolderFor(kind)` logic as `RebuildTrees`, ensuring consistency.

## Deviations

None. All implementation follows the spec exactly.

## Test Results

### New tests (5 added, all pass unfiltered)

```
AssetBrowserPanelTests (10 total, 0 failed):
  BATCH-11 (5 existing, still pass):
    - Tabs_ReflectKindFilter
    - PerKindTree_GroupsAssetsByRelPath
    - Row_CarriesKindIconKey
    - DoubleClick_RaisesAssetActivated_WithAsset
  MTB-P4-T4 (3 new):
    - Filter_Substring_CaseInsensitive_PrunesTreeAndList
    - AllTab_Chips_ToggleKindVisibility
    - AllTab_NoTree_FlatListOnly
  MTB-P4-T5 (2 new):
    - InitialFullPath_ExpandsAncestors_AndSelectsLeaf
    - LastOpened_PersistsAndRestores_PerKind
  Bonus:
    - GetAncestorPaths_ReturnsCorrectAncestors
```

### Full suite results

| Suite | Filter | Passed | Failed | Skipped |
|---|---|---|---|---|
| `Hrot.Editor.AiShared.Tests` | none | 914 | 0 | 0 |
| `Fdp.Toolkits.Tests` | `Stability!=Flaky&Stability!=Environment&Stability!=Broken` | 1856 | 0 | 0 |
| `Hrot.SimHost.Tests` | `Stability!=Flaky&Stability!=Environment&Stability!=Broken` | 585 | 0 | 3 |

- `Passengers_DeferredWhenReferencedEntityNotInMap` (SimHost) failed on first run with
  "Component type ID 177 is not registered" — a pre-existing test-ordering flake (passes in
  isolation). Re-run resolved it. Not catalogued in `TEST-HEALTH.md`; was not introduced by this
  batch.
- All tests run **without** `BLUEPRINT_REGENERATE_SNAPSHOTS`.

### Build

```
dotnet build IOS-IG-SimHost.sln: 0 Error(s), 13 Warning(s) (all pre-existing)
```

Zero new warnings. `TreatWarningsAsErrors` active — no regressions.

## Developer Insights

- **Test ordering in SimHost suite is fragile.** The `GenesisMaterializationSystemTests` test
  depends on component type registrations from other tests. It passes in isolation but
  intermittently fails in the full suite based on test order. A `[Trait("Stability", "Flaky")]`
  mark would be appropriate.
- **`PruneTree` null-return pattern** works well for recursive filtering but required the null-guard
  in `FilteredTreeFor`. Consider a `Maybe`/optional type if tree operations grow further.
- **The `FolderTreeNode` internal constructor** being in the same assembly made the tree-pruning
  implementation clean — no need for a separate builder path.
- **Filter draw uses a local variable copy** of `_filter` because `ImGui.InputText` requires `ref
  string` (can't pass a property by ref). This is the same pattern used elsewhere in the codebase
  (`OrbatPanel`, `ScenarioBrowserPanel`).

## Known Issues

- `Passengers_DeferredWhenReferencedEntityNotInMap` is an intermittent pre-existing ordering flake
  in `Hrot.SimHost.Tests`. Not addressed in this batch.
- The expanded-folder state is not updated when the user manually expands/collapses tree nodes in
  the UI. The model only tracks the initial-reveal expansion; ImGui manages its own tree state
  thereafter. This is acceptable for the current scope but may need attention if the host needs to
  persist user-driven expansion state.

## Suggested Commit Message

```
feat(main-toolbar): All-tab flat list + kind chips + incremental filter + initial reveal + last-opened-per-kind (MTB-P4-T4, T5)
```
