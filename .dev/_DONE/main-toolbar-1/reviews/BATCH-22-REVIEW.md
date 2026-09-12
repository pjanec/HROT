# BATCH-22 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-11

## Summary
MTB-P7-T2/T4: deleted `ScenarioBrowserPanel` and AiShared `AssetBrowserWindow` (+ their tests);
registered `AssetBrowserDockedWindow` in the old window's place with an `AiDocumentManager.Open`
activation callback.

## Issues Found
No issues found.

## Verification (done by lead)
- **Full `dotnet build IOS-IG-SimHost.sln` → 0 errors** (20 pre-existing warnings; touched AiShared
  project rebuilds `--no-incremental` with **0 warnings** → no new warnings).
- **Authorized-deletion audit (critical):** exactly 4 files deleted — AiShared `AssetBrowserWindow.cs`
  + `AssetBrowserWindowTests.cs`, `ScenarioBrowserPanel.cs` + `ScenarioBrowserPanelTests.cs`. The
  Blueprints `AssetBrowserWindow` and `FileSystemAssetCatalog` are correctly **still present** (T5).
  No other deletions. No dangling `ScenarioBrowserPanel`/AiShared-`AssetBrowserWindow` references.
- Tests run by lead: `SharedAiEditorDiTests` + `AssetBrowserDockedWindowTests` → 23/23. Suites green:
  AiShared 1014, Hrot.Editor.Tests 176, Fdp.Toolkits 1856, SimHost 585.
- Docked host registered with `ExpectedId="AssetBrowser"`, `WindowScope.Global`, activation callback
  → `AiDocumentManager.Open` (null-safe); `CustomToolbarDraw` retained for the recipe modal toolbar.
  Scenario LOGIC in `IEditorLogic`/`EditorApplication` untouched (only panel wiring removed).

## DBT-2
Docked-host wiring now complete (host registered + Open callback). Combined with BATCH-21 (Load picker
+ Save-As dialog surfaced), DBT-2 is essentially resolved; any residual Save-As ImGui popup rendering
is minor surfacing polish.

## Test Quality
Adequate. Obsolete tests for deleted types removed; `SharedAiEditorDiTests` updated to resolve the
docked host with `ExpectedId`. Docked-host activation→Open covered.

## Verdict
APPROVED. MTB-P7-T2, MTB-P7-T4 → `[x]`. Phase 7 remainder: MTB-P7-T5 (Blueprints AssetBrowserWindow +
FileSystemAssetCatalog retirement).

## Commit Message
```
feat(main-toolbar): retire ScenarioBrowserPanel + AiShared AssetBrowserWindow (MTB-P7-T2, T4)

Delete ScenarioBrowserPanel (logic now in the Scenario menu, BATCH-21) and the AiShared
AssetBrowserWindow (open-docs now in the Workspace submenu). Register AssetBrowserDockedWindow
(Id="AssetBrowser", Global) in its place via DI + SharedAiWindowRegistrar, with an
AiDocumentManager.Open activation callback (completes DBT-2 docked-host wiring). Obsolete tests
removed; SharedAiEditorDiTests updated. Only the two authorized types deleted.
```
