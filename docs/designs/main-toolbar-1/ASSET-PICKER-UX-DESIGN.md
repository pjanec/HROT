# Asset Picker UX via NodeEdit's Picker (Tree layout) — Design (Phase 8)

> **Pivot (after exploring the NodeEdit unification you suggested):** NodeEdit's picker framework is
> ALREADY a generic, installable-source, icon-per-type, fuzzy-with-highlight, favorites/recent,
> virtualized, multi-layout picker — and it already has a **`Tree` layout** (`PickerLayout.Tree`,
> `Picker/Layouts/TreeLayout.cs`) that builds a folder hierarchy from each entry's `Category` path.
> So we do NOT build a new picker. We **adopt NodeEdit's picker**, close a few small gaps in its Tree
> layout, and add an **asset source**. Maximum reuse; the unification is the design, not a follow-up.

## What NodeEdit already provides (no work needed)
`NodeEditor.UI/Picker` + `NodeEditor.Core/Interfaces/IPickerRegistry.cs`:
- `IPickerSource<TItem>` (installable; `Title`, `PreferredLayout`, `SelectionMode`, `Query`, `RenderItem`,
  `RenderPreview`, `GetSearchableText`, `GetItemKey`, drag in/out, async) + `IPickerRegistry`
  (`Register`, `Open(key, pos, onPick, onCancel, context)`, `DrawFrame()`).
- `PickerEntry(Id, Name, Description, Category "A/B/C", Keywords, IconTextureId, Tag)` — Category drives
  tree grouping; IconTextureId = per-item icon; Tag = opaque payload.
- `IPickerRenderContext { IIconProvider Icons; IEditorTheme Theme; IReadOnlyList<int>? MatchPositions }`.
- `PickerLayout { Standard, Compact, Wide, Grid, Tree }`; `PickerSelectionMode { Single, Multi, MultiOrdered }`.
- Fuzzy match + `MatchPositions` highlighting, Favorites > Recent > Score ordering, clipper virtualization.
- `PickerWindow`: auto-focus filter (`SetKeyboardFocusHere`), ↑/↓ nav **clamped** (`Math.Clamp`, no wrap),
  Enter confirm, Esc cancel, multi-select, preview pane. `TreeLayout`: implicit tree from `Category`,
  **auto-DefaultOpen when searching**, empty folders absent (driven by the filtered list), folders are
  non-selectable `TreeNodeEx` headers, leaves are `Selectable(AllowDoubleClick)` → double-click confirm.
- Demo suite `NodeEditor.Demo/Scenarios/S07–S12` (node/wire/variable/type/flags/asset-grid pickers) for
  fast iteration.

## Requirement → status (your 5 + agreed extras)
- (1) **Type icons** → framework supports `IconTextureId`; **Tree leaf rendering doesn't draw it yet** → GAP.
- (2) **Folders** → `TreeLayout` ✅. (3) **Auto-focus + immediate filter + ↑/↓ + Enter** → ✅ (clamp,
  no wrap — your preference). (4) **Auto-unfold + hide-empty + leaf-only nav** → ✅. (5) **dblclick
  confirm + folders non-selectable** → ✅.
- Extras: **clamp (no wrap)** ✅; **match highlight** ✅ framework but **not drawn in Tree leaves** → GAP;
  **folder icons** → GAP (Tree folders have no icon); **Left/Right collapse/expand, Home/End, PageUp/Down**
  → ✅ framework; **empty-state + count** ✅; **OK/Cancel** ✅; **preview pane** ✅; **recent/favorites** ✅;
  **remember last** ✅; **fuzzy** ✅. Excluded multi-select/sort: framework HAS them; we just leave the
  asset source single-select.

## The only code gaps (in NodeEdit `TreeLayout`)
`TreeLayout.DrawLeafItem` currently does `ImGui.Selectable(entry.Name, …)` — plain text. Bring it to
parity with `PickerItemListHelper` (the Standard layout):
1. Draw the entry's **type icon** (`entry.IconTextureId` via `ctx.Icons`) before the name.
2. Draw the name with **fuzzy match-range highlight** (reuse the run-coloring already in
   `PickerItemListHelper`; factor it into a shared helper so both layouts use it).
3. Draw **folder icons** on the folder `TreeNodeEx` nodes (closed `folder` / open `folder_open`).
4. **Scroll the keyboard-focused leaf into view** in the tree (the flat layouts already scroll).
Everything else (auto-focus, clamp nav, auto-open-on-search, hide-empty, folders-non-selectable,
dblclick, Enter/Esc, preview, recent/favorites) is already wired at the framework level and works in
Tree mode. This keeps the picker **fully generic** — no asset specifics leak into NodeEdit.

## Editor integration (assets as a NodeEdit picker source)
- **`AssetPickerSource : IPickerSource<AssetPickEntry>`** (editor side, e.g. `Hrot.Editor`/AiShared):
  - `PreferredLayout => Tree`, `SelectionMode => Single`.
  - `Query(text, ctx)` → asset items; each maps to a `PickerEntry`:
    `Category = "<Kind>/<subfolder>"` (so the "All" view groups by kind, then folders) or `<subfolder>`
    for a single-kind variant; `IconTextureId` = per-kind icon; `Tag = IEditableAsset`;
    `Description` = recipe metadata (preview).
  - A `context`/source-key variant filters to `Kinds = Scenario` for **Scenario→Load**.
- **Recognizable icons (resolves DBT-1):** register distinct per-kind icons (`asset/blueprint|btree|hsm|
  scenario|blackboard|utility`) + folder icons (`folder`/`folder_open`) in the editor's `IIconProvider`,
  mapped to clearly-different atlas cells.
- **Wiring:** register the source(s) in `IPickerRegistry`; the Open-Asset entry points
  (toolbar button, File→Open Asset…, Ctrl+O = all kinds; Scenario→Load = scenario-filtered) call
  `registry.Open(sourceKey, pos, onPick: payload => AssetPickActionRouter.Route((IEditableAsset)payload))`
  and the host calls `registry.DrawFrame()` once per frame. Retire the current `AssetPickerModal`
  (AssetBrowserPanel-based) path. **The docked `AssetBrowserPanel` browser stays untouched** (only the
  transient *picker* moves to NodeEdit's framework).

## Testability
- NodeEdit Tree gaps: model-level where possible (the tree grouping / filtered-leaf set already exist
  via `state.Filtered`); the icon/highlight/scroll are ImGui render — verify in the **NodeEdit demo**
  Tree scenario (and the editor). Add `NodeEditor.UI.Tests` for any extracted highlight/tree helper.
- `AssetPickerSource`: headless tests — entries carry correct `Category`/`IconTextureId`/`Tag`;
  scenario variant yields only scenarios; `GetItemKey` stable; preview/description present.
- Editor wiring: confirm route (file→Open, scenario→Load) via the existing `AssetPickActionRouter` tests.

## Decisions
- **D1:** Adopt NodeEdit's picker (`PickerLayout.Tree`); do NOT build a parallel picker.
- **D2:** Fix the gaps **generically in NodeEdit `TreeLayout`** (icons + highlight + folder icons +
  scroll) — no asset specifics in NodeEdit. Benefits every NodeEdit Tree picker.
- **D3:** Assets plug in as a standard `IPickerSource` (Tree, single-select); Scenario→Load is a
  filtered variant.
- **D4:** Keep the docked `AssetBrowserPanel` browser as-is; only the transient Open-Asset picker moves
  to the NodeEdit framework. Clamp nav (already), folder icons (Explorer-style).
- **D5 (future, optional):** the docked browser could later also be re-expressed on the NodeEdit
  picker/Tree — out of scope now.

## Task breakdown (Phase 8 — see TASK-DETAIL `MTB-P8-T*`)
- **T1** NodeEdit `TreeLayout` parity: type icons + fuzzy match-highlight (shared helper) + folder
  icons + scroll-focused-leaf-into-view; add a Tree demo scenario; tests for the extracted helper.
- **T2** `AssetPickerSource` (+ scenario-filtered variant) + register recognizable per-kind & folder
  icons in the editor `IIconProvider` (resolves DBT-1). Headless source tests.
- **T3** Editor wiring: register source(s) in `IPickerRegistry`, route the four Open-Asset entry points
  through `registry.Open(...)`/`DrawFrame()` → `AssetPickActionRouter.Route`; retire `AssetPickerModal`.
  Docked browser untouched.
(Execution: T1 (NodeEdit, demo-verified) → T2 (source+icons) → T3 (editor wiring).)
