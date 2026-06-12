# BATCH-13 — Palette offers only bindable actions/conditions

**Task:** TASK-BT-13. **One objective.**

## 🔒 Working agreement (MANDATORY)
One task; **NO cheating**; finish without asking until build clean + `Failed: 0`; tests assert real values; litter-free; report = diffs.

## 📋 Onboarding
- Report → `.dev/ai-hsm-btree-vis-edit-2/reports/BATCH-13-REPORT.md`.
- Context: BT-01 made `BTreeNodeCatalog` surface a dynamic palette entry per BTree-hosted action/condition from `IActionSchemaExporter`. But it offers **all** of them — including actions whose method takes a typed DTO param that **can't bind** to this tree's blackboard (the codegen then can't emit a compilable `.Action(Method,…)`). The palette should only offer **bindable** entries: those whose `ActionSchemaEntry.DtoType` matches the asset's blackboard type (the 4-param `NodeLogicDelegate<TBB,…>` shape, like `Action_Wander(ref BrainBlackboard,…)`).
- `ActionSchemaEntry` (in `Hrot.Editor.AiShared/Blackboard/IActionSchemaExporter.cs`) has `Type DtoType` (the method's first `ref` param type) and `bool IsCondition`.

## 🎯 Objective
Filter the dynamic Action/Condition palette entries to those whose `DtoType.FullName == <asset's BlackboardTypeName>`. Keep the static composite/leaf/decorator entries and the generic `Action`/`Condition` fallback entries unchanged.

## Files (exact)
1. `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeNodeCatalog.cs` — add a blackboard-type-name parameter to the `IActionSchemaExporter` constructor (e.g. `BTreeNodeCatalog(IActionSchemaExporter? actionSchema, string? blackboardTypeName = null)`; keep the existing parameterless + single-arg ctors working). In `BuildDynamicEntries`, in addition to the existing `Hosting.HasFlag(ActionHosting.BTree)` filter, **skip an entry whose `DtoType?.FullName != blackboardTypeName`** when `blackboardTypeName` is non-empty. When `blackboardTypeName` is null/empty, keep current behavior (no DTO filter) for back-compat.
2. `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeDocumentFactory.cs` — pass `btAsset.BlackboardTypeName` to `new BTreeNodeCatalog(actionSchema, btAsset.BlackboardTypeName)`.

> Confirm the exact property name for the asset's blackboard type (likely `BehaviorTreeAsset.BlackboardTypeName`) and `ActionSchemaEntry.DtoType` — match the real code.

## 🧪 Tests (extend `Host/BTreeDynamicCatalogTests.cs`, mirror the BT-01 fake-exporter pattern)
- `Catalog_FiltersToBlackboardCompatibleActions`: seed a `FakeActionSchemaExporter` with a BTree action whose `DtoType == typeof(BrainBlackboardStub)` (use a real type whose FullName you assert) and another BTree action whose `DtoType == typeof(SomeOtherDto)`. Build `new BTreeNodeCatalog(fake, "<FullName of BrainBlackboardStub>")`. Assert the catalog contains the matching action's encoded kind and does NOT contain the mismatched one.
- `Catalog_NullBlackboard_NoDtoFilter`: `new BTreeNodeCatalog(fake, null)` → both dynamic entries present (back-compat).
- `Catalog_StaticEntries_AlwaysPresent`: static composites/leaves/decorators + generic Action/Condition still present regardless of the filter.
- (Condition variant) a condition with a matching DtoType is offered; one with a mismatched DtoType is not.

(Use ordinary test DTO types defined in the test assembly for `DtoType`, and assert against their `typeof(...).FullName`.)

## ✅ Success criteria
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 new warnings in `Hrot.BTree.Editor`.
- [ ] `Failed: 0` in `Hrot.BTree.Editor.Tests` (incl. new tests; existing BT-01 dynamic-catalog tests still pass — they may need `blackboardTypeName: null` to keep showing entries, OR update them to pass a matching type).
- [ ] Palette dynamic entries filtered to DtoType == blackboard; static + generic entries unchanged.
- [ ] Report written.

## Notes
- This is the UX half; the build-break *guarantee* is BATCH-17 (generator symbol-check), which also covers Inspector-bound + hand-edited assets. Do NOT touch the Inspector picker (BB1) or the generator here.
- Don't break BT-01's existing tests — adjust them to pass `blackboardTypeName: null` (or a matching type) so their dynamic entries still appear.
