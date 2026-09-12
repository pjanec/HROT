# BATCH-36 Review — MTB2-T7

**Status:** ✅ APPROVED · **Date:** 2026-06-12 · Reviewer: Dev Lead

## Verified (independent)
- `NewAssetLauncher.Open()` mirrors `AssetPickerLauncher`: builds `RecipePickerSource` + Tree `PickerRequest`
  (`ItemsProvider = source.BuildEntries`), `openPicker(req, result => pick→showNewAssetDialog(rc.Kind, rc.Recipe))`,
  cancel→noop; null-safe ctor.
- EditorSubsystem: `shell.newAsset` command (DisplayName "New Asset…", Ctrl+N, IconKey `asset/new`); launcher built
  with `_shellPickers.OpenPicker` + `_newAssetServices` + `ShowNewAssetDialog` (seeds `NewAssetDialog`, default name
  = recipe name / `New{Kind}` for "Empty", `Confirm`→`AiDocumentManager.Open`); toolbar at sortOrder -11 (left of
  Open Asset); `File/New Asset…` menu. `RecipeCreateModal` construction + `CustomToolbarDraw` hookup removed;
  `RecipeCreateModal.cs` + `NewFromRecipeService.cs` **kept** (confirmed on disk).
- Interactive name/folder popup deferred → **DBT-A3** (functional default-name create now; D-T7-1).
- Launcher tests invoke the captured handler and assert Tree request + `showNewAssetDialog(kind, recipe)` on pick /
  not-on-cancel. Guardrail asserts the command (Ctrl+N) + `ContainsEntry("shell.newAsset")` + File/New Asset… menu.
- Build 0 warnings; `Hrot.Editor.Tests` 186/186; `EditorSubsystemBlueprintWindows` 15/15; **full `Hrot.Blueprints.Tests`
  = 7 distinct PRE-1 failures, ZERO new** (modal-retirement broke nothing).

## Issues
None.

## Pending (lead runtime, non-blocking)
- Live: toolbar "New" button / File→New Asset… / Ctrl+N opens the recipe Tree picker → pick creates+opens the asset.
- DBT-A3: interactive name/folder popup for New (currently default-named).

## Commit
`feat(main-toolbar2): NewAssetLauncher + File/New + New toolbar button; retire RecipeCreateModal wiring (MTB2-T7)`
