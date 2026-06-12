# BATCH-54 REPORT — consolidate scenario menu into File → Scenario submenu (BUG-A21)

**Date:** 2026-06-12
**Branch:** blueprint-integ-1
**Model:** pro (Zoo)

## Changes Made

### 1. `ScenarioMenuCommands.cs` — MenuPrefix + re-add save/saveAs + rename + always-enabled

- **MenuPrefix** changed from `"Scenario"` to `"File/Scenario"` — all 5 items now land under **File → Scenario** submenu (3-level trie path).
- **Re-added** `scenario.save` and `scenario.saveAs` registrations (removed in BATCH-53), with always-enabled:
  - `scenario.save`: handler falls back to Save-As dialog when no scenario is loaded (`string.IsNullOrEmpty(LoadedScenarioName)`), else calls `SaveCurrentScenario()`.
  - `scenario.saveAs`: always opens Save-As dialog → `SaveScenarioAs(name)`.
- **Renamed all 5 display names** to read naturally inside a generic File menu:
  - `"New"` → `"New Scenario"`
  - `"Load…"` → `"Load Scenario…"`
  - (new) `"Save Scenario"`
  - (new) `"Save Scenario As…"`
  - `"Migration History…"` stays (no change)
- **All 5 `isEnabled: () => true`** — including Migration History (previously gated on `!string.IsNullOrEmpty(LoadedScenarioName)`). Handler already no-ops gracefully when no scenario is loaded.
- **Registration order**: New, Load, Save, Save As, Migration History.
- Updated class/method doc comments to reflect the 5-command, File→Scenario structure and the last-registration-wins override of ShellSaveCommands' gated versions.

### 2. `EditorSubsystem.cs` — removed gated File-menu surfacing

Deleted the two `if`-blocks (previously lines ~3235-3241) that registered `scenario.save` / `scenario.saveAs` at `"File/Save Scenario"` and `"File/Save Scenario As…"` via `MenuCommandAdapter`. These were the **disabled** items on the File menu — now fully replaced by the always-enabled File → Scenario submenu.

The `ScenarioMenuCommands.Register(...)` call site is unchanged — its new `MenuPrefix` handles the relocation automatically.

### 3. `ScenarioMenuTests.cs` — updated to File → Scenario submenu structure

- **`MenuItems_Registered_UnderFileScenario`** (renamed from `_UnderScenario`): navigates `File` → `Scenario`, asserts 5 children (`New Scenario`, `Load Scenario`, `Save Scenario`, `Save Scenario As`, `Migration History`).
- **`FiveCommands_Registered_InCommandSet`** (renamed from `ThreeCommands_`): asserts 5 registrations, includes `SaveId` and `SaveAsId`.
- **Re-added save/saveAs tests**:
  - `Save_WithLoadedScenario_CallsSaveCurrentScenario` — invokes `SaveId` with loaded scenario → `SaveCurrentScenario()` called, no save-as.
  - `Save_WithoutLoadedScenario_OpensSaveAsDialog` — invokes `SaveId` with null `LoadedScenarioName` → falls back to save-as dialog → `SaveScenarioAs("NewName")`.
  - `SaveAs_OpensSaveAsDialog_AndCallsSaveScenarioAs` — invokes `SaveAsId` → save-as dialog → `SaveScenarioAs("RenamedScenario")`.
- **`AllFiveCommands_AreEnabled`** — asserts all 5 descriptors have `IsEnabled() == true` regardless of `LoadedScenarioName`.
- **`AllFiveCommands_AreEnabled_WhenScenarioLoaded`** — same assertion with a loaded scenario.
- **Removed** `MigrationHistory_DisabledWhenNoScenarioLoaded` / `MigrationHistory_EnabledWhenScenarioLoaded` — replaced by the combined `AllFiveCommands_AreEnabled` tests.
- **`New_MenuItem_OnClick_InvokesCommand`** — updated menu path to `File → Scenario → New Scenario`.
- All existing Load, Migration History, BATCH-26 unified-modal, and edge-case tests preserved unchanged.

### No Touch
- `ShellSaveCommands.cs` — untouched. Last-registration-wins handles the override: `ScenarioMenuCommands.Register` runs after `ShellSaveCommands.Register` in `EditorSubsystem`, so the always-enabled descriptors win.
- `SaveCommandsTests.cs` — untouched. Scenario save tests live in `ScenarioMenuTests.cs`.

## Build & Test

```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
  Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
  Passed! Failed: 0, Passed: 185, Skipped: 0, Total: 185
```

(Note: initial build failed on stale generated code in Hrot.AI.Behaviors — resolved with `--no-incremental` clean rebuild; error was pre-existing and unrelated to this batch.)

## Definition of Done

- [x] No top-level Scenario menu (MenuPrefix changed to `File/Scenario`)
- [x] File → Scenario submenu with 5 always-enabled items: New Scenario, Load Scenario, Save Scenario, Save Scenario As, Migration History
- [x] Old disabled File/Save Scenario and File/Save Scenario As items removed from EditorSubsystem
- [x] Ctrl+S unchanged (ShellSaveCommands independent)
- [x] Build: 0 warnings, 0 errors
- [x] Tests: 185 passed, 0 failed
