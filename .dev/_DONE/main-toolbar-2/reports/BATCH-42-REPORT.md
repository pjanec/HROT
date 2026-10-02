# BATCH-42 REPORT — MTB2-T8 (b): New-asset flow via Save-As browser dialog

**Date:** 2026-06-12
**Task:** MTB2-T8 option (b) — wire **New** through the generic `SaveAsBrowserDialog`
**Decision ref:** DECISIONS D-T8-4

---

## Implementation Summary

### Part A — `AssetFolderDerivation.ToCategoryNode` (testable helper)

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetFolderDerivation.cs`

Added `public static CategoryNode ToCategoryNode(IReadOnlyList<string> relPaths)` — builds a `NodeEditor.UI.Picker.CategoryNode` tree from a flat list of `/`-separated folder relative paths.

- **Trie-based construction:** Paths are split on `/` and inserted into a `Dictionary<string, HashSet<string>>` trie keyed by accumulated full path.
- **Recursive freeze:** `FreezeNode()` recursively builds `CategoryNode` subtrees, extracting the segment name from the full path.
- **Deterministic sort:** Children at each level are sorted by `StringComparer.Ordinal` on `CategoryNode.Name`.
- **Root:** `new CategoryNode("", [...])` — empty name, children are the top-level folders.
- **Null safety:** `relPaths == null` throws `ArgumentNullException`; `null` entries and `""` are skipped.
- Added `using NodeEditor.UI.Picker;` for the `CategoryNode` type (the `Hrot.Editor.AiShared.csproj` already references `NodeEditor.UI`).

**Test added:** `ToCategoryNode_BuildsNestedTree` in `AssetFolderDerivationTests.cs`
- Input: `["", "AI", "AI/Combat", "Patrol"]`
- Asserts root name is `""`, children names are `"AI"` and `"Patrol"` (sorted), `AI` has child `Combat` with no children, `Patrol` has no children.

### Part B — Dialog host in `EditorSubsystem.cs`

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

1. **Fields added** (lines ~367-371):
   - `private NodeEditor.UI.Dialogs.SaveAsBrowserDialog? _saveAsBrowser;`
   - `private NodeEditor.Core.Interfaces.IIconProvider? _iconProvider;`

2. **Initialization in `RegisterWindows`** (after `_assetNameFolderModal` creation):
   ```csharp
   _iconProvider = adapterBundle.IconProvider;
   _saveAsBrowser = new NodeEditor.UI.Dialogs.SaveAsBrowserDialog();
   ```
   Both are null-safe in the bare-ctor path (fields default to `null`).

3. **Per-frame draw in `DrawUI`** (after `_shellPickers?.DrawFrame()`):
   ```csharp
   if (_saveAsBrowser != null && _iconProvider != null)
       _saveAsBrowser.DrawFrame(_iconProvider);
   ```
   Null-safe: both must be non-null before `DrawFrame` is called.

### Part C — New flow opens the Save-As browser

**Rewrote `ShowNewAssetDialog`** (local function in `RegisterWindows`):

**New behavior:**
1. Creates a `FolderPickerState` seeded with `KnownSubfolders(catalog.All, kind, baseFolderFor)`.
2. Builds a `SaveAsRequest` with data-driven callbacks:
   - **`GetFolderTree`** → `ToCategoryNode(folderPicker.FolderPaths.ToList())` — live folder tree including newly created folders.
   - **`GetFolderContents`** → filters `catalog.All` by kind and folder, returns `SaveAsContentItem` with the kind's icon key from `AssetKindIcons.GetIconKey`.
   - **`OnCreateFolder`** → `folderPicker.AddFolder(parent, newName)` — adds folder to picker state, which then appears in the tree via `GetFolderTree`.
   - **`NameExists`** → collision check using the `FolderOf` helper to match by kind + folder + name.
   - **`ValidateName`** → rejects empty/whitespace names.
3. Opens `_saveAsBrowser` with the request.
4. **On confirm:**
   - Calls `_newAssetServices![kind].CreateNew(recipe, result.Name, result.DestinationPath)`.
   - For Blueprint: composes the absolute save path via `AssetSavePath.Compose` and calls `saveAsBlueprintToFile` (which expects an absolute path — **adaptation from the batch instructions' simplified signature**).
   - For document kinds (Blueprint, BTree, HSM): refreshes the catalog from the `Hrot.AI.Behaviors` assembly, finds the catalogued asset by `AssetId`, and opens it via `_aiDocumentManager?.Open`.
   - For non-document kinds: sets `_saveAllStatus` with a success message.

5. **Local `FolderOf` helper** (static local function):
   ```csharp
   static string FolderOf(IEditableAsset a, AssetKind k, Func<AssetKind, string?> bf)
   ```
   Extracts the directory part of `AssetRelPath.RelPath(a, bf(k))` — mirrors `AssetPickerSource.ToEntry` subfolder extraction.

**Removed:** All references to `NewAssetDialog` and `_assetNameFolderModal` from `ShowNewAssetDialog`. The `AssetNameFolderModal` class and its tests are preserved (retired-but-kept, per DBT-A1). Its `DrawModal()` call in `DrawUI` remains harmlessly (no-op when `IsOpen` is false).

---

## Design Decisions

### 1. `AssetSavePath.Compose` for blueprint absolute path
**Decision:** Used `AssetSavePath.Compose(AssetKind.Blueprint, result.DestinationPath, result.Name)` to compose the absolute file path before passing to `saveAsBlueprintToFile`.

**Why:** The batch instructions' pseudocode showed `saveAsBlueprintToFile(minted, result.DestinationPath)` passing just the folder relpath. The actual delegate (defined earlier in `RegisterWindows`) expects `(IEditableAsset, string absoluteFilePath)`. Rather than changing the delegate or writing the file differently, I compose the absolute path exactly as `NewAssetDialog.Confirm` does — using the same `AssetSavePath.Compose` helper that `CreateNew` implementations also use. This is zero-risk and preserves the save contract.

### 2. `FolderOf` as a static local function
**Decision:** Made `FolderOf` a `static` local function that takes `baseFolderFor` as an explicit parameter.

**Why:** Static local functions cannot capture enclosing variables (C# rule). The batch instructions specified it as `static`. Passing `baseFolderFor` as a parameter keeps it pure and testable while still matching the spec's intent.

### 3. Icon provider capture via `_iconProvider` field
**Decision:** Stored `adapterBundle.IconProvider` in a new `_iconProvider` field rather than constructing a separate `SilkIconProvider` or storing the entire bundle.

**Why:** The `SaveAsBrowserDialog.DrawFrame(IIconProvider)` needs the icon provider each frame. The `adapterBundle` is a local variable in `RegisterWindows`, not accessible from `DrawUI`. Capturing just the `IIconProvider` is minimal and matches the pattern used by `_shellPickers.SetServices(adapterBundle.IconProvider, ...)`.

### 4. Catalog refresh before FindByAssetId
**Decision:** Added `_aiCatalogBuilder?.RefreshFromAssembly(aiAsm)` before `FindByAssetId` in the onChosen callback.

**Why:** The old `ShowNewAssetDialog` did NOT refresh the catalog — it tried `FindByAssetId` directly after `CreateNew`, and if that returned null, it showed a fallback message. The batch instructions require a full catalog refresh to ensure the newly created asset is findable. This makes the Open-after-create flow reliable.

---

## Deviations

**None.** All changes follow the batch instructions exactly, with one documented adaptation:

- **Blueprint save path composition:** The instructions showed `saveAsBlueprintToFile(minted, result.DestinationPath)` passing a folder relpath, but the delegate expects an absolute path. I used `AssetSavePath.Compose` to build the absolute path, which is the same mechanism used by `NewAssetDialog.Confirm` and by `CreateNew` implementations. This is a necessary adaptation, not a design change.

---

## Files Changed

| File | Change | Lines |
|------|--------|-------|
| `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetFolderDerivation.cs` | Added `ToCategoryNode` + `FreezeNode` + using | +82 |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Browser/AssetFolderDerivationTests.cs` | Added `ToCategoryNode_BuildsNestedTree` test | +31 |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | Fields + init + DrawFrame + rewritten `ShowNewAssetDialog` | +93 / -16 |

**Only these 3 files touched.** `AssetNameFolderModal` class preserved unchanged. No NodeEdit changes.

---

## Test Results

### Build — 0 warnings

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### `Hrot.Editor.AiShared.Tests` (filtered: `FullyQualifiedName~AssetFolderDerivation`)

```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 13 ms
```

5 tests pass:
- `KnownSubfolders_ReturnsDistinctDirsForKind`
- `KnownSubfolders_FiltersByKind`
- `KnownSubfolders_IncludesRoot_WhenAssetsAtRoot`
- `KnownSubfolders_EmptyKind_YieldsRootOnly`
- **`ToCategoryNode_BuildsNestedTree`** ← NEW

### `Hrot.Editor.Tests` (no BLUEPRINT_REGENERATE_SNAPSHOTS)

```
Passed!  - Failed:     0, Passed:   186, Skipped:     0, Total:   186, Duration: 766 ms
```

All 186 tests pass with 0 failures.

---

## Summary

- **`ToCategoryNode`** added and tested — builds a `CategoryNode` folder tree from relpaths.
- **`_saveAsBrowser`** hosted in `EditorSubsystem` — field, init, and per-frame `DrawFrame`.
- **`ShowNewAssetDialog`** rewritten to build a `SaveAsRequest` and open the Save-As browser instead of the `AssetNameFolderModal`. On confirm: `CreateNew` + Blueprint-persist + catalog-refresh + open.
- **`AssetNameFolderModal`** retired from New flow, class preserved (DBT-A1).
- **Build:** 0 warnings. **Tests:** 5 (filtered) + 186 (full) = all `Failed: 0`.
- **Non-document kinds** (Scenario, Blackboard, Utility) get a simple status message on create; no catalog refresh or open.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
