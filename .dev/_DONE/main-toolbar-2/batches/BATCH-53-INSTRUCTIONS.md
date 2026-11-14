# BATCH-53 — retire dead classes (DBT-A1) + duplicate Scenario-menu Save/Save-As (DBT-A2)

**Model: pro (Zoo).** Do NOT use codebase-memory tooling. **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`.
User approved retirement (2026-06-12). Two independent cleanups. **Build everything touched with 0 warnings and keep
ALL affected test suites `Failed: 0`** (adjust assertions to the intentional removal — do NOT weaken/skip/delete
tests beyond what these removals require).

## Part 1 — DBT-A1: delete the dead modal classes
**DELETE these files** (their production wiring is already gone):
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Windows/RecipeCreateModal.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetNameFolderModal.cs`
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Browser/AssetNameFolderModalTests.cs`

**DO NOT DELETE `NewFromRecipeService`** — it is STILL LIVE: `BlueprintNewAssetService.cs:16` uses
`new NewFromRecipeService()`, and `WhenNodeEditorSmokeTest.cs` + `NewFromRecipeServiceTests.cs` exercise it. Leave
`NewFromRecipeService.cs` and its tests untouched.

Steps:
1. Before deleting, `grep` the whole repo (production `.cs` only, ignore `.dev/` and `docs/`) for `RecipeCreateModal`
   and `AssetNameFolderModal`. The ONLY production reference should be the stale COMMENT at
   `EditorSubsystem.cs:~2122` ("RecipeCreateModal production wiring removed; class + tests kept."). If you find any
   real code reference (field/using/call), STOP and report — do not force the delete.
2. Delete the three files above.
3. Update the `EditorSubsystem.cs:~2122` comment to reflect that the class is now deleted (e.g.
   "(MTB2-T7: legacy RecipeCreateModal removed.)") — keep it a comment, change nothing else.
4. If a `using` for the deleted `AssetNameFolderModal` namespace is now unused in `EditorSubsystem.cs`, leave it (it's
   a shared namespace) unless it produces a warning — only remove it if it causes an unused-using warning.

## Part 2 — DBT-A2: remove the duplicate Scenario-menu Save / Save As
The Scenario menu's **Save** and **Save As…** duplicate the unified **File → Save / Save As** (which already handles
scenarios via the `shell.save` scenario branch in `EditorSubsystem`). `ScenarioMenuCommands` AND `ShellSaveCommands`
both register the `scenario.save`/`scenario.saveAs` IDs into the same command set today (a latent duplicate);
removing them from `ScenarioMenuCommands` leaves `ShellSaveCommands`' versions as the sole registration.

In `Hrot/Subsystems/Hrot.Editor/ScenarioMenuCommands.cs`:
1. **Remove the `scenario.save` and `scenario.saveAs` registration blocks** in `Register(...)` (the two
   `RegisterCommand(... SaveId ...)` and `(... SaveAsId ...)` calls). KEEP `scenario.new`, `scenario.load`,
   `scenario.migrationHistory`. KEEP the `SaveId`/`SaveAsId` const declarations (other code references the strings;
   removing the const would break references) — they're simply no longer registered here.
2. The `openSaveAsDialog` constructor param is now unused by this class IF nothing else uses it — check: if after
   removing Save/SaveAs nothing references `openSaveAsDialog`, you may keep the parameter (to avoid changing the
   `EditorSubsystem` call site) but it will be unused. Prefer: KEEP the parameter signature unchanged (do NOT change
   the public Register signature or the EditorSubsystem call site) to minimize blast radius; if the unused parameter
   triggers a warning, suppress by referencing it in a harmless way is NOT allowed — instead, if it warns, report and
   we will decide. (Unused method PARAMETERS do not produce C# warnings by default, so this should be fine as-is.)

3. **Update `Hrot/Subsystems/Hrot.Editor.Tests/ScenarioMenuTests.cs`** to the new reality:
   - The Scenario menu now has **three** sub-items: New, Load, Migration History (was five). Update the count and the
     `ContainsKey("Save")` / `ContainsKey("Save As")` assertions (remove them).
   - Remove the tests that assert `ScenarioMenuCommands` registers/invokes `scenario.save` / `scenario.saveAs`
     (the `commands.Get(SaveId)` / `Invoke(SaveId)` / `Invoke(SaveAsId)` cases). Keep New/Load/MigrationHistory tests.
4. **Check `Hrot/Subsystems/Hrot.Editor.Tests/IntegrationTests/EditorFileOpsIntegrationTests.cs`** (it references
   `scenario.save`): if it invokes `scenario.save` and relies on `ShellSaveCommands` having registered it, it stays
   green; if it relied on `ScenarioMenuCommands` registering it AND that test does NOT wire `ShellSaveCommands`,
   adjust the test to invoke scenario save the way production now does (via `shell.save` in the scenario context, or
   via `ShellSaveCommands`' `scenario.save`). Keep it a real, meaningful test — do not stub. If the right fix is
   unclear, STOP and report rather than guessing.

## Build & test (no BLUEPRINT_REGENERATE_SNAPSHOTS)
Build + test every affected project:
```
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Hrot.Blueprints.Editor.csproj
dotnet build Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
dotnet test  Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj
dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
dotnet test  Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName~NewFromRecipeService|FullyQualifiedName~WhenNodeEditorSmokeTest"
```
All `Failed: 0` (Blueprints full suite stays at the ~9 PRE-1 baseline — no NEW failures); builds 0 warnings.

## Definition of done
- `RecipeCreateModal.cs`, `AssetNameFolderModal.cs`, `AssetNameFolderModalTests.cs` deleted; `NewFromRecipeService`
  kept + green. Scenario menu shows only New / Load / Migration History (Save/Save-As removed); `ScenarioMenuTests`
  updated + green; File→Save still saves scenarios (unchanged, verified by reading the `shell.save` scenario branch).
  Builds 0 warnings; all listed suites `Failed: 0`.
- Write `.dev/_DONE/main-toolbar-2/reports/BATCH-53-REPORT.md`: files deleted, the ScenarioMenuCommands edit, the test
  updates, the grep results proving no dangling production refs, build/test summaries.

If something cannot be done as specified, STOP and report why rather than stubbing.
