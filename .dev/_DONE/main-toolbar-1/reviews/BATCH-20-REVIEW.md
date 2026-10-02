# BATCH-20 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-11

## Summary
MTB-P6-T6: `SaveAsDialog` (fresh-AssetId duplicate semantics over the current document) + DEC-9
resolution (`ShellSaveCommands.requestSaveAs` now drives a `SaveAsDialog`). Completes Phase 6.

## Issues Found
No issues found.

## Verification (done by lead)
- `dotnet build IOS-IG-SimHost.sln` → 0 errors, 0 new warnings.
- New tests run by lead: `SaveAsDialogTests` → **17 passed, 0 failed** (incl. the 4 named). Suites
  green: AiShared 1024, Fdp.Toolkits 1856, SimHost 585 (re-run; the 1 ZeroAlloc/EQS flake is PRE-1/3 family).
- `SaveAsDialog` read: every Confirm mints a **fresh AssetId** via `INewAssetService.CreateNew(source,
  name, relPath)` (≠ source — §18.5); Scenario routes via a dedicated `saveScenarioAs` delegate (NOT
  CreateNew, which would reload) — sound distinction. Reuses `AssetSavePath`, collision guard,
  `FolderPickerState`, `ConfirmResult`. `CanConfirm` pure.
- DEC-9 connection in `EditorSubsystem`: a per-kind `INewAssetService` registry + `requestSaveAs`
  seeds a `SaveAsDialog` from the active document's asset and confirms. In-scope, coherent. Residual
  ImGui popup rendering for the dialog is folded into DBT-2 (Phase 7 surfacing) — worker updated the
  debt tracker (DEC-9 → ✅; DBT-2 extended).
- Scope: 2 new (SaveAsDialog + adapter) + EditorSubsystem wiring + tests. No legacy deletions.

## Test Quality
Strong. `SaveAs_WritesNewFile_WithFreshAssetId` asserts new id ≠ source AND the round-tripped file
carries the fresh id; `SaveAs_RespectsPickedRelPath`, `CollisionGuard_RejectsExistingBaseName`,
`EmptySourcePathSave_RoutesToSaveAs` all present. No tautological/skipped tests.

## Verdict
APPROVED. MTB-P6-T6 → `[x]`. **Phase 6 complete.** DEC-9 resolved (residual UI glue → DBT-2/Phase 7).

## Commit Message
```
feat(main-toolbar): Save-As dialog (fresh-id duplicate semantics) + DEC-9 wiring (MTB-P6-T6)

SaveAsDialog (Hrot.Editor.AiShared/Recipes): over the current document, Confirm mints a FRESH
AssetId via INewAssetService.CreateNew (≠ source, §18.5), saves under the picked relpath (DEC-12
per-kind; Scenario via saveScenarioAs delegate), collision-guarded. EditorSubsystem connects
ShellSaveCommands.requestSaveAs (DEC-9) to seed+confirm a SaveAsDialog from the active document;
ImGui popup rendering deferred to Phase 7 (DBT-2). Tests: 17 new. Completes Phase 6.
```
