# BATCH-19: Subfolder-aware save + New Asset dialog
**Tasks:** MTB-P6-T7, MTB-P6-T5   **Phase:** 6   **Est:** ~11h
**Dependencies:** BATCH-17 (`INewAssetService`), BATCH-18 (per-kind impls, `FolderPickerState`).

> Do T7 then T5 in sequence (the dialog uses subfolder-aware save). Do NOT advance until the current
> task's impl + tests pass.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/_DONE/main-toolbar-1/DESIGN.md` §18.4 (subfolder-aware save), §18.2 (dialogs), §18.5 (identity rule).
3. `.dev/_DONE/main-toolbar-1/TASK-DETAIL.md` → MTB-P6-T7, MTB-P6-T5.
4. Existing code (find via codebase-memory + read):
   - `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/AssetBaseNameCollisionGuard.cs` — the existing
     path-at-creation collision guard (REUSE it).
   - `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetRoots.cs` — `AssetsFor(kind)` (write base).
   - `Hrot/Editor/Hrot.Editor.AiShared/Recipes/INewAssetService.cs` + the per-kind impls (BATCH-17/18).
   - `Hrot/Editor/Hrot.Editor.AiShared/Browser/FolderTreePicker.cs` → `FolderPickerState` (BATCH-18).
   - The Save path: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/SaveActiveBlueprintCommand.cs`
     + how `ShellSaveCommands`/`EditorSubsystem` save per kind (BATCH-06).
   - `AssetRelPath` (BATCH-10) + `ScenarioEnumeration` (BATCH-16) for round-trip scanning.

## Task 1 — Subfolder-aware file save (MTB-P6-T7) — §18.4
- Ensure file saves compose the path as **`AssetRoots.AssetsFor(kind)/<relpath>/<name>.<ext>`**
  (`.bp.json`/`.btree.json`/`.hsm.json`), creating the nested folder. Extract a small testable
  helper, e.g. `AssetSavePath.Compose(AssetKind kind, string relPath, string baseName)` → absolute
  path under the kind's Assets root (normalize separators; root-bounded — no `..`/absolute escape,
  reuse the FolderPickerState sanitization rules or AssetBaseNameCollisionGuard where applicable).
- Wire the file-save path so an asset with a subfolder relpath is written there (and the existing
  `Save` of an asset whose `SourceFilePath` already includes a subfolder keeps writing to that path).

**Tests required:**
- `Save_PreservesSubfolder_RoundTrip` — compose a save path for a nested relpath (e.g. `combat/Guard`),
  write a file there (real or via the kind's JSON service), then a **recursive scan** (the kind's
  contributor / `AssetRelPath`) finds the asset at the SAME relpath `combat/Guard`. Use a temp Assets root.

## Task 2 — New Asset dialog (MTB-P6-T5) — §18.2
A thin dialog (logic separated from ImGui draw; the success conditions are logic-level). Place it so
it can reach the per-kind `INewAssetService` impls (inject a set/registry of `INewAssetService` keyed
by `AssetKind`, so the dialog stays decoupled — likely `Hrot.Editor` or AiShared with injected
services). Model:
- Inputs: **Kind** (from the permitted set), **Recipe** (dropdown incl. the in-code "Empty" from
  `service.AvailableRecipes()`), **Name**, **`FolderPickerState`** (pick subfolder under the kind's
  Assets root), and the **collision guard**.
- On **Confirm**: collision-check the chosen `name` at the picked relpath (reject existing base name
  via `AssetBaseNameCollisionGuard`); if OK → `service.CreateNew(recipe, name, relPath)` (mints fresh
  `AssetId`); then **save** under `AssetsFor(kind)/<relpath>/<name>.<ext>` (DEC-12: for BTree/HSM/
  Scenario the per-kind `CreateNew` already persisted; for **Blueprint** (mint-only) the dialog
  performs the subfolder-aware save from T7). Then invoke the caller callback with the new asset.
- Expose testable seams: `bool CanConfirm()` / `ConfirmResult Confirm()` (returns success + the new
  asset or a collision error); `IReadOnlyList<IEditableAsset> RecipesForKind(AssetKind)`.

**Tests required (`NewAssetDialogTests`, fakes for services/guard + temp root):**
- `Confirm_WritesFile_AtAssetsRootRelPath_WithFreshId` — confirm with kind+recipe+name+relpath writes
  the file at `Assets/<Kind>/<relpath>/<name>.<ext>` with a fresh `AssetId`.
- `CollisionGuard_RejectsExistingBaseName` — confirming a name that already exists at the relpath is
  rejected (no write, error surfaced), via the collision guard.
- `Callback_ReceivesNewAsset` — on success the caller callback receives the newly minted asset
  (correct Kind/Name/fresh id).

## Hard constraints
- REUSE `AssetBaseNameCollisionGuard` (do not reimplement). Honor DEC-12 (Blueprint mint-only → dialog
  saves it; others persist in CreateNew — do not double-write). Do NOT build the Save-As dialog
  (MTB-P6-T6). No scope creep.
- Do NOT delete/modify legacy/assembly-loading code. Do NOT weaken/skip/auto-pass tests; zero new
  warnings (TreatWarningsAsErrors).

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings).
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New tests pass UNFILTERED. 0-failed with the Stability
  filter for the affected editor test projects + the hot suites `Fdp.Toolkits.Tests` +
  `Hrot.SimHost.Tests` (PRE-3 EQS flake → re-run; for `Hrot.Blueprints.Tests` run new tests by class
  filter and do NOT touch PRE-1 pre-existing failures).
- Write `.dev/_DONE/main-toolbar-1/reports/BATCH-19-REPORT.md`: files changed, the save-path helper +
  root-bounding, the dialog model seams + per-kind save reconciliation (DEC-12), collision-guard reuse,
  each new test + assertions, paste actual test-run summaries, insights.

If something cannot be done as specified, stop and report why rather than stubbing it.
