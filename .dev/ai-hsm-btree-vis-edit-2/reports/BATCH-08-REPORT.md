# BATCH-08 Report — Wire the Add-Node picker for the BTree canvas

**Task:** TASK-BT-08 (REVIEW-BT finding F2)  
**Date:** 2026-06-12  
**Result:** ✅ PASS — build clean, Failed: 0

## Summary

Registered a BTree node picker source (`BTreeNodePickerSource`) backed by `BTreeNodeCatalog` so that pressing Tab or right-clicking on the BTree canvas opens the "Add Node" palette. The picker→place flow (picker returns a `NodeCatalogEntry` → canvas issues `GraphCommand.AddNode` with `entry.Kind`) was already handled generically by NodeEditor's canvas — only the source registration was missing.

## Changes

### 1. NEW: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreePickerSources.cs`

Mirrors `BlueprintPickerSources.cs` exactly:
- `BTreePickerSources.Register(IPickerRegistry registry, BTreeNodeCatalog catalog)` — registers `"nodes.all"` and `"nodes.by-pin"` sources
- `BTreeNodePickerSource : IPickerSource<NodeCatalogEntry>` — delegates `Query(text, context)` to `_catalog.Query(new NodeSearchQuery(text))`; with pin-context support via `QueryForPinContext`
- `GetItemKey` = `item.Kind.Id`, `GetSearchableText` = `item.DisplayName`, `Title` = "Add Node", `PreferredLayout` = Wide, `SelectionMode` = Single, `Cost` = Cheap

### 2. MODIFIED: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeDocumentFactory.cs`

Added after `BuiltinCommandHandlers.RegisterAll(...)`:
```csharp
BTreePickerSources.Register(bundle.PickerRegistry, nodeCatalog);
```

### 3. NEW: `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Host/BTreePickerSourceTests.cs`

6 tests (4 required + 2 bonus):
- `Register_AddsNodesAllSource` — `PickerRegistry.Get<NodeCatalogEntry>("nodes.all")` is non-null
- `Register_AddsNodesByPinSource` — `"nodes.by-pin"` also registered
- `PickerSource_Query_ReturnsCatalogEntries` — Query("Sequence") contains entry with `Kind.Id == "bt.composite.sequence"`
- `PickerSource_Query_ReturnsSequenceByDisplayName` — same via `BTreeNodePickerSource.Query`
- `PickerSource_Query_Empty_ReturnsManyStatics` — Query("") returns ≥ 10 entries (static composites + leaves + decorators)
- `PickerSource_GetItemKey_IsKindId` — `GetItemKey(entry) == entry.Kind.Id == "bt.composite.sequence"`

Uses a `BTreeNodePickerSourceInvoker` helper that obtains the internal `BTreeNodePickerSource` via `PickerRegistry.Get<NodeCatalogEntry>("nodes.all")` after registration.

## Build & Test Results

| Check | Status |
|-------|--------|
| `dotnet build Hrot.BTree.Editor.csproj` | 0 errors, 0 warnings |
| `dotnet build Hrot.BTree.Editor.Tests.csproj` | 0 errors, 0 warnings |
| `dotnet build IOS-IG-SimHost.sln` | 0 errors, 21 pre-existing warnings (none in BTree.Editor) |
| `dotnet test Hrot.BTree.Editor.Tests` | **Failed: 0, Passed: 491** |
| New tests (BTreePickerSourceTests) | 6 passed, 0 failed |

## Files changed

```
Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreePickerSources.cs       (NEW)
Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeDocumentFactory.cs     (+3 lines)
Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Host/BTreePickerSourceTests.cs (NEW)
```

## Notes

- The actual "picker opens visually + places node" is an ImGui UI flow confirmed at the next visual review — the tests prove the source is registered + queryable.
- No modifications to the generic NodeEditor canvas/picker code were needed — the canvas already calls `Pickers.Open("nodes.all", …)`.
- The `BTreeNodePickerSource` is `internal sealed` (same visibility as `BlueprintNodePickerSource`); tests access it through the public `IPickerRegistry.Get<T>()` API.
