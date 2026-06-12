# BATCH-37 — BUG-A1: New Asset crash (open the catalogued asset, not the minted adapter)

**Bug:** BUG-A1 (runtime, from MTB2-T7) · **Model:** flash (tiny, prescribed) · **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`

## Onboarding (do NOT use codebase-memory tooling)
1. `.dev/.guides/DEV-GUIDE.md`. 2. This file.

## ⚙️ RULES (non-negotiable)
1. Make ONLY the single edit below. Touch ONLY `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`. No other changes.
2. NEVER hide a problem to pass a build (no excluded assets / `[Skip]` / weakened tests / stubs / suppression).
3. DO NOT STOP until the build has 0 warnings AND `Hrot.Editor.Tests` shows `Failed: 0` (no `BLUEPRINT_REGENERATE_SNAPSHOTS`).
4. Report the exact change + final test summary. No litter.

## The bug
Creating a New Asset (Blueprint/HSM/BTree) crashes:
`System.ArgumentException: Expected BlueprintFileAsset but got BlueprintEditableAssetAdapter (Parameter 'asset')`.
The local function `ShowNewAssetDialog` (added in BATCH-36, inside `EditorSubsystem.RegisterWindows`) opens the
**minted adapter** returned by `INewAssetService.CreateNew`, but `AiDocumentManager.Open` → `*DocumentFactory.Build`
require the **catalogued concrete asset** (e.g. `BlueprintFileAsset`). (The retired `RecipeCreateModal` did it right:
create → save → `_aiCatalogBuilder.Catalog.FindByAssetId(id)` → open that.) Scenario is NOT document-backed (its
`CreateNew` persists via `IEditorLogic`) — it must NOT be opened as a document.

## The exact edit
In `EditorSubsystem.cs`, in the `ShowNewAssetDialog(AssetKind kind, IEditableAsset recipe)` local function, find:
```csharp
                if (dlg.CanConfirm())
                {
                    var r = dlg.Confirm(onCreated: a => _aiDocumentManager?.Open(a));
                    _saveAllStatus = r.IsSuccess
                        ? $"[OK] Created new {kind}: '{r.Asset?.Name}'."
                        : $"[INFO] New {kind}: {r.Error}";
                }
```
Replace the `onCreated` callback so it opens the **catalogued** asset (resolved by AssetId), and skips Scenario:
```csharp
                if (dlg.CanConfirm())
                {
                    var r = dlg.Confirm(onCreated: minted =>
                    {
                        // BUG-A1: Open requires the catalogued concrete asset (e.g. BlueprintFileAsset),
                        // NOT the minted INewAssetService adapter. Resolve via the catalog by AssetId
                        // (mirrors the retired RecipeCreateModal). Scenario is not document-backed.
                        if (minted.Kind is Hrot.Editor.AiShared.AssetKind.Blueprint
                            or Hrot.Editor.AiShared.AssetKind.BTree
                            or Hrot.Editor.AiShared.AssetKind.Hsm)
                        {
                            var catalogued = _aiCatalogBuilder?.Catalog?.FindByAssetId(minted.AssetId);
                            if (catalogued != null)
                                _aiDocumentManager?.Open(catalogued);
                            else
                                _saveAllStatus = $"[INFO] Created '{minted.Name}'. Open it from the Asset Browser (catalog refresh pending).";
                        }
                    });
                    _saveAllStatus = r.IsSuccess
                        ? $"[OK] Created new {kind}: '{r.Asset?.Name}'."
                        : $"[INFO] New {kind}: {r.Error}";
                }
```
(If the exact whitespace differs, match the existing code; keep the logic identical to the replacement above.)

## Build & test (no BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
```
Build 0 warnings; `Hrot.Editor.Tests` `Failed: 0` (the existing `NewAssetLauncherTests` are unaffected — this is the
production open path, runtime-verified by the lead).

## Definition of done
- The `onCreated` callback opens the **catalogued** asset for document kinds (never the minted adapter) and does not
  open Scenario as a document. Build 0 warnings; `Hrot.Editor.Tests` `Failed: 0`.
- Write `.dev/main-toolbar-2/reports/BATCH-37-REPORT.md`: the change + final test summary.

If something cannot be done as specified, STOP and report why.
