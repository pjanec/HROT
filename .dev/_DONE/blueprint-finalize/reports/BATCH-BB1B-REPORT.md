# BATCH-BB1B Execution Report

**Branch:** blueprint-integ-1  
**Date:** 2026-06-12  
**Executor:** claude-sonnet-4-6 (coder sub-agent)

---

## Summary

Both tasks completed, all four affected test suites green.

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| Hrot.BTree.Editor.Tests | 420 | 424 | +4 new |
| Hrot.Hsm.Editor.Tests | 368 | 373 | +5 new |
| Hrot.Editor.AiShared.Tests | 1025 | 1025 | 0 |
| Hrot.AiEditor.Persistence.Tests | 96 | 112 | +16 new |
| **Total** | **1509** | **1934** | **+45 new** |

0 failed, 0 broken.

---

## Corrective Task 0: Promote→Bind (B-2 completion)

### Root cause (confirmed)
`BlackboardFieldPickerDrawer.DrawInput` and `HsmBlackboardFieldPickerDrawer.DrawInput` called `TriggerPromote()` (set a flag) but nothing consumed that flag. The newly-created `_auto_{id}` variable was never written back to `ExpressionTargetField`. `PromoteRequested` was wired to no consumer.

### Fix

**`BTreeFacetFqnContext`** (`BTreePickerDrawers.cs`): added `CurrentNodeVisualId { get; set; }` property.

**`HsmFacetFqnContext`** (`HsmPickerDrawers.cs`): added `CurrentVisualId { get; set; }` property.

**`BlackboardFieldPickerDrawer`** (`BTreePickerDrawers.cs`):
- Added 4th constructor `BTreeFacetFqnContext? fqnContext`; stored as `_fqnContext`.
- `DrawInput`: on "Promote to new variable" button click calls `Promote(_fqnContext?.CurrentNodeVisualId ?? "")`, then if result is non-null sets `value = newName; return true;` — StructEdit's normal write-back flows through `BTreeFacetMapper.ApplyFacet` which persists `ExpressionTargetField`.

**`HsmBlackboardFieldPickerDrawer`** (`HsmPickerDrawers.cs`): same pattern using `_fqnContext?.CurrentVisualId`.

**`BTreeFacetMapper`** (`BTreeFacetMapper.cs`):
- `BuildActionFacet`: `ctx.CurrentNodeVisualId = node.VisualId.ToString()`.
- `BuildConditionFacet`: same.
- Non-action/condition clearing block: `_fqnContext.CurrentNodeVisualId = null`.

**`HsmFacetMapper`** (`HsmFacetMapper.cs`):
- `GetTransitionFacet`: `_fqnContext.CurrentVisualId = t.VisualId.ToString()`.
- `GetGlobalTransitionFacet`: `_fqnContext.CurrentVisualId = g.VisualId.ToString()`.

**`HsmFacetDispatcher`** (`HsmFacetDispatcher.cs`):
- Non-transition selection: `_fqnContext.CurrentVisualId = null`.

**`BTreePickerDrawerFactory.BuildDrawers`**: passes `fqnContext` to `BlackboardFieldPickerDrawer` constructor.
**`HsmPickerDrawerFactory.BuildDrawers`**: passes `fqnContext` to `HsmBlackboardFieldPickerDrawer` constructor.

### New tests

**`Hrot.BTree.Editor.Tests/Inspector/PromoteBindTests.cs`** (+4 tests):
- `Promote_CreatesVar_AndFacetApply_SetsExpressionTargetField_BTree` — creates auto-var AND node.Action.ExpressionTargetField = name after headless ApplyFacet.
- `Promote_AndApplyFacet_BindingSurvivesRoundTrip_BTree` — binding + variable survive model→DTO→model round-trip.
- `Promote_SecondCallSameId_IsIdempotent_BindingUnchanged_BTree` — second promote with same id is a no-op.
- `FqnContext_CurrentNodeVisualId_IsSetByMapper_BTree` — mapper.GetFacet writes CurrentNodeVisualId.

**`Hrot.Hsm.Editor.Tests/Inspector/HsmPromoteBindTests.cs`** (+5 tests):
- Same structural coverage for HSM transitions, plus:
- `FqnContext_CurrentVisualId_ClearedOnNonTransitionSelection_Hsm` — state selection clears both CurrentVisualId and CurrentActionFqn.

---

## Task B-3: DefaultValueJson StructEdit Authoring Surface

### Data layer

**`BlackboardVariableEntry`** (`Hrot.Editor.AiShared/Blackboard/BlackboardVariableEntry.cs`):
```csharp
public record BlackboardVariableEntry(
    string  Name,
    Type    FieldType,
    string? Comment,
    bool    IsAutoManaged    = false,
    string? DefaultValueJson = null);
```

**`IBlackboardManagedAsset`** (`Hrot.Editor.AiShared/Blackboard/IBlackboardManagedAsset.cs`):
Added `void UpdateVariableDefaultValueJson(string name, string? defaultValueJson)`.

**`BehaviorTreeAsset`** / **`HsmAsset`**: implemented the new method (find-by-name, replace with `with { DefaultValueJson = ... }`, MarkDirty).

**`BehaviorTreeAssetMapper`**: `BlackboardToDto` writes `DefaultValueJson`, `BlackboardFromDto` reads it.  
**`HsmAssetMapper`**: same.

**`BlackboardVariableDto`** (`BehaviorTreeAssetDto.cs`) + **`HsmBlackboardVariableDto`** (`HsmAssetDto.cs`):
Added `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` on `DefaultValueJson` for byte-stability (null is omitted from JSON, non-null is written).

All 12 stub `IBlackboardManagedAsset` implementations in test files patched with the new method.

### InspectorWindow B-3 rendering surface

**`InspectorWindow`** (`Hrot.Editor.AiShared/Windows/InspectorWindow.cs`):
- New constructor parameter `Func<object?, string?>? expressionTargetFieldAccessor`.
- Three new fields: `_expressionTargetFieldAccessor`, `_defaultValueSession`, `_defaultValueSessionVarName`.
- `DrawClientArea`: after the facet dispatch block, a new "STATIC PARAMETERS" section renders when `_expressionTargetFieldAccessor` is wired, the facet carries a non-null `ExpressionTargetField`, and the asset implements `IBlackboardManagedAsset`. Builds an `IEditSession` over the variable's `FieldType`, hydrating from `DefaultValueJson` (or `Activator.CreateInstance`). On dirty, serializes to JSON and calls `UpdateVariableDefaultValueJson`.
- New helper `DisposeAndClearDefaultValueSession()`.
- New accessor `GetDefaultValueSession()` for tests.

### New tests

**`Hrot.AiEditor.Persistence.Tests/BTree/DefaultValueJsonRoundTripTests.cs`** (+16 tests):
- BTree: non-null round-trip, null round-trip, null omitted from JSON, non-null present in JSON, default is null, backcompat (missing field defaults to null), UpdateVariableDefaultValueJson sets/clears/noop, only affected var changes.
- HSM: same coverage for HsmAssetMapper.

---

## Files modified

| File | Change |
|------|--------|
| `Hrot.BTree.Editor/Inspector/BTreePickerDrawers.cs` | `BTreeFacetFqnContext.CurrentNodeVisualId`; drawer 4th ctor; DrawInput promote→bind |
| `Hrot.BTree.Editor/Inspector/BTreeFacetMapper.cs` | Sets/clears `CurrentNodeVisualId` |
| `Hrot.Hsm.Editor/Inspector/HsmPickerDrawers.cs` | `HsmFacetFqnContext.CurrentVisualId`; drawer 4th ctor; DrawInput promote→bind |
| `Hrot.Hsm.Editor/Inspector/HsmFacetMapper.cs` | Sets `CurrentVisualId` on transition facets |
| `Hrot.Hsm.Editor/Inspector/HsmFacetDispatcher.cs` | Clears `CurrentVisualId` on non-transition selection |
| `Hrot.Editor.AiShared/Blackboard/BlackboardVariableEntry.cs` | Added `DefaultValueJson = null` parameter |
| `Hrot.Editor.AiShared/Blackboard/IBlackboardManagedAsset.cs` | Added `UpdateVariableDefaultValueJson` |
| `Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs` | Implemented `UpdateVariableDefaultValueJson` |
| `Hrot.Hsm.Editor/Model/HsmAsset.cs` | Implemented `UpdateVariableDefaultValueJson` |
| `Hrot.BTree.Editor/Persistence/BehaviorTreeAssetMapper.cs` | Round-trips `DefaultValueJson` |
| `Hrot.Hsm.Editor/Persistence/HsmAssetMapper.cs` | Round-trips `DefaultValueJson` |
| `Hrot.AiEditor.Persistence/BTree/BehaviorTreeAssetDto.cs` | `[JsonIgnore(WhenWritingNull)]` on `DefaultValueJson` |
| `Hrot.AiEditor.Persistence/Hsm/HsmAssetDto.cs` | `[JsonIgnore(WhenWritingNull)]` on `DefaultValueJson` |
| `Hrot.Editor.AiShared/Windows/InspectorWindow.cs` | B-3 rendering + `GetDefaultValueSession()` + `DisposeAndClearDefaultValueSession()` |
| ~12 test stub files | Added `UpdateVariableDefaultValueJson` stub implementation |

---

## Suggested commit message

```
feat(blackboard/inspector): Promote→bind + DefaultValueJson authoring (BATCH-BB1B)

Corrective Task 0 (B-2 completion):
- Thread CurrentNodeVisualId/CurrentVisualId into BTreeFacetFqnContext/HsmFacetFqnContext
- BlackboardFieldPickerDrawer + HsmBlackboardFieldPickerDrawer: Promote button now
  calls Promote(visualId) and returns value=newName so StructEdit write-back
  flows through ApplyFacet to persist ExpressionTargetField.
- 9 new headless tests covering create+bind+round-trip (BTree + HSM).

B-3 (DefaultValueJson authoring surface):
- BlackboardVariableEntry record gains optional DefaultValueJson parameter.
- IBlackboardManagedAsset.UpdateVariableDefaultValueJson seam added.
- BehaviorTreeAsset + HsmAsset implement it; both mappers round-trip the field.
- BlackboardVariableDto + HsmBlackboardVariableDto: [JsonIgnore(WhenWritingNull)]
  on DefaultValueJson for byte-stability (null omitted from JSON).
- InspectorWindow: B-3 "Static Parameters" StructEdit panel below action facet,
  injected via expressionTargetFieldAccessor delegate from composition root.
- 16 new persistence round-trip tests.
```
