# Main Toolbar 2 — Orchestration Decisions Log

Autonomous run (per `.dev/.guides/multi-batch-NON-interactive-ds.md` + user override: **no stopping after each batch,
fully autonomous**). Decisions made mid-run are recorded here; design decisions live in [DESIGN.md](./DESIGN.md)
(DEC-A1…A7) and [DEBT-TRACKER.md](./DEBT-TRACKER.md).

## Run mode
- **D-RUN-1 (2026-06-12):** user overrode the guide's per-task review gate — run all 7 tasks autonomously,
  hard-review + independently verify each, commit after each (after my verification), update tracker, continue.
  No pause for user review between tasks. Don't stop until all tasks + debt resolved.

## Pre-flight grounding (before BATCH-30)
- All task seams verified against source: T1 `IconWidgets` IconHandle overloads + `IconWidgetsTests`; T2 `shell.save`
  (IconKey `shell/save`, no atlas cell yet) + Open-Asset toolbar at sortOrder -10; T3 `EditorCommandDescriptor`
  (`NodeEditor.Core/Action/IEditorCommands.cs`) + `MenuCommandAdapter`/`ToolbarCommandAdapter`; T4 `ShellSaveCommands`
  seam-based + `IEditorLogic.SaveCurrentScenario/SaveScenarioAs/LoadedScenarioName` + `CurrentPerspective` + scenario
  Save-As path (`saveAsScenarioDelegate`/`openSaveAsDialog`); T5 perspective menu render
  (`WindowManager` L656 `Gui.MenuItem(perspective,…)`) + `BuildPerspectiveMenuModel`; T6 `INewAssetService.
  AvailableRecipes()` incl "Empty" + `_newAssetServices` (Blueprint/BTree/HSM/Scenario, EditorSubsystem L2299–2308);
  T7 `AssetPickerLauncher`/`NewAssetDialog`/`_shellPickers`/`RecipeCreateModal`/`asset/new` cell. **No blocking
  discrepancies.**

## Mid-run decisions
- **D-T3-1:** The menu item label is rendered from the menu **node's `Name`** (`Gui.MenuItem(child.Name, …)`,
  `WindowManager.cs` L537/L548/L656), NOT `descriptor.DisplayName`. So the dynamic Save label requires a
  **`Func<string>? DynamicLabel` on the menu node** (`GlobalMenuRegistry` `MenuItemNode`), rendered as
  `child.DynamicLabel?.Invoke() ?? child.Name`, with `MenuCommandAdapter` setting it from
  `descriptor.DynamicDisplayName`. TASK-DETAIL T3 refined accordingly. (The toolbar tooltip side still reads
  `DynamicDisplayName ?? DisplayName` directly.)
- **D-T2-1 (in-review fix, BATCH-31):** the Save-toolbar guardrail must assert the **entry exists**, but
  `MainToolbarManager.GetVisibleItemPlan` is `internal` with `InternalsVisibleTo` only for `Fdp.Presentation.Tests`
  (NOT `Hrot.Blueprints.Tests`), so the worker's test fell back to `Height > 0` (would pass even if Save were
  missing). Lead fix: added a small **public `MainToolbarManager.ContainsEntry(string id)`** accessor (entries only)
  and rewrote the guardrail to assert `ContainsEntry("shell.save")` + `ContainsEntry("shell.openAsset")`. Production
  Save registration (sortOrder -9) was correct; only the test was weak. Re-verified: build 0 warnings, icon test 3/3,
  guardrail 13/13.
- **D-T6-1 (BATCH-35):** recipe metadata (Category/Description) is NOT uniformly exposed on the `IEditableAsset`
  returned by `INewAssetService.AvailableRecipes()` (only kind-specific paths like `BlueprintAsset.EditorMetadata.Recipe`
  have it). So `RecipePickerSource` takes **injected delegates** `Func<IEditableAsset,string?>? describe` and
  `Func<IEditableAsset,string?>? recipeCategory` (both default null) — mirroring Phase-8 `AssetPickerSource.describe`.
  Default `Category = "<Kind>"`; with a `recipeCategory` → `"<Kind>/<sub>"`. Keeps the source headless-testable and
  unblocks T7 (production can pass null now; per-kind metadata mapping is a later enhancement).
- **D-T7-1 (BATCH-36):** there is **no generic `NewAssetDialog` ImGui renderer** (only the blueprint-only
  `RecipeCreateModal`; `NewAssetDialog` is a model). Building the interactive name/folder popup is the same
  deferred-UI class as the SaveAsDialog UI (main-toolbar-1 DBT-2). So T7 delivers: testable `NewAssetLauncher`
  (openPicker + services + `showNewAssetDialog` seam) + `shell.newAsset` command + File/New + New toolbar button +
  retire `RecipeCreateModal` production wiring (keep the class). Production `showNewAssetDialog` **seeds a
  `NewAssetDialog` and `Confirm()`s with a default name** (recipe name, or `New{Kind}` for the "Empty" recipe) →
  opens the created asset — a **functional** pick→create→open flow. The interactive name/folder popup is deferred
  as **DBT-A3**. Launcher is unit-tested up to the `showNewAssetDialog(kind, recipe)` boundary.
- **D-T8-2 (2026-06-12, supersedes the editor-specific modal): the name+folder dialog is GENERIC, in NodeEdit.**
  User: aim generic + ALWAYS reuse existing first. The BATCH-40 `AssetNameFolderModal` (in `Hrot.Editor.AiShared`)
  was the wrong layer. **Reuse search:** the picker already has `CategoryNode` + `PickerTreeBuilder` (pure tree model
  from T1), an auto-focused text box, Enter/Esc/OK-Cancel, and the once-per-frame `DrawFrame` host pattern
  (`AllowArbitraryTextInput` is declared-but-unimplemented; `MiniEditors` are pin editors). A "type a name + pick
  destination + create folder + return (name, dest)" interaction is NOT the picker's "select one of N", so it is a
  **generic sibling dialog in `NodeEditor.UI`** that REUSES `CategoryNode`/`PickerTreeBuilder` + the picker UX idioms.
  Neutral API: `Title`, name + **`Func<string,string?> ValidateName`** (null=OK, else error — covers the
  **must-not-already-exist** rule, host-driven per user), destination `CategoryNode`, create-folder callback,
  confirm→`(name, destPath)`. NO asset/editor types. Editor adapts: folders from `KnownSubfolders`, `ValidateName`
  from the dialog models (`NewAssetDialog.CanConfirm`/collision), create via `FolderPickerState.AddFolder`,
  confirm→create+catalog-refresh+open. Retire `AssetNameFolderModal`. The 5 runtime UX bugs (focus, Enter, ESC,
  New-Folder-as-button-popup, show-created-folder) are fixed IN the generic component; the create→open
  catalog-refresh (BUG-A1 root cause) stays editor-side. Batches: BATCH-41 = generic NodeEdit dialog (+demo+tests);
  BATCH-42 = editor adapter/wiring + retire the modal.
- **D-T8-3 (2026-06-12, user-chosen "proper (a)"):** New = **always from a recipe** (incl "Empty"); the recipe's
  output is an **in-memory** asset (not yet on disk). Flow: recipe **Tree picker** → `CreateNew(recipe, defaultName,
  "")` (default name, **empty SourceFilePath**) → **open** → first Ctrl+S → **Save-As** (empty path already routes
  there). Uniform with scenarios (which already do this natively via the map + `IEditorLogic`).
  **Enabler:** documents are file-backed (`*DocumentFactory.Build` only touches the file in `LoadAsset` =
  ReadAllText+Deserialize; everything after runs off the in-memory `BlueprintAsset`), so add an **open-from-in-memory**
  entry point to the 3 document factories (use the supplied in-memory asset instead of `LoadAsset`). **Rename is NOT
  supported** (Save-As mints a fresh AssetId = duplicate/copy, §18.5; no true asset-rename) — which is *why* proper
  (a) needs open-from-in-memory (no file exists until first Save-As; nothing to rename/clean). Batches: 41 = generic
  NodeEdit Save-As browser dialog; 42 = open-from-in-memory (3 factories); 43 = New/Save-As wiring + retire
  `AssetNameFolderModal`.
