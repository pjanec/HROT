# BATCH-43 — MTB2-T8 (b): Save-As (document + scenario) via the dialog; retire AssetNameFolderModal

**Task:** MTB2-T8 (b) — route Save-As through the generic `SaveAsBrowserDialog`; finish retiring the editor modal.
**Model:** pro · **Repo root:** `D:\Work\IOS-IG-SimHost-FDP` · DECISIONS D-T8-4. Depends on BATCH-42 (New flow + host).

## Onboarding (do NOT use codebase-memory tooling)
1. `.dev/.guides/DEV-GUIDE.md`. 2. This file. 3. Read in `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`:
   the BATCH-42 `ShowNewAssetDialog` (the `SaveAsRequest`-building pattern + `_saveAsBrowser` + `FolderOf`/
   `KnownSubfolders`/`ToCategoryNode`); the `ShellSaveCommands.Register(requestSaveAs: doc => { … SaveAsDialog …
   Confirm() })` seam (~L2350); the `openScenarioSaveAs` local Action (seeds `ScenarioSaveAsAsset` + `SaveAsDialog` +
   `Confirm()`, ~L2480). Plus `Hrot.Editor.AiShared/Recipes/SaveAsDialog.cs` (its `Name`/`FolderPicker`/`Confirm`).

## ⚙️ RULES (non-negotiable)
1. Touch ONLY `EditorSubsystem.cs` (+ remove `AssetNameFolderModal` usages there). Do NOT delete the
   `AssetNameFolderModal.cs` class or its tests (retired-but-kept — DBT-A1/awaits-approval). No NodeEdit changes.
2. NEVER hide a problem to pass a build (no excluded assets/`[Skip]`/weakened tests/stubs/suppression).
3. DO NOT STOP until build = 0 warnings AND `Hrot.Editor.Tests` `Failed: 0` (no `BLUEPRINT_REGENERATE_SNAPSHOTS`).
4. Report exact changes + final summary. No litter.

## Part A — extract a shared `SaveAsRequest` builder (de-dup New + Save-As)
Extract the `SaveAsRequest`-building from BATCH-42's `ShowNewAssetDialog` into a private method on `EditorSubsystem`:
```csharp
private NodeEditor.UI.Dialogs.SaveAsRequest BuildSaveAsRequest(
    Hrot.Editor.AiShared.AssetKind kind, string title, string initialName, string initialDestination,
    string confirmLabel, Hrot.Editor.AiShared.Browser.FolderPickerState folderPicker)
{ /* GetFolderTree=ToCategoryNode(folderPicker.FolderPaths), GetFolderContents=kind's assets in folder w/ kind icon,
     OnCreateFolder=folderPicker.AddFolder, NameExists=collision, ValidateName=non-empty (mirror BATCH-42) */ }
```
Refactor `ShowNewAssetDialog` to call it (Title `$"New {kind}"`, ConfirmLabel "Create"). Keep `FolderOf` helper.

## Part B — document Save-As → the browser
In the `requestSaveAs: doc => { … }` seam, REPLACE the immediate `SaveAsDialog(...).Confirm()` with:
```csharp
var fp = new FolderPickerState(KnownSubfolders(catalog.All, doc.Kind, AssetBrowserPanel.BaseFolderFor));
var req = BuildSaveAsRequest(doc.Kind, $"Save {doc.Kind} As", doc.Asset.Name, FolderOf(doc.Asset, doc.Kind), "Save", fp);
_saveAsBrowser?.Open(req, result =>
{
    if (!result.Confirmed) return;
    // Reuse the Phase-6 SaveAsDialog (fresh-id duplicate + writes the doc's content) — the browser only supplies
    // the chosen name + destination.
    if (_newAssetServices == null) return;
    var dialog = new Hrot.Editor.AiShared.Recipes.SaveAsDialog(doc.Asset, _newAssetServices,
        saveMintOnlyAsset: saveAsBlueprintToFile /* + any existing args */);
    dialog.Name = result.Name;
    dialog.FolderPicker.SelectedRelPath = result.DestinationPath;
    var r = dialog.Confirm();
    _saveAllStatus = r.IsSuccess ? $"[OK] Saved as '{result.Name}'." : $"[INFO] Save As: {r.Error}";
});
```
(Match the existing `SaveAsDialog` ctor args used today at the `requestSaveAs` site — do not invent args.)

## Part C — scenario Save-As → the browser
In `openScenarioSaveAs`, REPLACE the immediate seed+`Confirm()` with opening the browser for `AssetKind.Scenario`
(folders from `KnownSubfolders(catalog.All, AssetKind.Scenario, BaseFolderFor)`; `initialName` = current
`_editorLogic.LoadedScenarioName` leaf or ""), `onChosen` → on Confirmed, compute the scenario name as
`DestinationPath`+"/"+`Name` (trim leading "/") and call the existing scenario save path
(`_editorLogic?.SaveScenarioAs(fullName)` / `saveAsScenarioDelegate`); set `_saveAllStatus`.

## Part D — retire `AssetNameFolderModal`
Remove the `_assetNameFolderModal` field, its construction, its `DrawModal()` call, and any remaining usages in
`EditorSubsystem.cs`. **Keep** `AssetNameFolderModal.cs` + its tests (add to the DBT-A1 awaits-approval deletion
list). Build must stay green.

Keep all wiring null-safe (bare-ctor `RegisterWindows` must not throw).

## Build & test (no BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
dotnet test  Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj --filter "FullyQualifiedName~SaveCommands|FullyQualifiedName~AssetFolderDerivation"
```
All `Failed: 0`; build 0 warnings. (The Save-As/New flows are ImGui+integration — runtime-verified by the lead.)

## Definition of done
- New + document Save-As + scenario Save-As all open the **same** `SaveAsBrowserDialog` (via `BuildSaveAsRequest`);
  document Save-As reuses `SaveAsDialog` for the fresh-id write; scenario Save-As routes to `SaveScenarioAs`.
  `AssetNameFolderModal` no longer referenced in `EditorSubsystem` (class/tests kept).
- Build 0 warnings; `Hrot.Editor.Tests` + filtered AiShared tests `Failed: 0`.
- Write `.dev/_DONE/main-toolbar-2/reports/BATCH-43-REPORT.md`: the shared builder, the 2 Save-As repoints, the retirement,
  files/tests, summary.

If something cannot be done as specified, STOP and report why.
