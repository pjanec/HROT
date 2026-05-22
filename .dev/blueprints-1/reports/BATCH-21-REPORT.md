# BATCH-21 Report: TASK-ED-001 -- Editor Infrastructure, Window Lifecycle, IWindowRegistrar

**Batch:** BATCH-21
**Task:** TASK-ED-001
**Status:** COMPLETE
**Commit:** 24f32829

---

## Summary

All 13 production files created in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/`.
All 3 test files created in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/`.
Tests pass: 439 total (434 pass / 0 fail / 5 skip). Target met.

---

## Files Created

### Production (Hrot.Blueprints.Editor/)

| File | Description |
|------|-------------|
| `IBlueprintEditorWindow.cs` | Interface for all editor windows |
| `BlueprintEditorWindowBase.cs` | Abstract base with ToggleVisible, IsVisible |
| `DirtyTracker.cs` | Tracks dirty asset GUIDs via HashSet |
| `EditorSelectionStore.cs` | Holds selected BlueprintAsset, fires OnSelectionChanged |
| `IOutputConsole.cs` | Logging interface using Microsoft.CodeAnalysis.Diagnostic |
| `EditorState.cs` | In-memory asset map keyed by AssetId |
| `IAssetCatalog.cs` | IAssetCatalog interface + AssetCatalogEntry record |
| `FileSystemAssetCatalog.cs` | Walks directory for *.bp.json, reads AssetId from JSON |
| `ReloadInfo.cs` | ReloadSource enum + ReloadCompletedInfo record (Patch 2) |
| `IWindowRegistrar.cs` | RegisterMenuEntry / RegisterToolbarEntry / RegisterShortcut |
| `BlueprintEditorModule.cs` | Owns windows, OnEditorActivated/Deactivated, DrawAllWindows, OnReloadCompleted |
| `EngineTimeControllerAdapter.cs` | IBlueprintTimeController stub (TODO M13) |
| `BlueprintEditorServiceCollectionExtensions.cs` | AddBlueprintEditor DI helper |

### Test Helpers and Tests (Hrot.Blueprints.Tests/Editor/)

| File | Description |
|------|-------------|
| `MockWindowRegistrar.cs` | Captures RegisterMenuEntry/Toolbar/Shortcut calls |
| `CountingWindow.cs` | BlueprintEditorWindowBase stub counting DrawUI calls |
| `EditorInfrastructureTests.cs` | 10 tests SC1-SC10 |

---

## csproj Changes

- `Hrot.Blueprints.Editor.csproj`: added `Microsoft.Extensions.DependencyInjection` 8.0.0 package reference.
- `Hrot.Blueprints.Tests.csproj`: added project reference to `Hrot.Blueprints.Editor`.

---

## Notable Decisions

- `BlueprintAsset` is in `Hrot.Blueprints.Core.Assets` (not `Fdp.Toolkit.Blueprints` as the instructions stated). Used the correct namespace.
- `IBlueprintTimeController` is in `Hrot.Blueprints.Core.Debug`. `EngineTimeControllerAdapter` uses `using Hrot.Blueprints.Core.Debug`.
- `Microsoft.CodeAnalysis` is available transitively through `Hrot.Blueprints.Core` (which has `Microsoft.CodeAnalysis.CSharp` 4.8.0).

---

## Test Results

| Metric | Before | After |
|--------|--------|-------|
| Total | 429 | 439 |
| Passed | 424 | 434 |
| Failed | 0 | 0 |
| Skipped | 5 | 5 |

---

## Success Criteria

| SC | Result |
|----|--------|
| SC1-SC2 | DirtyTracker mark/clean/query works correctly -- PASS |
| SC3-SC4 | EditorSelectionStore fires event + updates property -- PASS |
| SC5-SC6 | EditorState set/get/remove in-memory asset -- PASS |
| SC7 | FileSystemAssetCatalog returns no entries for empty directory -- PASS |
| SC8 | BlueprintEditorModule registers menu entries on activation -- PASS |
| SC9 | DrawAllWindows only draws visible windows -- PASS |
| SC10 | EngineTimeControllerAdapter implements interface + doesn't throw -- PASS |
| Build | `dotnet build Hrot.Blueprints.Editor` zero errors -- PASS |
| Tests | 0 failures full suite -- PASS |
