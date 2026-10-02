# BATCH-23 Review
**Status:** ✅ APPROVED (after lead-applied round-3 corrective)   **Date:** 2026-06-11

## Summary
MTB-P7-T5: retired the Blueprints `AssetBrowserWindow`, `FileSystemAssetCatalog`, and the Blueprints
`IAssetCatalog`/`AssetCatalogEntry`; salvaged the peer-signature scan into `BlueprintPeerSource`
(contributor-style Guid→Path) per DEC-13. **Completes Phase 7.**

## Issues Found — TWO real regressions the worker mislabeled as "pre-existing" (caught in review)
The worker's reports claimed all extra failures were pre-existing/flaky. Lead verification against the
pre-retirement baseline `f24659de` + isolated runs proved otherwise. Round-1 mislabeled; round-2 worker
call returned empty output (no-op). Given the precise diagnosis + faltering worker + end-of-project,
the lead applied the (small, well-bounded) corrective fixes directly:

### Regression 1 (introduced earlier, BATCH-21; surfaced now): `RegisterWindows` threw
`ArgumentNullException('editorLogic')` — the BATCH-21 `ScenarioMenuCommands.Register` call in
`EditorSubsystem.RegisterWindows` was not null-guarded, so a minimally-constructed `EditorSubsystem`
(8 `EditorSubsystemBlueprintWindowsTests`) aborted before registering the perspective windows.
**Fix:** null-guard the scenario-menu wiring (`if (_editorLogic != null) ScenarioMenuCommands.Register(...)`).
Also the docked host was registered with id `"AssetBrowser"` instead of the prior `"ai_asset_browser"`
the test asserts (T4 said "prior id/scope") → registered with `id: "ai_asset_browser"`.

### Regression 2 (introduced by THIS batch): quick-reload broken
The round-1 swap pointed the test peer-sources at `Path.GetTempPath()` (the whole user temp tree) →
`UnauthorizedAccessException` on `%TEMP%\WinSAT`, then (after a robustness fix) duplicate-AssetId throw
from unrelated fixture `*.bp.json`. **Fix:** (a) `BlueprintPeerSource.EnumerateAll` now uses
`EnumerationOptions{RecurseSubdirectories,IgnoreInaccessible=true}`; (b) `QuickReloadServiceTests` +
`BlueprintCompileOnDemandMveTests` peer-sources point at a **dedicated empty temp dir** (restoring the
prior empty-stub semantics). The 3 QuickReload tests now pass in isolation (and dropped 42s→~1s).

## Verification (done by lead)
- Full `dotnet build IOS-IG-SimHost.sln` → 0 errors, 0 new warnings.
- 3 types confirmed deleted; AiShared `IAssetCatalog` untouched (different interface).
- `Hrot.Blueprints.Tests` (Stability filter) → **exactly the 9 PRE-1 failures, no others** (the 8 window
  + 3 quick-reload tests all GREEN; Passed 1842→1853). `Hrot.Editor.Tests` 176/0, AiShared 1014/0,
  Fdp.Toolkits 1856/0, SimHost 585/0.

## Verdict
APPROVED. MTB-P7-T5 → `[x]`. **Phase 7 complete. All 39 tracker tasks done.**

## Commit Message
```
feat(main-toolbar): retire Blueprints AssetBrowserWindow + FileSystemAssetCatalog (MTB-P7-T5)

Delete the Blueprints AssetBrowserWindow, FileSystemAssetCatalog, and the Blueprints IAssetCatalog/
AssetCatalogEntry; salvage the peer-blueprint-signature scan into BlueprintPeerSource (Guid→Path,
IgnoreInaccessible) wired into BlueprintDocumentFactory + QuickReloadService + EditorSubsystem
quick-reload (DEC-13). Lead correctives: null-guard EditorSubsystem.RegisterWindows scenario-menu
wiring + register docked host with prior id "ai_asset_browser" (fixes 8 window tests); point quick-
reload test peer-sources at empty temp dirs (fixes 3 quick-reload tests). Blueprints.Tests = the 9
pre-existing failures only. Completes Phase 7.
```
