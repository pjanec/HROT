# BATCH-03 REPORT

**Batch:** BATCH-03  
**Tasks:** D-08, TASK-C-08, TASK-C-09  
**Date:** 2026-05-28  
**Status:** COMPLETE

---

## Summary

All three tasks have been implemented and all tests pass.

| Task | Status | Tests |
|------|--------|-------|
| D-08 FakeCatalog consolidation | DONE | BTree: 291 pass, HSM: 250 pass |
| TASK-C-08 No-op adapters | DONE | AiShared: 407 pass (3 new) |
| TASK-C-09 BlueprintComparisonSanitizer | DONE | 13 new tests pass |

---

## D-08 — FakeCatalog/FakeAsset Consolidation

### What was done

**BTree test project (`Hrot.BTree.Editor.Tests`):**
- Created `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/FakeCatalogHelper.cs`
  with `internal sealed class FakeAsset : IEditableAsset` and `internal sealed class FakeCatalog : IAssetCatalog`.
- Removed private nested `FakeAsset` and `FakeCatalog` from `BTreeComparisonSanitizerTests.cs`, `BTreeSanitizationDeterminismTests.cs`, and `BTreeSelfComparisonTests.cs`.
- Removed now-unused `using System.Collections.Generic`, `using System.Linq`, `using Hrot.Editor.AiShared.Catalog` from the latter two files.

**HSM test project (`Hrot.Hsm.Editor.Tests`):**
- Created `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/FakeCatalogHelper.cs`
  with the same `internal sealed` classes.
- Removed private nested `FakeAsset` and `FakeCatalog` from `HsmComparisonSanitizerTests.cs`, `HsmSanitizationDeterminismTests.cs`, and `HsmSelfComparisonTests.cs`.
- Cleaned up unused usings in those files.

**Note:** The D-08 instructions explicitly mentioned `HsmComparisonSanitizerTests.cs` only, but `HsmSanitizationDeterminismTests.cs` and `HsmSelfComparisonTests.cs` also had the same duplicates. All three files were consolidated for full consistency within the project.

### Test results
- BTree: **291 passed**, 0 failed
- HSM: **250 passed**, 0 failed

---

## TASK-C-08 — No-Op `IComparisonMigrationAdapter` and `IMetaEnvelopeSanitizer`

### New files

| File | Description |
|------|-------------|
| `Hrot/Editor/Hrot.Editor.AiShared/Comparison/NoOpComparisonMigrationAdapter.cs` | Returns input unchanged, `didMigrate=false` |
| `Hrot/Editor/Hrot.Editor.AiShared/Comparison/NoOpMetaEnvelopeSanitizer.cs` | Returns input unchanged |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/NoOpAdapterTests.cs` | 3 tests |

### DI registration change

`SharedAiEditorServiceCollectionExtensions.cs` updated to add:
```csharp
using Microsoft.Extensions.DependencyInjection.Extensions;
// ...
services.TryAddSingleton<IComparisonMigrationAdapter, NoOpComparisonMigrationAdapter>();
services.TryAddSingleton<IMetaEnvelopeSanitizer, NoOpMetaEnvelopeSanitizer>();
```
`TryAddSingleton` is used so that production implementations registered before `AddSharedAiEditor()` are not overwritten.

### Test results
- AiShared: **407 passed** (was 404 before TASK-C-08; +3 new NoOpAdapterTests)

---

## TASK-C-09 — `BlueprintComparisonSanitizer`

### New files

| File | Description |
|------|-------------|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Comparison/BlueprintComparisonSanitizer.cs` | JSON DOM sanitizer |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Comparison/BlueprintEditorComparisonServiceCollectionExtensions.cs` | DI extension |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Comparison/BlueprintComparisonSanitizerTests.cs` | 13 unit tests |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Comparison/Fixtures/simple_node.bp.json` | Minimal fixture |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Comparison/Fixtures/with_editor_metadata.bp.json` | EditorMetadata fixture |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Comparison/Fixtures/with_peer_call.bp.json` | CallPeerBlueprint fixture |

### csproj changes to `Hrot.Blueprints.Tests.csproj`

Added:
```xml
<ProjectReference Include="..\..\..\Editor\Hrot.Editor.AiShared\Hrot.Editor.AiShared.csproj" />
<Content Include="Comparison\Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
```

### Namespace disambiguation

The `Hrot.Blueprints.Editor` project already has a local `IAssetCatalog` (in `Hrot.Blueprints.Editor` namespace) with different methods than `Hrot.Editor.AiShared.Catalog.IAssetCatalog`. Both `BlueprintComparisonSanitizer.cs` and `BlueprintEditorComparisonServiceCollectionExtensions.cs` use a `using` alias:
```csharp
using AiCatalog = Hrot.Editor.AiShared.Catalog.IAssetCatalog;
```

### Implementation notes

**EditorMetadata processing rules implemented:**
- **Root level:** `EditorMetadata` removed entirely.
- **Graph level:** `CanvasComments` hoisted to `_canvasComments` with only `Text` key; all other keys (Viewport, DockState, NodeViewStates, unrecognized) stripped; `EditorMetadata` object removed.
- **Node level:** `Comment` hoisted to top-level node property; `X`, `Y`, and all other keys stripped; `EditorMetadata` object removed.

**CallPeerBlueprint humanization:** Reads `PeerBlueprintId` (the real field name, confirmed from `Nodes.cs`) and sets `_targetName` to `"Name (Kind)"` on catalog hit, or `"(asset not found in catalog)"` on miss.

**Alphabetical sort:** `SortPropertiesRecursive` walks the entire DOM recursively, building new `JsonObject` instances with `OrderBy(kv => kv.Key, StringComparer.Ordinal)`. Arrays retain source order.

**Never-throws contract:** Core pipeline is wrapped in try/catch; returns raw file text + fallback metadata + warning on exception.

### Test results
- **13 passed**, 0 failed

All 13 required tests implemented and passing:
1. `Sanitize_NodeComment_IsHoistedToTopLevelNodeProperty`
2. `Sanitize_CanvasComments_AreHoistedToGraphLevelWithTextOnly`
3. `Sanitize_NodePositionXY_IsStripped`
4. `Sanitize_GraphViewport_IsStripped`
5. `Sanitize_NodeId_IsPreserved`
6. `Sanitize_CallPeerBlueprint_AddsTargetName_WhenCatalogHit`
7. `Sanitize_CallPeerBlueprint_AddsMissMessage_WhenCatalogMiss`
8. `Sanitize_OutputIsAlphabeticallySorted`
9. `Sanitize_RunTenTimes_ProducesByteIdenticalOutput`
10. `Sanitize_ShuffledInput_SameOutputAsCanonicalInput`
11. `Sanitize_MissingFile_ReturnsWarning_NeverThrows`
12. `Sanitize_WithNoOpMigrationAdapter_NoMigrationNotice`
13. `Sanitize_WithFakeMigrationAdapter_MigrationNoticePopulated`

---

## Build Verification

All relevant projects build without `error CS` messages:
- `Hrot.BTree.Editor.Tests` — Build succeeded
- `Hrot.Hsm.Editor.Tests` — Build succeeded
- `Hrot.Editor.AiShared` — Build succeeded
- `Hrot.Editor.AiShared.Tests` — Build succeeded
- `Hrot.Blueprints.Editor` — Build succeeded (TreatWarningsAsErrors=true, 0 new warnings)
- `Hrot.Blueprints.Tests` — Build succeeded (pre-existing warnings in other test files; 0 new errors)

The pre-existing test failure `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes` is unrelated to any of the BATCH-03 changes.

---

## Developer Insights

**Q1: Blueprint EditorMetadata subkeys not covered by the classification table?**

The actual test assets (`simple-action.bp.json`, `with-callable-peer.bp.json`) use only an empty `EditorMetadata: {}` at all levels. The `with_editor_metadata.bp.json` fixture (created for tests) uses `X`, `Y`, `Comment` at node level and `Viewport`, `CanvasComments` at graph level — all explicitly covered by the design §3.5 table.

The only discrepancy was that `with-callable-peer.bp.json` in `TestAssets/` has a `CallPeerBlueprint` node without `PeerBlueprintId` (the field is entirely absent). This is handled correctly: the sanitizer reads `node["PeerBlueprintId"]?.GetValue<string>()` and skips the node if the field is null or not a valid GUID — no `_targetName` added.

**Q2: Can alphabetical sorting of object keys change the semantics of the Blueprint DOM?**

Arrays (including `Graphs`, `Nodes`, `Pins`, `Links`, `Inputs`, `Outputs`, `CanvasComments`, `CallablePeers`) retain source order because `SortPropertiesRecursive` only reorders `JsonObject` keys, not `JsonArray` elements. For arrays where execution order matters (e.g., execution links in a graph, pin order for a function call), order is preserved.

Object keys in the Blueprint JSON have no inherent ordering semantics — `JsonObject` is an unordered map by JSON specification (RFC 8259). Alphabetical sorting of object keys is therefore semantically neutral.
