# BATCH-44 — BUG-A6 (P0): New asset must be discovered, opened, and switch perspective (Blueprint/BTree/HSM)

**Bug:** BUG-A6 (runtime, CRITICAL) — creating a new asset does NOT open a canvas and does NOT switch perspective.
**Assigned to: sonnet** (integration-sensitive; inspect the SOURCE path end-to-end — headless tests are NOT proof).
**Repo root:** `D:\Work\IOS-IG-SimHost-FDP`.

## Confirmed root cause (Blueprint — verified by the lead in source)
- `BlueprintAssetContributor` scans **`bpRootDir = <source-project-dir>/Assets/Blueprints`** (`EditorSubsystem.cs`
  ~L678, resolved from the `.csproj` via `ResolveAiBehaviorsDir`); `RefreshFromAssembly` → `_blueprintRefresh()` →
  `bpContrib.Refresh()` rescans that dir + reads each `.bp.json` header AssetId.
- But the New-flow (`ShowNewAssetDialog` onChosen) writes the new `.bp.json` via
  `AssetSavePath.Compose(Blueprint, dest, name)` which uses **`AssetRoots.AssetsFor(Blueprint)` =
  `AppContext.BaseDirectory/Assets/Blueprints` (the bin/OUTPUT dir)**.
- **Output dir ≠ source scan dir ⇒ the new file is never found ⇒ `FindByAssetId(minted.AssetId)` returns null ⇒
  `_aiDocumentManager.Open(...)` is never called ⇒ no canvas + no perspective switch** (the perspective switch is
  wired via `AiDocumentManager(_perspectiveSwitcher)` → `Activate`, but it only runs on a real `Open`).

## Objective
After **Create** in the New-asset flow, for **each document kind (Blueprint, BTree, HSM)** the new asset must be:
**written where its catalog contributor scans → refreshed → found by `FindByAssetId` → `Open`ed → perspective
switches to the kind's canvas → the canvas shows the new asset.** Fix all three kinds.

## What to do (inspect each kind's source; the Blueprint cause above is the template)
For EACH of Blueprint / BTree / HSM, trace and reconcile **write location vs scan location**:
1. **Find the contributor's scan root** (e.g. blueprint `bpRootDir`; for BTree/HSM see `BTreeJsonAssetContributor` /
   `HsmJsonAssetContributor` construction in `EditorSubsystem.cs` ~L682-683 — what dir/source do they enumerate?).
2. **Find where the new asset is written** (Blueprint: `saveAsBlueprintToFile` via `AssetSavePath.Compose` →
   `AssetsFor` OUTPUT; BTree/HSM: their `INewAssetService.CreateNew` persists JSON itself per DEC-12 — find where).
3. **Make them match:** write the new asset into the directory the contributor scans (prefer the **source** project
   dir, like the working old `RecipeCreateModal` which wrote under the resolved source project dir). For Blueprint,
   `AssetSavePath.Compose` has an **`assetRootOverride`** param — pass the contributor's scan root (`bpRootDir`) so
   the file lands where `bpContrib.Refresh()` will find it. For BTree/HSM, make `CreateNew`'s write target (or the
   composed path you pass) equal the JSON contributor's scan root + `dest`/`name`.
4. **Refresh the right contributor after write:** ensure the New-flow calls the refresh that rescans that location
   (Blueprint: `RefreshFromAssembly` already calls `bpContrib.Refresh()`; BTree/HSM JSON contributors may need their
   own refresh — find/call it, e.g. a `RefreshJsonContributors()` on `AiAssetCatalogBuilder`, or the contributor's
   `Refresh()`). THEN `FindByAssetId(minted.AssetId)` → `_aiDocumentManager.Open(catalogued)`.
5. **Verify the perspective switches** to the kind's canvas on open (it should, via the wired `_perspectiveSwitcher`
   → `Activate`). If it does not for a kind, fix that too.
6. **BUG-A9 (Save-As disabled):** likely a symptom of A6 (no active document). After A6, confirm `shell.saveAs`
   (and `shell.save`) become enabled once a document is open; if the enable logic itself is wrong, fix it.

## Hard requirements
- **Do NOT rely on headless tests as proof.** Trace the real path in source and confirm each link connects
  (write→scan→refresh→FindByAssetId→Open→Activate→perspective). Explain the path per kind in the report.
- Touch the minimum needed (`EditorSubsystem.cs`, possibly `AssetSavePath.cs`/`AiAssetCatalogBuilder.cs`/the
  BTree/HSM `INewAssetService` write target). Do NOT regress opening EXISTING (file-backed) assets. Keep null-safe.
- No test weakening / skips / stubs. Build 0 warnings.
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`: `dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj`;
  `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj` (Failed:0); `Hrot.Blueprints.Tests`
  (filtered `EditorSubsystemBlueprintWindows`) Failed:0; full `Hrot.Blueprints.Tests` stays at the ~9 PRE-1 baseline
  (no NEW failures).

## Definition of done
- New Blueprint / BTree / HSM: created → discovered → **opened in its canvas + perspective switched** (traced in
  source per kind). BUG-A9 resolved/verified. Build 0 warnings; tests Failed:0; no new Blueprints failures.
- Write `.dev/main-toolbar-2/reports/BATCH-44-REPORT.md`: the per-kind write-vs-scan reconciliation, the exact
  end-to-end path you verified in source for each kind, what you changed, and the test summaries. Note that the live
  GUI confirmation is the user's (you cannot run the editor).

If something cannot be done as specified, STOP and report why rather than stubbing.
