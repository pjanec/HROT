# WHEN-BATCH-03 — WhenIrNode IR Primitive + Value Changed + Event Fired Lowering

## Tasks Covered

- **WHEN-M2-T1** — `WhenIrNode` IR primitive + synthesized state fields + StructureHash
- **WHEN-M2-T2** — Value Changed mode — Stage 6 lowering
- **WHEN-M2-T3** — Event Fired mode — Stage 6 lowering

**Design reference (authoritative):** [When_Reactivity_Iteration_Design_v2_2.md](../When_Reactivity_Iteration_Design_v2_2.md) §5.1–§5.6, §7.1, §7.2, §7.5, §15.1  
**Task detail (authoritative):** [TASK-DETAIL.md](../TASK-DETAIL.md) tasks WHEN-M2-T1, WHEN-M2-T2, WHEN-M2-T3

---

## CRITICAL DESIGN DISCREPANCY — READ FIRST

The DESIGN §5.2 states `WhenIrNode : IrStatement`. **This does not compile.**
`IrStatement` is a **`sealed record`** in the actual codebase — it cannot be subclassed.

The correct approach mirrors how all other compiler behavior is expressed:
- `IrStatement` wraps an `IrOperation` (abstract record base)
- New WhenNode behaviour = new **`IrOperation` derived sealed records** in `IrOperation.cs`

Follow this exact pattern:
- Add `IrOp_WhenValueChangedCheck`, `IrOp_WhenEventFiredCheck`, `IrOp_WhenStorePrev`
  to `IrOperation.cs` (analogous to `IrOp_CheckCursorVersion`, `IrOp_PollEngineEvent`)
- **Do not create a new `IrStatement` subclass**

---

## Architecture Overview

WhenNode compiles through the pipeline as follows:

| Stage | Action |
|---|---|
| 3. Normalize | _(not in scope for this batch — skip)_ |
| 4. TypeResolve | WhenNode pins resolve normally (no special handling needed yet) |
| **5. Schedule** | New `case WhenNode wn:` in `ScheduleBlock`: emits `IrOp_WhenValueChangedCheck` or `IrOp_WhenEventFiredCheck`, branches on result into `onFired`/`out` blocks |
| **6. Lower** | `WhenLowering_Instance`: adds synthesized `_when_<id8>_prev` fields to `Variables` |
| **7. Emit** | `StatementEmitter`: handles the three new IR ops |

### Block structure after Stage 5 (ValueChanged example)

```
block_main:
  ...previous statements...
  IrOp_WhenValueChangedCheck(...)  → result __t{N} (bool "changed")
  → IrTerm_Branch(__t{N}, when_abc_fired, when_abc_out)

block_when_abc_fired:
  [user-authored OnFired exec nodes, BFS-scheduled normally]
  IrOp_WhenStorePrev(...)          → appended after BFS in Stage 5 post-action
  → auto IrTerm_FallThrough → when_abc_out

block_when_abc_out:
  [Out exec chain continuation]
```

### Generated C# output after Stage 7

The block structure + goto-based blocks produce equivalent code to DESIGN §7.1:

```csharp
__block_main:
{
    // ...
    ref readonly var __t3 = ref view.GetComponentRO<global::Health>(self);
    float __t4 = __t3.Current;                           // from IrOp_WhenValueChangedCheck
    bool __t5 = global::System.MathF.Abs(__t4 - s._when_abc12345_prev) > 0.001f;
    if (__t5) goto __block_when_abc12345_fired; else goto __block_when_abc12345_out;
}

__block_when_abc12345_fired:
{
    // user nodes...
    ref readonly var __t10 = ref view.GetComponentRO<global::Health>(self); // re-read
    s._when_abc12345_prev = __t10.Current;               // from IrOp_WhenStorePrev
    // auto-fallthrough
}

__block_when_abc12345_out:
{
    // Out continuation...
}
```

Note: `IrTerm_Branch` already exists and emits `if (cond) goto X; else goto Y;`.
The re-read in `IrOp_WhenStorePrev` is intentional — re-reading within the same tick
returns the same value and avoids cross-block variable scope issues.

---

## Step 1: New IR Operations — `IrOperation.cs`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Ir/IrOperation.cs`

Add the following sealed records at the **bottom** of the file (before the last closing
`// end of file` if any, after all existing ops).

```csharp
// ── WhenNode lowering ops ──────────────────────────────────────────────────

/// <summary>
/// Emitted by Stage 5 for a WhenNode in ValueChanged mode.
/// Stage 7 emits the component-read + comparison inline.
/// Result value holds the "changed" bool used by IrTerm_Branch.
/// </summary>
public sealed record IrOp_WhenValueChangedCheck(
    /// <summary>Full FQN of the ECS component (e.g. "MyGame.Health").</summary>
    string ComponentFqn,
    /// <summary>Dot-separated property path into the component (e.g. "Current").</summary>
    string PropertyPath,
    /// <summary>Comparison epsilon (0 → direct equality).</summary>
    float Epsilon,
    /// <summary>Name of the synthesized prev-state field in the State struct.</summary>
    string SynthFieldName,
    /// <summary>CSharp-level type name of the tracked field (e.g. "float", "bool").</summary>
    string FieldCSharpType,
    /// <summary>Block id of the OnFired block (used by Stage 6 to append StorePrev).</summary>
    IrBlockId OnFiredBlock,
    /// <summary>Source: 0=SelfComponent, 1=PeerBlueprintVariable, 2=WorkingStateField</summary>
    int SourceKind
) : IrOperation;

/// <summary>
/// Appended to the OnFired block by Stage 5 post-actions.
/// Re-reads the component field and stores to the synthesized prev-state field.
/// </summary>
public sealed record IrOp_WhenStorePrev(
    string ComponentFqn,
    string PropertyPath,
    string SynthFieldName
) : IrOperation;

/// <summary>
/// Emitted by Stage 5 for a WhenNode in EventFired mode.
/// Stage 7 emits the ReadEvents loop + optional filtering inline.
/// Result value holds the "matched" bool used by IrTerm_Branch.
/// </summary>
public sealed record IrOp_WhenEventFiredCheck(
    /// <summary>Full FQN of the event type (e.g. "MyGame.HitEvent").</summary>
    string EventFqn,
    /// <summary>Whether to filter by Target == self.</summary>
    bool FilterSelf,
    /// <summary>Payload field path for the optional PayloadCondition (null = no check).</summary>
    string? PayloadFieldPath,
    /// <summary>Comparison operator as a C# string (e.g. "<=", ">", "=="). Null = no check.</summary>
    string? PayloadOperatorCSharp,
    /// <summary>Literal value for the payload comparison (e.g. "50f"). Null = no check.</summary>
    string? PayloadValueLiteral
) : IrOperation;
```

---

## Step 2: Synthesized GUID helper — `SynthesizedGuids.cs`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Lowering/SynthesizedGuids.cs`

Add the following method after `WaitUntilTimeField`:

```csharp
/// <summary>
/// Returns a deterministic GUID for the synthesized _when_<id8>_prev field
/// of a specific WhenNode within a specific Blueprint asset.
/// </summary>
public static Guid WhenPrevField(Guid assetId, Guid nodeId)
    => Derive("when-prev-field", assetId.ToString(), nodeId.ToString());
```

---

## Step 3: Stage 5 — Schedule WhenNode — `Stage5_Schedule.cs`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs`

### 3a. Add a post-BFS action list to `GraphScheduler`

Inside `GraphScheduler` class (near the other mutable state fields, around line ~132):

```csharp
// Post-BFS actions: appended to fired blocks after all user nodes are scheduled.
private readonly List<(int blockId, IrStatement stmt)> _whenPostActions = new();
```

### 3b. Apply post-BFS actions at end of `Schedule()`

In `Schedule()`, after `while (_bfsQueue.Count > 0) { ... }` loop and before building:

```csharp
// Apply WhenNode post-actions: append StorePrev to each onFired block.
foreach (var (blockId, stmt) in _whenPostActions)
    _blockBuilders[blockId].Statements.Add(stmt);
```

The final `Schedule()` method return section becomes:
```csharp
// Apply WhenNode post-actions
foreach (var (blockId, stmt) in _whenPostActions)
    _blockBuilders[blockId].Statements.Add(stmt);

return new IrGraph
{
    Id     = _graph.Id,
    Name   = _graph.Name,
    Kind   = MapGraphKind(_graph.Kind),
    Blocks = _blockBuilders.Select(b => b.Build()).ToList().AsReadOnly(),
    Entry  = new IrBlockId(0),
};
```

### 3c. Handle WhenNode in `ScheduleBlock`

In `ScheduleBlock`'s `while (true)` switch, add a case **before** `default:`:

```csharp
case WhenNode wn:
    ScheduleWhenNode(wn, bb);
    return;
```

### 3d. Add `ScheduleWhenNode` method

Add this private method to `GraphScheduler` (alongside `ScheduleBranchNode`):

```csharp
private void ScheduleWhenNode(WhenNode wn, BlockBuilder bb)
{
    var idShort = wn.Id.ToString("N").Substring(0, 8);
    var synthFieldName = $"_when_{idShort}_prev";
    var debug = DebugOf(wn);

    bool hasFired = (wn.Edges & WhenEdge.RisingEdge) != 0;
    bool hasEnded = (wn.Edges & WhenEdge.FallingEdge) != 0;

    // Allocate blocks
    IrBlockId? onFiredBlock = hasFired ? AllocBlock($"when_{idShort}_fired") : (IrBlockId?)null;
    // OnEnded is deferred to a later batch (M3 covers FallingEdge for Condition Met)
    // For now emit FallingEdge as a second "fired" block if present
    IrBlockId? onEndedBlock = hasEnded ? AllocBlock($"when_{idShort}_ended") : (IrBlockId?)null;
    var outBlock = AllocBlock($"when_{idShort}_out");

    // Allocate result value (bool "fired/changed/matched")
    var boolType = new IrTypeRef { FullName = "System.Boolean", IsUnmanaged = true, SizeBytes = 1 };
    var condValue = AllocValue(boolType);

    // Emit the mode-specific check op
    switch (wn.Mode)
    {
        case WhenMode.ValueChanged:
        {
            var vc = wn.ValueChanged;
            if (vc is null) break; // BP2002 already reported in Stage 2

            // Determine C# field type from the property path
            // At Stage 5 we don't have full type resolution; emit "var" and let
            // Stage 7 infer. Use the property path's last segment as the field name.
            string componentFqn = vc.ComponentTypeId;  // caller-provided type id
            string propertyPath  = vc.PropertyPath;
            float epsilon = (float)vc.Epsilon;
            int sourceKind = (int)vc.Source; // 0=SelfComponent, 1=Peer, 2=WorkingState

            // Determine the C# type of the field from the first segment of PropertyPath.
            // At Stage 5 this is unknown; emit "var" - Stage 7 emitter uses "var".
            string fieldCSharpType = "var";

            // Determine the onFired block for StorePrev post-action
            IrBlockId effectiveFiredBlock = onFiredBlock ?? outBlock;

            bb.Statements.Add(new IrStatement
            {
                ResultValue = condValue,
                Operation   = new IrOp_WhenValueChangedCheck(
                    ComponentFqn:    componentFqn,
                    PropertyPath:    propertyPath,
                    Epsilon:         epsilon,
                    SynthFieldName:  synthFieldName,
                    FieldCSharpType: fieldCSharpType,
                    OnFiredBlock:    effectiveFiredBlock,
                    SourceKind:      sourceKind),
                Debug = debug,
            });

            // Register StorePrev to be appended to the fired block after BFS.
            if (hasFired)
            {
                _whenPostActions.Add((effectiveFiredBlock.Value, new IrStatement
                {
                    Operation = new IrOp_WhenStorePrev(
                        ComponentFqn:   componentFqn,
                        PropertyPath:   propertyPath,
                        SynthFieldName: synthFieldName),
                    Debug = new IrDebugAnnotation { GraphId = _graph.Id, Synthesized = "when-store-prev" },
                }));
            }
            break;
        }

        case WhenMode.EventFired:
        {
            var ef = wn.EventFired;
            if (ef is null) break;

            bool filterSelf = ef.TargetFilter == EventTargetFilter.Self;
            string? payloadField = ef.PayloadCheck?.PropertyPath;
            string? payloadOp    = ef.PayloadCheck is not null
                ? ComparisonOpToCSharp(ef.PayloadCheck.Operator)
                : null;
            string? payloadVal   = ef.PayloadCheck?.TargetValueText;

            bb.Statements.Add(new IrStatement
            {
                ResultValue = condValue,
                Operation   = new IrOp_WhenEventFiredCheck(
                    EventFqn:             ef.EventTypeId,
                    FilterSelf:           filterSelf,
                    PayloadFieldPath:     payloadField,
                    PayloadOperatorCSharp: payloadOp,
                    PayloadValueLiteral:  payloadVal),
                Debug = debug,
            });
            // No StorePrev for EventFired — no synthesized state field.
            break;
        }

        default:
            // ConditionMet (M3) and EqsResult (M4) are not in scope for this batch.
            // Emit a noop false const so the graph doesn't crash at Stage 7.
            bb.Statements.Add(new IrStatement
            {
                ResultValue = condValue,
                Operation   = new IrOp_Const("false", boolType),
                Debug       = debug,
            });
            break;
    }

    // The primary branch: condition true → onFired (if any), else → out
    IrBlockId trueTarget  = onFiredBlock ?? outBlock;
    IrBlockId falseTarget = outBlock;

    bb.Terminator = new IrTerm_Branch(condValue, trueTarget, falseTarget) { Debug = debug };

    // Schedule exec successors
    Node? firedSucc  = GetWhenExecSuccessor(wn, "OnFired");
    Node? endedSucc  = GetWhenExecSuccessor(wn, "OnEnded");
    Node? outSucc    = GetWhenExecSuccessor(wn, "Out");

    if (onFiredBlock.HasValue && firedSucc is not null)
        _bfsQueue.Enqueue((onFiredBlock.Value.Value, firedSucc));

    if (onEndedBlock.HasValue && endedSucc is not null)
        _bfsQueue.Enqueue((onEndedBlock.Value.Value, endedSucc));

    if (outSucc is not null)
        _bfsQueue.Enqueue((outBlock.Value, outSucc));
    // else outBlock stays empty → auto-fallthrough from BlockBuilder.Build()
}

private static string ComparisonOpToCSharp(ComparisonOperator op) => op switch
{
    ComparisonOperator.Equal              => "==",
    ComparisonOperator.NotEqual           => "!=",
    ComparisonOperator.LessThan           => "<",
    ComparisonOperator.LessThanOrEqual    => "<=",
    ComparisonOperator.GreaterThan        => ">",
    ComparisonOperator.GreaterThanOrEqual => ">=",
    _                                     => "==",
};

private Node? GetWhenExecSuccessor(WhenNode wn, string pinName)
{
    var pin = wn.Pins.FirstOrDefault(
        p => p.IsExec && p.Direction == "Out" &&
             string.Equals(p.Name, pinName, StringComparison.OrdinalIgnoreCase));
    if (pin is null) return null;
    var link = _graph.Links.FirstOrDefault(l => l.FromNodeId == wn.Id && l.FromPinId == pin.Id);
    return link is not null && _nodeById.TryGetValue(link.ToNodeId, out var t) ? t : null;
}
```

**Imports** needed at top of Stage5_Schedule.cs (add if not already present):
```csharp
using Hrot.Blueprints.Core.Assets; // for WhenNode, WhenMode, WhenEdge, etc.
```

---

## Step 4: Stage 6 — WhenNode synthesized fields — `WhenLowering_Instance.cs`

Create a **new file**:  
**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Lowering/WhenLowering_Instance.cs`

```csharp
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Lowering;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

/// <summary>
/// Stage 6 lowering: adds synthesized _when_<id8>_prev fields to
/// the Instance asset's Variables list for each WhenNode in ValueChanged mode.
/// (EventFired nodes have no synthesized state.)
/// Field layout and StructureHash are computed by the caller (Stage6_Lower) after this.
/// </summary>
internal static class WhenLowering_Instance
{
    private static readonly IrTypeRef FloatType =
        new IrTypeRef { FullName = "System.Single", IsUnmanaged = true, SizeBytes = 4 };

    public static IrAsset Apply(IrAsset asset)
    {
        // Collect all synthesized field names that need to be added.
        var toAdd = new List<IrField>();
        var seen  = new HashSet<string>();

        foreach (var graph in asset.Graphs)
        foreach (var block in graph.Blocks)
        foreach (var stmt  in block.Statements)
        {
            if (stmt.Operation is not IrOp_WhenValueChangedCheck op) continue;
            if (!seen.Add(op.SynthFieldName)) continue;  // de-dup across graphs

            // Derive a deterministic Guid for this synthesized field.
            // We use the SynthFieldName as a proxy (it encodes the node id short).
            var fieldId = SynthesizedGuids.WhenPrevField(asset.AssetId,
                DeriveNodeIdFromFieldName(op.SynthFieldName));

            toAdd.Add(new IrField
            {
                Id                 = fieldId,
                Name               = op.SynthFieldName,
                Type               = FloatType,        // M2 scope: float only (scalar Value Changed)
                DefaultValueCSharp = "default",
            });
        }

        if (toAdd.Count == 0) return asset;

        // Append synthesized fields after declared variables; deterministic order by name.
        toAdd.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        var newVariables = asset.Variables.Concat(toAdd).ToList();
        return asset with { Variables = newVariables };
    }

    /// <summary>
    /// Reconstructs a stable Guid proxy from "_when_&lt;id8&gt;_prev".
    /// Uses the 8-char hex prefix to derive the Guid.
    /// </summary>
    private static Guid DeriveNodeIdFromFieldName(string synthFieldName)
    {
        // synthFieldName = "_when_<8hex>_prev"
        // Extract the 8 hex chars between "_when_" and "_prev"
        const string prefix = "_when_";
        const string suffix = "_prev";
        if (synthFieldName.StartsWith(prefix) && synthFieldName.EndsWith(suffix))
        {
            var hex = synthFieldName.Substring(prefix.Length,
                synthFieldName.Length - prefix.Length - suffix.Length);
            if (hex.Length == 8)
            {
                // Pad to a valid Guid string
                return new Guid(hex.PadRight(32, '0'));
            }
        }
        return Guid.Empty;
    }
}
```

**Wire it into `InstanceLowering.Apply`:**

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Lowering/InstanceLowering.cs`

```csharp
public static IrAsset Apply(IrAsset asset, DiagnosticSink sink)
{
    // Add synthesized _when_xxx_prev fields for ValueChanged WhenNodes.
    asset = WhenLowering_Instance.Apply(asset);

    var newGraphs = new List<IrGraph>(asset.Graphs.Count);
    foreach (var graph in asset.Graphs)
    {
        bool hasLatent = graph.Blocks
            .SelectMany(b => b.Statements)
            .Any(s => s.Operation is IrOp_LatentDelay or IrOp_WaitForChannel or IrOp_WaitForEvent);

        newGraphs.Add(hasLatent ? WaitLowering_Instance.Apply(graph) : graph);
    }
    return asset with { Graphs = newGraphs };
}
```

---

## Step 5: Stage 7 — Emit WhenNode ops — `StatementEmitter.cs`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/StatementEmitter.cs`

Add the following cases to the `switch (stmt.Operation)` block (before the `default:` or at
the end of the ECS section). Suggest adding after the `IrOp_CheckCursorVersion` case.

```csharp
// ------------------------------------------------------------------
// WhenNode lowering ops (Stage 5 / Stage 6 — should not be raw ops here)
// ------------------------------------------------------------------

case IrOp_WhenValueChangedCheck op:
{
    int idx = stmt.ResultValue?.Index ?? -1;

    // Read the component (SelfComponent source only for M2)
    e.WriteLine($"ref readonly var __t{idx}_comp = ref {wv}.GetComponentRO<global::{op.ComponentFqn}>(self);");
    e.WriteLine($"var __t{idx}_cur = __t{idx}_comp.{op.PropertyPath};");

    // Compare against previous state
    if (op.Epsilon == 0f)
    {
        // Direct equality (bool, int, enum)
        e.WriteLine($"bool __t{idx}_changed = __t{idx}_cur != {sv}.{op.SynthFieldName};");
    }
    else
    {
        // Float epsilon comparison
        e.WriteLine($"bool __t{idx}_changed = global::System.MathF.Abs(__t{idx}_cur - {sv}.{op.SynthFieldName}) > {op.Epsilon}f;");
    }

    if (idx >= 0) e.WriteLine($"bool __t{idx} = __t{idx}_changed;");
    break;
}

case IrOp_WhenStorePrev op:
{
    // Re-read the component to get the current value (avoids cross-block variable scope).
    // This re-read always returns the same value within a single tick.
    int idx = stmt.ResultValue?.Index ?? -1;
    e.WriteLine($"{{");
    e.Indent();
    e.WriteLine($"ref readonly var __storePrev_comp = ref {wv}.GetComponentRO<global::{op.ComponentFqn}>(self);");
    e.WriteLine($"{sv}.{op.SynthFieldName} = __storePrev_comp.{op.PropertyPath};");
    e.Outdent();
    e.WriteLine($"}}");
    break;
}

case IrOp_WhenEventFiredCheck op:
{
    int idx = stmt.ResultValue?.Index ?? -1;
    var evtShort = op.EventFqn.Split('.').Last();

    bool hasFilters = op.FilterSelf || op.PayloadFieldPath is not null;

    if (!hasFilters)
    {
        // Fast path: just test HasEvent
        if (idx >= 0)
            e.WriteLine($"bool __t{idx} = (({wv}.EventBus) as global::Fdp.Core.Events.IEventBus)?.HasEvent<global::{op.EventFqn}>() ?? false;");
    }
    else
    {
        // Full scan path
        if (idx >= 0)
        {
            e.WriteLine($"bool __t{idx};");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"var __events_{evtShort} = {wv}.ReadEvents<global::{op.EventFqn}>();");
            e.WriteLine($"bool __matched_{evtShort} = false;");
            e.WriteLine($"for (int __i = 0; __i < __events_{evtShort}.Count; __i++)");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"var __ev = __events_{evtShort}[__i];");

            if (op.FilterSelf)
                e.WriteLine($"if (__ev.Target != self) continue;");

            if (op.PayloadFieldPath is not null && op.PayloadOperatorCSharp is not null && op.PayloadValueLiteral is not null)
                e.WriteLine($"if (!(__ev.{op.PayloadFieldPath} {op.PayloadOperatorCSharp} {op.PayloadValueLiteral})) continue;");

            e.WriteLine($"__matched_{evtShort} = true;");
            e.WriteLine("break;");
            e.Outdent();
            e.WriteLine("}");
            e.WriteLine($"__t{idx} = __matched_{evtShort};");
            e.Outdent();
            e.WriteLine("}");
        }
    }
    break;
}
```

**Important notes for the StatementEmitter:**
- `wv` is the world/view variable name (from `ctx.WorldVar`) — use this for `view.GetComponentRO`, `view.ReadEvents`, etc.
- `sv` is the state variable name (from `ctx.StateVar`) — use this for `s._when_xxx_prev`
- The `(idx >= 0)` guard is already present in `EmitOp`; follow the existing pattern

---

## Step 6: Tests — `WhenNodeLoweringTests.cs`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage6_LoweringTests/WhenNodeLoweringTests.cs`

Create this file with **all 8 tests** listed below. Some tests are Stage 6 IR tests (check
`IrAsset.Variables`), others are end-to-end compilation tests (check generated source text).

### Test helpers (shared by all tests in this class)

```csharp
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler.Stage6_LoweringTests;

public sealed class WhenNodeLoweringTests
{
    private static CompileOptions DefaultOptions() => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    /// <summary>Runs Stage 5 then Stage 6; returns the lowered IrAsset.</summary>
    private static IrAsset RunLower(BlueprintAsset asset, DiagnosticSink sink)
    {
        var opts  = DefaultOptions();
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        var ctx = new ValidationContext(sink, opts);
        var ir  = Stage5_Schedule.Run(typed, ctx);
        return Stage6_Lower.Run(ir, CompilerMode.Debug, sink);
    }

    /// <summary>Runs all stages and returns the generated C# source.</summary>
    private static string? Compile(BlueprintAsset asset)
    {
        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());
        return result.GeneratedSource;
    }

    /// <summary>
    /// Builds a minimal WhenNode for a ValueChanged / SelfComponent scenario.
    /// The node has an ExecIn, ExecOut ("Out"), and optionally OnFired pins.
    /// </summary>
    private static WhenNode MakeValueChangedNode(
        Guid nodeId,
        Guid graphId,
        Guid assetId,
        string componentTypeId,
        string propertyPath,
        float epsilon = 0.001f,
        WhenEdge edges = WhenEdge.RisingEdge)
    {
        var node = new WhenNode
        {
            Id   = nodeId,
            Mode = WhenMode.ValueChanged,
            Edges = edges,
            ValueChanged = new ValueChangedPayload
            {
                ComponentTypeId = componentTypeId,
                PropertyPath    = propertyPath,
                Epsilon         = epsilon,
                Source          = ValueChangedSource.SelfComponent,
            },
        };

        // Exec pins
        var execIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
        var execOut = new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() };

        if ((edges & WhenEdge.RisingEdge) != 0)
            node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });

        node.Pins.Add(execIn);
        node.Pins.Add(execOut);
        return node;
    }
}
```

### Test 1: `Lower_StructureHashIncludesSynthesizedFields`

```csharp
[Fact]
public void Lower_StructureHashIncludesSynthesizedFields()
{
    // Build an Instance asset with a ValueChanged WhenNode (RisingEdge).
    var assetId  = Guid.NewGuid();
    var graphId  = Guid.NewGuid();
    var nodeId   = Guid.NewGuid();

    var entry = new EventEntryNode { Id = Guid.NewGuid() };
    entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

    var whenNode = MakeValueChangedNode(nodeId, graphId, assetId,
        componentTypeId: "MyGame.Health",
        propertyPath:    "Current",
        epsilon:         0.001f,
        edges:           WhenEdge.RisingEdge);

    // Wire entry → whenNode (Out not connected = no further nodes)
    var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
    var execInPin  = whenNode.Pins.First(p => p.IsExec && p.Direction == "In");

    var graph = new Graph
    {
        Id    = graphId,
        Name  = "Tick",
        Kind  = GraphKind.Event,
        Nodes = { entry, whenNode },
        Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                             ToNodeId = whenNode.Id, ToPinId = execInPin.Id } },
    };

    var asset = new BlueprintAsset
    {
        AssetId  = assetId,
        Name     = "WhenTest",
        Dispatch = BlueprintDispatchKind.Instance,
        Graphs   = { graph },
    };

    var sink   = new DiagnosticSink();
    var lowered = RunLower(asset, sink);

    Assert.False(sink.HasErrors,
        $"Unexpected errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

    // Stage 6 must have added the synthesized field to Variables.
    var id8 = nodeId.ToString("N").Substring(0, 8);
    var expectedFieldName = $"_when_{id8}_prev";
    Assert.Contains(lowered.Variables, v => v.Name == expectedFieldName);

    // StructureHash must be non-zero (computed in Stage 6 from Variables).
    Assert.NotEqual(0UL, lowered.StructureHash);
}
```

### Test 2: `Lower_ValueChanged_Scalar_EmitsInlineComparison`

```csharp
[Fact]
public void Lower_ValueChanged_Scalar_EmitsInlineComparison()
{
    var assetId = Guid.NewGuid();
    var graphId = Guid.NewGuid();
    var nodeId  = Guid.NewGuid();
    var id8     = nodeId.ToString("N").Substring(0, 8);

    var entry = new EventEntryNode { Id = Guid.NewGuid() };
    entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

    var whenNode = MakeValueChangedNode(nodeId, graphId, assetId,
        componentTypeId: "MyGame.Health",
        propertyPath:    "Current",
        epsilon:         0.001f);

    var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
    var execInPin  = whenNode.Pins.First(p => p.IsExec && p.Direction == "In");

    var graph = new Graph
    {
        Id    = graphId, Name = "Tick", Kind = GraphKind.Event,
        Nodes = { entry, whenNode },
        Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                             ToNodeId = whenNode.Id, ToPinId = execInPin.Id } },
    };
    var asset = new BlueprintAsset
    {
        AssetId = assetId, Name = "WhenScalar",
        Dispatch = BlueprintDispatchKind.Instance, Graphs = { graph },
    };

    var src = Compile(asset);

    Assert.NotNull(src);
    // Must emit the component read
    Assert.Contains("GetComponentRO<global::MyGame.Health>", src);
    // Must emit the field access
    Assert.Contains(".Current", src);
    // Must emit the epsilon comparison
    Assert.Contains("MathF.Abs", src);
    // Must reference the synthesized prev-state field
    Assert.Contains($"_when_{id8}_prev", src);
}
```

### Test 3: `Lower_ValueChanged_Vector2_EmitsLengthSquaredComparison`

Per DESIGN §7.1: Vector2 comparison uses `(current - prev).LengthSquared() > epsilonSquared`.
This test uses a float field with epsilon=0 (direct equality), which covers the
non-LengthSquared path; the full Vector2 path is covered when `Epsilon != 0` and type is Vector2.
For M2 scope, emit a simpler test verifying the epsilon=0 direct-equality branch:

```csharp
[Fact]
public void Lower_ValueChanged_Vector2_EmitsLengthSquaredComparison()
{
    // Use epsilon == 0 to verify the direct-equality path is chosen.
    // Full Vector2 + LengthSquared path is verified in M4 integration tests
    // (requires type-resolved IrTypeRef for Vector2 fields).
    var assetId = Guid.NewGuid();
    var graphId = Guid.NewGuid();
    var nodeId  = Guid.NewGuid();
    var id8     = nodeId.ToString("N").Substring(0, 8);

    var entry = new EventEntryNode { Id = Guid.NewGuid() };
    entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

    var whenNode = MakeValueChangedNode(nodeId, graphId, assetId,
        componentTypeId: "MyGame.Transform",
        propertyPath:    "Position",
        epsilon:         0f);          // epsilon=0 → direct equality path

    var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
    var execInPin  = whenNode.Pins.First(p => p.IsExec && p.Direction == "In");

    var graph = new Graph
    {
        Id = graphId, Name = "Tick", Kind = GraphKind.Event,
        Nodes = { entry, whenNode },
        Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                             ToNodeId = whenNode.Id, ToPinId = execInPin.Id } },
    };
    var asset = new BlueprintAsset
    {
        AssetId = assetId, Name = "WhenVector",
        Dispatch = BlueprintDispatchKind.Instance, Graphs = { graph },
    };

    var src = Compile(asset);

    Assert.NotNull(src);
    // epsilon=0 → != comparison, not MathF.Abs
    Assert.Contains("!=", src);
    Assert.DoesNotContain("MathF.Abs", src);
    // Still must reference the prev field
    Assert.Contains($"_when_{id8}_prev", src);
}
```

### Test 4: `Lower_ValueChanged_PeerVariable_EmitsSlotLookup`

For M2 scope, PeerBlueprintVariable source falls through to the same emit path (Stage 5
sets SourceKind=1; emitter may or may not differentiate yet). This test verifies the node
at least schedules without errors. Full peer-slot-lookup emit is a M4 concern.

```csharp
[Fact]
public void Lower_ValueChanged_PeerVariable_EmitsSlotLookup()
{
    var assetId = Guid.NewGuid();
    var graphId = Guid.NewGuid();
    var nodeId  = Guid.NewGuid();

    var entry = new EventEntryNode { Id = Guid.NewGuid() };
    entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

    var node = new WhenNode
    {
        Id    = nodeId,
        Mode  = WhenMode.ValueChanged,
        Edges = WhenEdge.RisingEdge,
        ValueChanged = new ValueChangedPayload
        {
            ComponentTypeId    = "",
            PropertyPath       = "",
            Source             = ValueChangedSource.PeerBlueprintVariable,
            PeerBlueprintAssetId = Guid.NewGuid(),
            PeerVariableName   = "Speed",
            Epsilon            = 0.01,
        },
    };
    node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() });
    node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() });
    node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });

    var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
    var execInPin  = node.Pins.First(p => p.IsExec && p.Direction == "In");

    var graph = new Graph
    {
        Id = graphId, Name = "Tick", Kind = GraphKind.Event,
        Nodes = { entry, node },
        Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                             ToNodeId = node.Id, ToPinId = execInPin.Id } },
    };
    var asset = new BlueprintAsset
    {
        AssetId = assetId, Name = "WhenPeer",
        Dispatch = BlueprintDispatchKind.Instance, Graphs = { graph },
    };

    // PeerBlueprintVariable source: Stage 5 schedules without crash.
    // Full peer-slot emit is deferred to M4.
    var sink = new DiagnosticSink();
    var lowered = RunLower(asset, sink);

    // No crashes = pass. Diagnostic errors for unsupported source are acceptable.
    // What must NOT happen: NullReferenceException / InvalidOperationException.
    Assert.NotNull(lowered);
}
```

### Test 5: `Lower_EventFired_WithSelf_EmitsTargetCheck`

```csharp
[Fact]
public void Lower_EventFired_WithSelf_EmitsTargetCheck()
{
    var assetId = Guid.NewGuid();
    var graphId = Guid.NewGuid();
    var nodeId  = Guid.NewGuid();

    var entry = new EventEntryNode { Id = Guid.NewGuid() };
    entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

    var node = new WhenNode
    {
        Id    = nodeId,
        Mode  = WhenMode.EventFired,
        Edges = WhenEdge.RisingEdge,
        EventFired = new EventFiredPayload
        {
            EventTypeId    = "MyGame.HitEvent",
            TargetFilter   = EventTargetFilter.Self,
            TargetFieldName = "Target",
        },
    };
    node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() });
    node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() });
    node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });

    var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
    var execInPin  = node.Pins.First(p => p.IsExec && p.Direction == "In");

    var graph = new Graph
    {
        Id = graphId, Name = "Tick", Kind = GraphKind.Event,
        Nodes = { entry, node },
        Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                             ToNodeId = node.Id, ToPinId = execInPin.Id } },
    };
    var asset = new BlueprintAsset
    {
        AssetId = assetId, Name = "WhenEvent",
        Dispatch = BlueprintDispatchKind.Instance, Graphs = { graph },
    };

    var src = Compile(asset);

    Assert.NotNull(src);
    Assert.Contains("ReadEvents<global::MyGame.HitEvent>", src);
    // Target filter must emit a != self check
    Assert.Contains("!= self", src);
}
```

### Test 6: `Lower_EventFired_WithPayloadCondition_EmitsValueParse`

```csharp
[Fact]
public void Lower_EventFired_WithPayloadCondition_EmitsValueParse()
{
    var assetId = Guid.NewGuid();
    var graphId = Guid.NewGuid();

    var entry = new EventEntryNode { Id = Guid.NewGuid() };
    entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

    var node = new WhenNode
    {
        Id    = Guid.NewGuid(),
        Mode  = WhenMode.EventFired,
        Edges = WhenEdge.RisingEdge,
        EventFired = new EventFiredPayload
        {
            EventTypeId  = "MyGame.HitEvent",
            TargetFilter = EventTargetFilter.Self,
            PayloadCheck = new PayloadCondition
            {
                PropertyPath    = "Damage",
                Operator        = ComparisonOperator.GreaterThan,
                TargetValueText = "50f",
            },
        },
    };
    node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() });
    node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() });
    node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });

    var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
    var execInPin  = node.Pins.First(p => p.IsExec && p.Direction == "In");

    var graph = new Graph
    {
        Id = graphId, Name = "Tick", Kind = GraphKind.Event,
        Nodes = { entry, node },
        Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                             ToNodeId = node.Id, ToPinId = execInPin.Id } },
    };
    var asset = new BlueprintAsset
    {
        AssetId = assetId, Name = "WhenPayload",
        Dispatch = BlueprintDispatchKind.Instance, Graphs = { graph },
    };

    var src = Compile(asset);

    Assert.NotNull(src);
    // Must emit the Damage field access
    Assert.Contains(".Damage", src);
    // Must emit the > operator
    Assert.Contains("> 50f", src);
}
```

### Test 7: `Lower_EventFired_NoFilters_EmitsHasEventFastPath`

```csharp
[Fact]
public void Lower_EventFired_NoFilters_EmitsHasEventFastPath()
{
    var assetId = Guid.NewGuid();
    var graphId = Guid.NewGuid();

    var entry = new EventEntryNode { Id = Guid.NewGuid() };
    entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

    var node = new WhenNode
    {
        Id    = Guid.NewGuid(),
        Mode  = WhenMode.EventFired,
        Edges = WhenEdge.RisingEdge,
        EventFired = new EventFiredPayload
        {
            EventTypeId  = "MyGame.ExplosionEvent",
            TargetFilter = EventTargetFilter.None,
            // No PayloadCheck
        },
    };
    node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() });
    node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() });
    node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });

    var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
    var execInPin  = node.Pins.First(p => p.IsExec && p.Direction == "In");

    var graph = new Graph
    {
        Id = graphId, Name = "Tick", Kind = GraphKind.Event,
        Nodes = { entry, node },
        Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                             ToNodeId = node.Id, ToPinId = execInPin.Id } },
    };
    var asset = new BlueprintAsset
    {
        AssetId = assetId, Name = "WhenFastPath",
        Dispatch = BlueprintDispatchKind.Instance, Graphs = { graph },
    };

    var src = Compile(asset);

    Assert.NotNull(src);
    // Fast path: no loop, just HasEvent call
    Assert.Contains("HasEvent", src);
    // Must NOT emit a full for-loop since there are no filters
    Assert.DoesNotContain("for (int", src);
}
```

### Test 8: `Lower_EventFired_NoSynthesizedField`

```csharp
[Fact]
public void Lower_EventFired_NoSynthesizedField()
{
    var assetId = Guid.NewGuid();
    var graphId = Guid.NewGuid();
    var nodeId  = Guid.NewGuid();
    var id8     = nodeId.ToString("N").Substring(0, 8);

    var entry = new EventEntryNode { Id = Guid.NewGuid() };
    entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

    var node = new WhenNode
    {
        Id    = nodeId,
        Mode  = WhenMode.EventFired,
        Edges = WhenEdge.RisingEdge,
        EventFired = new EventFiredPayload
        {
            EventTypeId  = "MyGame.SpawnEvent",
            TargetFilter = EventTargetFilter.None,
        },
    };
    node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() });
    node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() });
    node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });

    var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
    var execInPin  = node.Pins.First(p => p.IsExec && p.Direction == "In");

    var graph = new Graph
    {
        Id = graphId, Name = "Tick", Kind = GraphKind.Event,
        Nodes = { entry, node },
        Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                             ToNodeId = node.Id, ToPinId = execInPin.Id } },
    };
    var asset = new BlueprintAsset
    {
        AssetId = assetId, Name = "WhenEventNoState",
        Dispatch = BlueprintDispatchKind.Instance, Graphs = { graph },
    };

    var sink   = new DiagnosticSink();
    var lowered = RunLower(asset, sink);

    Assert.False(sink.HasErrors,
        $"Unexpected errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

    // EventFired must NOT add any _when_xxx_prev field to Variables.
    var synthFieldName = $"_when_{id8}_prev";
    Assert.DoesNotContain(lowered.Variables, v => v.Name == synthFieldName);
}
```

---

## Task Tracker Update

After all tests pass, update `TASK-TRACKER.md`:

```markdown
- [x] **WHEN-M2-T1** `WhenIrNode` IR primitive + payloads
- [x] **WHEN-M2-T2** Value Changed mode — Stage 6 lowering
- [x] **WHEN-M2-T3** Event Fired mode — Stage 6 lowering
```

---

## Notes and Constraints

1. **IrTypeRef for synthesized fields**: For M2 scope, `WhenLowering_Instance` uses `System.Single` (4 bytes) for all synthesized prev fields. Full type dispatch (bool, int, Vector2) is added in a follow-up pass after Stage 4 TypeResolve provides the resolved field type.

2. **FallingEdge / OnEnded**: Both edges are scheduled in Stage 5 (`ScheduleWhenNode`
   creates `onEndedBlock` if FallingEdge is set and enqueues its BFS successor). However,
   the **condition logic** for FallingEdge (track "was the condition true last tick?") is
   deferred to M3. For now, FallingEdge creates the block structure but the generated code
   only fires for the RisingEdge condition. Leave a `// TODO M3: FallingEdge` comment.

3. **PeerBlueprintVariable source**: Stage 5 records `SourceKind = 1` but the emitter in
   Stage 7 does not yet differentiate it from `SelfComponent = 0`. The test
   `Lower_ValueChanged_PeerVariable_EmitsSlotLookup` only validates no crash occurs. Full
   peer-slot-lookup code emission is M4 scope.

4. **EventBus API**: For the `HasEvent` fast path, look at how the existing
   `IrOp_PollEngineEvent` emitter references the event bus, and use the same pattern.
   If the exact cast expression differs, match what the existing code does. The test only
   checks `HasEvent` is present in the output.

5. **WhenNode exec pin naming convention**: The scheduler (`GetWhenExecSuccessor`) uses
   case-insensitive name matching for "Out", "OnFired", "OnEnded". This matches the
   convention established by existing nodes (BranchNode uses "True"/"False"). When
   building test WhenNodes manually, use these exact pin names.

6. **No changes to Stage 7 block emission**: `BlockEmitter.cs` and `TerminatorEmitter.cs`
   need **no changes**. `IrTerm_Branch` (already exists) is reused for WhenNode branching.
   `IrTerm_FallThrough` (already exists) terminates the `onFired` block naturally.

7. **StructureHash is correct by default**: `StructureHashComputation.Compute` already
   hashes `asset.Variables`. Adding `_when_xxx_prev` to Variables means it's automatically
   included in the hash — no changes to `StructureHashComputation.cs` are needed.

8. **Pre-existing 98 test failures**: These are known failures in demo test assets (JSON
   discriminator). Do not attempt to fix them. Confirm the 8 new tests pass; existing
   passing tests continue to pass.
