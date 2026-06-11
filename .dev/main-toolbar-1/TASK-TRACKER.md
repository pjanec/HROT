# Main Toolbar & Asset Browser — Task Tracker

**Reference:** detailed tasks in [TASK-DETAIL.md](./TASK-DETAIL.md); design in
[DESIGN.md](./DESIGN.md). Dev rules: [../.guides/DEV-GUIDE.md](../.guides/DEV-GUIDE.md).

Status: `[ ]` not done · `[x]` done. Phases are sequential — finish (build + tests green)
before starting the next. **Do not remove legacy/assembly code** except the items named in
Phase 7.

---

## Phase 0 — Folder Reorganization (prerequisite)
**Goal:** disjoint `Assets/` and `Recipes/` roots, single `AssetRoots` authority.

- [x] **MTB-P0-T1** `AssetRoots` constants [details](./TASK-DETAIL.md#mtb-p0-t1--assetroots-constants)
- [x] **MTB-P0-T2** Move asset/recipe files + `.csproj` globs [details](./TASK-DETAIL.md#mtb-p0-t2--move-asset--recipe-files--csproj-globs)
- [x] **MTB-P0-T3** Repoint discovery/scan/save paths [details](./TASK-DETAIL.md#mtb-p0-t3--repoint-discoveryscansave-paths-to-assetroots)

## Phase 1 — Toolbar & Icon Infrastructure
**Goal:** generic top toolbar (jitter-free height), 64px icon widgets, icon keys.

- [x] **MTB-P1-T1** `MainToolbarManager` [details](./TASK-DETAIL.md#mtb-p1-t1--maintoolbarmanager)
- [x] **MTB-P1-T2** Icon widget `IconHandle` + size overloads [details](./TASK-DETAIL.md#mtb-p1-t2--icon-widget-iconhandle--size-overloads)
- [x] **MTB-P1-T3** `WindowManager.MainToolbar` + dockspace inset [details](./TASK-DETAIL.md#mtb-p1-t3--windowmanagermaintoolbar--dockspace-inset)
- [x] **MTB-P1-T4** Icon keys + `AssetKind → IconKey` [details](./TASK-DETAIL.md#mtb-p1-t4--icon-keys--assetkind--iconkey)

## Phase 2 — Shell Command Set & Binding Adapters
**Goal:** command-driven menus/toolbar/hotkeys; Ctrl+S fix.

- [x] **MTB-P2-T1** Shell `EditorCommandsImpl` [details](./TASK-DETAIL.md#mtb-p2-t1--shell-editorcommandsimpl)
- [x] **MTB-P2-T2** Menu-binding adapter [details](./TASK-DETAIL.md#mtb-p2-t2--menu-binding-adapter)
- [x] **MTB-P2-T3** Toolbar-binding adapter [details](./TASK-DETAIL.md#mtb-p2-t3--toolbar-binding-adapter)
- [x] **MTB-P2-T4** Save / Save-As / Save-All commands + Ctrl+S [details](./TASK-DETAIL.md#mtb-p2-t4--save--save-as--save-all-commands--ctrls)

## Phase 3 — Toolbar Groups: Time, Perspective, AI Debug
**Goal:** the three functional groups + perspective relocation.

- [x] **MTB-P3-T1** Extract `TransportIcons` helper [details](./TASK-DETAIL.md#mtb-p3-t1--extract-transporticons-helper)
- [x] **MTB-P3-T2** `MainToolbarTimeControlSection` [details](./TASK-DETAIL.md#mtb-p3-t2--maintoolbartimecontrolsection)
- [x] **MTB-P3-T3** Perspective menu (relocate) [details](./TASK-DETAIL.md#mtb-p3-t3--perspective-menu-relocate-out-of-menu-bar)
- [x] **MTB-P3-T4** Perspective toolbar radio-group + `IconKey` [details](./TASK-DETAIL.md#mtb-p3-t4--perspective-toolbar-radio-group--per-perspective-iconkey)
- [x] **MTB-P3-T5** AI Debug group (polymorphic) [details](./TASK-DETAIL.md#mtb-p3-t5--ai-debug-group-polymorphic)

## Phase 4 — Generic Asset Browser Panel
**Goal:** the reusable browser panel + folder tree.

- [x] **MTB-P4-T1** `FolderTreePicker` (read mode) [details](./TASK-DETAIL.md#mtb-p4-t1--foldertreepicker-read-mode)
- [x] **MTB-P4-T2** `BaseFolder` seam + relative-path helper [details](./TASK-DETAIL.md#mtb-p4-t2--basefolder-seam--relative-path-helper)
- [x] **MTB-P4-T3** `AssetBrowserPanel` tabs + tree + row icons [details](./TASK-DETAIL.md#mtb-p4-t3--assetbrowserpanel-tabs--per-kind-tree--row-icons)
- [x] **MTB-P4-T4** "All" tab (flat + chips) + filter [details](./TASK-DETAIL.md#mtb-p4-t4--all-tab-flat--chips--incremental-filter)
- [x] **MTB-P4-T5** Auto-expand/select + last-opened-per-kind [details](./TASK-DETAIL.md#mtb-p4-t5--auto-expandselect--last-opened-per-kind)

## Phase 5 — Hosts, Scenarios, Typed Change, Wiring
**Goal:** modal+window hosts, scenarios in the catalog, perf-safe change event.

- [ ] **MTB-P5-T1** Typed `IAssetCatalog.Changed` + ReferenceCatalog skip [details](./TASK-DETAIL.md#mtb-p5-t1--typed-iassetcatalogchanged--referencecatalog-skip)
- [x] **MTB-P5-T2** `AssetKind.Scenario` + `ScenarioCatalogContributor` [details](./TASK-DETAIL.md#mtb-p5-t2--assetkindscenario--scenariocatalogcontributor)
- [ ] **MTB-P5-T3** Modal picker host [details](./TASK-DETAIL.md#mtb-p5-t3--modal-picker-host)
- [ ] **MTB-P5-T4** Docked window host [details](./TASK-DETAIL.md#mtb-p5-t4--docked-window-host)
- [ ] **MTB-P5-T5** Scenario nested-name support [details](./TASK-DETAIL.md#mtb-p5-t5--scenario-nested-name-support)
- [ ] **MTB-P5-T6** Caller wiring (pick → action) [details](./TASK-DETAIL.md#mtb-p5-t6--caller-wiring-pick--action)

## Phase 6 — Unified Creation & Recipes
**Goal:** New/Save-As dialogs, generalized recipes, folder picker.

- [ ] **MTB-P6-T1** Kind-agnostic `RecipeMetadata` [details](./TASK-DETAIL.md#mtb-p6-t1--kind-agnostic-recipemetadata)
- [ ] **MTB-P6-T2** `INewAssetService` + Blueprint impl + "Empty" [details](./TASK-DETAIL.md#mtb-p6-t2--inewassetservice--blueprint-impl--hardcoded-empty)
- [ ] **MTB-P6-T3** BTree/HSM/Scenario `INewAssetService` impls [details](./TASK-DETAIL.md#mtb-p6-t3--btreehsmscenario-inewassetservice-impls)
- [ ] **MTB-P6-T4** `FolderTreePicker` pick mode [details](./TASK-DETAIL.md#mtb-p6-t4--foldertreepicker-pick-mode)
- [ ] **MTB-P6-T5** New Asset dialog [details](./TASK-DETAIL.md#mtb-p6-t5--new-asset-dialog)
- [ ] **MTB-P6-T6** Save-As dialog (fresh-id) [details](./TASK-DETAIL.md#mtb-p6-t6--save-as-dialog-fresh-id-duplicate-semantics)
- [ ] **MTB-P6-T7** Subfolder-aware file save [details](./TASK-DETAIL.md#mtb-p6-t7--subfolder-aware-file-save)

## Phase 7 — Scenario Menu, Workspace, Retirement
**Goal:** menu migration, Workspace surface, retire legacy browsers.

- [ ] **MTB-P7-T1** Scenario lifecycle menu commands [details](./TASK-DETAIL.md#mtb-p7-t1--scenario-lifecycle-menu-commands)
- [ ] **MTB-P7-T2** Delete `ScenarioBrowserPanel` [details](./TASK-DETAIL.md#mtb-p7-t2--delete-scenariobrowserpanel)
- [ ] **MTB-P7-T3** Workspace dynamic submenu [details](./TASK-DETAIL.md#mtb-p7-t3--workspace-dynamic-submenu)
- [ ] **MTB-P7-T4** Retire AiShared `AssetBrowserWindow` [details](./TASK-DETAIL.md#mtb-p7-t4--retire-aishared-assetbrowserwindow)
- [ ] **MTB-P7-T5** Retire Blueprints `AssetBrowserWindow` + `FileSystemAssetCatalog` [details](./TASK-DETAIL.md#mtb-p7-t5--retire-blueprints-assetbrowserwindow--filesystemassetcatalog)
