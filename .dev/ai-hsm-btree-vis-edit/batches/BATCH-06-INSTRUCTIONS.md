# BATCH-06 — Blackboard Recursive Aggregation (Phase 1.5c, Tasks 1c-01 + 1c-02)

## Overview

Implement the blackboard aggregation services that walk a BTree or HSM asset (and their
statically-resolvable nested assets) and return a flat list of DTO requirements
(`DtoRequirement`) found via the action schema.

Two tasks in this batch:
- **TASK-BB-1c-01** — `IBlackboardAggregator` + BTree walker
- **TASK-BB-1c-02** — HSM walker

---

## Key design references

- `Blackboard_Authoring_Detailed_Design.md` sections **BB §5.1 – §5.6** (aggregation algorithm)
- `TASK-DETAIL.md` sections **TASK-BB-1c-01** and **TASK-BB-1c-02**

---

## Existing code to read before writing anything

| File | Purpose |
|------|---------|
| `Hrot/Editor/Hrot.Editor.AiShared/Identity/IEditableAsset.cs` | Asset identity interface |
| `Hrot/Editor/Hrot.Editor.AiShared/Catalog/IAssetCatalog.cs` | Catalog: `FindByAssetId`, `All`, `Changed` |
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IActionSchemaExporter.cs` | Schema lookup interface |
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/ActionSchemaExporter.cs` | Concrete exporter (to understand `ActionSchemaEntry`) |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs` | `BehaviorTreeAsset`, `BTreeEditorNode`, `BTreeActionPayload`, `BTreeConditionPayload`, `BTreeSubtreePayload` |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` | `HsmAsset`, `StateNode`, `TransitionNode`, `GlobalTransitionNode` |
| `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetKind.cs` | `AssetKind` enum values |
| `Hrot/Editor/Hrot.Editor.AiShared/References/IReferenceCatalogContributor.cs` | Pattern for contributor/strategy injection |

Read those files **first** before any implementation.

---

## Architecture constraint — CRITICAL

The reference direction in this codebase is ONE-WAY:

```
Hrot.BTree.Editor  ──references──>  Hrot.Editor.AiShared
Hrot.Hsm.Editor    ──references──>  Hrot.Editor.AiShared
```

`Hrot.Editor.AiShared` must NEVER reference `Hrot.BTree.Editor` or `Hrot.Hsm.Editor`.

Therefore:
- `IBlackboardAggregator`, `AggregationResult`, `DtoRequirement`, `AggregationWarning`,
  and `IBlackboardAggregatorStrategy` live in **`Hrot.Editor.AiShared`**.
- The BTree-specific walker lives in **`Hrot.BTree.Editor`** (it references `BehaviorTreeAsset`).
- The HSM-specific walker lives in **`Hrot.Hsm.Editor`** (it references `HsmAsset`).

The shared layer dispatches via a strategy/contributor pattern — see implementation
guidance below.

---

## Implementation Guidance

### Step 1 — Shared types in `Hrot.Editor.AiShared/Blackboard/`

#### `IBlackboardAggregator.cs`

Define the public contract and supporting types in one file:

```csharp
namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// A strategy that can aggregate blackboard DTO requirements from one specific
/// asset kind. Registered by subsystem editors and dispatched by
/// <see cref="BlackboardAggregatorService"/>.
/// </summary>
public interface IBlackboardAggregatorStrategy
{
    bool CanHandle(IEditableAsset asset);

    /// <summary>
    /// Aggregate DTO requirements from <paramref name="asset"/> and
    /// statically-resolvable descendants. <paramref name="visited"/> is the
    /// caller-maintained cycle-guard set; the strategy must add
    /// <paramref name="asset"/>.AssetId before recursing.
    /// </summary>
    AggregationResult Aggregate(
        IEditableAsset            asset,
        IActionSchemaExporter     schema,
        IAssetCatalog             catalog,
        HashSet<Guid>             visited);
}

/// <summary>
/// Dispatches aggregation to registered <see cref="IBlackboardAggregatorStrategy"/>
/// implementations.  Register strategies in ascending priority order; the first
/// one whose CanHandle returns true wins.
/// </summary>
public sealed class BlackboardAggregatorService
{
    private readonly IReadOnlyList<IBlackboardAggregatorStrategy> _strategies;
    private readonly IActionSchemaExporter _schema;
    private readonly IAssetCatalog _catalog;

    public BlackboardAggregatorService(
        IEnumerable<IBlackboardAggregatorStrategy> strategies,
        IActionSchemaExporter schema,
        IAssetCatalog catalog)
    {
        _strategies = strategies.ToList();
        _schema = schema;
        _catalog = catalog;
    }

    public AggregationResult Aggregate(IEditableAsset asset)
    {
        var visited = new HashSet<Guid>();
        return AggregateInternal(asset, visited);
    }

    // Internal entry point used by strategies that recurse.
    internal AggregationResult AggregateInternal(IEditableAsset asset, HashSet<Guid> visited)
    {
        foreach (var s in _strategies)
            if (s.CanHandle(asset))
                return s.Aggregate(asset, _schema, _catalog, visited);

        // No registered strategy for this asset kind -- return empty.
        return new AggregationResult(
            Array.Empty<DtoRequirement>(),
            Array.Empty<AggregationWarning>());
    }
}

public sealed record AggregationResult(
    IReadOnlyList<DtoRequirement>  Requirements,
    IReadOnlyList<AggregationWarning> Warnings)
{
    public static AggregationResult Empty { get; } =
        new(Array.Empty<DtoRequirement>(), Array.Empty<AggregationWarning>());

    public AggregationResult Merge(AggregationResult other) =>
        new(Requirements.Concat(other.Requirements).ToList(),
            Warnings.Concat(other.Warnings).ToList());
}

/// <summary>
/// One parameter DTO requirement discovered during aggregation.
/// </summary>
/// <param name="DtoType">The action's parameter DTO type.</param>
/// <param name="RequiredByPath">
/// Human-readable provenance string, e.g. "Shoot_BT > Action#7 (FireAtTarget)".
/// </param>
/// <param name="RequiringAssetId">Asset in which the requirement was found.</param>
/// <param name="RequiringElementId">Node/element visual-id within that asset.</param>
public sealed record DtoRequirement(
    Type   DtoType,
    string RequiredByPath,
    Guid   RequiringAssetId,
    Guid   RequiringElementId);

public enum AggregationWarningKind
{
    UnresolvedSubtree,
    Cycle,
    SchemaEntryNotFound,
}

public sealed record AggregationWarning(
    AggregationWarningKind Kind,
    string Message,
    Guid? AssetId = null);
```

**Notes:**
- Do NOT use Unicode characters (arrows, bullets, dashes) in comment text.
- `BlackboardAggregatorService` does not need to implement an interface — it is
  the concrete service. Tests instantiate it directly.
- The strategies list is injected (no DI container in tests — just pass in a list).

---

### Step 2 — BTree walker in `Hrot.BTree.Editor/Blackboard/`

#### `BTreeBlackboardAggregatorStrategy.cs`

```csharp
namespace Hrot.BTree.Editor.Blackboard;

public sealed class BTreeBlackboardAggregatorStrategy : IBlackboardAggregatorStrategy
{
    // Injected so the strategy can recurse into subtree assets.
    private readonly BlackboardAggregatorService _service;

    public BTreeBlackboardAggregatorStrategy(BlackboardAggregatorService service)
        => _service = service;

    public bool CanHandle(IEditableAsset asset)
        => asset is BehaviorTreeAsset;

    public AggregationResult Aggregate(
        IEditableAsset        asset,
        IActionSchemaExporter schema,
        IAssetCatalog         catalog,
        HashSet<Guid>         visited)
    {
        var btAsset = (BehaviorTreeAsset)asset;
        if (!visited.Add(btAsset.AssetId))
            return AggregationResult.Empty;  // already visited (cycle)

        var requirements = new List<DtoRequirement>();
        var warnings     = new List<AggregationWarning>();

        foreach (var node in btAsset.Nodes)
        {
            // ---- Action / Condition: look up schema by MethodFqn ----
            string? fqn = node.Action?.MethodFqn ?? node.Condition?.MethodFqn;
            if (fqn != null)
            {
                var entry = schema.Lookup(fqn);
                if (entry != null)
                {
                    string path = $"{btAsset.Name} > {node.DisplayLabel} ({fqn})";
                    requirements.Add(new DtoRequirement(
                        entry.DtoType, path,
                        btAsset.AssetId, node.VisualId));
                }
                else
                {
                    warnings.Add(new AggregationWarning(
                        AggregationWarningKind.SchemaEntryNotFound,
                        $"Schema entry not found for FQN '{fqn}' in asset '{btAsset.Name}'.",
                        btAsset.AssetId));
                }
            }

            // ---- Subtree: recurse ----
            if (node.Subtree != null && node.Subtree.SubtreeAssetId != Guid.Empty)
            {
                var childAsset = catalog.FindByAssetId(node.Subtree.SubtreeAssetId);
                if (childAsset == null)
                {
                    warnings.Add(new AggregationWarning(
                        AggregationWarningKind.UnresolvedSubtree,
                        $"Subtree asset '{node.Subtree.SubtreeName}' ({node.Subtree.SubtreeAssetId:D}) not found in catalog.",
                        btAsset.AssetId));
                }
                else if (visited.Contains(childAsset.AssetId))
                {
                    warnings.Add(new AggregationWarning(
                        AggregationWarningKind.Cycle,
                        $"Cycle detected: asset '{childAsset.Name}' ({childAsset.AssetId:D}) already visited.",
                        childAsset.AssetId));
                }
                else
                {
                    var childResult = _service.AggregateInternal(childAsset, visited);
                    requirements.AddRange(childResult.Requirements);
                    warnings.AddRange(childResult.Warnings);
                }
            }
        }

        return new AggregationResult(requirements, warnings);
    }
}
```

**Notes:**
- `node.DisplayLabel` must be read from the existing `BTreeEditorNode` fields.
  Check what the node exposes for a human-readable label before writing the code.
- `node.Action`, `node.Condition`, `node.Subtree` are the `BTreeActionPayload?`,
  `BTreeConditionPayload?`, and `BTreeSubtreePayload?` properties respectively.
  Confirm the exact property names in `BTreeEditorNode` from the model file.
- The cycle guard is the outer `visited.Contains` check + the `visited.Add` at
  method entry. The strategy adds the CURRENT asset's ID on entry; it checks the
  CHILD asset's ID before recursing.

---

### Step 3 — HSM walker in `Hrot.Hsm.Editor/Blackboard/`

#### `HsmBlackboardAggregatorStrategy.cs`

Walk the HSM asset and emit one `DtoRequirement` per resolved action FQN.

Sources of FQNs in an `HsmAsset`:
- Each `StateNode` in `AllStates`: `OnEntryAction`, `OnExitAction`, `ActivityAction`, `TimerAction`
  (each is `string?` — null means no action)
- Each `TransitionNode` in `AllTransitions`: `GuardFunction`, `ActionFunction`
- Each `GlobalTransitionNode` in `AllGlobalTransitions`: `GuardFunction`, `ActionFunction`

For each non-null FQN: look up in `schema.Lookup(fqn)`.
- If found: emit a `DtoRequirement` with a descriptive path (e.g. `"MyMachine > State 'Patrol' OnEntry"`).
- If not found: emit an `AggregationWarning(SchemaEntryNotFound, ...)`.

There is no "sub-BTree recursion" in HSM in this batch. That detail (detecting orchestrator
sub-BTree invocation) is deferred to future work. For 1c-02, only walk the flat
`AllStates`, `AllTransitions`, `AllGlobalTransitions` collections.

Cycle guard: call `visited.Add(hsmAsset.AssetId)` at method entry and return
`AggregationResult.Empty` if it was already present.

```csharp
namespace Hrot.Hsm.Editor.Blackboard;

public sealed class HsmBlackboardAggregatorStrategy : IBlackboardAggregatorStrategy
{
    private readonly BlackboardAggregatorService _service;

    public HsmBlackboardAggregatorStrategy(BlackboardAggregatorService service)
        => _service = service;

    public bool CanHandle(IEditableAsset asset)
        => asset is HsmAsset;

    public AggregationResult Aggregate(
        IEditableAsset        asset,
        IActionSchemaExporter schema,
        IAssetCatalog         catalog,
        HashSet<Guid>         visited)
    {
        var hsmAsset = (HsmAsset)asset;
        if (!visited.Add(hsmAsset.AssetId))
            return AggregationResult.Empty;

        var requirements = new List<DtoRequirement>();
        var warnings     = new List<AggregationWarning>();

        // States
        foreach (var state in hsmAsset.AllStates)
        {
            EmitIfFound(state.OnEntryAction,  $"{hsmAsset.Name} > State '{state.Name}' OnEntry",  hsmAsset, state.StableId, schema, requirements, warnings);
            EmitIfFound(state.OnExitAction,   $"{hsmAsset.Name} > State '{state.Name}' OnExit",   hsmAsset, state.StableId, schema, requirements, warnings);
            EmitIfFound(state.ActivityAction, $"{hsmAsset.Name} > State '{state.Name}' Activity", hsmAsset, state.StableId, schema, requirements, warnings);
            EmitIfFound(state.TimerAction,    $"{hsmAsset.Name} > State '{state.Name}' Timer",    hsmAsset, state.StableId, schema, requirements, warnings);
        }

        // Transitions
        foreach (var t in hsmAsset.AllTransitions)
        {
            string label = $"{hsmAsset.Name} > Transition '{t.Source?.Name}' -> '{t.Target?.Name}'";
            EmitIfFound(t.GuardFunction,  label + " Guard",  hsmAsset, t.VisualId, schema, requirements, warnings);
            EmitIfFound(t.ActionFunction, label + " Action", hsmAsset, t.VisualId, schema, requirements, warnings);
        }

        // Global transitions
        foreach (var g in hsmAsset.AllGlobalTransitions)
        {
            string label = $"{hsmAsset.Name} > GlobalTransition -> '{g.Target?.Name}'";
            EmitIfFound(g.GuardFunction,  label + " Guard",  hsmAsset, g.VisualId, schema, requirements, warnings);
            EmitIfFound(g.ActionFunction, label + " Action", hsmAsset, g.VisualId, schema, requirements, warnings);
        }

        return new AggregationResult(requirements, warnings);
    }

    private static void EmitIfFound(
        string?               fqn,
        string                path,
        HsmAsset              asset,
        Guid                  elementId,
        IActionSchemaExporter schema,
        List<DtoRequirement>  reqs,
        List<AggregationWarning> warns)
    {
        if (fqn == null) return;
        var entry = schema.Lookup(fqn);
        if (entry != null)
            reqs.Add(new DtoRequirement(entry.DtoType, path, asset.AssetId, elementId));
        else
            warns.Add(new AggregationWarning(
                AggregationWarningKind.SchemaEntryNotFound,
                $"Schema entry not found for FQN '{fqn}' in asset '{asset.Name}'.",
                asset.AssetId));
    }
}
```

---

## Tests to write

### `Hrot.Editor.AiShared.Tests/Blackboard/BlackboardAggregatorServiceTests.cs`

Test the `BlackboardAggregatorService` dispatch logic (no concrete walkers needed here —
use a stub strategy):

```
[Fact] CanHandle_false_returns_empty_result
[Fact] Aggregate_dispatches_to_matching_strategy
[Fact] AggregationResult_Merge_concatenates_requirements_and_warnings
[Fact] AggregationResult_Empty_has_no_requirements_or_warnings
```

### `Hrot.BTree.Editor.Tests/Blackboard/BTreeBlackboardAggregatorTests.cs`

Test the BTree walker. You will need to construct `BehaviorTreeAsset` instances with
specific node payloads. Look at how existing BTree tests in that project construct
assets (see `BlackboardRenameTests.cs` for the asset factory pattern).

Minimum tests:
```
[Fact] Aggregate_action_node_emits_requirement_for_known_fqn
[Fact] Aggregate_condition_node_emits_requirement_for_known_fqn
[Fact] Aggregate_unknown_fqn_emits_schema_not_found_warning_not_exception
[Fact] Aggregate_subtree_node_resolved_recurses_and_collects_child_requirements
[Fact] Aggregate_subtree_node_unresolved_emits_warning_and_skips
[Fact] Aggregate_cycle_stops_recursion_and_emits_cycle_warning
[Fact] Aggregate_empty_tree_returns_empty_result
```

For the schema, create a stub or use a `Dictionary<string, ActionSchemaEntry>`-backed
fake `IActionSchemaExporter`. Do NOT use `ActionSchemaExporter` (real reflection-based
one) in these tests -- it would scan live assemblies.

For the catalog, use a stub `IAssetCatalog` backed by a `Dictionary<Guid, IEditableAsset>`.

### `Hrot.Hsm.Editor.Tests/Blackboard/HsmBlackboardAggregatorTests.cs`

Use the existing `HsmAssetProjector.Project(...)` / `HsmBuilder` / `HsmFlattener`
pattern from `HsmAssetProjectionTests.cs` to create real `HsmAsset` instances with
known state/transition action FQNs.

Minimum tests:
```
[Fact] Aggregate_state_OnEntry_action_emits_requirement
[Fact] Aggregate_state_OnExit_action_emits_requirement
[Fact] Aggregate_state_Activity_action_emits_requirement
[Fact] Aggregate_state_Timer_action_emits_requirement
[Fact] Aggregate_transition_guard_emits_requirement
[Fact] Aggregate_transition_action_emits_requirement
[Fact] Aggregate_global_transition_guard_emits_requirement
[Fact] Aggregate_null_fqn_not_emitted
[Fact] Aggregate_unknown_fqn_emits_schema_not_found_warning
[Fact] Aggregate_cycle_guard_returns_empty_on_second_visit
```

Note: `HsmBuilder` is in the `Fhsm.Compiler` namespace and uses fluent DSL.
See `HsmAssetProjectionTests.cs` for how to build a machine with transitions and
actions. The `GuardFunction` and `ActionFunction` are method FQN strings — just
set them directly on the projected `TransitionNode` object if `HsmBuilder` doesn't
expose a direct API for them.

---

## Construction of BTree test assets with action payloads

To construct a `BehaviorTreeAsset` node with an action payload, you need to look at
how `BehaviorTreeAsset` nodes are structured (read `BehaviorTreeAsset.cs` fully).

The test from `BlackboardRenameTests.cs` builds an asset from a `BehaviorTreeBlob`
with empty nodes. For aggregation tests you need nodes with action/condition/subtree
payloads.

Look at the `BTreeEditorNode` class fields in `BehaviorTreeAsset.cs` and the
`BehaviorTreeAssetProjector` to understand how nodes are built. The simplest path
for tests is to directly construct `BTreeEditorNode` objects and use
`BehaviorTreeAsset`'s internal `_nodes` list — or better, find if there's an
`AddNode` / `SetNodes` helper already on the asset.

If there is no test-friendly node builder, add an `internal` method
`SetNodesForTest(IEnumerable<BTreeEditorNode> nodes)` to `BehaviorTreeAsset` that
replaces `_nodes` without firing `Changed`. Tests are in the same assembly or a
test-friend, so this should be accessible.

---

## Fake IActionSchemaExporter for tests

Create a simple test double in the test project:

```csharp
internal sealed class StubSchemaExporter : IActionSchemaExporter
{
    private readonly Dictionary<string, ActionSchemaEntry> _entries;
    public StubSchemaExporter(Dictionary<string, ActionSchemaEntry> entries)
        => _entries = entries;
    public IReadOnlyDictionary<string, ActionSchemaEntry> All => _entries;
    public ActionSchemaEntry? Lookup(string fqn)
        => _entries.TryGetValue(fqn, out var e) ? e : null;
    public void Rebuild() { }
    public event Action? Changed;
}
```

---

## Fake IAssetCatalog for tests

```csharp
internal sealed class StubCatalog : IAssetCatalog
{
    private readonly Dictionary<Guid, IEditableAsset> _assets;
    public StubCatalog(IEnumerable<IEditableAsset> assets)
        => _assets = assets.ToDictionary(a => a.AssetId);
    public IReadOnlyList<IEditableAsset> All => _assets.Values.ToList();
    public IEditableAsset? FindByAssetId(Guid id)
        => _assets.GetValueOrDefault(id);
    public IEditableAsset? FindByName(string name)
        => _assets.Values.FirstOrDefault(a => a.Name == name);
    public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid id)
        => Array.Empty<IEditableAsset>();
    public event Action? Changed;
}
```

---

## Build and test commands

After each task:
```
dotnet build IOS-IG-SimHost.sln
```

After completing both tasks:
```
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj --no-build
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj --no-build
dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj --no-build
```

---

## Report

After completing all tasks, create:
`.dev/ai-hsm-btree-vis-edit/reports/BATCH-06-REPORT.md`

Include:
- List of new files created
- List of modified files
- Test counts per project (AiShared.Tests, BTree.Editor.Tests, Hsm.Editor.Tests)
- Confirmation: `dotnet build` = 0 errors
- Any open questions or deferred items

---

## Definition of done

- [ ] `IBlackboardAggregatorStrategy`, `BlackboardAggregatorService`, `AggregationResult`,
      `DtoRequirement`, `AggregationWarning` in `Hrot.Editor.AiShared`
- [ ] `BTreeBlackboardAggregatorStrategy` in `Hrot.BTree.Editor`
- [ ] `HsmBlackboardAggregatorStrategy` in `Hrot.Hsm.Editor`
- [ ] `BlackboardAggregatorServiceTests` in `AiShared.Tests` (4+ tests)
- [ ] `BTreeBlackboardAggregatorTests` in `BTree.Editor.Tests` (7+ tests)
- [ ] `HsmBlackboardAggregatorTests` in `Hsm.Editor.Tests` (10+ tests)
- [ ] `dotnet build IOS-IG-SimHost.sln` = 0 errors
- [ ] All tests pass (no regressions in existing 320 + 201 + existing HSM tests)
- [ ] Report filed at `.dev/ai-hsm-btree-vis-edit/reports/BATCH-06-REPORT.md`
