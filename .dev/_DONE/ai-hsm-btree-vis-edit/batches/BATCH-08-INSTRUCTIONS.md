# BATCH-08 — Alias Binding Model, Drag-to-Alias UX, Type Match Validation, Aliased-By Badge (Phase 1.5d, Tasks 1d-01, 1d-02, 1d-05)

## Overview

This batch implements the whole-DTO aliasing mechanics from Phase 1.5d — the data model,
the drag interaction, type-match validation, and badge rendering. Orchestrator code emit
(1d-03, 1d-04) is deferred to Batch 09.

Tasks:
- **TASK-BB-1d-01** — Alias binding data model + drag-to-alias UX
- **TASK-BB-1d-02** — Type-match validation on drop (exact equality only)
- **TASK-BB-1d-05** — "Aliased by" badge rendering

---

## Key design references

- `Blackboard_Authoring_Detailed_Design.md` sections **BB §7.1–§7.3**, **§7.5 (emit shape, Batch 09)**,
  **§7.6 (alias does not affect bin-packing size)**, **§4.2 (visual glyphs)**, **§4.5 (row interactions)**
- `TASK-DETAIL.md` sections **TASK-BB-1d-01**, **TASK-BB-1d-02**, **TASK-BB-1d-05**

---

## Existing files to read before making any changes

| File | Why |
|------|-----|
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IBlackboardManagedAsset.cs` | Interface to extend |
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardVariableEntry.cs` | Existing entry record |
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IBlackboardAggregator.cs` | `DtoRequirement` type (fields referenced in alias binding) |
| `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs` | Window + view-model to extend |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs` | Must implement new interface methods |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` | Must implement new interface methods |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/BlackboardAuthoringWindowTests.cs` | Existing window tests |

---

## TASK-BB-1d-01: Alias binding model + drag-to-alias UX

### Part A: New `BlackboardAliasBinding` record

Create a new file `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardAliasBinding.cs`:

```csharp
/// <summary>
/// Records one aliasing entry: a sub-tree requirement that has been bound to a defined variable.
/// Stored on the variable in the asset model; used by the Variables panel to render the "aliased by" badge.
/// </summary>
public record BlackboardAliasBinding(
    Guid   RequiringAssetId,
    Guid   RequiringElementId,
    string RequiringAssetName,
    string RequiredByPath,
    Type   DtoType);
```

### Part B: Extend `IBlackboardManagedAsset`

Add three new methods to the interface:

```csharp
/// <summary>Returns all alias bindings recorded against the named variable. Empty list if none.</summary>
IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName);

/// <summary>Binds an unbound sub-tree requirement to a defined variable. Fires Changed.</summary>
void AddAlias(string variableName, BlackboardAliasBinding binding);

/// <summary>
/// Removes an alias binding from the named variable. No-op if not found.
/// Returns the removed requirement back to the "unbound" pool implicitly (the aggregation result
/// re-surfaces it on the next BuildViewModel call). Fires Changed.
/// </summary>
void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId);
```

### Part C: Implement in `BehaviorTreeAsset` and `HsmAsset`

Both assets need to:
- Store a `Dictionary<string, List<BlackboardAliasBinding>> _aliases = new()` field.
- Implement `GetAliasesFor(variableName)`: return `_aliases.TryGetValue(name, out var list) ? list.AsReadOnly() : Array.Empty<BlackboardAliasBinding>()`.
- Implement `AddAlias(variableName, binding)`: ensure the key exists in the dict, then add (prevent duplicate by `(RequiringAssetId, RequiringElementId)` pair).
- Implement `RemoveAlias(variableName, assetId, elementId)`: remove matching entry from the list; fire Changed if removed.

When a variable is removed from the asset (`RemoveVariable`), also clear its alias entry
from `_aliases`. (Update the existing `RemoveVariable` implementation in both assets.)

When a variable is renamed (`RenameVariable`), also rename the alias dict key.
(Update the existing `RenameVariable` implementation in both assets.)

### Part D: Drag-to-alias in `BlackboardAuthoringWindow`

Add drag-source support on the unbound requirement rows, and drop-target support on
the defined variable rows (in addition to the existing `BB_VAR_DRAG` reorder drop target).

**Payload type string:** `"BB_UNBOUND_DRAG"` carrying the index of the
`UnboundRequirementViewModel` in the list.

In `DrawClientArea`, for each unbound requirement row, add:
```csharp
if (ImGuiNET.ImGui.BeginDragDropSource(...))
{
    unsafe { int idx = i; ImGuiNET.ImGui.SetDragDropPayload("BB_UNBOUND_DRAG", (IntPtr)(&idx), sizeof(int)); }
    ImGuiNET.ImGui.Text(req.DtoTypeName);
    ImGuiNET.ImGui.EndDragDropSource();
}
```

For each defined variable row (in the variable table loop), add a second drop target
that accepts `"BB_UNBOUND_DRAG"`:
```csharp
if (ImGuiNET.ImGui.BeginDragDropTarget())
{
    unsafe
    {
        // Accept BB_UNBOUND_DRAG (alias) -- exact type match required.
        var payload = ImGuiNET.ImGui.AcceptDragDropPayload("BB_UNBOUND_DRAG");
        if (payload.NativePtr != null)
        {
            int srcIdx = *(int*)payload.Data;
            if (srcIdx >= 0 && srcIdx < vm.UnboundRequirements.Count)
            {
                var req = vm.UnboundRequirements[srcIdx];
                if (req.DtoType == row.FieldType)  // type-match guard (1d-02)
                {
                    bbAsset.AddAlias(row.Name, new BlackboardAliasBinding(
                        req.RequiringAssetId,
                        req.RequiringElementId,
                        req.RequiringAssetName,
                        req.RequiredByPath,
                        req.DtoType));
                }
            }
        }
    }
    ImGuiNET.ImGui.EndDragDropTarget();
}
```

**IMPORTANT:** The `UnboundRequirementViewModel` currently does not have `DtoType` as a CLR
`Type` — it stores only `DtoTypeName` (a string). Update `UnboundRequirementViewModel` to
also carry `Type DtoType` and `string RequiringAssetName`. This is needed for the type-match
drop guard. Update `BuildViewModel` to populate these fields from `DtoRequirement`.

Also update `DtoRequirement` usage -- the `DtoRequirement` record (in `IBlackboardAggregator.cs`)
already has `Type DtoType` and `string RequiredByPath`. Map them.

---

## TASK-BB-1d-02: Type-match validation

Type matching is already enforced at the drop site in Part D above (the `if (req.DtoType == row.FieldType)` guard). No additional method is needed.

For visual feedback (green/red highlight while hovering):
In the unbound requirement drag loop, check whether the currently-hovered item's `FieldType`
matches the dragged requirement's `DtoType`. Since ImGui doesn't expose hover-during-drag
as a first-class concept, it is acceptable to use ImGui's `AcceptDragDropPayload` with
`ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect`.

For this batch, it is sufficient to enforce type-match at drop time and skip the hover
color change (the hover color is a visual nicety; it can be added in a polish pass).

---

## TASK-BB-1d-05: "Aliased by" badge rendering

### Extend `VariableViewModel`

Add `IReadOnlyList<(string AssetName, Guid AssetId, Guid ElementId)> AliasedBy` to
`VariableViewModel`. Use a named value tuple.

### Extend `BuildViewModel`

After building the `sizeMap` and before returning, look up each variable's aliases:

```csharp
var aliases = bbAsset.GetAliasesFor(v.Name)
    .Select(a => (a.RequiringAssetName, a.RequiringAssetId, a.RequiringElementId))
    .ToList();
rows.Add(new VariableViewModel(v.Name, ..., ByteSize: ..., Comment: ..., AliasedBy: aliases));
```

When `AliasedBy` is empty, it is `Array.Empty<(string, Guid, Guid)>()`.

### Extend `BuildViewModel` to filter already-aliased unbound requirements

An unbound requirement that has been aliased to a defined variable should NOT appear in
`UnboundRequirements`. Filter:

```csharp
var aliasedKeys = new HashSet<(Guid, Guid)>();
foreach (var v in rawVars)
{
    foreach (var a in bbAsset.GetAliasesFor(v.Name))
        aliasedKeys.Add((a.RequiringAssetId, a.RequiringElementId));
}

unboundRows = aggregationResult?.Requirements
    .Where(r => !aliasedKeys.Contains((r.RequiringAssetId, r.RequiringElementId)))
    .Select(r => new UnboundRequirementViewModel(...))
    .ToList()
    ?? (IReadOnlyList<UnboundRequirementViewModel>)Array.Empty<UnboundRequirementViewModel>();
```

**Note:** The alias filtering must happen at the view-model level — `BuildViewModel` does it —
not at the aggregation level. The aggregation service always returns all requirements; the window
decides which ones are "unbound" vs "aliased".

### Badge rendering in `DrawClientArea`

Below each defined variable row where `AliasedBy.Count > 0`, render a dimmed sub-row:

```
↳ aliased by: Shoot_BT, Reload_BT
```

Right-click context menu on the badge row: for each aliaser in `AliasedBy`, show
`"Remove alias: {AssetName}"` menu item that calls `bbAsset.RemoveAlias(row.Name, assetId, elementId)`.

---

## Changes required to `VariableViewModel`

The current signature is:
```csharp
internal sealed record VariableViewModel(
    string Name,
    string TypeName,
    int    ByteSize,
    string? Comment);
```

Add:
```csharp
internal sealed record VariableViewModel(
    string Name,
    string TypeName,
    int    ByteSize,
    Type   FieldType,   // needed for type-match drop guard
    string? Comment,
    IReadOnlyList<(string AssetName, Guid AssetId, Guid ElementId)> AliasedBy);
```

Update all call sites to pass the new parameters.

---

## Tests

### Tests in `Hrot.Editor.AiShared.Tests` — extend `BlackboardAuthoringWindowTests.cs`

Add a test file `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/BlackboardAliasingTests.cs`
(or add to existing test class) covering:

```
[Fact] AddAlias_stores_binding_for_variable
[Fact] AddAlias_does_not_duplicate_same_requirement
[Fact] RemoveAlias_removes_binding
[Fact] RemoveAlias_noop_when_not_found
[Fact] RemoveVariable_clears_its_aliases
[Fact] RenameVariable_renames_alias_key
[Fact] BuildViewModel_aliased_requirement_absent_from_unbound_list
[Fact] BuildViewModel_unaliased_requirement_present_in_unbound_list
[Fact] BuildViewModel_variable_row_shows_aliased_by_name
[Fact] BuildViewModel_variable_row_aliased_by_empty_when_no_aliases
```

### Tests in `Hrot.BTree.Editor.Tests`

Add `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BlackboardAliasingAssetTests.cs`:

```
[Fact] BehaviorTreeAsset_AddAlias_stores_binding
[Fact] BehaviorTreeAsset_RemoveAlias_removes_binding
[Fact] BehaviorTreeAsset_RemoveVariable_clears_aliases
[Fact] BehaviorTreeAsset_RenameVariable_renames_alias_dict_key
```

### Tests in `Hrot.Hsm.Editor.Tests`

Same pattern for `HsmAsset`:

```
[Fact] HsmAsset_AddAlias_stores_binding
[Fact] HsmAsset_RemoveAlias_removes_binding
[Fact] HsmAsset_RemoveVariable_clears_aliases
[Fact] HsmAsset_RenameVariable_renames_alias_dict_key
```

---

## Build and test commands

```
dotnet build IOS-IG-SimHost.sln
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj --no-build
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj --no-build
dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj --no-build
```

---

## Report

Create `.dev/_DONE/ai-hsm-btree-vis-edit/reports/BATCH-08-REPORT.md` with:
- Changed files list
- Test counts per project
- Build: 0 errors

---

## Definition of done

- [ ] `BlackboardAliasBinding` record created in `Hrot.Editor.AiShared.Blackboard`
- [ ] `IBlackboardManagedAsset` extended with `GetAliasesFor`, `AddAlias`, `RemoveAlias`
- [ ] `BehaviorTreeAsset` implements the three new methods; `RemoveVariable`+`RenameVariable` updated
- [ ] `HsmAsset` implements the three new methods; `RemoveVariable`+`RenameVariable` updated
- [ ] `UnboundRequirementViewModel` carries `Type DtoType` and `string RequiringAssetName`
- [ ] `VariableViewModel` carries `Type FieldType` and `IReadOnlyList<...> AliasedBy`
- [ ] `BuildViewModel` filters aliased requirements from `UnboundRequirements`
- [ ] `BuildViewModel` populates `AliasedBy` on variable rows
- [ ] `DrawClientArea` handles `BB_UNBOUND_DRAG` drop on variable rows with type-match guard
- [ ] `DrawClientArea` renders aliased-by badge below variable rows
- [ ] `DrawClientArea` provides "Remove alias" context menu on badge rows
- [ ] 18+ new tests; all prior tests still pass
- [ ] `dotnet build IOS-IG-SimHost.sln` = 0 errors
- [ ] Report filed
