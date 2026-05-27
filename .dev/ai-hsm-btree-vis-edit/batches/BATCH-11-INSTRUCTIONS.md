# BATCH-11 Developer Instructions

**Target tasks:** TASK-BB-1e-03, TASK-BB-1e-04, TASK-BB-1e-05  
**Phase:** 1.5e — Approach B field-level synchronization (completion)  
**Expected new test count:** BTree >= 265 (was 239), AiShared >= 375 (was 372)

---

## Context

Batches 09 and 10 completed Approach B UI groundwork:
- `IBTreeSyncableAsset` with `GetSubtreeNodeInfo`, `GetSyncBindings`, `SetSyncBinding`, `ClearSyncBindings`, `GetVariablesOfType` (Batch 10)
- `InspectorWindow` renders a sync table per sub-DTO field, with "Bound to" dropdown and `SyncIn`/`SyncOut` checkboxes (Batch 10)
- `SubtreeSyncBinding` record: `(FieldName, MasterVariableName?, SyncIn, SyncOut)` (Batch 10)
- `SubtreeNodeInfo` record: `(IsResolved, SubtreeAssetId)` (Batch 10)

**Missing pieces** (this batch):
- Sync bindings are in-memory only — not persisted to the layout method (1e-03)
- No orchestrator is emitted for approach-B nodes (1e-04)
- No dedicated memory slice is auto-allocated for approach-B subtrees (1e-05)

All three tasks are in `Hrot.BTree.Editor` (plus `Hrot.Editor.AiShared` for the new shared record type).

---

## Reference Documents

- **Main design:** `.dev/ai-hsm-btree-vis-edit/Blackboard_Authoring_Detailed_Design.md`  
  Key sections: BB §8.2 (workflow), §8.3 (emit), §8.4 (per-subtree DTO), §8.7 (ordering), §14.6 (layout persistence)
- **Task detail:** `.dev/ai-hsm-btree-vis-edit/TASK-DETAIL.md` — sections 1e-03, 1e-04, 1e-05

---

## TASK-BB-1e-03 — Layout method persistence for sync bindings

### Goal

When a `BehaviorTreeAsset` is emitted (saved), sync bindings must be written into the
`[BTreeLayout]` method so they survive a round-trip through `BTreeFluentEmitter` →
`BehaviorTreeAssetProjector`.

### Changes required

#### 1. `BTreeEditorLayoutBuilder` — add `SubtreeSyncField` method

File: `Hrot/Editor/Hrot.Editor.AiShared/Layout/BTreeEditorLayoutBuilder.cs`

```csharp
// New field accumulator (add to the class):
private readonly Dictionary<Guid, List<SubtreeSyncBinding>> _syncBindings = new();

// New method:
public BTreeEditorLayoutBuilder SubtreeSyncField(
    string visualId,
    string fieldName,
    string? masterVar,
    bool syncIn,
    bool syncOut)
{
    var id = Guid.Parse(visualId);
    if (!_syncBindings.TryGetValue(id, out var list))
    {
        list = new List<SubtreeSyncBinding>();
        _syncBindings[id] = list;
    }
    list.Add(new SubtreeSyncBinding(fieldName, masterVar, syncIn, syncOut));
    return this;
}
```

The `Build()` method must expose `_syncBindings` in the returned `BTreeEditorLayout`. See next item.

The `using` directive for `Hrot.Editor.AiShared.Blackboard` is needed for `SubtreeSyncBinding`.

#### 2. `BTreeEditorLayout` — add `SyncBindings` property

File: `Hrot/Editor/Hrot.Editor.AiShared/Layout/BTreeEditorLayout.cs`

Add:
```csharp
// Sync bindings per subtree-node visual ID. Empty when none configured.
public IReadOnlyDictionary<Guid, IReadOnlyList<SubtreeSyncBinding>> SyncBindings { get; init; } =
    new Dictionary<Guid, IReadOnlyList<SubtreeSyncBinding>>();
```

Update `Build()` in `BTreeEditorLayoutBuilder` to populate this from `_syncBindings`:
```csharp
SyncBindings = _syncBindings.ToDictionary(
    kv => kv.Key,
    kv => (IReadOnlyList<SubtreeSyncBinding>)kv.Value.AsReadOnly()),
```

#### 3. `BTreeFluentEmitter.EmitLayout` — emit `.SubtreeSyncField(...)` calls

File: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Emit/BTreeFluentEmitter.cs`

After the pill entries loop and before the `.Build()` line:

```
// Emit subtree sync field entries sorted by nodeVisualId then fieldName for determinism.
```

Collect sync bindings via a new internal accessor on `BehaviorTreeAsset`:
```csharp
var syncGroups = asset.GetAllSyncBindings(); // returns IReadOnlyDictionary<Guid, IReadOnlyList<SubtreeSyncBinding>>
```

For each node's bindings, emit one `.SubtreeSyncField(...)` call per binding, sorted:
1. By `nodeVisualId.ToString("D")` ascending (for determinism across nodes)
2. Within each node, by `FieldName` ascending (for determinism within a node)

The emitted form:
```csharp
.SubtreeSyncField("{visualId:D}", "{fieldName}", {masterVarExpr}, syncIn: {syncIn}, syncOut: {syncOut})
```

Where `{masterVarExpr}` is either `masterVar: "{name}"` or `masterVar: null`.

Do NOT emit entries where both `SyncIn=false` and `SyncOut=false` AND `MasterVariableName=null`
(completely empty binding — no-op to persist).

The `lastEntry` detection for the `.Build()` call must account for the new `.SubtreeSyncField`
entries: the last `.SubtreeSyncField` call is the last chain call (gets the `.Build();` suffix)
only if no nodes or pills come after it. Since sync fields come last in the chain, the last
sync field entry is the last chained call overall.

Refactor `EmitLayout` to compute `lastEntry` for nodes, pills, and sync fields correctly:
- `isLast` for a node entry: `i == nodeEntries.Count - 1 && pillEntries.Count == 0 && syncFields.Count == 0`
- `isLast` for a pill entry: `i == pillEntries.Count - 1 && syncFields.Count == 0`
- `isLast` for a sync field entry: it's the last item in the flattened sorted list

Keep the helper methods `EmitLayoutNodeEntry` and `EmitLayoutPillEntry` unchanged in signature.
Add a new `EmitSyncFieldEntry(StringBuilder sb, string visualId, SubtreeSyncBinding b, bool isLast)`.

#### 4. `BehaviorTreeAsset` — add `GetAllSyncBindings()` and `LoadSyncBindings()`

File: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs`

```csharp
// Exposes _syncBindings for the emitter (read-only view).
public IReadOnlyDictionary<Guid, IReadOnlyList<SubtreeSyncBinding>> GetAllSyncBindings() =>
    _syncBindings.ToDictionary(
        kv => kv.Key,
        kv => (IReadOnlyList<SubtreeSyncBinding>)kv.Value.AsReadOnly());

// Called by BehaviorTreeAssetProjector after applying node layout.
public void LoadSyncBindings(IReadOnlyDictionary<Guid, IReadOnlyList<SubtreeSyncBinding>>? bindings)
{
    _syncBindings.Clear();
    if (bindings is null) return;
    foreach (var kv in bindings)
        _syncBindings[kv.Key] = new List<SubtreeSyncBinding>(kv.Value);
}
```

#### 5. `BehaviorTreeAssetProjector` — call `LoadSyncBindings`

File: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAssetProjector.cs`

After the `foreach (var node in nodes)` block that applies layout positions, add:
```csharp
asset.LoadSyncBindings(layout?.SyncBindings);
```

### Tests for 1e-03

New file: `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeSyncPersistenceTests.cs`

```
T1: EmitLayout_IncludesSyncField_WhenBindingHasSyncIn
    Setup: asset with one subtree node; add a binding (field="Aim", masterVar="SharedAim", syncIn: true, syncOut: false).
    Act:   BTreeFluentEmitter.Emit(asset).
    Assert: output contains .SubtreeSyncField(...) with the correct arguments.

T2: EmitLayout_OmitsSyncField_WhenBindingIsAllFalseAndNoMasterVar
    Setup: asset with one subtree node; add a binding (field="Aim", masterVar: null, syncIn: false, syncOut: false).
    Act:   BTreeFluentEmitter.Emit(asset).
    Assert: output does NOT contain .SubtreeSyncField.

T3: EmitLayout_EmitsSyncFields_InFieldNameOrder
    Setup: two bindings on one node — FieldName="ZField" and FieldName="AAField".
    Act:   Emit.
    Assert: "AAField" appears before "ZField" in the output.

T4: LoadSyncBindings_RestoresState_AfterProjection
    Setup: build a BTreeEditorLayout with SyncBindings populated (one node, two bindings).
    Act:   BehaviorTreeAssetProjector.Project(..., layout, ...).
    Assert: asset.GetSyncBindings(nodeId) returns the two bindings.

T5: RoundTrip_EmitThenProject_PreservesBindings
    Setup: asset with a subtree node + two bindings; emit to string; re-project from the
           emitted layout via BTreeEditorLayoutBuilder (manually parse the emitted
           .SubtreeSyncField calls into a BTreeEditorLayout, or use a test helper that
           builds the layout directly to verify round-trip data).
    Assert: bindings after round-trip match original.
    NOTE: full parse of emitted C# is out of scope; test the Emit and LoadSyncBindings paths
          separately (T1 + T4 form the round-trip confidence).
```

---

## TASK-BB-1e-04 — Orchestrator emit with sync copies

### Goal

Extend `BTreeOrchestratorEmitter` to emit Approach B orchestrators: one `[BTreeAction]`
static method per subtree node that has at least one `SyncIn=true` or `SyncOut=true` binding.

The emitted method:
```csharp
[BTreeAction(Name = "Orchestrate_{SubtreeName}")]
public static NodeStatus Orchestrate_{SubtreeName}_Tick(
    ref {MasterBbType} master,
    ref BehaviorTreeState state,
    ref {CtxType} ctx,
    int paramIndex)
{
    // Project the sub-tree's DTO from the auto-allocated slice (from 1e-05).
    ref var subDto = ref master.{SubtreeName}_{SubDtoTypeName};

    // Sync In (pre-tick, in declared field order)
    subDto.{Field1} = master.{MasterVar1};

    // Tick
    var result = {SubtreeName}.GetInterpreter().Tick(ref subDto, ref state, ref ctx);

    // Sync Out (post-tick, in declared field order)
    master.{MasterVar2} = subDto.{Field2};

    return result;
}
```

Rules:
- Only emit sync-in assignments where `SyncIn=true` AND `MasterVariableName != null`.
- Only emit sync-out assignments where `SyncOut=true` AND `MasterVariableName != null`.
- If a node has bindings but ALL effective sync operations are suppressed (all SyncIn=false
  or no MasterVar), do NOT emit an orchestrator for that node.
- Field order within sync-in and sync-out blocks: alphabetical by `FieldName` (deterministic).
- Do not conflict with Approach A orchestrators; if a variable alias covers the same sub-tree
  (same `SubtreeName` identifier), prefer Approach A and skip Approach B for that node.
  Detection: if `methods` (from the existing Approach A loop) already contains an entry with
  `subTreeName == SanitizeIdentifier(group.SubtreeName)`, skip the Approach B entry.

### New shared record type

New file: `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/ApproachBSyncGroup.cs`

```csharp
namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Describes one subtree node that needs an Approach B orchestrator.
/// Produced by <see cref="IBTreeSyncableAsset.GetApproachBSyncGroups"/>.
/// </summary>
/// <param name="NodeVisualId">Visual ID of the Subtree node in the master BTree.</param>
/// <param name="SubtreeName">
/// Identifier-safe name of the sub-tree asset (e.g. "Shoot_BT").
/// Used as the orchestrator method name suffix.
/// </param>
/// <param name="SubtreeDtoTypeName">
/// Short type name of the sub-tree's blackboard struct (e.g. "FireAtTargetParams").
/// </param>
/// <param name="SubtreeDtoTypeNs">
/// Namespace of the sub-tree's blackboard struct, or null when in the same namespace.
/// </param>
/// <param name="Bindings">
/// All sync bindings for this node (including SyncIn=false/SyncOut=false entries).
/// The emitter filters to active sync operations.
/// </param>
public sealed record ApproachBSyncGroup(
    Guid NodeVisualId,
    string SubtreeName,
    string SubtreeDtoTypeName,
    string? SubtreeDtoTypeNs,
    IReadOnlyList<SubtreeSyncBinding> Bindings);
```

### Changes to `IBTreeSyncableAsset`

File: `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IBTreeSyncableAsset.cs`

Add two methods:

```csharp
/// <summary>
/// Records sub-tree identity metadata for a Subtree node.
/// Called by the Inspector whenever it renders the sync table for a resolved node.
/// This metadata is used by the orchestrator emitter to generate Approach B actions.
/// </summary>
void RecordSubtreeNodeMeta(
    Guid nodeVisualId,
    string subTreeName,
    string subDtoTypeName,
    string? subDtoTypeNs);

/// <summary>
/// Returns Approach B sync groups: subtree nodes that have at least one binding
/// where <see cref="SubtreeSyncBinding.SyncIn"/> or <see cref="SubtreeSyncBinding.SyncOut"/>
/// is true, and whose sub-tree identity has been recorded via
/// <see cref="RecordSubtreeNodeMeta"/>.
/// </summary>
IReadOnlyList<ApproachBSyncGroup> GetApproachBSyncGroups();
```

### Changes to `BehaviorTreeAsset`

File: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs`

Add a metadata dictionary alongside `_syncBindings`:

```csharp
// Sub-tree identity metadata per subtree-node visual ID. Populated by Inspector callbacks.
private readonly Dictionary<Guid, (string SubtreeName, string SubDtoTypeName, string? SubDtoTypeNs)> _syncNodeMeta = new();
```

Implement `RecordSubtreeNodeMeta`:
```csharp
public void RecordSubtreeNodeMeta(Guid nodeVisualId, string subTreeName, string subDtoTypeName, string? subDtoTypeNs)
    => _syncNodeMeta[nodeVisualId] = (subTreeName, subDtoTypeName, subDtoTypeNs);
```

Implement `GetApproachBSyncGroups`:
```csharp
public IReadOnlyList<ApproachBSyncGroup> GetApproachBSyncGroups()
{
    var result = new List<ApproachBSyncGroup>();
    foreach (var kv in _syncBindings)
    {
        var nodeId = kv.Key;
        var bindings = kv.Value;
        // Only include if at least one binding has active sync direction.
        bool anyActive = bindings.Any(b =>
            (b.SyncIn || b.SyncOut) && b.MasterVariableName != null);
        if (!anyActive) continue;
        // Only include if identity metadata has been recorded.
        if (!_syncNodeMeta.TryGetValue(nodeId, out var meta)) continue;
        result.Add(new ApproachBSyncGroup(
            nodeId,
            meta.SubtreeName,
            meta.SubDtoTypeName,
            meta.SubDtoTypeNs,
            bindings.AsReadOnly()));
    }
    return result;
}
```

### Changes to `InspectorWindow`

File: `Hrot/Editor/Hrot.Editor.AiShared/Windows/InspectorWindow.cs`

In `DrawSyncBindingsTable`, after resolving `subAsset` and before calling `BeginTable`,
call:
```csharp
string subDtoTypeName = BlackboardTypeHelper.GetDisplayName(
    subAsset.BlackboardVariables.Count > 0
        ? subAsset.BlackboardVariables[0].FieldType
        : typeof(void));
// Derive the display-name namespace from the first variable's type, or null.
string? subDtoTypeNs = subAsset.BlackboardVariables.Count > 0
    ? subAsset.BlackboardVariables[0].FieldType.Namespace
    : null;
```

Wait — this is wrong. The sub-DTO type is NOT derived from its variables; it's the `BlackboardTypeName` property of the sub-asset. But `IBlackboardManagedAsset` has `BlackboardTypeName`? Let me check...

Looking at `HsmAsset.BlackboardTypeName` and `BehaviorTreeAsset.BlackboardTypeName` — yes, both assets expose `BlackboardTypeName` as a string. But `IBlackboardManagedAsset` doesn't declare `BlackboardTypeName`.

**Option**: Add `BlackboardTypeName` to `IBlackboardManagedAsset` as a default property or use the asset cast.

**Simplest approach for this batch**: Add `string BlackboardTypeName { get; }` to `IBlackboardManagedAsset` with a default implementation:
```csharp
string BlackboardTypeName => GetType().Name + "_Blackboard";
```

Then `DrawSyncBindingsTable` calls:
```csharp
string subDtoTypeName = ShortName(subAsset.BlackboardTypeName);
string? subDtoTypeNs  = NsOf(subAsset.BlackboardTypeName);
syncAsset.RecordSubtreeNodeMeta(nodeVisualId, SanitizeName(subAsset.Name??""), subDtoTypeName, subDtoTypeNs);
```

Where `ShortName` extracts the last `.`-separated segment and `NsOf` extracts the namespace.

Actually, this is getting complex. **Simpler approach**: just use the sub-asset's `Name` property (which `IBlackboardManagedAsset` does expose via the base interface) for the sub-tree name, and use `BlackboardTypeName` directly from the cast.

**Actual simplest approach**: Don't call `RecordSubtreeNodeMeta` from `InspectorWindow`. Instead, make `GetApproachBSyncGroups()` look at the `_syncNodeMeta` dictionary, which is populated from outside. Add a method to `BehaviorTreeAsset` that the test can use directly, bypassing the InspectorWindow.

For `InspectorWindow`, extend `DrawSyncBindingsTable` signature to accept `subAsset.BlackboardTypeName` context and call `RecordSubtreeNodeMeta`. This is the right design.

Add `string BlackboardTypeName { get; }` to `IBlackboardManagedAsset`:
```csharp
// Default: derive from the implementing type name.
string BlackboardTypeName => GetType().Name + "_Blackboard";
```

Both `BehaviorTreeAsset` and `HsmAsset` already have this property, so they'll override the default.

In `DrawSyncBindingsTable`, derive the sub-tree name from `subAsset` (cast to `IBlackboardManagedAsset`). The sub-tree asset's `Name` is its asset name (e.g. "Shoot_BT"). Call `RecordSubtreeNodeMeta` once per node render:

```csharp
// Before drawing the table:
string shortName   = ShortTypeName(subAsset.BlackboardTypeName);
string? typeNs     = NsOf(subAsset.BlackboardTypeName);
syncAsset.RecordSubtreeNodeMeta(nodeVisualId, SanitizeIdentifier(/* subAsset name */), shortName, typeNs);
```

Where do we get the sub-asset's name? `IBlackboardManagedAsset` doesn't expose `Name` directly.

Add `string Name { get; }` to `IBlackboardManagedAsset` as well? It would be a default:
```csharp
string Name => GetType().Name;
```

Both `BehaviorTreeAsset` and `HsmAsset` already have a `Name` property, so they'll satisfy this.

**Summary of IBlackboardManagedAsset additions:**
```csharp
string Name => GetType().Name;
string BlackboardTypeName => GetType().Name + "_Blackboard";
```

These are backward-compatible default implementations. Existing stubs don't need to change unless they're missing `Name` or `BlackboardTypeName` (they will use the defaults).

### Changes to `BTreeOrchestratorEmitter`

File: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Emit/BTreeOrchestratorEmitter.cs`

After the Approach A loop (which processes alias bindings), add an Approach B section:

```csharp
// Approach B: subtree nodes with field-level sync bindings.
var syncGroups = asset.GetApproachBSyncGroups();
var approachBMethods = new List<(ApproachBSyncGroup group)>();
foreach (var group in syncGroups)
{
    string key = SanitizeIdentifier(group.SubtreeName);
    // Skip if already covered by Approach A.
    if (seen.Contains("\x1F" + key) || methods.Any(m => m.subTreeName == key)) continue;
    // Collect effective sync-in and sync-out bindings.
    var syncIn  = group.Bindings
        .Where(b => b.SyncIn  && b.MasterVariableName != null)
        .OrderBy(b => b.FieldName, StringComparer.Ordinal)
        .ToList();
    var syncOut = group.Bindings
        .Where(b => b.SyncOut && b.MasterVariableName != null)
        .OrderBy(b => b.FieldName, StringComparer.Ordinal)
        .ToList();
    if (syncIn.Count == 0 && syncOut.Count == 0) continue;
    approachBMethods.Add((group));
    // Collect using directives.
    if (!string.IsNullOrEmpty(group.SubtreeDtoTypeNs)
        && !string.Equals(group.SubtreeDtoTypeNs, targetNs, StringComparison.Ordinal))
        usingsSet.Add(group.SubtreeDtoTypeNs!);
}
```

Emit Approach B methods after the Approach A methods:

```csharp
foreach (var (group) in approachBMethods)
{
    string subTreeId = SanitizeIdentifier(group.SubtreeName);
    string sliceField = $"{subTreeId}_{group.SubtreeDtoTypeName}";
    var syncIn  = group.Bindings
        .Where(b => b.SyncIn  && b.MasterVariableName != null)
        .OrderBy(b => b.FieldName, StringComparer.Ordinal).ToList();
    var syncOut = group.Bindings
        .Where(b => b.SyncOut && b.MasterVariableName != null)
        .OrderBy(b => b.FieldName, StringComparer.Ordinal).ToList();

    sb.AppendLine();
    sb.AppendLine($"{Indent}[BTreeAction(Name = \"Orchestrate_{subTreeId}\")]");
    sb.AppendLine($"{Indent}public static NodeStatus Orchestrate_{subTreeId}_Tick(");
    sb.AppendLine($"{Indent}{Indent}ref {bbShort} master,");
    sb.AppendLine($"{Indent}{Indent}ref BehaviorTreeState state,");
    sb.AppendLine($"{Indent}{Indent}ref {ctxShort} ctx,");
    sb.AppendLine($"{Indent}{Indent}int paramIndex)");
    sb.AppendLine($"{Indent}{{");
    sb.AppendLine($"{Indent}{Indent}ref var subDto = ref master.{sliceField};");
    foreach (var b in syncIn)
        sb.AppendLine($"{Indent}{Indent}subDto.{b.FieldName} = master.{b.MasterVariableName};");
    sb.AppendLine($"{Indent}{Indent}var result = {subTreeId}.GetInterpreter().Tick(ref subDto, ref state, ref ctx);");
    foreach (var b in syncOut)
        sb.AppendLine($"{Indent}{Indent}master.{b.MasterVariableName} = subDto.{b.FieldName};");
    sb.AppendLine($"{Indent}{Indent}return result;");
    sb.AppendLine($"{Indent}}}");
}
```

The `Emit()` method should return non-null if either Approach A methods or Approach B methods exist.

Update the early-return check:
```csharp
if (methods.Count == 0 && approachBMethods.Count == 0) return null;
```

### Tests for 1e-04

New file: `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeOrchestratorSyncEmitterTests.cs`

```
T1: Emit_ReturnsNull_WhenNoAliasesAndNoSyncGroups
    (Existing Emit_ReturnsNull test covers this; write one specifically for no sync groups.)

T2: Emit_ContainsApproachBMethod_WhenSyncInBinding
    Setup: asset with subtree node nid; RecordSubtreeNodeMeta(nid, "Shoot_BT", "FireDto", null);
           SetSyncBinding(nid, new SubtreeSyncBinding("Aim", "SharedAim", true, false)).
    Assert: output contains [BTreeAction(Name = "Orchestrate_Shoot_BT")];
            output contains "ref var subDto = ref master.Shoot_BT_FireDto;";
            output contains "subDto.Aim = master.SharedAim;";
            output does NOT contain "master.SharedAim = subDto.Aim;" (no sync-out).

T3: Emit_ContainsApproachBMethod_WhenSyncOutBinding
    Setup: syncOut only binding.
    Assert: output contains "master.SharedStatus = subDto.StatusOut;";
            no pre-tick assignment line.

T4: Emit_SyncInBeforeTick_SyncOutAfterTick
    Setup: both SyncIn and SyncOut on different fields.
    Assert: in the output, the sync-in line appears before the Tick call,
            the sync-out line appears after.

T5: Emit_SyncInFields_InAlphaOrder
    Setup: two sync-in bindings: FieldName="ZAim", FieldName="AAim".
    Assert: "subDto.AAim = ..." appears before "subDto.ZAim = ..." in the output.

T6: Emit_SkipsBinding_WhenNoMasterVar
    Setup: binding with MasterVariableName=null, SyncIn=true.
    Assert: no sync-in line in output; if no other active bindings, no orchestrator emitted.

T7: Emit_ApproachAPreemptsApproachB_WhenSameSubtreeName
    Setup: asset has an alias binding for var "SharedFire" to "Shoot_BT" (Approach A),
           AND a sync group for "Shoot_BT" (Approach B).
    Assert: only one orchestrator method emitted (the Approach A one);
            no Approach B method for "Shoot_BT".
```

---

## TASK-BB-1e-05 — Per-Subtree DTO allocation when no aliasing

### Goal

When a subtree node uses Approach B (has sync bindings) but is NOT covered by an Approach A
alias, the editor automatically reserves a named field `{SubtreeName}_{SubtreeDtoTypeName}` in
the master blackboard. This field:

- Appears in the Blackboard Authoring panel under a "Sub-tree allocations" sub-section, dimmed.
- Is added to the bin-packer's aggregated input so it participates in inline/heavy placement.
- Is suppressed when Approach A aliasing is configured for the same sub-tree node.

### New method on `BehaviorTreeAsset`

```csharp
/// <summary>
/// Returns auto-allocated blackboard variable entries for Approach B subtree nodes
/// that are not covered by an Approach A alias.
/// The caller adds these to the bin-packer's aggregated variable list.
/// </summary>
public IReadOnlyList<BlackboardVariableEntry> GetAutoAllocatedVariables()
{
    var groups = GetApproachBSyncGroups();
    if (groups.Count == 0) return Array.Empty<BlackboardVariableEntry>();

    var result = new List<BlackboardVariableEntry>(groups.Count);
    foreach (var group in groups)
    {
        // Check if covered by Approach A: any master variable has an alias binding
        // whose RequiringElementId == group.NodeVisualId.
        bool coveredByA = _blackboardVariables.Any(v =>
            GetAliasesFor(v.Name).Any(a => a.RequiringElementId == group.NodeVisualId));
        if (coveredByA) continue;

        string fieldName = $"{group.SubtreeName}_{group.SubtreeDtoTypeName}";
        // Use object as a placeholder type — the real type is known only at runtime.
        // The panel displays the type name textually; bin-packer uses Marshal.SizeOf which
        // needs a real type. For Approach B auto-allocations, use a placeholder size of
        // sizeof(SubtreeDtoType) which callers must inject, OR expose the size directly.
        // For this batch: record the entry with type=typeof(object) and a display-name override.
        // DEBT: real type resolution requires catalog integration (deferred to a future batch).
        result.Add(new BlackboardVariableEntry(fieldName, typeof(object), comment: null));
    }
    return result;
}
```

**Important caveat:** The bin-packer uses `Marshal.SizeOf(variable.FieldType)` to compute byte
offsets. `typeof(object)` is not blittable and will throw. For this batch, the auto-allocated
variable is DISPLAY-ONLY — it is rendered in the panel but NOT passed to the bin-packer until
the type resolution path is available. Leave a `// TODO(1e-05): pass to bin-packer when type
resolution is available` comment in `BlackboardAuthoringWindow`.

### Changes to `BlackboardAuthoringWindow`

File: `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs`

In `BuildViewModel` (or wherever `PackResult` is assembled from the aggregated variables),
call `autoAllocs = (masterAsset as BehaviorTreeAsset)?.GetAutoAllocatedVariables()` if it
is a `BTreeSyncableAsset`. Add them to the view model's display only (NOT to the bin-packer input
until size resolution is implemented).

In `DrawClientArea`, after the "DEFINED VARIABLES" table and before the "UNBOUND SUB-TREE
REQUIREMENTS" section, add a new collapsible sub-section:

```
▼ SUB-TREE ALLOCATIONS (auto-managed)
   (dimmed rows; no edit controls)
   {fieldName}    {dtoTypeName}    (size unknown until build)
```

If `autoAllocs` is empty or null, omit the section entirely.

Each row shows the field name and type name (from `group.SubtreeDtoTypeName`), dimmed.
No edit controls, no drag source, no delete button.

### Tests for 1e-05

New file: `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeAutoAllocationTests.cs`

```
T1: GetAutoAllocatedVariables_ReturnsEmpty_WhenNoSyncGroups
    Assert: GetAutoAllocatedVariables() returns empty list.

T2: GetAutoAllocatedVariables_ReturnsEntry_WhenApproachBSyncGroupExists
    Setup: RecordSubtreeNodeMeta(nid, "Shoot_BT", "FireDto", null);
           SetSyncBinding(nid, new SubtreeSyncBinding("Aim", "SharedAim", true, false)).
    Assert: GetAutoAllocatedVariables() has one entry with Name=="Shoot_BT_FireDto".

T3: GetAutoAllocatedVariables_Suppresses_WhenApproachACoversSameNode
    Setup: same as T2 PLUS add a master variable "SharedFire" and AddAlias("SharedFire",
           new BlackboardAliasBinding(subAssetId, nid, "Shoot_BT", "/path", typeof(object))).
    Assert: GetAutoAllocatedVariables() returns empty (covered by Approach A).

T4: GetAutoAllocatedVariables_FieldName_IsSubtreeNameUnderscoreDtoTypeName
    Assert: entry.Name == "Shoot_BT_FireDto".

T5: GetAutoAllocatedVariables_ReturnsEmpty_WhenSyncGroupHasNoActiveSyncOps
    Setup: RecordSubtreeNodeMeta(nid, ...); SetSyncBinding with SyncIn=false, SyncOut=false,
           MasterVariableName=null.
    Assert: GetAutoAllocatedVariables() returns empty.
```

---

## Mandatory Workflow

Follow this exactly for each task:

1. Read the relevant spec section and this task description fully before writing production code.
2. Write the test file first (or at minimum the test class + method signatures that compile with NotImplementedException bodies).
3. Implement until tests pass.
4. Run `dotnet test` for the affected projects before moving to the next task. Do not carry red tests forward.
5. Check for xUnit analyzer violations — `xUnit2013` (use `Assert.Empty`, `Assert.Single`, `Assert.Equal(N,...)` not `Assert.True(count > 0)`), `xUnit2002`/`xUnit2006`.

---

## Developer Insights Required in Report

The report **must** answer:

1. **What was harder than expected?** Any spec ambiguities requiring interpretation?
2. **`RecordSubtreeNodeMeta` timing concern:** When exactly is `RecordSubtreeNodeMeta` called relative to `Emit`? Is there a scenario where the emitter runs before the Inspector has rendered the node? How should this be handled?
3. **Auto-allocation size deferral:** The `typeof(object)` placeholder — is this a real problem in practice? What would it take to resolve? (Descriptive answer only; implementation is out of scope.)
4. **`IBlackboardManagedAsset` additions (`Name`, `BlackboardTypeName`):** Were the default implementations adequate for all existing stubs? If any stub needed explicit overrides, list them.

---

## Report Format

Write the completion report to: `.dev/ai-hsm-btree-vis-edit/reports/BATCH-11-REPORT.md`

```
# BATCH-11 Report

## Summary
One paragraph.

## Tasks Completed
Table: Task ID | Deliverable | Tests Written | Tests Passing

## Developer Insights
### Issues Encountered
### Spec Ambiguities
### RecordSubtreeNodeMeta Timing
### Auto-Allocation Size Deferral

## Test Counts
Project | Before | After
```

---

## Success Criteria

Before declaring the batch done:

- [ ] `dotnet build IOS-IG-SimHost.sln` exits 0, 0 errors, 0 warnings
- [ ] `Hrot.BTree.Editor.Tests`: all tests pass, count >= 265
- [ ] `Hrot.Editor.AiShared.Tests`: all tests pass, count >= 375 (if any new AiShared tests added)
- [ ] `Hrot.Hsm.Editor.Tests`: all tests pass (no regressions), count >= 215
- [ ] No xUnit2013/xUnit2002/xUnit2006 violations
- [ ] `IBlackboardManagedAsset` additions are backward-compatible (all existing stubs compile)
- [ ] `BTreeEditorLayout.SyncBindings` is populated by `BTreeEditorLayoutBuilder.SubtreeSyncField`
- [ ] `BehaviorTreeAsset.LoadSyncBindings` is called from `BehaviorTreeAssetProjector`
- [ ] `BTreeOrchestratorEmitter.Emit` emits Approach B methods for nodes with active sync bindings
- [ ] `GetAutoAllocatedVariables` returns suppressed entries when Approach A alias covers the same node

---

## File Checklist

### New files
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/ApproachBSyncGroup.cs`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeSyncPersistenceTests.cs`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeOrchestratorSyncEmitterTests.cs`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeAutoAllocationTests.cs`

### Modified files
- `Hrot/Editor/Hrot.Editor.AiShared/Layout/BTreeEditorLayoutBuilder.cs` — add `SubtreeSyncField`
- `Hrot/Editor/Hrot.Editor.AiShared/Layout/BTreeEditorLayout.cs` — add `SyncBindings`
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IBlackboardManagedAsset.cs` — add `Name`, `BlackboardTypeName` default impls
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IBTreeSyncableAsset.cs` — add `RecordSubtreeNodeMeta`, `GetApproachBSyncGroups`
- `Hrot/Editor/Hrot.Editor.AiShared/Windows/InspectorWindow.cs` — call `RecordSubtreeNodeMeta` in `DrawSyncBindingsTable`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs` — implement new methods + `_syncNodeMeta`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAssetProjector.cs` — call `LoadSyncBindings`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Emit/BTreeOrchestratorEmitter.cs` — Approach B emit
- `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs` — Sub-tree allocations section
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Emit/BTreeFluentEmitter.cs` — emit `.SubtreeSyncField` in layout

### Do NOT modify
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/**` (HSM not in scope for this batch)
- Any existing test file beyond adding new test methods
- Any file outside the `Hrot/` tree
- `BlackboardBinPacker.cs` (size resolution deferred; auto-allocs are display-only this batch)
