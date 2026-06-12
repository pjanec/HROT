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
