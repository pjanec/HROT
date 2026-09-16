# BATCH-19 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-11

## Summary
MTB-P6-T7/T5: `AssetSavePath.Compose` (subfolder-aware, root-bounded, per-kind extension) wired into
BTree/HSM services; `NewAssetDialog` model (kind+recipe+name+FolderPickerState → collision-check →
CreateNew → save → callback) with explicit DEC-12 reconciliation.

## Issues Found
No issues found.

## Verification (done by lead)
- `dotnet build IOS-IG-SimHost.sln` → 0 errors, 0 new warnings.
- New tests run by lead: `AssetSavePathTests` (22) + `NewAssetDialogTests` (16) → **38 passed, 0 failed**.
  Suites green: AiShared 1007, Fdp.Toolkits 1856, SimHost 585, BTree.Editor 406, Hsm.Editor 358.
- `AssetSavePath.Compose`: `AssetsFor(kind)/relPath/name.ext`, root-escape validation (no `..`/absolute),
  `GetExtension` `.bp.json`/`.btree.json`/`.hsm.json` (throws for Scenario). Matches §18.4.
- `NewAssetDialog` documents + implements DEC-12: `CreateNew` for all kinds, then an extra save ONLY
  for Blueprint (mint-only) via an injected `_saveMintOnlyAsset` delegate; BTree/HSM/Scenario persist in
  `CreateNew` (no double-write). Reuses `AssetBaseNameCollisionGuard`. `CanConfirm` pure; `Confirm`
  returns success+asset or collision error; invokes `onCreated` callback. Matches §18.2.
- Scope: 2 new source + 2 wiring edits (BTree/HSM services) + 2 test files. No legacy deletions; no
  Save-As dialog (T6 deferred).

## Test Quality
Strong. SavePath tests cover composition, extensions, and root-escape rejection. Dialog tests cover
`Confirm_WritesFile_AtAssetsRootRelPath_WithFreshId` (real write at relpath, fresh id),
`CollisionGuard_RejectsExistingBaseName` (no write on collision), `Callback_ReceivesNewAsset`. No
tautological/skipped tests.

## Verdict
APPROVED. MTB-P6-T7, MTB-P6-T5 → `[x]`. Phase 6 continues (T6 remains).

## Commit Message
```
feat(main-toolbar): subfolder-aware save + New Asset dialog (MTB-P6-T7, T5)

AssetSavePath.Compose builds AssetsFor(kind)/relPath/name.ext (per-kind extension, root-bounded);
wired into BTree/HSM services. NewAssetDialog (Hrot.Editor.AiShared/Recipes): kind+recipe(incl Empty)
+name+FolderPickerState → AssetBaseNameCollisionGuard check → INewAssetService.CreateNew (fresh id) →
save (DEC-12: Blueprint via injected save delegate; BTree/HSM/Scenario persist in CreateNew) →
onCreated callback; testable CanConfirm/Confirm. Tests: 38 new. Save-As dialog (T6) next.
```
