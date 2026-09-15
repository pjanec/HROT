# BATCH-18 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-11

## Summary
MTB-P6-T3/T4: BTree/HSM/Scenario `INewAssetService` impls (BTree/HSM mint+write JSON; Scenario routes
to `IEditorLogic` via an `IScenarioCreationSession` seam) + `FolderPickerState` pick mode (root-bounded).

## Issues Found
No issues found.

## Verification (done by lead)
- `dotnet build IOS-IG-SimHost.sln` → 0 errors, 0 new warnings.
- New tests run by lead: FolderTreePickerPickTests 17/17, BTreeNewAssetTests 7/7, ScenarioNewAssetTests
  6/6 (HSM 6/6 per worker). 36 total. Suites green (AiShared 968, BTree.Editor 406, Hsm.Editor 358,
  Hrot.Editor 156, Fdp.Toolkits 1856, SimHost 585). The lone AiShared "flaky" is the known
  order-dependent test-infra flake (passes in isolation) — not from this batch.
- DEC-12 honored: BTree/HSM `CreateNew` mint fresh id + write valid JSON under
  `AssetRoots.AssetsFor(kind)/<relPath>` via `BTreeJsonServices`/`HsmJsonServices` (+AtomicFileWriter),
  round-trip tested; Scenario routes Empty→`NewScenario`+`SaveScenarioAs`, FromSeed→load+`SaveScenarioAs`
  via the testable `IScenarioCreationSession` seam.
- `FolderPickerState`: `SelectedRelPath`, `AddFolder(parent,name)→relPath`, with thorough root-bounding
  (rejects `..` anywhere, absolute paths, drive letters; per-segment validation). Logic ImGui-free.
- Scope: 8 new files. Blueprint impl stays mint-only. No legacy deletions, no dialog work (T5/T6).

## Test Quality
Strong. BTree/HSM tests assert written JSON exists at the relpath, fresh AssetId, and round-trip
deserialization. Scenario tests assert the exact `IEditorLogic` call sequence via a fake seam.
Pick tests assert AddFolder relpaths, existing-folder selection, and `CannotEscapeRoot` (no `..` in
any produced path). No tautological/skipped tests.

## Verdict
APPROVED. MTB-P6-T3, MTB-P6-T4 → `[x]`. Phase 6 continues (T5/T6/T7 remain).

## Commit Message
```
feat(main-toolbar): BTree/HSM/Scenario new-asset services + FolderTreePicker pick mode (MTB-P6-T3, T4)

BTreeNewAssetService/HsmNewAssetService mint fresh-id assets and write valid JSON under
AssetRoots.AssetsFor(kind)/relPath via the JSON services (round-trip tested). ScenarioNewAssetService
routes to IEditorLogic via IScenarioCreationSession seam (Empty→NewScenario+SaveScenarioAs;
FromSeed→load+SaveScenarioAs). FolderTreePicker pick mode (FolderPickerState): AddFolder/selection
yielding root-relative paths, bounded to root (no .. / absolute escape). Tests: 36 new.
```
