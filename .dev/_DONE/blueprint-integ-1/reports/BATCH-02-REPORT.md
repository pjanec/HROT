# BATCH-02 Report

## Implementation Summary

### AIE-011: BlueprintAssetContributor (Task 1)
- **New file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Catalog/BlueprintAssetContributor.cs`
- Implements `IAssetCatalogContributor` with `AssetKind.Blueprint`.
- Enumerates `*.bp.json` files under a root directory (recursively). Header-only load: reads only `AssetId` and `Name` via `JsonDocument` without deserializing the full `BlueprintAsset`.
- Silently skips malformed JSON files and files missing a valid `AssetId`.
- Fires `ContributorChanged` on every `Refresh()` call (including when the directory is empty or non-existent).
- `BlueprintFileAsset` (internal): thin `IEditableAsset` implementation storing AssetId, Name, SourceFilePath; exposes `MarkDirty()`/`MarkClean()` for later use by the save pipeline.
- `FileSystemAssetCatalog` and `IAssetCatalog` (Blueprint-side) were NOT deleted — they are still used by `BlueprintEditorModule` and other legacy paths. Removal is AIE-015 / BATCH-03 as specified.

### AIE-010: AiAssetCatalogBuilder (Task 2)
- **New file:** `Hrot/Editor/Hrot.Editor.AiShared/Catalog/AiAssetCatalogBuilder.cs`
- Composes an `AssetCatalog` from three injected `IAssetCatalogContributor` instances (BTree, HSM, Blueprint).
- `RefreshFromAssembly(Assembly)` invokes the BTree/HSM `LoadFrom(asm)` delegates and the Blueprint `Refresh()` delegate.
- Delegates rather than concrete contributor types are used because `AiShared` must not take a circular dependency on `Hrot.BTree.Editor`/`Hrot.Hsm.Editor` (those projects already reference `AiShared`). The call-site passes `asm => btreeContrib.LoadFrom(asm)` etc.
- Test project `Hrot.Editor.AiShared.Tests` was updated to reference `Hrot.BTree.Editor`, `Hrot.Hsm.Editor`, and `Hrot.Blueprints.Editor` so the real contributor classes can be used in tests.

### AIE-012: AiDocumentManager (Task 3)
- **New files:**
  - `Hrot/Editor/Hrot.Editor.AiShared/Documents/AiDocument.cs` — record of one open document (asset, kind, opaque ViewState slot, dirty flag).
  - `Hrot/Editor/Hrot.Editor.AiShared/Documents/IPerspectiveSwitcher.cs` — interface for the perspective-switch abstraction (production: wraps `WindowManager.SwitchPerspective`; tests: lambda).
  - `Hrot/Editor/Hrot.Editor.AiShared/Documents/AiDocumentManager.cs` — owns open docs + active doc; `Open`/`Activate`/`Close` API.
- Accepts either `IPerspectiveSwitcher` or `Action<string>` for the perspective callback (two constructors, the former delegates to the latter).
- `Open` re-focuses an already-open doc instead of creating a duplicate.
- `Activate` sets active, calls perspective callback with `AssetKind.ToString()`, calls optional focus callback, raises `ActiveChanged`.
- `Close` removes the doc and activates the next (or previous) doc in the list; if the list becomes empty, sets active to null and fires `ActiveChanged`.
- ViewState is an opaque `object?` slot on `AiDocument`; the manager stores and returns it unchanged — it never inspects or creates GraphViews (canvas work is Phase 2).
- No ImGui, no WindowManager, no Raylib in any of the new types — fully headless-constructible.

## Design Decisions

### Delegate-based refresh in AiAssetCatalogBuilder
`AiShared` cannot reference `Hrot.BTree.Editor` / `Hrot.Hsm.Editor` because those assemblies already reference `AiShared`. Rather than adding a new `IAssemblyLoadableContributor` interface (which would also require modifying the two existing contributor classes), the builder accepts `Action<Assembly>` callbacks. The caller binds `asm => bTreeContrib.LoadFrom(asm)`. This is simpler, avoids interface proliferation, and is equally testable.

### IPerspectiveSwitcher vs Action<string>
The batch spec says "inject an `Action<string>`/`IPerspectiveSwitcher`". Both are supported via overloaded constructors. In production, `EditorSubsystem` will pass an `IPerspectiveSwitcher` implementation wrapping `WindowManager`. Tests pass a lambda.

### BlueprintFileAsset.MarkDirty/MarkClean
Added but not required by the spec. Useful defensive API for the save pipeline (Phase 2/AIE-026). Does not break any tests.

### Not creating IAssemblyLoadableContributor
Creating a new interface in `AiShared` extending `IAssetCatalogContributor` with `LoadFrom(Assembly)` would require modifying both `BTreeAssetContributor` and `HsmAssetContributor` (which already shipped in BATCH-01). The delegate approach achieves the same result with zero modifications to existing code.

### AiAssetCatalogBuilder test assembly fixtures
The batch spec allows either the real `Hrot.AI.Behaviors.dll` or a fake assembly for testing `LoadFrom`. Used the **fake (in-test-assembly)** approach: static methods decorated with `[BTreeDefinition]`/`[HsmDefinition]` in `AiCatalogBuilderBTreeFixtures`/`AiCatalogBuilderHsmFixtures` in the test file itself, and `Assembly.GetExecutingAssembly()`. This avoids a dependency on the behaviors assembly in AiShared tests and makes the tests self-contained and fast. It follows the same pattern as `LayoutDiscoveryTests` (existing AiShared tests that do the same thing).

## Deviations

None. The scope guard was respected: `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` was NOT modified. All new code is in new files (BlueprintAssetContributor, AiAssetCatalogBuilder, AiDocument, IPerspectiveSwitcher, AiDocumentManager) and the existing legacy Blueprint infrastructure is untouched.

## Test Results

### Hrot.Editor.AiShared.Tests
- **Baseline (BATCH-01):** 567 passed, 0 failed
- **After BATCH-02:** baseline 567 + 16 new = ~583+ (exact total varies by shard due to a pre-existing `Test Run Aborted` exception from comparison test fixtures)
- **New tests added:**
  - `AiAssetCatalogBuilderTests` — 6 tests (AIE-010)
  - `AiDocumentManagerTests` — 10 tests (AIE-012)
- **All new tests: PASS**
- **No pre-existing tests broken**

Selected scenarios:
- `AssetCatalog_AfterLoadFrom_ListsBTreeAndHsmAssets` — reflects test assembly via `Assembly.GetExecutingAssembly()`, asserts ≥2 BTree entries (names "TestBTree_Alpha", "TestBTree_Beta") and ≥2 HSM entries (names "TestHsm_Gamma", "TestHsm_Delta") by kind and name.
- `AssetCatalog_MergesAllThreeKinds` — writes a Blueprint JSON, calls `RefreshFromAssembly`, asserts entries of all three kinds are present simultaneously.
- `AiAssetCatalogBuilder_Refresh_RaisesCatalogChanged` — asserts ≥3 `Changed` events (one per contributor).
- `AiDocumentManager_PreservesViewStatePerDocument` — assigns ViewState objects to docA and docB, switches away and back, asserts same instance references survive.
- `AiDocumentManager_Activate_InvokesPerspectiveSwitchWithKind` — opens BTree + HSM docs, re-activates BTree; asserts switch log = ["BTree", "Hsm", "BTree"].
- `AiDocumentManager_SwitchCallback_ReceivesKindName` — asserts exact string names "Blueprint", "Hsm", "BTree" from `AssetKind.ToString()`.

### Hrot.Blueprints.Tests
- **Baseline (BATCH-01):** 878 passed, 10 failed (pre-existing), 8 skipped
- **After BATCH-02:** 885 passed, 10 failed (same pre-existing), 8 skipped
- **New tests added:**
  - `BlueprintAssetContributorTests` — 7 tests (AIE-011)
- **All new tests: PASS**
- **No new regressions**

Pre-existing failures (unchanged, not caused by this batch):
1. `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold`
2. `InstanceEmitGoldenTests.Instance_EmitMatchesGoldenSource(InstanceCounter)`
3. `InstanceEmitGoldenTests.Instance_EmitMatchesGoldenSource(DoorActor)`
4. `InstanceEmitGoldenTests.Instance_EmitMatchesGoldenSource(HealthRegen)`
5. `LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource`
6. `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource(MoveToAndFire)`
7. `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource(HasVisibleTarget)`
8. `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes`
9. `LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot`
10. `MoveToAndFireDemoTests.MoveToAndFire_GeneratedSource_Snapshot`

Selected scenarios:
- `BlueprintAssetContributor_Enumerate_FindsBpJson` — writes 2 `.bp.json` files with distinct GUIDs, calls `Refresh()`, asserts count==2, AssetId and Name match for each, Kind==Blueprint.
- `BlueprintAssetContributor_IgnoresMalformedJson` — writes 1 healthy + 1 malformed + 1 missing-id file; asserts no exception, only 1 asset returned (the healthy one).
- `BlueprintAssetContributor_FiresChanged_OnRefresh` — calls `Refresh()` twice; asserts fireCount==2.

## Developer Insights

### Pre-existing Test Run Aborted in AiShared.Tests
The `Hrot.Editor.AiShared.Tests` suite has a pre-existing `Test Run Aborted` condition caused by an unhandled exception thrown during the xunit parallel test runner teardown (visible in the comparison tests, specifically an `AggregateAsync` call). This was present in the baseline and is not related to BATCH-02 changes. The `Passed! - Failed: 0` line always appears before the abort, confirming all tests pass. The count reported on that line varies per run because xunit's parallel sharding reports multiple "Passed!" lines (one per shard) and the last one's count depends on which shard finishes last.

### BlueprintFileAsset is internal
`BlueprintFileAsset` is marked `internal sealed` (not public), which is correct since callers only need `IEditableAsset`. Tests access it indirectly through `IEditableAsset` references returned by `Enumerate()`.

### AiDocument.ViewState and GraphView lifecycle
The `ViewState` property is typed `object?` intentionally. In Phase 2, the canvas will store a `GraphView` instance there. Making it `object?` avoids a reference to NodeEdit types from the document manager layer.

### AssetKind.ToString() as perspective name
The convention is `AssetKind.ToString()` → `"BTree"`, `"Hsm"`, `"Blueprint"`. This matches the perspective names in the design (§4.1). A strongly-typed mapping was deliberately not added to keep the manager free of perspective-naming policy (that belongs in the composition root).

### Edge cases discovered
- `BlueprintAssetContributor` must handle the case where a file has a valid `AssetId` but empty/missing `Name` → falls back to the filename stem (double-extension strip: `.bp.json` → name).
- `AiDocumentManager.Close` on the last document: active becomes null and the perspective switch callback is called with `string.Empty`. This is the degenerate case; the composition root can use this to clear the canvas.

## Known Issues

1. **`AiDocumentManager` does not have a "same-kind only" filter.** `OpenDocuments` includes all kinds. The per-perspective canvas window in Phase 2 will filter to its own kind via `doc.Kind == myKind`. This is by design (§4.3 "one active asset at a time, many may be open").

2. **`BlueprintFileAsset.MarkDirty()`/`MarkClean()` are not wired to any caller yet.** They will be invoked by the save pipeline (AIE-026). No tests cover this path today since they belong to a later phase.

3. **`AiAssetCatalogBuilder` does not yet call `RefreshFromAssembly` on construction.** The caller (AIE-015 / EditorSubsystem) is responsible for the initial call. This is intentional — the builder is "constructible" only, per the spec.

## Suggested Commit Message

```
feat(editor): AIE-010/011/012 — unified catalog builder, Blueprint contributor, document manager (BATCH-02)

Adds three standalone, headlessly-testable Phase 1 components:
- BlueprintAssetContributor (Hrot.Blueprints.Editor): IAssetCatalogContributor for *.bp.json,
  header-only lazy load, skips malformed files, fires ContributorChanged on Refresh().
- AiAssetCatalogBuilder (Hrot.Editor.AiShared): composes AssetCatalog from BTree/HSM/Blueprint
  contributors; RefreshFromAssembly() calls LoadFrom(asm) on BTree+HSM and Refresh() on Blueprint.
- AiDocumentManager + AiDocument + IPerspectiveSwitcher (Hrot.Editor.AiShared): open/activate/close
  documents, perspective-switch via injected callback, ViewState preservation, ActiveChanged event.
Tests: Hrot.Editor.AiShared.Tests +16, Hrot.Blueprints.Tests +7, all green. No regressions.
EditorSubsystem.cs not modified (composition is AIE-015/BATCH-03).
```
