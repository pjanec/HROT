# BATCH-36 — MTB2-T7: `NewAssetLauncher` + File/New + New toolbar button (retire RecipeCreateModal wiring)

**Task:** MTB2-T7 (Item 1) · **Model:** pro · **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`
**Detail:** `.dev/main-toolbar-2/TASK-DETAIL.md` (`MTB2-T7`) · DECISIONS D-T7-1 · **Mirrors Phase-8 `AssetPickerLauncher`.**

## Onboarding (do NOT use codebase-memory tooling)
1. `.dev/.guides/DEV-GUIDE.md`. 2. This file. 3. **Mirror these:**
   `Hrot/Subsystems/Hrot.Editor/Browser/AssetPickerLauncher.cs` (+ its tests) and the BATCH-26/29 "Open Asset"
   command/menu/toolbar wiring in `EditorSubsystem.cs` (`shell.openAsset`).
4. `Hrot/Editor/Hrot.Editor.AiShared/Browser/RecipePickerSource.cs` (T6 — `BuildEntries`, `RecipeChoice`);
   `Hrot/Editor/Hrot.Editor.AiShared/Recipes/NewAssetDialog.cs` (`Kind`/`Recipe`/`Name`/`FolderPicker`/`CanConfirm`/
   `Confirm(onCreated)`); the existing scenario `SaveAsDialog.Confirm()` wiring (~EditorSubsystem L2356-2370) as the
   create-and-report pattern.

## ⚙️ RULES (non-negotiable)
1. Do this ONE objective only. Touch ONLY the files listed. No drive-by edits/renames.
2. NEVER hide a problem to pass a build (no excluded assets/`[Skip]`/weakened tests/stubs/suppression/`#if false`).
3. Add the EXACT named tests; assert real behavior; fail if code is wrong.
4. DO NOT STOP until build = 0 warnings AND test commands show `Failed: 0` (no `BLUEPRINT_REGENERATE_SNAPSHOTS`).
5. Report exact files/tests + final summaries. No litter.

## Objective
A "New Asset" entry (toolbar button + File menu) opens the recipe **Tree picker** (`RecipePickerSource` via the
shell `_shellPickers` registry); on pick → create the asset from the chosen (kind, recipe) and open it. Retire the
blueprint-only `RecipeCreateModal` production wiring (keep the class).

---

## PART A — `NewAssetLauncher` (testable) + tests
**File (NEW):** `Hrot/Subsystems/Hrot.Editor/Browser/NewAssetLauncher.cs` (namespace `Hrot.Editor`)
```csharp
public sealed class NewAssetLauncher
{
    public NewAssetLauncher(
        Action<PickerRequest, Action<PickerResult>> openPicker,                       // = _shellPickers.OpenPicker
        IReadOnlyDictionary<AssetKind, Recipes.INewAssetService> services,
        Action<AssetKind, IEditableAsset> showNewAssetDialog,                          // pick -> open the New dialog/create
        Func<IEditableAsset, string?>? describe = null,
        Func<IEditableAsset, string?>? recipeCategory = null);

    public void Open();
}
```
`Open()`:
- build `new RecipePickerSource(services, describe, recipeCategory)`;
- build `PickerRequest { ContextKey = "assets.new", Title = "New Asset", Layout = PickerLayout.Tree,
  SelectionMode = Single, ItemsProvider = () => source.BuildEntries("", null) }`;
- `openPicker(request, result => { if (result.Cancelled) return; if (result.First?.Tag is RecipeChoice rc)
  showNewAssetDialog(rc.Kind, rc.Recipe); })`.
- Null-safe ctor (ArgumentNullException on null `openPicker`/`services`/`showNewAssetDialog`).

**Tests (NEW)** `Hrot/Subsystems/Hrot.Editor.Tests/Browser/NewAssetLauncherTests.cs` — copy the fake-openPicker +
`FakeCatalog`/fakes pattern from `AssetPickerLauncherTests.cs`; add a fake `INewAssetService` whose
`AvailableRecipes()` returns fakes incl "Empty".
- `Open_BuildsTreeRequest_FromRecipeSource` — captured `PickerRequest.Layout == Tree`, `SelectionMode == Single`;
  `ItemsProvider()` yields recipe entries (incl an "Empty") whose `Tag` is a `RecipeChoice`.
- `Open_Pick_InvokesNewAssetDialog_WithKindAndRecipe` — invoke the captured handler with a `PickerResult` whose entry
  `Tag` is `RecipeChoice(Blueprint, recipe)` → the `showNewAssetDialog` spy fired with `(Blueprint, recipe)`.
- `Open_Cancel_DoesNothing` — handler with a cancelled (empty) `PickerResult` → `showNewAssetDialog` NOT called.

---

## PART B — production wiring in `EditorSubsystem.cs`
1. **`shell.newAsset` command:** register (mirror `shell.openAsset`) — DisplayName "New Asset…", Category "File",
   `IconKey = "asset/new"`, `DefaultKey = Ctrl+N`, always enabled; handler → `newAssetLauncher?.Open()`.
2. **Construct the launcher** (after `_shellPickers` + `_newAssetServices` exist):
   `var newAssetLauncher = (_newAssetServices != null) ? new Hrot.Editor.NewAssetLauncher(openPicker:
   _shellPickers.OpenPicker, services: _newAssetServices, showNewAssetDialog: ShowNewAssetDialog) : null;`
   where `ShowNewAssetDialog(AssetKind kind, IEditableAsset recipe)` is a local function that **seeds + confirms a
   `NewAssetDialog`** (D-T7-1; interactive UI deferred → DBT-A3):
   - `var dlg = new NewAssetDialog(<same service-resolver/knownFolders/saveMintOnlyAsset args used by the existing
     SaveAsDialog wiring>);` set `dlg.Kind = kind; dlg.Recipe = recipe;`
   - default name: `dlg.Name = string.Equals(recipe.Name, "Empty", OrdinalIgnoreCase) ? $"New{kind}" : recipe.Name;`
   - if `dlg.CanConfirm()` → `var r = dlg.Confirm(onCreated: a => _aiDocumentManager?.Open(a));` set `_saveAllStatus`
     to a success/info message from `r` (mirror the scenario SaveAsDialog Confirm reporting).
   - Guard `_editorLogic`/`_newAssetServices` null-safely.
3. **Toolbar + menu:** `ToolbarCommandAdapter.Register(MainToolbar, ShellCommands, "shell.newAsset",
   toolbarIconProvider, sortOrder: -11)` (LEFT of Open Asset at -10), inside the null-safe `if (MainToolbar != null)`
   block; `MenuCommandAdapter.Register(GlobalMenu, ShellCommands, "shell.newAsset", "File/New Asset…")`.
4. **Retire `RecipeCreateModal` production wiring:** remove the `recipeModal` construction (~L2093) and the
   `_aiAssetBrowser.CustomToolbarDraw = () => { … recipeModal … }` hookup (~L2139). **Do NOT delete**
   `RecipeCreateModal.cs` / `NewFromRecipeService.cs` (keep the classes + their tests). If `CustomToolbarDraw` then
   has no other use, leaving it unset is correct.

Keep all wiring null-safe (bare-ctor `RegisterWindows` must not throw).

**Tests** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/EditorSubsystemBlueprintWindowsTests.cs`:
- `EditorSubsystem_RegisterWindows_RegistersNewAssetCommandAndEntries` — after `RegisterWindows`:
  `wm.ShellCommands.Get("shell.newAsset") != null` (DisplayName "New Asset…", DefaultKey Ctrl+N);
  `wm.MainToolbar.ContainsEntry("shell.newAsset")`; `wm.GlobalMenu.Root.Children["File"].Children` contains
  `"New Asset…"`.

## Build & test (no BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
dotnet test  Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName~EditorSubsystemBlueprintWindows"
```
`Hrot.Editor.Tests` `Failed: 0`; filtered guardrail `Failed: 0`. The FULL `Hrot.Blueprints.Tests` has ~9 known PRE-1
failures — introduce **no NEW** failure (retiring `RecipeCreateModal` wiring must not break blueprint creation tests;
the class stays).

## Definition of done
- `NewAssetLauncher` opens a Tree recipe picker; pick → `showNewAssetDialog(kind, recipe)`. Production creates+opens
  via `NewAssetDialog.Confirm` (default name; interactive UI = DBT-A3). `shell.newAsset` (Ctrl+N) + File/New Asset… +
  New toolbar button (sortOrder -11). `RecipeCreateModal` production wiring removed; class/tests kept.
- The 4 named tests pass (3 launcher + 1 guardrail); build 0 warnings; `Hrot.Editor.Tests` `Failed: 0`; filtered
  Blueprints guardrail `Failed: 0`; no new Blueprints failures.
- Write `.dev/main-toolbar-2/reports/BATCH-36-REPORT.md`: launcher shape, the showNewAssetDialog create wiring + the
  DBT-A3 deferral, what was retired (and confirmation the classes/tests remain), files/tests, final summaries.

If something cannot be done as specified, STOP and report why.
