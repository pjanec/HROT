# BATCH-19 Report

## Implementation Summary

### MTB-P6-T7 — Subfolder-aware file save

Created `AssetSavePath` static class in `Hrot.Editor.AiShared` providing:

- **`GetExtension(AssetKind)`** — returns the canonical compound extension per kind
  (`.bp.json` / `.btree.json` / `.hsm.json`).
- **`Compose(kind, relPath, baseName, assetRootOverride?)`** — composes the absolute save
  path as `AssetsFor(kind)/<relpath>/<baseName>.<ext>`. Normalizes separators
  (backslashes → forward slashes in relPath, then `Path.Combine` per segment),
  validates root-bounding (rejects `..`, absolute paths, drive letters via
  `FolderPickerState.IsBounded`), and supports a test-only `assetRootOverride` to
  decouple from `AppContext.BaseDirectory`.

**Wired** `BTreeNewAssetService.CreateNew` and `HsmNewAssetService.CreateNew` to use
`AssetSavePath.Compose(Kind, relPath, name, _assetRootPath)` instead of manual
`Path.Combine`. This ensures assets land in the correct subfolder, and the existing
`Save` path (which writes to `SourceFilePath`) automatically preserves subfolder
structure.

**No change** to BlueprintNewAssetService (mint-only, no file path) or
ScenarioNewAssetService (uses `SaveScenarioAs`, not file paths).

### MTB-P6-T5 — New Asset dialog

Created `NewAssetDialog` model class in `Hrot.Editor.AiShared.Recipes` (logic
separated from ImGui draw):

- **Injects** `IReadOnlyDictionary<AssetKind, INewAssetService>` (per-kind registry)
  so the dialog stays decoupled from any specific service implementation.
- **Model:** `Kind`, `Recipe` (nullable; includes the in-code "Empty" from
  `AvailableRecipes`), `Name`, `FolderPickerState` (shared folder picker model).
- **`CanConfirm()`** — pure seam: requires non-empty Name, non-null Recipe, and a
  registered service for the current Kind.
- **`Confirm(onCreated?)`** — full creation pipeline:
  1. Validates via `CanConfirm()`.
  2. Composes the save path and runs `AssetBaseNameCollisionGuard.CheckCollisionOnDisk`
     (reuses the existing guard — CS↔JSON base-name collision).
  3. Also checks `File.Exists` for direct same-path collisions.
  4. Calls `service.CreateNew(recipe, name, relPath)` (mints fresh `AssetId`).
  5. **DEC-12:** for Blueprint (mint-only), the dialog performs the subfolder-aware
     save from T7 via the injected `saveMintOnlyAsset` delegate. BTree/HSM/Scenario
     persist in their own `CreateNew` — the dialog does NOT double-write.
  6. Invokes the caller callback with the new asset.
- **`RecipesForKind(AssetKind)`** — returns `AvailableRecipes()` from the registered
  service for that kind.
- **`ConfirmResult`** — discriminated result carrying `IsSuccess`, `Asset`, and `Error`.

Also supports an optional `assetRootOverride` constructor parameter so headless
tests can point at temporary directories without changing `AppContext.BaseDirectory`.

**No Save-As dialog** (MTB-P6-T6) was built — out of scope per batch instructions.

## Design Decisions

1. **`AssetSavePath.Compose` uses `Path.Combine` per segment** rather than a single
   `Path.Combine(base, "a/b/c")`, because the latter leaves forward slashes on Windows.
   Splitting on `/` and combining each segment produces OS-consistent separators.

2. **Root-bounding in `Compose` mirrors `FolderPickerState` semantics** — checks for
   leading `/`/`\`, drive letters, `..` traversal, and `Path.IsPathRooted` before
   normalization. This prevents the `Trim('/')` step from hiding an absolute path.

3. **`assetRootOverride` is a constructor parameter on the dialog** (not on `Confirm`),
   matching the lifecycle: the dialog is constructed once per open, with a fixed root.

4. **Collision guard for same-name file** — added a `File.Exists` check in addition to
   `AssetBaseNameCollisionGuard.CheckCollisionOnDisk`, because the guard only handles
   CS↔JSON collisions, not same-file-name conflicts.

5. **`saveMintOnlyAsset` delegate is nullable** — when null and Kind is Blueprint, the
   save step is skipped. This allows test scenarios to verify mint-only behavior
   without serializing, while production always provides the delegate.

## Deviations

None — the implementation follows the batch instructions exactly. All constraints
(DEC-12, collision-guard reuse, no double-write, no Save-As dialog) are honored.

## Test Results

### New tests (unfiltered, all pass)

**T7 — AssetSavePathTests** (22 tests):
- `Compose_Blueprint_AtRoot_ReturnsExpectedPath`
- `Compose_BTree_NestedRelPath_ReturnsExpectedPath`
- `Compose_Hsm_BackslashRelPath_NormalizedToForwardSlash`
- `Compose_NullRelPath_TreatedAsRoot`
- `Compose_AssetRootOverride_UsesOverridePath`
- `GetExtension_FileKinds_ReturnsCorrectExtension` (Theory: BP/BTree/HSM)
- `GetExtension_NonFileKinds_ThrowsArgumentOutOfRangeException` (Theory: Scenario/Blackboard/Utility)
- `Compose_DotDot_EscapesRoot_ThrowsArgumentException`
- `Compose_AbsoluteRelPath_ThrowsArgumentException`
- `Compose_DriveLetterInRelPath_ThrowsArgumentException`
- `Compose_EmptyBaseName_ThrowsArgumentException`
- `Compose_WhitespaceBaseName_ThrowsArgumentException`
- `Compose_ScenarioKind_ThrowsArgumentOutOfRangeException`
- **`Save_PreservesSubfolder_RoundTrip`** — composes path for "combat/Guard"/"PatrolBehavior", writes valid BTree JSON, recursive `DiscoverHeaders` scan finds file at same relative path.
- **`Save_PreservesSubfolder_RoundTrip_Hsm`** — same with HSM kind.
- `BTreeService_CreateNew_WritesToSubfolder` — verifies wiring: `CreateNew(relPath: "group/sub")` writes to `group/sub/NestedAsset.btree.json`.
- `HsmService_CreateNew_WritesToSubfolder` — same for HSM.
- `ExistingAssetWithSubfolder_Save_KeepsWritingToSamePath` — re-save overwrites same path, scan confirms.

**T5 — NewAssetDialogTests** (16 tests):
- **`Confirm_WritesFile_AtAssetsRootRelPath_WithFreshId`** — real BTree service + temp root; confirms with kind=BTree, name="Patrol", relPath="combat/Guard" → writes `combat/Guard/Patrol.btree.json` with fresh `AssetId`; deserializes to verify id matches.
- **`CollisionGuard_RejectsExistingBaseName`** — fake file lister reports `Patrol.cs` at target dir → D5 collision detected, no write, no callback.
- `CollisionGuard_RejectsExistingBaseName_WhenCsExistsInSubfolder` — same detection in nested subfolder.
- **`Callback_ReceivesNewAsset`** — fake service returns predictable asset → callback receives correct Kind/Name/fresh `AssetId`.
- `Callback_ReceivesNewAsset_WithNestedRelPath` — Blueprint with `saveMintOnlyAsset` delegate; verifies save path includes `combat/Guard/SniperBrain.bp.json`.
- `CanConfirm_AllSet_ReturnsTrue` / `_EmptyName_ReturnsFalse` / `_NullRecipe_ReturnsFalse` / `_UnregisteredKind_ReturnsFalse`
- `Confirm_WhenCannotConfirm_ReturnsFailure`
- `RecipesForKind_RegisteredKind_ReturnsRecipes` / `_UnregisteredKind_ReturnsEmpty`
- `Confirm_BTree_DoesNotDoubleWrite` — save delegate NOT called for BTree (DEC-12).
- `Confirm_Blueprint_CallsSaveMintOnlyAsset` — save delegate IS called for Blueprint (DEC-12).
- `Confirm_FileAlreadyExists_ReturnsFailure` — pre-existing file at target path → rejected.
- `Confirm_WhitespaceName_ReturnsFailure`

### Suite summaries (Stability filter applied)

| Suite | Passed | Failed | Skipped |
|-------|--------|--------|---------|
| `Hrot.Editor.AiShared.Tests` | 1,007 | 0 | 0 |
| `Fdp.Toolkits.Tests` | 1,856 | 0 | 0 |
| `Hrot.SimHost.Tests` | 585 | 0 | 3 |
| `Hrot.BTree.Editor.Tests` | 406 | 0 | 0 |
| `Hrot.Hsm.Editor.Tests` | 358 | 0 | 0 |
| `Hrot.Blueprints.Tests` (class filter) | 8 | 0 | 0 |
| `Hrot.Editor.Tests` (Scenario) | 53 | 0 | 0 |

No EqsModuleTests flake appeared. Zero new warnings in full solution build.

`dotnet build IOS-IG-SimHost.sln`: 0 errors, 20 pre-existing warnings, 0 new warnings.

## Developer Insights

- **`Path.Combine` with forward-slash segments on Windows** produces mixed separators
  (e.g. `Assets\BTrees\combat/Guard`). Splitting the relPath into individual segments
  and combining each via `Path.Combine` was the cleanest fix. This is a well-known
  .NET cross-platform gotcha.

- **`CheckCollisionOnDisk` swallows directory-access exceptions** — if the target
  directory doesn't exist, the collision check returns null (no collision). This
  correctly handles first-time creation where the subfolder hasn't been created yet.

- **Pre-existing warnings in the solution** are all xUnit2013 analyzer suggestions
  (use `Assert.Single`/`Assert.Empty` instead of `Assert.Equal` for collection sizes)
  and `IBlueprintTimeController` deprecation warnings in Blueprint tests. No action
  needed from this batch.

- **Edge case discovered**: `CollisionGuard_RejectsExistingBaseName_WhenCsExistsInSubfolder`
  revealed that `FolderPickerState.SelectedRelPath` requires the folder to be in the
  known folder set. Tests must seed known folders accordingly, and production callers
  seed from the contributor's existing folder paths.

## Known Issues

None. All batch requirements are met.

## Suggested Commit Message

```
feat(main-toolbar): subfolder-aware save + New Asset dialog model (MTB-P6-T7, T5)
```

**Files changed:**
- `Hrot/Editor/Hrot.Editor.AiShared/AssetSavePath.cs` (new)
- `Hrot/Editor/Hrot.Editor.AiShared/Recipes/NewAssetDialog.cs` (new)
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/BTreeNewAssetService.cs` (wired to AssetSavePath.Compose)
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/HsmNewAssetService.cs` (wired to AssetSavePath.Compose)
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Identity/AssetSavePathTests.cs` (new, 22 tests)
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Recipes/NewAssetDialogTests.cs` (new, 16 tests)
