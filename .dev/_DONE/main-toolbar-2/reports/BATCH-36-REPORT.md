# BATCH-36 — REPORT — MTB2-T7: NewAssetLauncher + File/New + New toolbar button

**Completed:** 2026-06-12 · **Model:** pro · **Repo:** `D:\Work\IOS-IG-SimHost-FDP`

---

## Implementation Summary

### PART A — `NewAssetLauncher` (testable) + tests

**NEW file:** `Hrot/Subsystems/Hrot.Editor/Browser/NewAssetLauncher.cs` (namespace `Hrot.Editor`)

- `NewAssetLauncher` mirrors `AssetPickerLauncher`'s injectable-seam pattern:
  - `openPicker`: `Action<PickerRequest, Action<PickerResult>>` — production = `_shellPickers.OpenPicker`
  - `services`: `IReadOnlyDictionary<AssetKind, INewAssetService>` — per-kind recipe registry
  - `showNewAssetDialog`: `Action<AssetKind, IEditableAsset>` — pick → create hook
  - `describe`/`recipeCategory`: optional delegates passed through to `RecipePickerSource`
- `Open()`: builds `RecipePickerSource` → Tree `PickerRequest` (`ContextKey = "assets.new"`, `Title = "New Asset"`) → on pick, extracts `RecipeChoice` Tag → `showNewAssetDialog(kind, recipe)`; cancel → nothing.
- Null-safe ctor: `ArgumentNullException` on null `openPicker`/`services`/`showNewAssetDialog`.

**NEW test file:** `Hrot/Subsystems/Hrot.Editor.Tests/Browser/NewAssetLauncherTests.cs` — 3 tests:

1. **`Open_BuildsTreeRequest_FromRecipeSource`** — captured `PickerRequest.Layout == Tree`, `SelectionMode == Single`; `ItemsProvider()` yields entries (incl "Empty") whose `Tag` is `RecipeChoice`.
2. **`Open_Pick_InvokesNewAssetDialog_WithKindAndRecipe`** — simulate pick with a `RecipeChoice(BTree, recipe)` → `showNewAssetDialog` spy received `(AssetKind.BTree, sameRecipe)`.
3. **`Open_Cancel_DoesNothing`** — cancelled `PickerResult` → `showNewAssetDialog` NOT called.

Uses fakes: `StubRecipe`, `FakeNewAssetService`, `FakeOpenPicker` (same pattern as `AssetPickerLauncherTests`).

### PART B — Production wiring in `EditorSubsystem.cs`

**Edited file:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

#### 1. Retired `RecipeCreateModal` production wiring
- Removed `recipeModal` construction ~L2093–2137 (the full `RecipeCreateModal` lambda + `NewFromRecipeService` + blueprint save + catalog refresh).
- Removed `_aiAssetBrowser.CustomToolbarDraw = () => { … recipeModal … }` hookup ~L2139–2146.
- **Classes kept:** `RecipeCreateModal.cs` and `NewFromRecipeService.cs` untouched.
- `CustomToolbarDraw` now left unset (had no other use).

#### 2. `ShowNewAssetDialog` local function + `NewAssetLauncher` construction
Added after `assetPickerLauncher` and before `saveAsScenarioDelegate`:
- `ShowNewAssetDialog(AssetKind kind, IEditableAsset recipe)` local function:
  - Seeds `NewAssetDialog(_newAssetServices, knownFolderPaths: empty, saveMintOnlyAsset: saveAsBlueprintToFile)`.
  - Sets `Kind`, `Recipe`, default `Name` (recipe name, or `New{Kind}` for "Empty").
  - If `CanConfirm()` → `Confirm(onCreated: a => _aiDocumentManager?.Open(a))`.
  - Reports success/failure to `_saveAllStatus`.
- `newAssetLauncher` constructed with `_shellPickers.OpenPicker`, `_newAssetServices`, `ShowNewAssetDialog`.
- Null-safe: `_newAssetServices != null` guard; handler uses `newAssetLauncher?.Open()`.

#### 3. `shell.newAsset` command (Ctrl+N)
- Registered in `ShellCommands`: `Id = "shell.newAsset"`, `DisplayName = "New Asset…"`, `Category = "File"`, `IconKey = "asset/new"`, `DefaultKey = Ctrl+N`, always enabled.
- Handler → `newAssetLauncher?.Open()`.

#### 4. Toolbar + menu
- Toolbar: `ToolbarCommandAdapter.Register` at `sortOrder: -11` (left of Open Asset at -10), inside the `if (MainToolbar != null)` block.
- Menu: `MenuCommandAdapter.Register` → `"File/New Asset…"`.

### Guardrail test

**Edited file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/EditorSubsystemBlueprintWindowsTests.cs`

**`EditorSubsystem_RegisterWindows_RegistersNewAssetCommandAndEntries`** — after `RegisterWindows`:
- `wm.ShellCommands.Get("shell.newAsset") != null`
- `DisplayName == "New Asset…"`
- `DefaultKey == Ctrl+N`
- `wm.MainToolbar.ContainsEntry("shell.newAsset")`
- `wm.GlobalMenu.Root.Children["File"].Children` contains `"New Asset…"`

---

## Files Changed

| File | Action | Lines |
|---|---|---|
| `Hrot/Subsystems/Hrot.Editor/Browser/NewAssetLauncher.cs` | **NEW** | 96 |
| `Hrot/Subsystems/Hrot.Editor.Tests/Browser/NewAssetLauncherTests.cs` | **NEW** | 170 |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | Edited | -55 (recipeModal removed), +48 (launcher+command+toolbar+menu) |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/EditorSubsystemBlueprintWindowsTests.cs` | Edited | +29 (guardrail test) |

**NOT deleted (as specified):**
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Windows/RecipeCreateModal.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NewFromRecipeService.cs`

---

## Test Results

### `dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj`
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj`
```
Passed!  - Failed: 0, Passed: 186, Skipped: 0, Total: 186, Duration: 733 ms
```

### `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName~EditorSubsystemBlueprintWindows"`
```
Passed!  - Failed: 0, Passed: 15, Skipped: 0, Total: 15, Duration: 2 s
```

All 15 Blueprints guardrail tests pass — no new failures (PRE-1 baseline unchanged).

---

## Design Decisions

- **D-T7-1:** Interactive name/folder picker for `NewAssetDialog` is deferred (DBT-A3). Production seeds a `NewAssetDialog` with a default name (recipe name, or `New{Kind}` for Empty) and `Confirm()`s immediately — a functional pick→create→open flow. The launcher's `showNewAssetDialog` seam accepts the interactive UI when DBT-A3 is implemented.
- **Launcher shape:** Mirrors `AssetPickerLauncher` — injects `openPicker` + services + action seam; headless-testable without ImGui or a live picker registry.
- **Default name logic:** `string.Equals(recipe.Name, "Empty", OrdinalIgnoreCase) ? $"New{kind}" : recipe.Name` — gives users a reasonable starting filename for each kind.

## Design Decisions I Made Beyond Spec

- **`ShowNewAssetDialog` as a local function:** Defined inline in `RegisterWindows` to capture `saveAsBlueprintToFile`, `_newAssetServices`, `_aiDocumentManager`, and `_saveAllStatus` without additional fields or wiring changes. Matches the existing `openScenarioSaveAs` / `requestSaveAs` local-function patterns already used in the file.
- **Status messages:** Mirror the existing Save-As confirm pattern (`_saveAllStatus = r.IsSuccess ? "[OK] Created new …" : "[INFO] New …: …"`).

---

## Challenges

None. The existing `AssetPickerLauncher` + `RecipePickerSource` + `NewAssetDialog` seams fit together cleanly; the spec was precise and all reference patterns were available in the codebase.

---

## Known Issues / Limitations

- The `NewAssetDialog` interactive name/folder UI is deferred (DBT-A3). The current flow creates with a default name and the kind's root folder; the user cannot customize the name or subfolder before creation.
- `ShowNewAssetDialog` passes `knownFolderPaths: Array.Empty<string>()` — the same as the existing Save-As wiring (which also defers folder-picker UI). When interactive UI lands, folder discovery can feed real paths.

---

## Integration Notes

- **RecipeCreateModal retirement:** The class and `NewFromRecipeService` remain for backward compatibility and their existing tests. Only the `EditorSubsystem` wiring that created and drew the modal was removed.
- **`shell.newAsset` is always enabled** — same as `shell.openAsset`. The launcher will open the recipe picker even if `_newAssetServices` is null (the `newAssetLauncher?.Open()` null-conditional handles it gracefully).
- **Toolbar sort order:** New (-11) → Open Asset (-10) → Save (-9) → Separator (0) → Perspective (20+). The New button is now the leftmost toolbar icon.
