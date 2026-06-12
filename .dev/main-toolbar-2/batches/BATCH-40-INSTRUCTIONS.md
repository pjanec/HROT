# BATCH-40 — MTB2-T8 (UI layer): `AssetNameFolderModal` + wire New & Save-As

**Task:** MTB2-T8 part 2 (DBT-A3 + DBT-2) · **Model:** pro · **Repo root:** `D:\Work\IOS-IG-SimHost-FDP` · DESIGN DEC-A8.
**Depends:** BATCH-39 (`INameFolderDialog`, `AssetFolderDerivation.KnownSubfolders` — merged).

## Onboarding (do NOT use codebase-memory tooling)
1. `.dev/.guides/DEV-GUIDE.md`. 2. This file. 3. **Mirror these:**
   `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetPickerModal.cs` (modal mechanics: identical `PopupId` for
   `OpenPopup`/`BeginPopupModal`, explicit `SetNextWindowSize`, pending-open retry, `Open`/`IsOpen`/`Close`/`DrawModal`
   seams — the BATCH-26 lock-up-fix pattern); `Browser/AssetBrowserPanel.cs` `DrawTreeNode`/`DrawLeafRow` (how a
   `FolderTreeNode` tree is drawn); `Browser/FolderTreePicker.cs` (`Build`, `FolderTreeNode`, `FolderPickerState`).
   `Recipes/INameFolderDialog.cs`, `NewAssetDialog.cs`, `SaveAsDialog.cs`, `Browser/AssetFolderDerivation.cs`.

## ⚙️ RULES (non-negotiable)
1. Touch ONLY: NEW `Browser/AssetNameFolderModal.cs`; NEW test `…Tests/Browser/AssetNameFolderModalTests.cs`;
   EDIT `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`. No other files. No drive-by edits.
2. NEVER hide a problem to pass a build (no excluded assets/`[Skip]`/weakened tests/stubs/suppression).
3. Add the EXACT named tests; assert real behavior via the headless seams.
4. DO NOT STOP until build = 0 warnings AND the test commands show `Failed: 0` (no `BLUEPRINT_REGENERATE_SNAPSHOTS`).
5. Report exact files/tests + final summaries. No litter. Do NOT touch `ScenarioMenuCommands`.

## Objective
A generic ImGui modal (`AssetNameFolderModal`) that renders any `INameFolderDialog` — **Name** textbox + **folder
tree** (select existing logical subfolder) + **"＋ New subfolder"** (create logical subfolder) + Create/Cancel — used
by BOTH New-Asset and Save-As. Replaces the BATCH-36 default-name stopgap and the default-name Save-As.

## Part A — `Browser/AssetNameFolderModal.cs` (NEW)
Mirror `AssetPickerModal`'s modal mechanics. Surface:
```csharp
public sealed class AssetNameFolderModal
{
    public const string PopupId = "Asset Name And Folder";
    public static readonly Vector2 DefaultWindowSize = new(560f, 560f);
    public bool IsOpen { get; }

    /// Open over a dialog model. onCreated is forwarded to dialog.Confirm on a successful Create/Save.
    public void Open(Recipes.INameFolderDialog dialog, Action<IEditableAsset>? onCreated = null);
    public void Close();                 // programmatic close, no Confirm

    /// Headless test seam: confirm the active dialog (CanConfirm-gated) exactly as the OK button does.
    /// Returns the ConfirmResult (or a Fail when not open / cannot confirm). Closes on success.
    public Recipes.ConfirmResult ConfirmActive();

    /// Per-frame draw. No-op when closed. (BATCH-26 pattern: pending-open flag + identical PopupId +
    /// explicit SetNextWindowSize so the modal is reliably visible.)
    public void DrawModal();
}
```
`DrawModal()` content (ImGui), bound to the stored `INameFolderDialog dlg`:
- Title text = `dlg.Title`.
- **Name:** `ImGui.InputText("Name", ref name…)` → write back to `dlg.Name`.
- **Folder:** build `FolderTreePicker.Build(dlg.FolderPicker.FolderPaths)`; render the `FolderTreeNode` tree (mirror
  `AssetBrowserPanel.DrawTreeNode`) with each folder node a `Selectable` → on click set
  `dlg.FolderPicker.SelectedRelPath = node.FullPath`. Show the current `SelectedRelPath` (root shows as "(root)").
- **"＋ New subfolder":** a small `InputText` + button; on submit call `dlg.FolderPicker.AddFolder(dlg.FolderPicker.SelectedRelPath, newName)` then select the new path. (Guard empty/duplicate names — `AddFolder`/`ContainsFolder` exist.)
- **Buttons:** an OK button labelled `"Create"` when `dlg is NewAssetDialog` else `"Save"`, **enabled iff
  `dlg.CanConfirm()`**; on click → `ConfirmActive()`-equivalent (`dlg.Confirm(onCreated)`; close on success; on failure
  show `result.Error` in the modal). `Cancel`/`Esc` → `Close()`.

## Part B — wire in `EditorSubsystem.cs`
- Add field `private Hrot.Editor.AiShared.Browser.AssetNameFolderModal? _assetNameFolderModal;` and init it in
  `RegisterWindows` (`new(...)`).
- **New flow** — in `ShowNewAssetDialog(kind, recipe)`, REPLACE the current default-name `CanConfirm()/Confirm()`
  stopgap with:
  ```csharp
  var known = Hrot.Editor.AiShared.Browser.AssetFolderDerivation.KnownSubfolders(
      catalog.All, kind, Hrot.Editor.AiShared.Browser.AssetBrowserPanel.BaseFolderFor);
  var dlg = new Hrot.Editor.AiShared.Recipes.NewAssetDialog(
      _newAssetServices, knownFolderPaths: known, saveMintOnlyAsset: saveAsBlueprintToFile);
  dlg.Kind = kind; dlg.Recipe = recipe;
  dlg.Name = string.Equals(recipe.Name, "Empty", System.StringComparison.OrdinalIgnoreCase) ? $"New{kind}" : recipe.Name;
  _assetNameFolderModal?.Open(dlg, onCreated: minted =>
  {
      // BUG-A1: open the CATALOGUED concrete asset (document kinds only); Scenario isn't document-backed.
      if (minted.Kind is Hrot.Editor.AiShared.AssetKind.Blueprint
          or Hrot.Editor.AiShared.AssetKind.BTree or Hrot.Editor.AiShared.AssetKind.Hsm)
      {
          var catalogued = _aiCatalogBuilder?.Catalog?.FindByAssetId(minted.AssetId);
          if (catalogued != null) _aiDocumentManager?.Open(catalogued);
      }
  });
  ```
  (The name is now a DEFAULT the user edits in the modal — not an auto-confirm.)
- **Save-As (document)** — in the `requestSaveAs:` seam of `ShellSaveCommands.Register`, instead of immediately
  `dialog.Confirm()`, construct the `SaveAsDialog` with `knownFolderPaths: KnownSubfolders(catalog.All, doc.Kind,
  BaseFolderFor)` and `_assetNameFolderModal?.Open(saveAsDialog)` (no onCreated needed — SaveAsDialog.Confirm persists
  via its own delegates).
- **Save-As (scenario)** — in `openScenarioSaveAs` (the `requestScenarioSaveAs` action), likewise open the modal over
  the seeded scenario `SaveAsDialog` instead of calling `Confirm()` directly.
- **Per-frame:** call `_assetNameFolderModal?.DrawModal();` once at the top-level `DrawUI` (next to
  `_shellPickers?.DrawFrame();` ~L1835).
- Keep all wiring null-safe. Do NOT change `ScenarioMenuCommands`.

## Tests — `…Tests/Browser/AssetNameFolderModalTests.cs` (EXACT names; headless via seams, no ImGui)
Use a fake `INameFolderDialog` (Title/Name/FolderPicker + CanConfirm/Confirm spies) — OR a real `NewAssetDialog` with a
fake `INewAssetService`.
- `Open_SetsIsOpen_True` — after `Open(dialog)`, `IsOpen` is true; after `Close()`, false.
- `ConfirmActive_WhenCanConfirm_CallsConfirm_AndCloses` — dialog `CanConfirm()==true`; `ConfirmActive()` invokes
  `dialog.Confirm` (and forwards `onCreated`), returns success, and `IsOpen` becomes false.
- `ConfirmActive_WhenCannotConfirm_DoesNotConfirm_StaysOpen` — `CanConfirm()==false` → `Confirm` NOT called, returns a
  Fail, `IsOpen` stays true.
- `Close_DoesNotConfirm` — `Open` then `Close` → `dialog.Confirm` never called.

## Build & test (no BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
dotnet test  Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj --filter "FullyQualifiedName~AssetNameFolderModal"
dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
```
All `Failed: 0`. (The ImGui rendering is runtime-verified by the lead; the seams above are the headless contract.)

## Definition of done
- `AssetNameFolderModal` renders name + folder-tree + new-subfolder + Create/Cancel over any `INameFolderDialog`;
  New + Save-As (document + scenario) open it (known folders from the catalog); per-frame `DrawModal` wired; the
  default-name stopgaps removed. Null-safe; `ScenarioMenuCommands` untouched.
- The 4 named tests pass; build 0 warnings; `Hrot.Editor.Tests` `Failed: 0`.
- Write `.dev/main-toolbar-2/reports/BATCH-40-REPORT.md`: modal shape, the 3 wire-ups, per-frame draw, what stopgaps
  were removed, files/tests, final summaries.

If something cannot be done as specified, STOP and report why.
