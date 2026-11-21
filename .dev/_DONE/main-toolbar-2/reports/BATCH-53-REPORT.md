# BATCH-53-REPORT — retire dead classes (DBT-A1) + duplicate Scenario-menu Save/Save-As (DBT-A2)

**Date:** 2026-06-12 | **Model:** pro | **Status:** ✅ Complete

## DBT-A1: Delete dead modal classes

### Pre-deletion grep
- `RecipeCreateModal` in production `.cs` (excluding `.dev/`, `docs/`): only the file itself + stale comment at `EditorSubsystem.cs:2122`. No real code references.
- `AssetNameFolderModal` in production `.cs`: only the files to delete. No production references at all.

### Files deleted
1. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Windows/RecipeCreateModal.cs`
2. `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetNameFolderModal.cs`
3. `Hrot/Editor/Hrot.Editor.AiShared.Tests/Browser/AssetNameFolderModalTests.cs`

### Files kept (still live)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NewFromRecipeService.cs` — used by `BlueprintNewAssetService.cs:16`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/NewFromRecipeServiceTests.cs` — 9 tests, all green
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Integration/WhenNodeEditorSmokeTest.cs` — exercises `NewFromRecipeService`

### Comment update
- `EditorSubsystem.cs:2122`: `"(MTB2-T7: legacy RecipeCreateModal removed.)"` (was `"(MTB2-T7: RecipeCreateModal production wiring removed; class + tests kept.)"`)

## DBT-A2: Remove duplicate Scenario-menu Save / Save As

### ScenarioMenuCommands.cs changes
- Removed `scenario.save` and `scenario.saveAs` `RegisterCommand(...)` blocks from `Register()`.
- Kept `SaveId` / `SaveAsId` const declarations (other code references them).
- Kept `openSaveAsDialog` parameter in `Register()` signature (now unused by body, but C# does not warn on unused method parameters; call site in `EditorSubsystem` unchanged).
- Updated XML doc comments: "five" → "three" shell commands; added note that save/saveAs are registered by `ShellSaveCommands`.
- Remaining registrations: `scenario.new`, `scenario.load`, `scenario.migrationHistory`.

### ScenarioMenuTests.cs changes
- `MenuItems_Registered_UnderScenario`: 5 → 3 items (New, Load, Migration History). Removed Save/SaveAs `ContainsKey` assertions.
- `FiveCommands_Registered_InCommandSet` → `ThreeCommands_Registered_InCommandSet`: 5 → 3, removed SaveId/SaveAsId assertions.
- Removed 4 test methods:
  - `Save_WhenScenarioLoaded_CallsSaveCurrentScenario`
  - `Save_WhenNoScenarioLoaded_RoutesToSaveAs`
  - `SaveAs_Invoke_OpensSaveAsDialog_AndCallsSaveScenarioAs`
  - `Save_MenuItem_HasEnabledState`
- All kept tests (New, Load, MigrationHistory, menu leaf OnClick, BATCH-26 unified modal) pass.

### EditorFileOpsIntegrationTests.cs
- No changes needed. All tests (`NewScenario_*`, `SaveScenario_*`, `LoadScenario_*`) test `IEditorLogic` facade methods directly — they never reference `ScenarioMenuCommands` or `scenario.save` command ID. Stayed green without modification.

## Build results (0 warnings)

| Project | Warnings | Errors |
|---|---|---|
| `Hrot.Blueprints.Editor` | 0 | 0 |
| `Hrot.Editor.AiShared` | 0 | 0 |
| `Hrot.Editor` | 0 | 0 |

## Test results (all Failed: 0)

| Test suite | Passed | Failed | Notes |
|---|---|---|---|
| `Hrot.Editor.AiShared.Tests` | 1080 | 0 | AssetNameFolderModalTests deleted; all remaining pass |
| `Hrot.Editor.Tests` | 182 | 0 | ScenarioMenuTests updated; EditorFileOpsIntegrationTests unchanged |
| `Hrot.Blueprints.Tests` (filtered: NewFromRecipeService + WhenNodeEditorSmokeTest) | 9 | 0 | NewFromRecipeService still live and green |

Blueprints full-suite PRE-1 baseline (~9 failures) unaffected — no NEW failures introduced.
