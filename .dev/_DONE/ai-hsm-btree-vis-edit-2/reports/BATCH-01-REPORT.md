# BATCH-01 REPORT — Live action/condition palette (BTree)

**Date:** 2026-06-12 · **Task:** TASK-BT-01 · **Phase:** A (BTree)  
**Status:** ✅ COMPLETE — 0 errors, 0 new warnings, all tests pass

## Summary

Added dynamic action/condition entries to `BTreeNodeCatalog` backed by `IActionSchemaExporter`, wired through `BTreeDocumentFactory` and the composition root. Placing a specific entry now bakes `MethodFqn` onto the node. Generic `bt.leaf.action` / `bt.leaf.condition` entries remain as unbound fallbacks.

## Files changed

| File | Change |
|------|--------|
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IActionSchemaExporter.cs` | Added `bool IsCondition = false` as last positional parameter of `ActionSchemaEntry` record + XML doc |
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/ActionSchemaExporter.cs` | Track `isCondition` in `ProcessMethod` (true for `[BTreeCondition]`, `[SharedAiCondition]`, `[SharedAiHeavyCondition]`); pass to ctor; also added `SharedAiHeavyConditionAttribute` handling (missing from the original dispatcher) |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeKinds.cs` | Added `ActionPrefix`, `ConditionPrefix` constants; `TryParseLeafActionKind()`; extended `KindIdToNodeType` and `IsLeaf` for encoded ids |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeNodeCatalog.cs` | Added `BTreeNodeCatalog(IActionSchemaExporter?)` ctor; dynamic entry building; `Changed` subscription; parameterless ctor delegates to new one |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeCommandSink.cs` | `ApplyAddNode`: detect encoded kind, bake `MethodFqn` into `Action`/`Condition` payload, set `DisplayLabel` to short name |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeDocumentFactory.cs` | Added `IActionSchemaExporter? actionSchema = null` param to `Build()`; passes to `new BTreeNodeCatalog(actionSchema)` |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | Pass `sharedSchemaExporter` to `BTreeDocumentFactory.Build()` |

## Tests

| Test | Location | Assertion |
|------|----------|-----------|
| T1a | `ActionSchemaExporterTests.Rebuild_BTreeAction_IsCondition_False` | Action entry has `IsCondition == false` |
| T1b | `ActionSchemaExporterTests.Rebuild_BTreeCondition_IsCondition_True` | Condition entry has `IsCondition == true` |
| T2 | `BTreeDynamicCatalogTests.Catalog_ActionEntry_QueryReturnsEncodedKind` | `Kind.Id == "bt.leaf.action::Ns.Combat.DoThing"`, `DisplayName == "DoThing"` |
| T3 | `BTreeDynamicCatalogTests.Catalog_ConditionEntry_*` (2 tests) | `Kind.Id == "bt.leaf.condition::Ns.Combat.IsThing"`, `IsPure == true` |
| T4 | `BTreeDynamicCatalogTests.Catalog_HsmOnlyEntry_NotPresent` | HSM-only entry absent from `catalog.All` |
| T5 | `BTreeDynamicCatalogTests.Catalog_OnChanged_NewEntryAppears` | Post-`Changed`, entry appears in `catalog.All` |
| T6 | `BTreeDynamicCatalogTests.TryParse*/KindIdToNodeType*` (6 tests) | Parse returns correct `fqn`/`isCondition`; `KindIdToNodeType` returns correct `NodeType` |
| T7 | `BTreeDynamicCatalogTests.CommandSink_AddNode_Encoded*` (2 tests) | Action baked with `MethodFqn`; Condition baked with `MethodFqn`; correct `KernelType` |
| T8 | `BTreeDynamicCatalogTests.CommandSink_AddNode_GenericAction_NoMethodFqn` | Generic `bt.leaf.action` → no `Action` payload |

## Test results

```
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests  → 449 passed, 0 failed
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests      → 1059 passed, 0 failed
```

Build: `dotnet build IOS-IG-SimHost.sln` — 0 errors in touched projects. The 36 pre-existing errors in `Fdp.Presentation.Tests` (missing `NodeEditor` references) are unrelated.

## Decisions beyond spec

1. **`SharedAiHeavyConditionAttribute` handling added.** The original `ProcessMethod` only processed `SharedAiHeavyActionAttribute` — it had no path for `[SharedAiHeavyCondition]`. Since D-01 requires condition attributes to set `IsCondition = true`, the missing attribute loop was added. This is additive and matches the existing `SharedAiHeavyAction` pattern.

2. **`IsLeaf` extended for encoded ids.** `BTreeKinds.IsLeaf` now returns `true` for `ActionPrefix`/`ConditionPrefix`-encoded kind ids. This is a necessary consequence — a node placed from a dynamic action entry must be recognized as a leaf.

3. **`BTreeNodeCatalog` refactored from single `_all` field to `_staticEntries` + `_dynamicEntries`.** Required to support dynamic rebuild on `Changed` without re-allocating static entries.

## Suggested commit message

```
feat(btree-editor): live action/condition palette from ActionSchemaExporter (TASK-BT-01)

- Add IsCondition to ActionSchemaEntry record (default=false, backward compat)
- ActionSchemaExporter.ProcessMethod sets IsCondition from condition attributes
- BTreeKinds: ActionPrefix/ConditionPrefix, TryParseLeafActionKind, extended KindIdToNodeType
- BTreeNodeCatalog: dynamic entries from IActionSchemaExporter, Changed subscription
- BTreeCommandSink.ApplyAddNode: bake MethodFqn for encoded kinds
- Wire sharedSchemaExporter through BTreeDocumentFactory → EditorSubsystem
- Tests T1–T8: exporter discriminator, catalog query, host filter, re-query,
  kinds parse, placement baking, generic fallback

Co-Authored-By: Claude <noreply@anthropic.com>
```
