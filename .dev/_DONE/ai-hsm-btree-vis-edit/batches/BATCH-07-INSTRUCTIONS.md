# BATCH-07 — Unbound Requirements Panel, Heavy Tier, Memory Budget (Phase 1.5c, Tasks 1c-03 through 1c-05)

## Overview

This batch finishes Phase 1.5c. Three tasks:

- **TASK-BB-1c-03** — Unbound Sub-Tree Requirements section in the `BlackboardAuthoringWindow`
- **TASK-BB-1c-04** — Heavy-tier bin-packing + `Blackboard1024` companion emit + Re-pack optimization pass
- **TASK-BB-1c-05** — Memory budget indicator (inline bar + dual-tier display)

---

## Key design references

- `Blackboard_Authoring_Detailed_Design.md` sections **BB §5.6** (grouping/unbound panel), **§6.1-6.6** (bin-packing, heavy tier), **§4.7** (budget indicator), **§4.1** (window layout)
- `TASK-DETAIL.md` sections **TASK-BB-1c-03**, **TASK-BB-1c-04**, **TASK-BB-1c-05**

---

## Existing code to read before writing anything

| File | Purpose |
|------|---------|
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardBinPacker.cs` | Current bin-packer (inline only; aggregated vars ignored) |
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardDtoEmitter.cs` | Current DTO emitter (inline struct only) |
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IBlackboardAggregator.cs` | AggregationResult, DtoRequirement types |
| `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs` | Window + BuildViewModel + BlackboardWindowViewModel |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/BlackboardBinPackerTests.cs` | Existing packer tests (do not break) |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/BlackboardDtoEmitterTests.cs` | Existing emitter tests (do not break) |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/BlackboardAuthoringWindowTests.cs` | Existing window tests (do not break) |

Read those files **first**.

---

## TASK-BB-1c-03: Unbound Sub-Tree Requirements section

### Goal
Add an **Unbound Requirements** section to the `BlackboardWindowViewModel` that shows
aggregated DTO requirements grouped per sub-tree (BB §5.6).

### Changes to `BlackboardWindowViewModel` + `BuildViewModel`

1. Add a new view-model record for one grouped unbound requirement row:

```csharp
internal sealed record UnboundRequirementViewModel(
    string DtoTypeName,       // e.g. "FireAtTargetParams"
    string RequiredByPath,    // e.g. "Shoot_BT > Action#7 (FireAtTarget)"
    Guid   RequiringAssetId,
    Guid   RequiringElementId);
```

2. Add `IReadOnlyList<UnboundRequirementViewModel> UnboundRequirements` to
   `BlackboardWindowViewModel`.

3. Extend `BuildViewModel` signature to accept an optional
   `AggregationResult? aggregationResult = null`.
   When `aggregationResult` is non-null, project its `Requirements` into
   `UnboundRequirementViewModel` rows — one row per `DtoRequirement`. The design
   (BB §5.6) says to group same-DTO requirements from the same sub-tree, but for
   this slice it is acceptable to show one row per `DtoRequirement` entry and
   note that grouping can be added later. Do NOT collapse requirements silently;
   show them all so the designer sees every dependency.

4. The `UnboundRequirements` list is empty when `aggregationResult` is null or has
   no requirements.

### Changes to `DrawClientArea` (the ImGui rendering method)

Add an "UNBOUND SUB-TREE REQUIREMENTS" collapsible section below the Defined Variables
section. For each `UnboundRequirementViewModel`:

- Show: `[diamond glyph] {DtoTypeName}  --  Required by: {RequiredByPath}`
- Right-click context menu item: "Promote to new variable" (for this batch:
  just show the menu item, no action yet -- promotion is Phase 1.5d)

The section should render with a sub-header label matching the design: display it
only when `UnboundRequirements.Count > 0` OR always (even empty). Pick always-visible
for discoverability.

### Tests to add in `BlackboardAuthoringWindowTests.cs`

```
[Fact] BuildViewModel_no_aggregation_result_yields_empty_unbound_list
[Fact] BuildViewModel_aggregation_result_with_requirements_yields_unbound_rows
[Fact] BuildViewModel_aggregation_result_requirement_DtoTypeName_uses_type_Name
[Fact] BuildViewModel_aggregation_result_requirement_RequiredByPath_preserved
```

---

## TASK-BB-1c-04: Heavy-tier bin-packing + companion emit + Re-pack pass

### Part A: Extend `BlackboardBinPacker.Pack()`

The current implementation ignores `aggregatedVars`. Extend it as follows:

**New behavior when `aggregatedVars` is non-null and non-empty:**

1. Pack `masterVars` as before (always inline). If they already exceed 100 bytes,
   return with `PackWarning.InlineMemoryExceeded` and `RequiresHeavyComponent = false`
   (the master budget error takes precedence; don't confuse the caller with a heavy
   upgrade that can't help).

2. For each aggregated var in order: try to pack inline (if `offset + size <= MaxInlineBytes`).
   If it fits, pack inline. If it does not fit, pack to **heavy tier** starting at the
   current heavy offset.

3. Heavy variables: use a separate offset counter starting at 0, with the same
   C# sequential alignment rules. The heavy budget is 928 bytes
   (`MaxHeavyBytes = 928`). Add `MaxHeavyBytes = 928` constant to the packer class.
   If heavy also exceeds 928 bytes, emit `PackWarning.HeavyMemoryExceeded` (new enum value).

4. `RequiresHeavyComponent = true` when any variable was placed in heavy.

5. `TotalHeavyBytes` is a new int property on `PackResult`. Add it.
   `TotalInlineBytes` remains the number of bytes used in the inline tier.

**Modified `PackResult` record:**

```csharp
public record PackResult(
    IReadOnlyList<PackedVariable> Variables,
    int TotalInlineBytes,
    int TotalHeavyBytes,       // NEW: 0 when no heavy variables
    bool RequiresHeavyComponent,
    PackWarning Warning);
```

Since `PackResult` is a record, adding a new positional parameter is a breaking
change. Use a named parameter with a default instead, OR change `PackResult` to
use init properties. Choose whichever causes fewer call-site changes.

Check existing call sites: `BlackboardAuthoringWindow.BuildViewModel` calls
`BlackboardBinPacker.Pack(descriptors)`. Update that call site after the change.

**New `PackWarning` enum values:**
- `HeavyMemoryExceeded` (in addition to existing `None`, `InlineMemoryExceeded`)

### Part B: Static Re-pack optimization in `BlackboardBinPacker`

Add a `static Repack(IReadOnlyList<BlackboardVariableDescriptor> vars)` method
that sorts the vars to minimize alignment padding (largest-alignment-first within
the same tier), then calls `Pack`. This is the "Re-pack" toolbar action.

For this batch, just implement the sort-and-pack logic and expose it as a method.
The toolbar button invocation is in the window's `DrawClientArea` (add a button
but for this batch it's OK to simply call `Repack` and log -- no further wiring needed).

### Part C: Heavy companion file emit in `BlackboardDtoEmitter`

The current emitter produces one inline struct. When `RequiresHeavyComponent` is true,
also produce a companion `{AssetName}HeavyBlackboard` struct.

Add a new method to `BlackboardDtoEmitter`:

```csharp
/// <summary>
/// Emits the companion heavy struct file content.
/// Only called when the pack result indicates heavy variables exist.
/// </summary>
public static string EmitHeavy(BlackboardDtoModel model, string heavyStructName)
```

This produces a `.cs` file with the same marker block, same namespace, same usings,
but a different struct name (passed as `heavyStructName`, typically `{AssetName}HeavyBlackboard`)
and only the fields whose `PackTier == Heavy`.

`BlackboardDtoModel` already exists. The `EditorManagedFieldEntry` / `ReadOnlyFieldEntry`
entries are already tagged -- you need to determine tier from the `PackResult` and
build a filtered `BlackboardDtoModel` for the heavy struct. Design this as the caller's
responsibility: the caller filters the model entries to only include heavy fields, then
calls `EmitHeavy` with the filtered model.

Alternatively: add an overload `Emit(BlackboardDtoModel model, PackTier tier)` that
filters internally. Choose whatever is cleaner for tests.

The content of the heavy struct file: same four-line marker block (same AssetId,
same AssetName); namespace; `[StructLayout(LayoutKind.Sequential)]`; `public partial struct {heavyStructName}` with only the heavy fields.

### Tests for Part A

Add to `BlackboardBinPackerTests.cs`:

```
[Fact] Pack_aggregated_vars_that_fit_inline_placed_inline
[Fact] Pack_aggregated_vars_that_overflow_inline_placed_heavy
[Fact] Pack_aggregated_vars_require_heavy_component_flag
[Fact] Pack_master_overflow_does_not_trigger_heavy_placement
[Fact] Pack_heavy_offset_starts_at_zero
[Fact] Pack_heavy_alignment_respected
[Fact] TotalHeavyBytes_zero_when_no_heavy_vars
[Fact] TotalHeavyBytes_nonzero_when_heavy_vars_present
```

### Tests for Part C

Add to `BlackboardDtoEmitterTests.cs`:

```
[Fact] EmitHeavy_produces_correct_marker_block
[Fact] EmitHeavy_includes_only_heavy_fields
[Fact] EmitHeavy_struct_name_matches_parameter
[Fact] EmitHeavy_empty_heavy_fields_produces_empty_struct
```

---

## TASK-BB-1c-05: Memory budget indicator in the window view-model

### Goal
Surface the bin-packer result as budget metrics in the window's view-model so the
header can show `X / 100 B` (single tier) or `Inline: a / 100 B  Heavy: b / 928 B`
(dual tier).

### Changes to `BlackboardWindowViewModel`

Add:
```csharp
int TotalHeavyBytes          // 0 when no heavy vars
int InlineBudget             // always 100
int HeavyBudget              // always 928 (meaningful only when RequiresHeavyComponent)
bool RequiresHeavyComponent  // from pack result
```

The existing `TotalInlineBytes` covers the inline used bytes.

### Changes to `BuildViewModel`

After calling `BlackboardBinPacker.Pack(descriptors, aggregatedDescriptors)`, read
`pack.TotalInlineBytes`, `pack.TotalHeavyBytes`, `pack.RequiresHeavyComponent` and
populate the new view-model fields.

`BuildViewModel` now needs access to the aggregated variables as `BlackboardVariableDescriptor`
list in addition to `AggregationResult`. Derive the aggregated descriptors from the
`AggregationResult.Requirements`:

```csharp
var aggregatedDescriptors = aggregationResult?.Requirements
    .Select(r => new BlackboardVariableDescriptor(r.DtoType.Name, r.DtoType))
    .ToList();
```

### DrawClientArea rendering

Add to the header bar:
- When `RequiresHeavyComponent == false`: `Memory: {TotalInlineBytes} / {InlineBudget} B`
- When `RequiresHeavyComponent == true`:
  `Inline: {TotalInlineBytes} / {InlineBudget} B  Heavy: {TotalHeavyBytes} / {HeavyBudget} B`

Colour thresholds (apply to ImGui text color):
- Below 80% of budget: default text color
- 80-99%: amber/yellow
- 100%+: red

Implement this as a helper: `BudgetColor(int used, int budget)` returning an
`ImGui.ColorU32` value. The exact colour constants can be approximated:
amber ~0xFF00BFFF, red ~0xFF0000FF (ABGR byte order for ImGui). Use whatever
looks reasonable and document the intent.

### Tests for view-model metrics

Add to `BlackboardAuthoringWindowTests.cs`:

```
[Fact] BuildViewModel_budget_inline_only_when_no_aggregation
[Fact] BuildViewModel_budget_inline_budget_is_100
[Fact] BuildViewModel_budget_heavy_is_928
[Fact] BuildViewModel_requires_heavy_false_when_all_fit_inline
[Fact] BuildViewModel_requires_heavy_true_when_aggregated_overflow_inline
```

Note: these tests should construct a mock `AggregationResult` with enough large-type
requirements to trigger heavy overflow and verify `RequiresHeavyComponent == true`
in the view-model.

---

## Build and test commands

After each task:
```
dotnet build IOS-IG-SimHost.sln
```

After completing all tasks:
```
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj --no-build
```

(BTree.Editor.Tests and Hsm.Editor.Tests should also still pass; run them to confirm no regressions.)

---

## Report

Create `.dev/_DONE/ai-hsm-btree-vis-edit/reports/BATCH-07-REPORT.md` with:
- List of changed files
- Test counts per project
- Build: 0 errors
- Any open questions or deferred items

---

## Definition of done

- [ ] `UnboundRequirementViewModel` + `UnboundRequirements` in `BlackboardWindowViewModel`
- [ ] `BuildViewModel` accepts `AggregationResult?`; projects requirements into `UnboundRequirements`
- [ ] `BlackboardBinPacker.Pack` handles aggregated vars with heavy-tier spill
- [ ] `BlackboardBinPacker.MaxHeavyBytes = 928` constant added
- [ ] `PackResult.TotalHeavyBytes` added
- [ ] `PackWarning.HeavyMemoryExceeded` added
- [ ] `BlackboardBinPacker.Repack(vars)` static optimization method added
- [ ] `BlackboardDtoEmitter.EmitHeavy(model, structName)` (or equivalent) added
- [ ] `BlackboardWindowViewModel` has `TotalHeavyBytes`, `InlineBudget`, `HeavyBudget`, `RequiresHeavyComponent`
- [ ] 20+ new tests; all prior tests still pass
- [ ] `dotnet build IOS-IG-SimHost.sln` = 0 errors
- [ ] Report filed
