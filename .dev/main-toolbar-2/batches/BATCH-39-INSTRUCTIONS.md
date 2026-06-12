# BATCH-39 — MTB2-T8 (model layer): `INameFolderDialog` + `KnownSubfolders` helper

**Task:** MTB2-T8 part 1 (DBT-A3) · **Model:** pro · **Repo root:** `D:\Work\IOS-IG-SimHost-FDP` · DESIGN DEC-A8.
**Pure/headless model layer for the New-Asset/Save-As name+folder modal (the ImGui renderer is BATCH-40).**

## Onboarding (do NOT use codebase-memory tooling)
1. `.dev/.guides/DEV-GUIDE.md`. 2. This file. 3. Read:
   `Hrot/Editor/Hrot.Editor.AiShared/Recipes/NewAssetDialog.cs`, `.../Recipes/SaveAsDialog.cs`,
   `.../Recipes/FolderPickerState.cs` (in `Browser/FolderTreePicker.cs`), `Browser/AssetRelPath.cs`,
   `Browser/AssetBrowserPanel.cs` (`BaseFolderFor`), `Browser/AssetPickerSource.cs` (its Category/relpath derivation).

## ⚙️ RULES (non-negotiable)
1. Touch ONLY the files listed below. No drive-by edits/renames. No ImGui in this batch.
2. NEVER hide a problem to pass a build (no excluded assets/`[Skip]`/weakened tests/stubs/suppression).
3. Add the EXACT named tests; assert real values.
4. DO NOT STOP until build = 0 warnings AND the test command shows `Failed: 0` (no `BLUEPRINT_REGENERATE_SNAPSHOTS`).
5. Report exact files/tests + final summary. No litter.

## Scope — ONLY these files
1. **NEW** `Hrot/Editor/Hrot.Editor.AiShared/Recipes/INameFolderDialog.cs`:
   ```csharp
   namespace Hrot.Editor.AiShared.Recipes;
   /// <summary>Common surface for the generic name+folder modal (New / Save-As).</summary>
   public interface INameFolderDialog
   {
       string Title { get; }                 // e.g. "New Blueprint" / "Save As"
       string Name { get; set; }
       FolderPickerState FolderPicker { get; }
       bool CanConfirm();
       ConfirmResult Confirm(Action<IEditableAsset>? onCreated = null);
   }
   ```
2. **EDIT** `NewAssetDialog.cs` — implement `INameFolderDialog`. `Name`/`FolderPicker`/`CanConfirm`/`Confirm` already
   exist; add a `public string Title => $"New {Kind}";` (and `: INameFolderDialog` on the class). Do NOT change
   existing behavior/signatures.
3. **EDIT** `SaveAsDialog.cs` — implement `INameFolderDialog`. Add `public string Title => "Save As";` (+ the
   interface). It must already expose `Name`/`FolderPicker`/`CanConfirm`/`Confirm` — if a member name differs, add a
   thin explicit interface member that forwards (do NOT rename existing public members).
4. **NEW** `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetFolderDerivation.cs`:
   ```csharp
   public static class AssetFolderDerivation
   {
       /// Distinct logical subfolder relpaths that already exist for `kind`, derived from the catalog assets'
       /// AssetRelPath directory parts (NOT the filesystem). Always includes "" (root). Ordered, case-insensitive-distinct.
       public static IReadOnlyList<string> KnownSubfolders(
           IReadOnlyList<IEditableAsset> assets, AssetKind kind, Func<AssetKind, string?> baseFolderResolver);
   }
   ```
   - For each asset of `kind`: `rel = AssetRelPath.RelPath(asset, baseFolderResolver(kind))`; take the directory part
     (everything before the last `/`, else ""); collect distinct (OrdinalIgnoreCase), include "" (root). Mirror the
     subfolder extraction in `AssetPickerSource`.
5. Tests: `Hrot/Editor/Hrot.Editor.AiShared.Tests/Recipes/NameFolderDialogTests.cs` (NEW) +
   `Hrot/Editor/Hrot.Editor.AiShared.Tests/Browser/AssetFolderDerivationTests.cs` (NEW).

## Tests — EXACT names
`AssetFolderDerivationTests.cs` (fake `IEditableAsset` like `AssetPickerSourceTests`; inject a deterministic
`baseFolderResolver`):
- `KnownSubfolders_ReturnsDistinctDirsForKind` — Blueprint assets at relpaths `AI/Foo`, `AI/Bar`, `Root` ⇒ result
  contains `"AI"` and `""` (root), and `"AI"` appears once (distinct).
- `KnownSubfolders_FiltersByKind` — a catalog with Blueprint + HSM assets, `kind = Blueprint` ⇒ only Blueprint
  subfolders contribute (HSM dirs absent).
- `KnownSubfolders_IncludesRoot_WhenAssetsAtRoot` — an asset with no subfolder ⇒ `""` present.
- `KnownSubfolders_EmptyKind_YieldsRootOnly` — no assets of the kind ⇒ `[""]` (just root) or empty per your choice —
  assert the documented behavior (recommend: returns `[""]`).

`NameFolderDialogTests.cs`:
- `NewAssetDialog_ImplementsINameFolderDialog` — a `NewAssetDialog` is assignable to `INameFolderDialog`; `Title`
  equals `$"New {Kind}"`; `Name` round-trips via the interface; `FolderPicker` is the same instance.
- `SaveAsDialog_ImplementsINameFolderDialog` — assignable; `Title == "Save As"`; `Name`/`FolderPicker` exposed.

## Build & test (no BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj
dotnet test  Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj ^
  --filter "FullyQualifiedName~AssetFolderDerivation|FullyQualifiedName~NameFolderDialog"
```
Filtered `Failed: 0`; then a FULL `Hrot.Editor.AiShared.Tests` run should stay green (lead confirms).

## Definition of done
- `INameFolderDialog` added + implemented by both dialogs (no behavior change); `AssetFolderDerivation.KnownSubfolders`
  added (catalog-derived logical subfolders). The 6 named tests pass; build 0 warnings.
- Write `.dev/main-toolbar-2/reports/BATCH-39-REPORT.md`: files, tests, final summary.

If something cannot be done as specified, STOP and report why.
