# Main Toolbar & Asset Browser — Task Detail

**Design:** see [DESIGN.md](./DESIGN.md) (chapter refs `§n` below). **Tracker:**
[TASK-TRACKER.md](./TASK-TRACKER.md). **Dev rules:** every task follows
[../.guides/DEV-GUIDE.md](../.guides/DEV-GUIDE.md).

## How to execute a task (Zoo conventions)

- Each task is **self-contained**: do only what its scope says; read the referenced design
  chapter for rationale. Do **not** refactor or delete unrelated code.
- **Do not remove legacy / assembly-loading code** (assembly contributors, `BTreeDefinition`/
  `HsmDefinition`, `AmbushTree`, `UrbanCombat`, the Persistence-Unification migration). It stays.
- A task is **done only when** every success condition is met **and** `dotnet build` is green
  **and** the full test suite passes (run **without** `BLUEPRINT_REGENERATE_SNAPSHOTS`).
- Success conditions are written as **named unit tests** wherever possible — add them in the
  matching `*.Tests` project. UI logic must be split from ImGui draw calls so it is testable
  headlessly (mirror existing `IconWidgetsTests`/`StatusBarManagerTests` patterns).
- Prefer additive changes; keep public APIs of existing types intact unless a task says to
  change them.

Task id scheme: `MTB-Pn-Tk` (phase n, task k).

---

## Phase 0 — Folder Reorganization (prerequisite) — §16

### MTB-P0-T1 — `AssetRoots` constants
**Scope:** Add an `AssetRoots` static class (in `Hrot.AI.Behaviors` or a shared editor infra
namespace per §13) exposing the Assets and Recipe roots per kind, resolved relative to the
`Hrot.AI.Behaviors` assembly location.
**Members:** `AssetsRoot`, `RecipesRoot`; `AssetsFor(AssetKind)`, `RecipesFor(AssetKind)`
returning `Assets/{Blueprints|HSMs|BTrees}` and `Recipes/{Blueprints|HSMs|BTrees|Scenarios}`.
Scenario has **no** Assets root (return null / throw documented).
**Success conditions:**
- `AssetRootsTests.AssetsFor_EachFileKind_ReturnsExpectedRelativeSegment` (Blueprint→`Assets/Blueprints`, Hsm→`Assets/HSMs`, BTree→`Assets/BTrees`).
- `AssetRootsTests.RecipesFor_AllKinds_IncludingScenario` (Scenario→`Recipes/Scenarios`).
- `AssetRootsTests.AssetsFor_Scenario_HasNoAssetsRoot` (documented null/throw).
- No behavior change elsewhere yet (constants only).

### MTB-P0-T2 — Move asset & recipe files + `.csproj` globs
**Scope:** Physically relocate files to the §16 layout: `Blueprints/*.bp.json` →
`Assets/Blueprints/`, `Blueprints/Recipes/*` → `Recipes/Blueprints/`, `Machines/*` →
`Assets/HSMs/`, `Trees/*` → `Assets/BTrees/`. Update `Hrot.AI.Behaviors.csproj`
content/`CopyToOutputDirectory` globs so the new layout ships to `bin`.
**Success conditions:**
- After build, output dir contains assets under `Assets/<Kind>/` and recipes under
  `Recipes/<Kind>/` (test: `FolderLayoutTests.Output_HasAssetsAndRecipesRoots` enumerates the
  build output and asserts both trees exist, and that no `*.bp.json` remains directly under a
  bare `Blueprints/` output folder).
- `git status` shows moves (not delete+add of unrelated content); no file content changes.

### MTB-P0-T3 — Repoint discovery/scan/save paths to `AssetRoots`
**Scope:** Update every hardcoded path to read from `AssetRoots`:
`BlueprintAssetContributor`/`*JsonAssetContributor` scan roots, `BlueprintEditorBootstrap.DiscoverRecipes`
(now `Recipes/Blueprints`), and the recipe-save path in `EditorSubsystem` (~L2057). Keep scans
recursive (`SearchOption.AllDirectories`).
**Success conditions:**
- `DiscoverRecipesTests.Discovers_FromRecipesBlueprintsRoot` (reads new recipe root).
- Asset contributors discover finals from `Assets/<Kind>` only; a recipe placed under
  `Recipes/Blueprints` is **not** returned by the final-asset scan
  (`AssetScanTests.RecipesExcludedFromFinalScan`).
- No string literal `"Blueprints/Recipes"`, `"Machines"`, `"Trees"` left in scanned code paths
  (grep-clean; all via `AssetRoots`).

---

## Phase 1 — Toolbar & Icon Infrastructure — §4, §5

### MTB-P1-T1 — `MainToolbarManager`
**Scope:** New class in `Fdp.Presentation.WindowManager` per §4.1: `RegisterEntry(id, sortOrder,
declaredHeight, Action render, perspective?)`, `RegisterSeparator(id, sortOrder, perspective?)`,
`Height`, `Render(currentPerspective)`. Top-anchored borderless window (§4.1.2).
**Success conditions (`MainToolbarManagerTests`):**
- `RegisterEntry_DuplicateId_LastWriteWins`.
- `Entries_RenderInAscendingSortOrder` (verify via a recording list of invoked ids).
- `PerspectiveFilter_NullIsGlobal_NamedOnlyWhenMatch`.
- `Height_IsMaxDeclaredOverAllRegistered_RegardlessOfCurrentPerspective` (registering a
  perspective-bound 80px entry raises Height to 80 even when a different perspective is active).
- `Separator_RegisteredAndOrdered`.

### MTB-P1-T2 — Icon widget `IconHandle` + size overloads
**Scope:** Add to `IconWidgets` (§4.2): `IconButton(in IconHandle, id, Vector2 size, bool
enabled, Vector4? tint)`, `ToggleIcon(...)`, `Tooltip(string)`. Disabled → passive `Dummy`
(no hit area) + dimmed draw. Keep existing overloads.
**Success conditions (`IconWidgetsTests`, mirroring existing style):**
- `IconButton_Handle_WhenNotClicked_ReturnsFalse`.
- `IconButton_Handle_Disabled_NeverReturnsTrue_AndRegistersNoHitArea`.
- `ToggleIcon_Handle_TogglesState_OnClick` / `_WhenDisabled_StateUnchanged`.
- `*_DoesNotThrow` for valid args at 64×64.

### MTB-P1-T3 — `WindowManager.MainToolbar` + dockspace inset
**Scope:** Add `MainToolbar` property (mirror `StatusBar`); call `MainToolbar.Render(CurrentPerspective)`
in `WindowManager.Render`. In `Program.cs` inset the dockspace top by `MainToolbar.Height`
(§4.1.2). Extract the inset math into a testable helper.
**Success conditions:**
- `WindowManager.MainToolbar` resolves; `Render` invokes it (test via a fake/registered entry
  recording invocation).
- `DockspaceLayoutTests.CentralSize_SubtractsToolbarAndStatusBar`
  (`size.Y == workH - toolbarH - statusBarH`, clamped ≥ 0).

### MTB-P1-T4 — Icon keys + `AssetKind → IconKey`
**Scope:** Extend `SilkIconProvider`'s map (or add a provider behind `IIconProvider`) with the
§5.1 keys; add an `AssetKind → IconKey` map (§5.2).
**Success conditions:**
- `IconKeysTests.TryGet_EachNewKey_ReturnsHandle` (all §5.1 keys resolve).
- `IconKeysTests.AssetKindToIconKey_CoversAllKinds_IncludingScenario`.
- `IconKeysTests.TryGet_UnknownKey_ReturnsFalse`.

---

## Phase 2 — Shell Command Set & Binding Adapters — §6, §20

### MTB-P2-T1 — Shell `EditorCommandsImpl`
**Scope:** Create a single long-lived `EditorCommandsImpl` in the editor composition root (§6.1)
and expose it for subsystem registration.
**Success conditions:**
- `ShellCommandsTests.RegisteredCommand_IsReturnedByGetAndAll`.
- `ShellCommandsTests.Invoke_CallsHandler_WhenEnabled` / `_NoOp_WhenDisabled`.

### MTB-P2-T2 — Menu-binding adapter
**Scope:** Adapter that registers a `GlobalMenuRegistry` item from a command id + path (§6.2):
`OnClick → Invoke(id)`; checkable when `IsChecked != null`; shortcut text from `DefaultKey`.
**Success conditions (`MenuCommandAdapterTests`):**
- `RegistersItem_AtPath_OnClickInvokesCommand`.
- `Checkable_ReflectsIsChecked`.
- `Disabled_ItemNotInvoked_WhenIsEnabledFalse`.

### MTB-P2-T3 — Toolbar-binding adapter
**Scope:** Adapter producing a `MainToolbarManager` entry from a command descriptor (§6.2):
resolve `IconKey` via `IIconProvider`, `enabled=IsEnabled()`, toggled=`IsChecked()`, tooltip,
click→`Invoke`.
**Success conditions (`ToolbarCommandAdapterTests`):**
- `Click_InvokesCommand` (recording fake).
- `Enabled_And_Toggled_TrackDescriptor`.
- `MissingIcon_FallsBackToText_NoThrow`.

### MTB-P2-T4 — Save / Save-As / Save-All commands + Ctrl+S
**Scope:** Register `Save` (Ctrl+S), `Save As` (—), `Save All` (Ctrl+Shift+S) shell commands
(§20). `Save` acts on the active document; **empty `SourceFilePath` → route to Save-As**
(§18.5). Wire `EditorHotkeyDispatcher` so bindings fire at perspective level.
**Success conditions (`SaveCommandsTests`, mock `AiDocumentManager`):**
- `Save_WithSourcePath_WritesActiveDocument`.
- `Save_EmptySourcePath_RoutesToSaveAs`.
- `SaveAll_SavesEveryDirtyDocument`.
- `Hotkey_CtrlS_InvokesSave_RegardlessOfFocusedWindow` (dispatcher test).

---

## Phase 3 — Toolbar Groups: Time, Perspective, AI Debug — §7, §8, §9

### MTB-P3-T1 — Extract `TransportIcons` helper
**Scope:** Move `DrawShape`/`DrawTransportButton` shape logic out of
`ClusterTimeControlStatusBarSection` into a shared static `TransportIcons` (§7); refactor the
status-bar section to call it. No visual change.
**Success conditions:**
- Existing `ClusterTimeControl*` tests still pass.
- `TransportIconsTests.Draw_AllShapes_Headless_NoThrow`.

### MTB-P3-T2 — `MainToolbarTimeControlSection`
**Scope:** Toolbar section reading `ITimeTransportFacade`, 64px via `TransportIcons` (§7):
play/pause/step/stop + time + multiplier.
**Success conditions (`MainToolbarTimeControlTests`, fake facade):**
- `PlayPause_Click_CallsTogglePlayPause`; `Step/Stop` likewise, gated by `Is*Enabled`.
- `PlayPauseFace_ReflectsIsPaused`.
- `TimeText_FormatsTotalTime`; `RateButton_OpensSelector_SetsTimeScale`.

### MTB-P3-T3 — Perspective menu (relocate out of menu bar)
**Scope:** Stop drawing `RenderPerspectiveSwitcher` inside the main menu bar; add a top-level
**Perspective** menu built from the perspective enumeration; checked=active; select→`SwitchPerspective`
(§8).
**Success conditions:**
- `PerspectiveMenuTests.MenuLists_DistinctPerspectives_Sorted`.
- `PerspectiveMenuTests.Select_CallsSwitchPerspective`; `Checked_EqualsCurrent`.
- Perspective buttons no longer rendered in `BeginMainMenuBar` (assert via the menu-bar build
  path / removed call).

### MTB-P3-T4 — Perspective toolbar radio-group + per-perspective `IconKey`
**Scope:** Add optional `IconKey` to perspective/window registration; toolbar section renders
one toggle per perspective as a radio group (§8).
**Success conditions (`PerspectiveToolbarTests`):**
- `ExactlyOneToggled_EqualsCurrentPerspective`.
- `ClickNonActive_SwitchesPerspective`.
- `MissingIconKey_FallsBackToTextButton`.

### MTB-P3-T5 — AI Debug group (polymorphic)
**Scope:** Shell commands Continue/StepOver/StepInto/StepOut/Pause bound to
`IDebugSessionRegistry.ActiveSession` (`IAiDebugSession`); blueprint-only Step Back + node
position when `ActiveSession is IBlueprintDebugSession` (§9). Toolbar group "AI Debug".
**Success conditions (`AiDebugCommandsTests`, fake registry/session):**
- `Continue_Enabled_WhenActiveSessionPaused_Else_Disabled`.
- `Continue_Invoke_CallsActiveSessionContinue` (and Step*/Pause).
- `StepBack_PresentOnly_WhenActiveSessionIsBlueprint`.
- `Group_Works_ForNonBlueprintSession` (BTree/HSM fake → common commands enabled).

---

## Phase 4 — Generic Asset Browser Panel — §10.1–§10.2, §18.1

### MTB-P4-T1 — `FolderTreePicker` (read mode)
**Scope:** Pure tree-builder from a list of relative paths (§18.1 read mode).
**Success conditions (`FolderTreePickerTests`):**
- `Build_NestedPaths_ProducesCorrectHierarchy`.
- `Build_EmptyAndRootLevelLeaves_Handled`.
- `Build_IsStable_Sorted`.

### MTB-P4-T2 — `BaseFolder` seam + relative-path helper
**Scope:** Add `string? BaseFolder` to `IAssetCatalogContributor`; implement on file
contributors (= `AssetRoots.AssetsFor(Kind)`); helper computes asset relpath (file:
`SourceFilePath` minus base; non-file: `Name`) (§10.2).
**Success conditions (`AssetRelPathTests`):**
- `FileAsset_RelPath_IsSourceMinusBase`.
- `ScenarioAsset_RelPath_IsName`.
- `Contributor_BaseFolder_MatchesAssetRoot`.

### MTB-P4-T3 — `AssetBrowserPanel` tabs + per-kind tree + row icons
**Scope:** Panel model (logic separate from draw): tabs per permitted kind, per-kind folder
tree, row carries kind `IconKey` (§10.1). `AssetActivated` event + `Selection`.
**Success conditions (`AssetBrowserPanelTests`):**
- `Tabs_ReflectKindFilter`.
- `PerKindTree_GroupsAssetsByRelPath`.
- `Row_CarriesKindIconKey`.
- `DoubleClick_RaisesAssetActivated_WithAsset` (logic-level).

### MTB-P4-T4 — "All" tab (flat + chips) + incremental filter
**Scope:** All tab: flat list + kind chips + incremental case-insensitive name filter present
in every tab (§10.1).
**Success conditions:**
- `Filter_Substring_CaseInsensitive_PrunesTreeAndList`.
- `AllTab_Chips_ToggleKindVisibility`.
- `AllTab_NoTree_FlatListOnly`.

### MTB-P4-T5 — Auto-expand/select + last-opened-per-kind
**Scope:** `InitialKind`/`InitialFullPath` expand+select; persist last-activated relpath per
kind in editor session prefs (§10.1).
**Success conditions:**
- `InitialFullPath_ExpandsAncestors_AndSelectsLeaf`.
- `LastOpened_PersistsAndRestores_PerKind`.

---

## Phase 5 — Hosts, Scenarios, Typed Change, Wiring — §10.3–§10.5, §19

### MTB-P5-T1 — Typed `IAssetCatalog.Changed` + ReferenceCatalog skip
**Scope:** Change `Changed` to carry the changed `AssetKind` (e.g. `event Action<AssetKind>?`
or args type); update all subscribers; `ReferenceCatalog.OnCatalogChanged` **ignores
`AssetKind.Scenario`** (§10.4).
**Success conditions (`ReferenceCatalogTests`):**
- `ScenarioChange_DoesNotRebuild_References` (elements/refs unchanged; no contributor walk).
- `NonScenarioChange_Rebuilds` (existing behavior preserved).
- All existing subscribers compile/pass against the new signature.

### MTB-P5-T2 — `AssetKind.Scenario` + `ScenarioCatalogContributor`
**Scope:** Add `Scenario` to `AssetKind`; contributor (editor-host assembly) projecting
available scenarios → `IEditableAsset` (`Name`=relpath, `SourceFilePath`=empty,
`IsEditorOwned=false`) (§10.4).
**Success conditions (`ScenarioContributorTests`):**
- `Kind_IsScenario`.
- `Enumerate_OneAssetPerScenario_NameIsRelPath`.
- `ContributorChanged_FiresOnListChange`.

### MTB-P5-T3 — Modal picker host
**Scope:** Modal hosting `AssetBrowserPanel` with `AssetKindFilter` + `Action<IEditableAsset?>`
callback (§10.3). Activate→close+callback(asset); Esc→callback(null). Never executes.
**Success conditions (`AssetPickerModalTests`):**
- `Activate_ClosesAndInvokesCallback_WithAsset`.
- `Escape_InvokesCallback_WithNull`.
- `NeverCalls_DocumentManager_Or_Load` (no side effects).

### MTB-P5-T4 — Docked window host
**Scope:** `ManagedWindow` hosting the same panel; registered in the window registry; on
`AssetActivated` invokes the registrant's callback; stays open (§10.3).
**Success conditions (`AssetBrowserDockedWindowTests`):**
- `Registered_WithExpectedId_AndScope`.
- `Activate_InvokesCallback_WindowStaysOpen`.

### MTB-P5-T5 — Scenario nested-name support
**Scope:** Treat scenario name as a relative path; `SaveScenarioAs`/`SaveCurrentScenario`
write `ScenariosRoot/<relpath>/scenario.json`; `AvailableScenarios` enumerates nested relpaths
(§19).
**Success conditions (`ScenarioNestedNameTests`):**
- `SaveAs_NestedName_CreatesNestedFolder` (`Combat/Patrol/scenario.json`).
- `AvailableScenarios_ReturnsNestedRelPaths`.
- `RoundTrip_SaveThenEnumerate_MatchesName`.

### MTB-P5-T6 — Caller wiring (pick → action)
**Scope:** Wire pick callbacks: file kinds → `AiDocumentManager.Open`; scenario →
`IEditorLogic.LoadScenarioByName` (§10.5).
**Success conditions (integration):**
- `Pick_FileAsset_OpensDocument`.
- `Pick_Scenario_CallsLoadScenarioByName_WithRelPath`.

---

## Phase 6 — Unified Creation & Recipes — §17, §18

### MTB-P6-T1 — Kind-agnostic `RecipeMetadata`
**Scope:** Lift `RecipeMetadata` into shared editor infra (§17); adapt blueprint recipe code to
the shared type.
**Success conditions:**
- `RecipeMetadataTests.SharedType_HasAllFields`.
- Existing blueprint recipe discovery/tests still pass against the shared type.

### MTB-P6-T2 — `INewAssetService` + Blueprint impl + hardcoded "Empty"
**Scope:** Define `INewAssetService` (`CreateNew(recipeOrEmpty, name, relPath) → IEditableAsset`
with fresh `AssetId`); Blueprint impl wrapping `NewFromRecipeService`; in-code **"Empty"**
recipe per §17 (no on-disk JSON).
**Success conditions (`NewAssetServiceTests`):**
- `CreateNew_MintsFreshAssetId`.
- `Empty_ProducesMinimalValidBlueprint_InCode` (no disk read).
- `CreateNew_FromRecipe_ClonesContent_NewIdentity`.

### MTB-P6-T3 — BTree/HSM/Scenario `INewAssetService` impls
**Scope:** Implementations minting + writing via JSON services (BTree/HSM) or `IEditorLogic`
(Scenario) (§18.3).
**Success conditions:**
- `BTreeNewAssetTests.Create_WritesValidJson_UnderAssetsRoot_FreshId` (round-trip load).
- `HsmNewAssetTests.Create_WritesValidJson_FreshId`.
- `ScenarioNewAssetTests.Create_Empty_NewWorld` / `_FromSeed_LoadsSeedThenSaveAs`.

### MTB-P6-T4 — `FolderTreePicker` pick mode
**Scope:** Add pick mode to the §18.1 widget: select existing folder / add new folder; returns
relpath; bounded to root.
**Success conditions (`FolderTreePickerPickTests`):**
- `AddFolder_CreatesNode_ReturnsRelPath`.
- `Selection_ReturnsRelPathRelativeToRoot`.
- `CannotEscapeRoot` (no `..` traversal).

### MTB-P6-T5 — New Asset dialog
**Scope:** Dialog: kind + recipe(incl. Empty) + name + `FolderTreePicker`(pick) + collision
guard → mint → save under `Assets/<Kind>/<relpath>` → hand new asset to caller (§18.2).
**Success conditions (`NewAssetDialogTests`):**
- `Confirm_WritesFile_AtAssetsRootRelPath_WithFreshId`.
- `CollisionGuard_RejectsExistingBaseName`.
- `Callback_ReceivesNewAsset`.

### MTB-P6-T6 — Save-As dialog (fresh-id duplicate semantics)
**Scope:** Dialog over current document: name + folder + guard → **mint fresh `AssetId`** →
write (§18.5). `Save` empty-path routes here.
**Success conditions (`SaveAsDialogTests`):**
- `SaveAs_WritesNewFile_WithFreshAssetId` (≠ source id).
- `SaveAs_RespectsPickedRelPath`.
- `CollisionGuard_RejectsExistingBaseName`.
- `EmptySourcePathSave_RoutesToSaveAs` (cross-check with MTB-P2-T4).

### MTB-P6-T7 — Subfolder-aware file save
**Scope:** Make `Save` write under the asset's relpath subfolder beneath the Assets root (§18.4).
**Success conditions:**
- `Save_PreservesSubfolder_RoundTrip` (save then recursive scan finds it at the same relpath).

---

## Phase 7 — Scenario Menu, Workspace, Retirement — §12, §10.6

### MTB-P7-T1 — Scenario lifecycle menu commands
**Scope:** Shell commands New/Save/Save As/Load/Migration History as **Scenario** menu items
(§12.1). Load opens the scenario-filtered picker; Migration History shows sidecars.
**Success conditions (`ScenarioMenuTests`):**
- `MenuItems_Registered_UnderScenario`.
- `New/Save/SaveAs_Invoke_EditorLogic`.
- `Load_OpensScenarioFilteredModal` (`Kinds == Scenario`).
- `MigrationHistory_ListsSidecars_ForLoadedScenario`.

### MTB-P7-T2 — Delete `ScenarioBrowserPanel`
**Scope:** Remove the button-based `ScenarioBrowserPanel` + its registration/usages (logic now
in menu commands).
**Success conditions:**
- Type removed; no references remain; build + suite green.

### MTB-P7-T3 — Workspace dynamic submenu
**Scope:** Read-only submenu aggregating `AiDocumentManager.OpenDocuments` + the loaded scenario,
each with kind icon; selecting a doc activates it (§12.2).
**Success conditions (`WorkspaceMenuTests`):**
- `Lists_OpenDocuments_AndLoadedScenario`.
- `SelectDocument_CallsActivate`.
- `RebuiltFromLiveState_EachBuild`.

### MTB-P7-T4 — Retire AiShared `AssetBrowserWindow`
**Scope:** Remove `Hrot.Editor.AiShared/Windows/AssetBrowserWindow.cs`; register the docked
host (MTB-P5-T4) in its place (§10.6). Open-docs section now lives in the Workspace submenu.
**Success conditions:**
- Old window type removed; docked host registered with the prior id/scope; build + suite green.

### MTB-P7-T5 — Retire Blueprints `AssetBrowserWindow` + `FileSystemAssetCatalog`
**Scope:** Remove the legacy Blueprints `AssetBrowserWindow` + `FileSystemAssetCatalog` + its
`IAssetCatalog` (scan logic already in contributors) (§10.6).
**Success conditions:**
- Types removed; no references; build + suite green.

---

## Cross-cutting acceptance

- The full test suite passes without `BLUEPRINT_REGENERATE_SNAPSHOTS` set.
- No legacy/assembly-loading code removed (only the items explicitly named in Phase 7).
- Each phase builds green before the next begins.
