# BATCH-40 REPORT — MTB2-T8 UI Layer: AssetNameFolderModal

**Date:** 2026-06-12 · **Branch:** `blueprint-integ-1` · **Model:** pro

## Summary
Created `AssetNameFolderModal` — a generic ImGui modal rendering any `INameFolderDialog` (name textbox + folder tree + "+ New subfolder" + Create/Cancel). Wired it into the New flow and Save-As (document + scenario). Replaced the BATCH-36 default-name stopgaps. All builds 0 warnings; all tests `Failed: 0`.

---

## Part A — `Browser/AssetNameFolderModal.cs` (NEW)

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetNameFolderModal.cs`

Mirrors `AssetPickerModal`'s BATCH-26 modal mechanics:

- `const string PopupId = "Asset Name And Folder"` — identical for both `OpenPopup`/`BeginPopupModal`
- `static readonly Vector2 DefaultWindowSize = new(560f, 560f)` — explicit size prevents zero-size collapse
- `bool IsOpen` — true when a dialog is active
- `Open(INameFolderDialog dialog, Action<IEditableAsset>? onCreated)` — stores dialog + callback
- `Close()` — programmatic close, no Confirm
- `ConfirmActive()` — headless test seam: gates on `CanConfirm()`, calls `dialog.Confirm(onCreated)`, closes on success, stores error message on failure
- `DrawModal()` — per-frame ImGui draw with retry `IsPopupOpen`/`OpenPopup` pattern, renders:
  - Title text (`dlg.Title`)
  - Name: `ImGui.InputText` bound to `dlg.Name`
  - Folder tree: `FolderTreePicker.Build(dlg.FolderPicker.FolderPaths)` → recursive `DrawFolderTreeNodes` (only folders, no leaves), each node a `Selectable` → sets `dlg.FolderPicker.SelectedRelPath`
  - "+ New subfolder": `InputTextWithHint` + button → `dlg.FolderPicker.AddFolder(parent, name)`, guards empty/duplicate via `ContainsFolder` + try/catch
  - OK button: labelled `"Create"` for `NewAssetDialog`, else `"Save"`; enabled iff `dlg.CanConfirm()`; on click → confirm → close on success, show error on failure
  - Cancel/Esc → `Close()`
- Internal seams: `Dialog`, `OnCreatedCallback`, `ErrorMessage` — exposed for test verification

---

## Part B — Wiring in `EditorSubsystem.cs`

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

### Field added
```csharp
private Hrot.Editor.AiShared.Browser.AssetNameFolderModal? _assetNameFolderModal;
```
(near `_shellPickers`, line ~363)

### Init in `RegisterWindows`
```csharp
_assetNameFolderModal = new Hrot.Editor.AiShared.Browser.AssetNameFolderModal();
```
(right after `_shellPickers` init, ~line 2468)

### New flow (ShowNewAssetDialog)
Replaced the BATCH-36 default-name stopgap that auto-confirmed `NewAssetDialog`:

**Before:** construct `NewAssetDialog` with `knownFolderPaths: Array.Empty<string>()` → immediately call `dlg.CanConfirm()`/`dlg.Confirm()` with auto-generated name.

**After:** construct `NewAssetDialog` with `knownFolderPaths: KnownSubfolders(catalog.All, kind, baseFolderFor)` → seed `dlg.Name` as a DEFAULT (still `$"New{kind}"` for "Empty" recipes, recipe name otherwise) → call `_assetNameFolderModal?.Open(dlg, onCreated: ...)` so the user edits the name and picks a folder in the modal.

### Save-As (document) — `requestSaveAs` seam
**Before:** construct `SaveAsDialog` with `knownFolderPaths: Array.Empty<string>()` → immediately call `dialog.Confirm()`.

**After:** construct `SaveAsDialog` with `knownFolderPaths: KnownSubfolders(catalog.All, doc.Asset.Kind, baseFolderFor)` → `_assetNameFolderModal?.Open(dialog)` (no onCreated — SaveAsDialog.Confirm persists via its own delegates).

### Save-As (scenario) — `openScenarioSaveAs` action
**Before:** construct `SaveAsDialog` with no known folders → immediately call `dialog.Confirm()`.

**After:** construct `SaveAsDialog` with `knownFolderPaths: KnownSubfolders(catalog.All, AssetKind.Scenario, baseFolderFor)` → `_assetNameFolderModal?.Open(dialog)`.

### Per-frame draw
Added `_assetNameFolderModal?.DrawModal();` at top-level `DrawUI`, next to `_shellPickers?.DrawFrame();` (~L1837).

### Base-folder resolver
Added local lambda `baseFolderFor` wrapping `AssetRoots.AssetsFor` in try/catch (mirrors `AssetBrowserPanel.BaseFolderFor` which is `internal` and inaccessible from Hrot.Editor assembly). All three call sites use it.

### Stopgaps removed
- The default-name auto-confirm in `ShowNewAssetDialog` (BATCH-36 stopgap).
- The immediate `Confirm()` in `requestSaveAs` seam (document Save-As stopgap).
- The immediate `Confirm()` in `openScenarioSaveAs` (scenario Save-As stopgap).

All wiring is null-safe (`?.` operators). `ScenarioMenuCommands` untouched.

---

## Tests — `AssetNameFolderModalTests.cs`

**File:** `Hrot/Editor/Hrot.Editor.AiShared.Tests/Browser/AssetNameFolderModalTests.cs`

Uses a `FakeNameFolderDialog` spy (settable `CanConfirmResult`/`ConfirmResult`, records `ConfirmCallCount` + `LastOnCreated`) and `FakeAsset` (minimal `IEditableAsset`).

| Test | Status | What it verifies |
|------|--------|-----------------|
| `Open_SetsIsOpen_True` | ✅ Passed | `Open(dialog)` → `IsOpen==true`; `Close()` → `IsOpen==false` |
| `ConfirmActive_WhenCanConfirm_CallsConfirm_AndCloses` | ✅ Passed | `CanConfirm()==true` → `ConfirmActive()` calls `dialog.Confirm`, forwards `onCreated`, returns success, `IsOpen` becomes false |
| `ConfirmActive_WhenCannotConfirm_DoesNotConfirm_StaysOpen` | ✅ Passed | `CanConfirm()==false` → `ConfirmActive()` does NOT call `Confirm`, returns Fail with Error, `IsOpen` stays true |
| `Close_DoesNotConfirm` | ✅ Passed | `Open` then `Close` → `dialog.Confirm` never called |

---

## Build & Test Results

```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
  Build succeeded. 0 Warning(s) 0 Error(s)

dotnet test ...AiShared.Tests.csproj --filter "FullyQualifiedName~AssetNameFolderModal"
  Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4

dotnet test ...Hrot.Editor.Tests.csproj
  Passed! - Failed: 0, Passed: 186, Skipped: 0, Total: 186
```

Both builds: **0 warnings**. Both test suites: **Failed: 0**. No `BLUEPRINT_REGENERATE_SNAPSHOTS` used.

---

## Files touched
| File | Action |
|------|--------|
| `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetNameFolderModal.cs` | **NEW** |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Browser/AssetNameFolderModalTests.cs` | **NEW** |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | **EDIT** (field + init + 3 wire-ups + per-frame draw) |

No other files touched. `ScenarioMenuCommands` unchanged.
