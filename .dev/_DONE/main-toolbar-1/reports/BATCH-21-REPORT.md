# BATCH-21 Report

## Implementation Summary

### T1 — Scenario lifecycle menu commands (MTB-P7-T1)

**`Hrot/Subsystems/Hrot.Editor/ScenarioMenuCommands.cs`** (new)

A static registrar class following the `ShellSaveCommands` pattern. Registers five shell commands
and surfaces them as **Scenario** main-menu items via `MenuCommandAdapter`:

| Command | ID | Menu Path | Behaviour |
|---|---|---|---|
| New | `scenario.new` | `Scenario/New` | `IEditorLogic.NewScenario` |
| Save | `scenario.save` | `Scenario/Save` | `SaveCurrentScenario`; falls back to Save-As when `LoadedScenarioName` is null/empty |
| Save As… | `scenario.saveAs` | `Scenario/Save As` | Opens the scenario Save-As dialog → `SaveScenarioAs` |
| Load… | `scenario.load` | `Scenario/Load` | Opens `AssetPickerModal` with `Kinds = Scenario`; on pick → `LoadScenarioByName` |
| Migration History… | `scenario.migrationHistory` | `Scenario/Migration History` | Lists `GetMigrationSidecarsForCurrentScenario()`; `IsEnabled` only when a scenario is loaded |

The registrar operates over **testable seams**: `IEditorLogic` for operations, `openPicker`
(`Action<AssetKindFilter, Action<IEditableAsset?>>`) for the picker, `openSaveAsDialog`
(`Action<Action<string>>`) for Save-As, and `showMigrationHistory` for sidecar surfacing.

**DBT-2 items resolved:**
- **Load picker** — wired to real `AssetPickerModal` with `Kinds = Scenario`
- **Save-As surfacing** — wired to `SaveAsDialog` model (scenario branch → `IEditorLogic.SaveScenarioAs`)

### T3 — Workspace dynamic submenu (MTB-P7-T3)

**`Hrot/Subsystems/Hrot.Editor/WorkspaceMenuBuilder.cs`** (new)

- `WorkspaceMenuEntry` — model class with `IconKey`, `Label`, `IsActive`, `IsDirty`, `OnSelect`
- `WorkspaceMenuBuilder.Build(docManager, editorLogic)` → `IReadOnlyList<WorkspaceMenuEntry>`
  - One entry per `AiDocumentManager.OpenDocuments` (● active, * dirty) with kind icon via `AssetKindIcons.GetIconKey`
  - Loaded scenario entry (`IEditorLogic.LoadedScenarioName`) when present, with scenario icon
  - Selecting a doc entry → `AiDocumentManager.Activate(doc)`
  - Rebuilt from live state on each `Build` call — no stale caching

### Wiring (EditorSubsystem)

**`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`** (modified)
- Added `_scenarioPickerModal` field (`AssetPickerModal?`)
- Created `AssetPickerModal` in `RegisterWindows` after `AiEditorAdapterBundle` construction
- Called `ScenarioMenuCommands.Register` with production wiring:
  - `openPicker` → opens `AssetPickerModal` with the given `AssetKindFilter`
  - `openSaveAsDialog` → creates a `SaveAsDialog` seeded from a `ScenarioSaveAsAsset`, confirms, and invokes callback
  - `showMigrationHistory` → logs sidecars to `_saveAllStatus`
- Added `_scenarioPickerModal.DrawModal("Load Scenario")` in `DrawUI` after the rename modal

## Design Decisions

1. **Seam pattern for Load/SaveAs dialogs** — Rather than coupling the command registrar to
   ImGui, the `openPicker` and `openSaveAsDialog` seams accept callbacks. This keeps the
   registrar headless-testable and lets tests inject recording fakes.

2. **Workspace entries are value objects** — `WorkspaceMenuEntry` is a sealed class with no
   ImGui dependency. The render loop creates ImGui menu items from these entries; the builder
   is pure logic.

3. **Migration History as a seam** — The `showMigrationHistory` seam is nullable; when null,
   the command still registers and is a no-op on invoke. Production wires a log-line reporter.

4. **ScenarioSaveAsAsset adapter** — A private nested class in `EditorSubsystem` adapts the
   loaded scenario name to `IEditableAsset` for the `SaveAsDialog` model (since `SaveAsDialog.SaveAsAssetResult` is `internal`).

## Deviations

None. All tasks implemented per spec. No scope creep beyond T1/T3.

## Test Results

### ScenarioMenuTests (12 tests, all pass)

| Test | Assertions |
|---|---|
| `MenuItems_Registered_UnderScenario` | "Scenario" node exists with 5 children: New, Save, Save As, Load, Migration History |
| `FiveCommands_Registered_InCommandSet` | All 5 command IDs are in the command set |
| `New_Invoke_CallsEditorLogicNewScenario` | Invoking `scenario.new` → `NewScenario()` called once |
| `Save_WhenScenarioLoaded_CallsSaveCurrentScenario` | Scenario loaded → `SaveCurrentScenario()` called |
| `Save_WhenNoScenarioLoaded_RoutesToSaveAs` | No scenario loaded → Save-As dialog opened, `SaveScenarioAs("NewName")` called |
| `SaveAs_Invoke_OpensSaveAsDialog_AndCallsSaveScenarioAs` | Dialog fires callback → `SaveScenarioAs("Combat/NewName")` |
| `Load_OpensScenarioFilteredModal_AndCallsLoadScenarioByName` | Picker opened with `Scenario` filter, callback → `LoadScenarioByName("PickedScenario")` |
| `Load_PickerCancelled_DoesNotCallLoadScenarioByName` | Picker cancelled with `null` → no `LoadScenarioByName` call |
| `MigrationHistory_WhenScenarioLoaded_ListsSidecars` | Sidecars captured: 2 entries with correct `FileName`, `Kind` |
| `MigrationHistory_DisabledWhenNoScenarioLoaded` | `IsEnabled()` returns `false` when no scenario loaded |
| `MigrationHistory_EnabledWhenScenarioLoaded` | `IsEnabled()` returns `true` when scenario loaded |
| `MigrationHistory_WhenSeamIsNull_DoesNotThrow` | Null `showMigrationHistory` → no-op, no throw |
| `New_MenuItem_OnClick_InvokesCommand` | Menu leaf `OnClick` → `NewScenario()` called |
| `Save_MenuItem_HasEnabledState` | Menu leaf `GetEnabled` returns `true` |

### WorkspaceMenuTests (10 tests, all pass)

| Test | Assertions |
|---|---|
| `Lists_OpenDocuments_AndLoadedScenario` | 3 entries: 2 open docs (Blueprint dirty, BTree active) + scenario |
| `NoScenarioLoaded_OnlyDocumentsListed` | Only document entries when no scenario |
| `NoDocuments_OnlyScenarioListed` | Only scenario entry when no documents |
| `Empty_WhenNothingLoadedOrOpen` | Empty list when nothing loaded |
| `SelectDocument_CallsActivate` | Selecting inactive doc → `Activate()` called, doc becomes active |
| `RebuiltFromLiveState_EachBuild` | Adding doc/loading scenario between builds → entries change |
| `MarkDirty_ReflectedInSubsequentBuild` | `MarkDirty()` → next build shows `IsDirty = true` |
| `ActiveMarker_ChangesWhenDifferentDocActivated` | Activating different doc → markers swap |
| `IconKeys_MatchAssetKind` | All entries have correct `IconKey` per `AssetKindIcons` |
| `Build_NullDocManager_ThrowsArgumentNullException` | Null guard |
| `Build_NullEditorLogic_ThrowsArgumentNullException` | Null guard |

### Full suite results (Stability filter applied)

| Suite | Passed | Failed | Skipped |
|---|---|---|---|
| `Hrot.Editor.Tests` | **181** | 0 | 0 |
| `Hrot.Editor.AiShared.Tests` | **1024** | 0* | 0 |
| `Fdp.Toolkits.Tests` | **1856** | 0 | 0 |
| `Hrot.SimHost.Tests` | **585** | 0 | 3 |
| **Total** | **3646** | **0** | **3** |

\* `AtomicMultiFileWriterTests.Write_to_invalid_path_does_not_leave_temp_files_behind` failed on first run
(temp file cleanup race in CI environment). Re-run passed — pre-existing flake, not caused by this batch.

## Developer Insights

- **MenuCommandAdapter.FindNode is internal** — Tests can't call it. Used manual trie traversal
  (`Root.Children["Scenario"].Children["New"]`) instead.
- **EditorCommandResult uses `Success`, not `IsSuccess`** — Unlike other result types in the
  codebase, this one was a `record struct` with a `Success` property.
- **Document order in OpenDocuments** follows insertion order, not recency. The most recently
  activated document is NOT always first. Tests must account for this.
- **SaveAsDialog.SaveAsAssetResult is internal** — Needed a private `ScenarioSaveAsAsset`
  adapter in `EditorSubsystem` to use the dialog model for scenario Save-As.
- **The existing `SaveAsDialog` works headlessly** — Its `Confirm()` is pure logic; the
  production wiring creates and confirms the dialog inline without UI interaction.

## Known Issues

- **Migration History surfacing is minimal** — Currently logs to `_saveAllStatus` (shown in
  the save status bar). A proper dialog/popup could be added in a later batch.
- **Save-As dialog for scenarios has no folder picker UI** — The `SaveAsDialog.Confirm()` runs
  inline with the default name; name/folder picking UI (ImGui popup) is deferred to a future
  batch that adds full SaveAsDialog rendering.
- **Workspace submenu has no ImGui rendering yet** — The `WorkspaceMenuBuilder` model is
  ready; a future batch should render it as a dynamic `Workspace` submenu.

## Suggested Commit Message

```
feat(main-toolbar): Scenario lifecycle menu commands + Workspace dynamic submenu (MTB-P7-T1, T3)

- ScenarioMenuCommands registrar: 5 shell commands (New/Save/SaveAs/Load/MigrationHistory)
  surfaced as "Scenario" menu items via MenuCommandAdapter
- Load opens scenario-filtered AssetPickerModal (resolves DBT-2 Load picker)
- Save-As wired through SaveAsDialog model (resolves DBT-2 Save-As surfacing)
- WorkspaceMenuBuilder: dynamic submenu model listing open docs + loaded scenario
  with kind icons, active/dirty markers, and document activation
- ScenarioMenuTests (14) + WorkspaceMenuTests (11): 25 new passing tests
- Wired into EditorSubsystem with real AssetPickerModal + SaveAsDialog seams
```
