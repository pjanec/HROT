# BATCH-12: "All" tab + chips + filter, and auto-expand + last-opened
**Tasks:** MTB-P4-T4, MTB-P4-T5   **Phase:** 4 — Generic Asset Browser Panel   **Est:** ~10h
**Dependencies:** BATCH-11 (`AssetBrowserPanel` with Tabs/TreeFor/AssetForLeaf/RowIconKey/Selection/
AssetActivated). Completes Phase 4.

> Do T4 then T5 in sequence; do NOT advance until the current task's impl + tests pass. Extend the
> existing `AssetBrowserPanel`; keep its public API/behavior from BATCH-11 intact (additive).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/main-toolbar-1/DESIGN.md` §10.1 ("All" tab, incremental filter, last-opened memory).
3. `.dev/main-toolbar-1/TASK-DETAIL.md` → MTB-P4-T4, MTB-P4-T5.
4. `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetBrowserPanel.cs` (BATCH-11) — `Tabs`,
   `TreeFor(kind)`, `AssetForLeaf`, `RowIconKey`, `Selection`/`SelectAsset`, `ActivateAsset`/
   `AssetActivated`, `AssetBrowserPanelOptions` (`Kinds`, `ShowAllTab`, `InitialKind`,
   `InitialFullPath`), `FolderTreePicker.Build`.

---

## Task 1 — "All" tab (flat + chips) + incremental filter (MTB-P4-T4) — §10.1
Extend the panel (logic separated from ImGui draw; keep everything testable):
- **Incremental name filter** (present in EVERY tab): a `string Filter { get; set; }` (or
  `SetFilter(string)`), case-insensitive substring match on `IEditableAsset.Name`.
  - **Per-kind tabs:** the tree prunes to matching leaves **plus their ancestor folders**. Expose a
    `FolderTreeNode FilteredTreeFor(AssetKind kind)` that applies the current filter (empty filter →
    same as `TreeFor`).
  - **"All" tab:** a **flat list** (NO tree) of all permitted-kind assets, filtered by the same
    substring; expose `IReadOnlyList<IEditableAsset> FilteredFlatList()`.
- **"All" tab kind chips** (only when `options.ShowAllTab`): a per-kind visibility toggle set; the
  flat list shows only assets whose kind chip is enabled. Expose the chip state
  (`bool IsKindChipEnabled(AssetKind)` + `ToggleKindChip(AssetKind)` / `SetKindChip(kind,bool)`),
  default all-on among permitted kinds. `FilteredFlatList()` honors both the chips and the name filter.
- The "All" tab has **no tree** (flat list only); per-kind tabs have **no chips**.
- `DrawContent` renders: the filter box in every tab; per-kind tabs draw `FilteredTreeFor`; the All
  tab draws chips + `FilteredFlatList`. Keep draw thin over the testable model.

**Tests required (extend `AssetBrowserPanelTests`):**
- `Filter_Substring_CaseInsensitive_PrunesTreeAndList` — set Filter to a lowercase substring of a
  mixed-case asset name; `FilteredTreeFor(kind)` keeps only matching leaves + their ancestor folders
  (non-matching folders/leaves removed); `FilteredFlatList()` returns only matching assets.
- `AllTab_Chips_ToggleKindVisibility` — disabling the Blueprint chip removes Blueprint assets from
  `FilteredFlatList()` while keeping others; re-enabling restores them.
- `AllTab_NoTree_FlatListOnly` — the All tab exposes a flat list and NO tree (assert the All-tab model
  is a flat list; `FilteredFlatList` spans multiple kinds; there is no `TreeFor`/`FilteredTreeFor`
  call needed/used for the All tab).

## Task 2 — Auto-expand/select + last-opened-per-kind (MTB-P4-T5) — §10.1
- **Initial reveal:** on construction, if `options.InitialFullPath` is set (a relative-to-root path)
  for `options.InitialKind` (default the first tab), compute the set of **ancestor folder paths** to
  expand and **select the leaf** (`Selection` = the matching asset). Expose this as testable state:
  e.g. `IReadOnlyCollection<string> ExpandedFolders(AssetKind)` (the FullPaths to expand) and the
  resulting `Selection`. `DrawContent` opens those tree nodes and highlights the leaf.
- **Last-opened-per-kind memory:** maintain a `AssetKind → relpath` map of the last **activated**
  asset's relpath per kind; update it inside `ActivateAsset`. Expose it so the host can persist it
  (`IReadOnlyDictionary<AssetKind,string> LastOpenedByKind`), and accept an initial map (e.g. a new
  optional ctor param or `RestoreLastOpened(IReadOnlyDictionary<...>)`). On open with no explicit
  `InitialFullPath` for a kind, pre-select/reveal the remembered relpath for that kind's tab.
  (The actual settings serialization is host glue — out of scope here; implement the in-memory
  persist/restore contract that the host will wire to `WindowManager` prefs.)

**Tests required (extend `AssetBrowserPanelTests`):**
- `InitialFullPath_ExpandsAncestors_AndSelectsLeaf` — with `InitialKind=Blueprint`,
  `InitialFullPath="combat/patrol/Guard.bp.json"`, `ExpandedFolders(Blueprint)` contains `combat` and
  `combat/patrol` (the ancestors) and `Selection` is the Guard asset.
- `LastOpened_PersistsAndRestores_PerKind` — `ActivateAsset` updates `LastOpenedByKind[kind]` to that
  asset's relpath; constructing a new panel and restoring that map pre-selects/reveals the remembered
  relpath for the kind (assert `Selection`/`ExpandedFolders` reflect it); the map is per-kind
  (activating a Blueprint doesn't change the BTree entry).

## Hard constraints
- Extend `AssetBrowserPanel`; keep BATCH-11 public members/behavior intact (additive only). Panel
  remains side-effect-free (no document open / scenario load).
- Do NOT add `AssetKind.Scenario` (MTB-P5-T2). No scope creep beyond T4/T5.
- Do NOT delete/modify legacy/assembly-loading code. Do NOT weaken/skip/auto-pass tests; zero new
  warnings (TreatWarningsAsErrors).

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings).
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New tests pass UNFILTERED; existing
  `AssetBrowserPanelTests` (BATCH-11) still pass. 0-failed with the Stability filter for
  `Hrot.Editor.AiShared.Tests` + the hot suites `Fdp.Toolkits.Tests` + `Hrot.SimHost.Tests`
  (PRE-3 EQS flake → re-run if it appears).
- Write `.dev/main-toolbar-1/reports/BATCH-12-REPORT.md`: files changed, the filter/chip/expand
  model seams, the last-opened persist/restore contract, each new test + assertions, paste actual
  test-run summaries, insights.

If something cannot be done as specified, stop and report why rather than stubbing it.
