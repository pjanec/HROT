# BATCH-13 REPORT — Palette offers only bindable actions/conditions

**Task:** TASK-BT-13
**Date:** 2026-06-12
**Status:** ✅ Complete

## Summary

Filtered the dynamic Action/Condition palette entries in `BTreeNodeCatalog` to only show entries whose `ActionSchemaEntry.DtoType.FullName` matches the asset's `BlackboardTypeName`. Static entries (composite/leaf/decorator) and generic Action/Condition fallbacks are never filtered. Back-compat preserved via default `blackboardTypeName = null`.

## Changes

### 1. `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeNodeCatalog.cs`

- Added `_blackboardTypeName` field (`string?`).
- Extended constructor to `BTreeNodeCatalog(IActionSchemaExporter? actionSchema, string? blackboardTypeName = null)` — existing parameterless and single-arg ctors remain working.
- Changed `BuildDynamicEntries` from `static` to instance method so it can read `_blackboardTypeName`.
- Added DTO-type filter inside the loop (after the existing `ActionHosting.BTree` gate):

```csharp
// When a blackboard type name is known, filter to actions/conditions
// whose DtoType matches the asset's blackboard so the codegen can bind.
if (!string.IsNullOrEmpty(_blackboardTypeName)
    && entry.DtoType?.FullName != _blackboardTypeName)
    continue;
```

- When `_blackboardTypeName` is null/empty: no DTO filter (back-compat). Static entries are never subject to this filter.
- `OnSchemaChanged` rebuilds call `BuildDynamicEntries` which reads the same `_blackboardTypeName` — consistent on re-query.

### 2. `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeDocumentFactory.cs`

Line 106 changed from:
```csharp
var nodeCatalog = new BTreeNodeCatalog(actionSchema);
```
to:
```csharp
var nodeCatalog = new BTreeNodeCatalog(actionSchema, btAsset.BlackboardTypeName);
```

Threads the asset's `BlackboardTypeName` (e.g. `"SomeNamespace.BrainBlackboard"`) into the catalog constructor.

### 3. Tests (`Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Host/BTreeDynamicCatalogTests.cs`)

Added two test DTO types:
- `BrainBlackboardStub` — represents a blackboard DTO for matching assertions.
- `SomeOtherDto` — represents a mismatched DTO type.

Added 4 new tests:

| Test | Result |
|------|--------|
| `Catalog_FiltersToBlackboardCompatibleActions` | Matching `DtoType` action (BrainBlackboardStub) is offered; mismatched (SomeOtherDto) is filtered out |
| `Catalog_FiltersToBlackboardCompatibleConditions` | Same for conditions — matching offered, mismatched filtered |
| `Catalog_NullBlackboard_NoDtoFilter` | `blackboardTypeName: null` → both entries present (back-compat) |
| `Catalog_StaticEntries_AlwaysPresent` | With an incompatible-only seed (SomeOtherDto), all static entries (Sequence, Selector, Parallel, Root, Action, Condition, Wait, Subtree, Inverter, Repeater, Cooldown, ForceSuccess, ForceFailure, UntilSuccess, UntilFailure, ObserverSelector) remain present |

Existing BT-01 tests continue to pass unchanged — they use the single-arg constructor which defaults `blackboardTypeName` to null (no filter).

## Validation

```
dotnet build IOS-IG-SimHost.sln  →  0 errors, 0 new warnings in Hrot.BTree.Editor
dotnet test Hrot.BTree.Editor.Tests  →  505 passed, 0 failed, 0 skipped
```

## Files touched

| File | Change |
|------|--------|
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeNodeCatalog.cs` | Add `_blackboardTypeName` field + filter logic in `BuildDynamicEntries` |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeDocumentFactory.cs` | Pass `btAsset.BlackboardTypeName` to catalog ctor |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Host/BTreeDynamicCatalogTests.cs` | 2 test DTO types + 4 new tests |

## Notes

- This is the UX half — the palette in the visual editor. The build-break *guarantee* is BATCH-17 (generator symbol-check), which also covers Inspector-bound + hand-edited assets.
- Inspector picker (BB1) and generator are intentionally untouched here.
