# BATCH-42 — MTB2-T8 (b): New-asset flow via the Save-As browser dialog

**Task:** MTB2-T8 option (b) — wire **New** through the generic `SaveAsBrowserDialog` (BATCH-41). **Model:** pro ·
**Repo root:** `D:\Work\IOS-IG-SimHost-FDP` · DECISIONS D-T8-4. (Save-As wiring = BATCH-43.)

## Onboarding (do NOT use codebase-memory tooling)
1. `.dev/.guides/DEV-GUIDE.md`. 2. This file. 3. Read:
   `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Dialogs/SaveAsBrowserDialog.cs` (API: `SaveAsRequest`/`SaveAsResult`/
   `SaveAsContentItem`, `Open`, `DrawFrame(IIconProvider)`), `Picker/PickerRequest.cs` (`CategoryNode`),
   `Hrot.Editor.AiShared/Browser/AssetFolderDerivation.cs` (`KnownSubfolders`), `Browser/FolderTreePicker.cs`
   (`FolderPickerState`: `FolderPaths`/`SelectedRelPath`/`AddFolder`), `Browser/AssetRelPath.cs`,
   `Identity/AssetKindIcons.cs`, and the New-flow in `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`
   (`ShowNewAssetDialog` local fn — currently opens `_assetNameFolderModal`; the `_aiCatalogBuilder.RefreshFromAssembly`
   + `FindByAssetId` + `_aiDocumentManager.Open` pattern; `saveAsBlueprintToFile`).

## ⚙️ RULES (non-negotiable)
1. Touch ONLY: `Hrot.Editor.AiShared/Browser/AssetFolderDerivation.cs` (+ its test file), `EditorSubsystem.cs`. No
   other files. No NodeEdit changes (BATCH-41 delivered the dialog).
2. NEVER hide a problem to pass a build (no excluded assets/`[Skip]`/weakened tests/stubs/suppression).
3. Add the EXACT named test; assert real values.
4. DO NOT STOP until build = 0 warnings AND `Hrot.Editor.AiShared.Tests` (filtered) + `Hrot.Editor.Tests` `Failed: 0`
   (no `BLUEPRINT_REGENERATE_SNAPSHOTS`).
5. Report exact changes + final summary. No litter.

## Part A — `AssetFolderDerivation.ToCategoryNode` (testable helper)
Add `public static CategoryNode ToCategoryNode(IReadOnlyList<string> relPaths)` — builds a `NodeEditor.UI.Picker.
CategoryNode` root whose nested `Children` mirror the "/"-split folder relpaths (root = `new CategoryNode("", [...])`).
Distinct, deterministic (sorted). (Reuse `FolderTreePicker.Build` then convert `FolderTreeNode`→`CategoryNode`, or
build directly.)
**Test** (`AssetFolderDerivationTests.cs`): `ToCategoryNode_BuildsNestedTree` — relpaths `["","AI","AI/Combat",
"Patrol"]` → root has children `AI` (child `Combat`) and `Patrol`; assert names + nesting.

## Part B — dialog host (`EditorSubsystem.cs`)
- Add field `private NodeEditor.UI.Dialogs.SaveAsBrowserDialog? _saveAsBrowser;`; init in `RegisterWindows`
  (`new(...)`).
- Per-frame: at the top-level `DrawUI` where `_shellPickers?.DrawFrame()` is called (~L1835), also call
  `_saveAsBrowser?.DrawFrame(adapterBundle.IconProvider);` (capture the icon provider — `adapterBundle.IconProvider`
  or a `SilkIconProvider(windowManager.Atlas)`; use the same one the toolbar uses). Null-safe.

## Part C — New flow opens the Save-As browser (replace the `AssetNameFolderModal` open in `ShowNewAssetDialog`)
Rewrite `ShowNewAssetDialog(AssetKind kind, IEditableAsset recipe)` to build a `SaveAsRequest` + open `_saveAsBrowser`:
```csharp
var folderPicker = new Hrot.Editor.AiShared.Browser.FolderPickerState(
    Hrot.Editor.AiShared.Browser.AssetFolderDerivation.KnownSubfolders(
        catalog.All, kind, Hrot.Editor.AiShared.Browser.AssetBrowserPanel.BaseFolderFor));

var request = new NodeEditor.UI.Dialogs.SaveAsRequest
{
    Title            = $"New {kind}",
    InitialName      = string.Equals(recipe.Name, "Empty", StringComparison.OrdinalIgnoreCase) ? $"New{kind}" : recipe.Name,
    ConfirmLabel     = "Create",
    GetFolderTree    = () => Hrot.Editor.AiShared.Browser.AssetFolderDerivation.ToCategoryNode(folderPicker.FolderPaths.ToList()),
    GetFolderContents= folder => catalog.All
        .Where(a => a.Kind == kind &&
            FolderOf(a, kind) == folder)
        .Select(a => new NodeEditor.UI.Dialogs.SaveAsContentItem(a.Name, Hrot.Editor.AiShared.AssetKindIcons.GetIconKey(kind)))
        .ToList(),
    OnCreateFolder   = (parent, newName) => folderPicker.AddFolder(parent, newName),
    NameExists       = (name, dest) => catalog.All.Any(a => a.Kind == kind && FolderOf(a, kind) == dest && a.Name == name),
    ValidateName     = name => string.IsNullOrWhiteSpace(name) ? "Name must not be empty." : null,
};

_saveAsBrowser?.Open(request, result =>
{
    if (!result.Confirmed) return;
    var minted = _newAssetServices![kind].CreateNew(recipe, result.Name, result.DestinationPath);
    // Blueprint is mint-only — write its file at the chosen folder; BTree/HSM/Scenario persist in CreateNew.
    if (kind == AssetKind.Blueprint) saveAsBlueprintToFile(minted, result.DestinationPath);
    // Refresh the catalog then open the catalogued (concrete) asset (document kinds).
    if (kind is AssetKind.Blueprint or AssetKind.BTree or AssetKind.Hsm)
    {
        var aiAsm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Hrot.AI.Behaviors");
        if (aiAsm != null) _aiCatalogBuilder?.RefreshFromAssembly(aiAsm);
        var catalogued = _aiCatalogBuilder?.Catalog?.FindByAssetId(minted.AssetId);
        if (catalogued != null) _aiDocumentManager?.Open(catalogued);
        else _saveAllStatus = $"[INFO] Created '{minted.Name}'.";
    }
    else _saveAllStatus = $"[OK] Created {kind}: '{minted.Name}'.";
});
```
- Add a local helper `static string FolderOf(IEditableAsset a, AssetKind k)` = directory part of
  `AssetRelPath.RelPath(a, AssetBrowserPanel.BaseFolderFor(k))` (mirror `AssetPickerSource` subfolder extraction; ""
  if none).
- Confirm `saveAsBlueprintToFile`'s signature matches `(IEditableAsset, string folderRelPath)` — if it expects a full
  path or different args, adapt the call to write the blueprint under `result.DestinationPath` with `result.Name`
  (check the existing usage; do NOT change `saveAsBlueprintToFile` itself unless trivial).
- **Remove** the old `AssetNameFolderModal` usage from `ShowNewAssetDialog`. Leave the `AssetNameFolderModal` class +
  its tests in place for now (retired-but-kept; full removal pending — DBT-A1/awaits-approval). Its `DrawModal` call
  at the top-level DrawUI may remain harmlessly (IsOpen false) OR be removed — your choice, but keep build green.

Keep all wiring null-safe (bare-ctor `RegisterWindows` must not throw).

## Build & test (no BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
dotnet test  Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj --filter "FullyQualifiedName~AssetFolderDerivation"
dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
```
All `Failed: 0`; build 0 warnings.

## Definition of done
- New (recipe picker → ) opens the Save-As browser with the kind's folders + existing assets (icons), create-folder,
  name + overwrite-confirm; on Create → `CreateNew` + persist + catalog-refresh + open the catalogued asset.
  `ToCategoryNode` added + tested. `_saveAsBrowser` drawn per frame. `AssetNameFolderModal` no longer used by New.
- Build 0 warnings; `ToCategoryNode` test + `Hrot.Editor.Tests` `Failed: 0`.
- Write `.dev/main-toolbar-2/reports/BATCH-42-REPORT.md`: the request-building, the create+refresh+open, files/tests,
  summary. (Lead will runtime-test New end-to-end.)

If something cannot be done as specified, STOP and report why.
