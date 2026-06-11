# Asset Picker UX — Design (Phase 8, post-release polish)

The unified Open-Asset modal now opens, but is "unusable as is": no recognizable per-type icons, no
folders (opens on the flat "All" list), no keyboard control, no auto-filter-focus. This doc designs the
polish so the picker behaves like **NodeEdit's node-type picker** — but over a **folder tree of assets**.

## Guiding principle — enhance the SHARED panel, don't fork
The modal (`AssetPickerModal`) and the persistent docked browser (`AssetBrowserDockedWindow`) both host
the SAME `AssetBrowserPanel.DrawContent()`. All improvements below go into that shared panel + its
helpers, so both surfaces gain them (this is the "reuse" the user expected — the picker is NOT a
separate implementation). The modal only adds *picker affordances* (auto-focus the filter, Enter =
confirm) via a panel option; the docked browser uses the same code without those.

Reference (inspiration, flat-list): `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerWindow.cs`
(`SetKeyboardFocusHere` on the search box; a `Filtered` list + `KeyboardFocusIndex`; Up/Down/Home/End
move the index; Enter/KeypadEnter confirm; Esc cancels). We adapt this to a **tree** by navigating its
**leaves** only.

Current code: `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetBrowserPanel.cs`
(`DrawContent`/`DrawAllTab`/`DrawKindTab`/`DrawTreeNode`/`DrawFilterBox`, `FilteredTreeFor`, `Filter`,
`Selection`, `RowIconKey`, `ExpandedFolders`); `FolderTreePicker.Build`; `AssetKindIcons.GetIconKey`;
`SilkIconProvider`.

## The five requirements → design

### (1) Per-asset-type icons — recognizable
`DrawTreeNode`/the flat row already call `_icons.TryGet(RowIconKey(asset))` and draw the handle, but the
atlas cells the keys map to are placeholder/unaudited (DBT-1), so types are indistinguishable.
- Map each `AssetKind` to a **distinct, recognizable** `SilkIconProvider` atlas cell:
  `asset/blueprint`, `asset/btree`, `asset/hsm`, `asset/scenario`, `asset/blackboard`, `asset/utility`.
  Pick visibly-different famfamfam-silk cells (document the chosen cell per kind). If a clearly-right
  silk glyph isn't available for a kind, choose the closest distinct one — the requirement is *distinct
  and recognizable per type*, not pixel-perfect iconography.
- Ensure the leading icon is drawn at row/line height before the name in BOTH the tree leaves and the
  flat list, with a text-only fallback when a key is unresolved. Resolves **DBT-1**.

### (2) Folders in the picker (reuse the tree)
The picker opens on the flat "All" tab → no folders. Make the picker show **folder trees**:
- The per-kind tabs already render trees — keep them.
- Replace the flat "All" tab with a **single unified tree grouped by kind**: build it via
  `FolderTreePicker.Build` from **kind-prefixed relative paths** (`"<Kind>/<relpath>"`, e.g.
  `"Blueprint/combat/Guard"`), so the top-level folders are the kinds and each asset sits under its
  kind + subfolder. Leaf→asset mapping as today. The All tab keeps the kind chips as a visibility
  filter (a disabled chip hides that kind's top folder).
- The docked browser is unaffected (its per-kind tabs are already trees).

### (3) Auto-focused filter + keyboard leaf navigation (NodeEdit-style)
Add a **testable navigation model** to `AssetBrowserPanel`, computed for the active tab's current
filtered tree:
- `IReadOnlyList<IEditableAsset> VisibleLeaves` — the leaves of the active tab's filtered tree in
  stable DFS order (folders excluded). Recomputed when the tab/filter/catalog changes.
- `int KeyboardFocusIndex` (into `VisibleLeaves`; -1 when empty) + `MoveFocus(int delta)` (moves over
  leaves only, clamps or wraps — document; NodeEdit wraps) + `IEditableAsset? FocusedAsset` +
  `ConfirmFocused()` (raises `AssetActivated(FocusedAsset)` when non-null) + `bool TrySetFocusToAsset`.
- `DrawContent`/`DrawFilterBox`: a new option `AssetBrowserPanelOptions.AutoFocusFilter` (default
  false; the modal sets true) → call `ImGui.SetKeyboardFocusHere()` on the filter `InputText` on the
  first frame after open. Filtering is already immediate (the `Filter` setter feeds `FilteredTreeFor`).
- Key handling in `DrawContent` (when the panel/modal has focus): `Up`/`Down` → `MoveFocus(-1/+1)`;
  `Enter`/`KeypadEnter` → `ConfirmFocused()`; the focused leaf is rendered highlighted (Selection ==
  focused) and **scrolled into view** (`SetScrollHereY` on the focused row). Mirrors `PickerWindow`.
- `Selection` and `KeyboardFocusIndex` stay in sync (clicking a leaf sets both; arrows move both).

### (4) Auto-unfold matches, hide empty folders, leaves-only nav
- When `Filter` is non-empty, render the ancestor folders of matching leaves **force-open**
  (`ImGui.SetNextItemOpen(true, ImGuiCond.Always)` while filtering) so all matches are visible without
  manual expansion. `ExpandedFolders(kind)` already yields those ancestors — drive the render from it.
- Folders with **no matching descendant are not rendered** — `FilteredTreeFor` already prunes them;
  confirm `DrawTreeNode` skips empty folder nodes.
- `VisibleLeaves` (req 3) contains **only leaves**, so Up/Down never lands on a folder.

### (5) Folders non-selectable; double-click confirms
- Folder tree nodes are **headers only**: clicking a folder only toggles open/close; a folder is never
  `Selection`, never `FocusedAsset`, never confirmable. (Render via `TreeNodeEx` without a selectable
  highlight; do not set Selection on folder click.)
- A **leaf** single-click sets `Selection`/focus; **double-click a leaf** → `ActivateAsset` (confirm),
  same as today. Activation on a folder is a no-op.

## Modal wiring (small)
`AssetPickerModal` opens the panel with `AutoFocusFilter = true`. Esc cancels (exists). The panel's
`ConfirmFocused`/`AssetActivated` already routes through the modal callback → `AssetPickActionRouter`.
Ctrl+Tab tab-cycle (BATCH-26) stays. No new modal mechanics — just the option + the panel honoring Enter
via `AssetActivated`.

## Testability
The ImGui input bits (`SetKeyboardFocusHere`, `IsKeyPressed`, `SetScrollHereY`, `TreeNodeEx`) are
runtime-only and verified by the user in the live editor. Everything else is a **headless model** and
unit-tested: `VisibleLeaves` ordering (DFS, leaves only), `MoveFocus` (skips folders, wrap/clamp,
empty-list), `ConfirmFocused` (raises `AssetActivated` with the focused leaf), filter pruning +
`ExpandedFolders` (ancestors of matches; empty folders excluded), the All-tab kind-grouped tree, and
the per-kind icon-key distinctness.

## Decisions
- **D1:** Enhance the shared `AssetBrowserPanel` (not a picker-specific fork) — modal + docked both gain
  icons/tree/nav. Picker-only behaviors gated by `AutoFocusFilter`.
- **D2:** The "All" view becomes a kind-grouped tree (kind-prefixed relpaths) — reuses `FolderTreePicker`.
- **D3:** Keyboard nav iterates `VisibleLeaves` (leaves only); folders are non-selectable headers.
- **D4:** Wrap-around on Up/Down at the ends (matches NodeEdit feel); document if clamp is preferred.

## Task breakdown (Phase 8 — see TASK-DETAIL `MTB-P8-T*`)
- **T1** Per-kind recognizable icons (resolves DBT-1).
- **T2** "All" view as a kind-grouped folder tree (reuse the tree).
- **T3** Auto-focus filter + keyboard leaf navigation model + Enter/Up/Down + scroll-into-view.
- **T4** Auto-unfold on filter + hide empty folders + folders non-selectable + double-click confirm.
(Execution: T1 and T2 are independent; T3+T4 are tightly coupled and likely one batch.)
