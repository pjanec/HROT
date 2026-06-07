# WHEN-BATCH-05 Instructions — ConditionMet IR + Stage 6 Lowering

## Scope
Implement **WHEN-M3-T1** from `.dev/blueprints-3-when-node/TASK-DETAIL.md`.

Task definition: `ConditionMetIrPayload` + Stage 6 lowering + 2 lowering tests.

**Design reference:** `.dev/blueprints-3-when-node/When_Reactivity_Iteration_Design_v2_2.md` §3.4, §7.3, §7.4.

---

## Files to Create

### 1. `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Compiler\Compiler\ISearchPredicateRegistry.cs`

This new interface is a DI contract used in `InitializePredicates` (the coordinator will inject it in M3-T2). For M3-T1, the interface body is intentionally empty — it's a forward declaration.

```csharp
namespace Hrot.Blueprints.Core.Compiler;

/// <summary>
/// Registry of known DTO types used by blueprint predicate compilation.
/// Passed to <c>InitializePredicates</c> so the coordinator can inject it.
/// Implementation is provided by the editor host (M3-T2).
/// </summary>
public interface ISearchPredicateRegistry
{
}
```

---

## Files to Modify

### 2. `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Compiler\Compiler\Ir\IrOperation.cs`

Add after the `IrOp_WhenEventFiredCheck` record (after line ~163):

```csharp
/// <summary>
/// Emitted by Stage 5 for a WhenNode in ConditionMet mode.
/// Stage 7 emits a self-contained predicate-check block with embedded goto branches.
/// No result value (ResultValue = null); branching is done inline with goto statements.
/// The block terminator must be IrTerm_Goto(outBlock) — NOT IrTerm_Branch.
/// </summary>
public sealed record IrOp_WhenConditionMetCheck(
    /// <summary>JSON-serialized SearchPredicateDto, embedded as a const string in generated code.</summary>
    string PredicateDtoJson,
    /// <summary>Name of the synthesized bool prev-state field in the State struct (e.g. "_when_a3f7c218_prev").</summary>
    string SynthFieldName,
    /// <summary>Block to goto when condition fires (current && !prev). Null if no RisingEdge.</summary>
    IrBlockId? OnFiredBlock,
    /// <summary>Block to goto when condition ends (!current && prev). Null if no FallingEdge.</summary>
    IrBlockId? OnEndedBlock
) : IrOperation;
```

### 3. `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Compiler\Compiler\Stages\Stage5_Schedule.cs`

In `ScheduleWhenNode`, replace the `default:` case (currently lines ~463-470) with a `ConditionMet` case and update the default. The structure must use a label to skip the standard `IrTerm_Branch` code.

Replace:
```csharp
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

        // The primary branch: condition true -> onFired (if any), else -> out
        IrBlockId trueTarget  = onFiredBlock ?? outBlock;
        IrBlockId falseTarget = outBlock;

        bb.Terminator = new IrTerm_Branch(condValue, trueTarget, falseTarget) { Debug = debug };

        // Schedule exec successors
```

With:
```csharp
            case WhenMode.ConditionMet:
            {
                var cm = wn.ConditionMet;
                if (cm is null) break; // BP2002 already reported

                // Serialize predicate DTO to JSON (embedded as const string in generated code).
                string predicateJson = cm.Condition is not null
                    ? System.Text.Json.JsonSerializer.Serialize(cm.Condition)
                    : "null";

                bb.Statements.Add(new IrStatement
                {
                    ResultValue = null, // No result value — branching is embedded in the op emit
                    Operation   = new IrOp_WhenConditionMetCheck(
                        PredicateDtoJson: predicateJson,
                        SynthFieldName:   synthFieldName,
                        OnFiredBlock:     hasFired  ? onFiredBlock  : null,
                        OnEndedBlock:     hasEnded  ? onEndedBlock  : null),
                    Debug = debug,
                });

                // ConditionMet uses Goto terminator (not Branch): prev-update and gotos
                // are emitted inline by StatementEmitter.
                bb.Terminator = new IrTerm_Goto(outBlock) { Debug = debug };

                // Skip the standard IrTerm_Branch code below.
                goto scheduleSuccessors;
            }

            default:
                // EqsResult (M4) and unknown modes: emit noop false const.
                bb.Statements.Add(new IrStatement
                {
                    ResultValue = condValue,
                    Operation   = new IrOp_Const("false", boolType),
                    Debug       = debug,
                });
                break;
        }

        // The primary branch: condition true -> onFired (if any), else -> out
        IrBlockId trueTarget  = onFiredBlock ?? outBlock;
        IrBlockId falseTarget = outBlock;

        bb.Terminator = new IrTerm_Branch(condValue, trueTarget, falseTarget) { Debug = debug };

        scheduleSuccessors:
        // Schedule exec successors
```

> **Note:** The `goto scheduleSuccessors;` pattern requires the label `scheduleSuccessors:` to be placed just before the "Schedule exec successors" comment. The existing code that follows (scheduling `firedSucc`, `endedSucc`, `outSucc`) stays unchanged.

### 4. `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Compiler\Compiler\Lowering\WhenLowering_Instance.cs`

Extend `Apply` to also synthesize `bool` prev fields for `IrOp_WhenConditionMetCheck`.

Replace the inner loop body:
```csharp
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
```

With:
```csharp
            if (stmt.Operation is IrOp_WhenValueChangedCheck vc)
            {
                if (!seen.Add(vc.SynthFieldName)) continue;
                var fieldId = SynthesizedGuids.WhenPrevField(asset.AssetId,
                    DeriveNodeIdFromFieldName(vc.SynthFieldName));
                toAdd.Add(new IrField
                {
                    Id                 = fieldId,
                    Name               = vc.SynthFieldName,
                    Type               = FloatType,   // M2 scope: float only (scalar Value Changed)
                    DefaultValueCSharp = "default",
                });
            }
            else if (stmt.Operation is IrOp_WhenConditionMetCheck cm)
            {
                if (!seen.Add(cm.SynthFieldName)) continue;
                var fieldId = SynthesizedGuids.WhenPrevField(asset.AssetId,
                    DeriveNodeIdFromFieldName(cm.SynthFieldName));
                toAdd.Add(new IrField
                {
                    Id                 = fieldId,
                    Name               = cm.SynthFieldName,
                    Type               = BoolType,
                    DefaultValueCSharp = "default",
                });
            }
            else
            {
                continue;
            }
```

Also add the `BoolType` constant alongside `FloatType`:
```csharp
    private static readonly IrTypeRef BoolType =
        new IrTypeRef { FullName = "System.Boolean", IsUnmanaged = true, SizeBytes = 1 };
```

Also update the `<summary>` comment of the class to mention ConditionMet:
```csharp
/// Stage 6 lowering: adds synthesized _when_<id8>_prev fields to
/// the Instance asset's Variables list for each WhenNode in ValueChanged or ConditionMet mode.
/// (EventFired nodes have no synthesized state.)
```

### 5. `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Compiler\Compiler\Emit\InstanceEmitter.cs`

#### 5a. Add using
No new using is needed — the emitter writes strings only.

#### 5b. Add helper to collect ConditionMet ops

Add after the existing private methods (e.g., near `EmitVarIds`):

```csharp
    /// <summary>
    /// Collects all unique IrOp_WhenConditionMetCheck operations across all graphs.
    /// Returns list of (id8, predicateJson) pairs, deduplicated by SynthFieldName.
    /// </summary>
    private static List<(string Id8, string PredicateDtoJson)> CollectConditionMetOps(IrAsset asset)
    {
        var result = new List<(string, string)>();
        var seen   = new HashSet<string>();

        foreach (var graph in asset.Graphs)
        foreach (var block in graph.Blocks)
        foreach (var stmt  in block.Statements)
        {
            if (stmt.Operation is not IrOp_WhenConditionMetCheck op) continue;
            if (!seen.Add(op.SynthFieldName)) continue;

            // Extract the 8-char hex id from "_when_{id8}_prev"
            const string prefix = "_when_";
            const string suffix = "_prev";
            string id8 = op.SynthFieldName.StartsWith(prefix) && op.SynthFieldName.EndsWith(suffix)
                ? op.SynthFieldName.Substring(prefix.Length,
                    op.SynthFieldName.Length - prefix.Length - suffix.Length)
                : op.SynthFieldName;

            result.Add((id8, op.PredicateDtoJson));
        }

        return result;
    }
```

#### 5c. Emit static fields + `InitializePredicates` in `EmitClass`

In `EmitClass`, after `EmitVarIds(e, asset)` and before `EmitInitDefault(e, asset)`, add:

```csharp
        var condMetOps = CollectConditionMetOps(asset);
        if (condMetOps.Count > 0)
        {
            e.WriteLine();
            EmitConditionMetFields(e, condMetOps);
            e.WriteLine();
            EmitInitializePredicates(e, condMetOps);
        }
```

#### 5d. Add the two new emit methods

```csharp
    private static void EmitConditionMetFields(
        CSharpEmitter e,
        List<(string Id8, string PredicateDtoJson)> ops)
    {
        foreach (var (id8, _) in ops)
        {
            e.WriteLine($"private static global::Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto? _whenCondDto_{id8};");
            e.WriteLine($"private static global::System.Func<global::Fdp.Core.EntityRepository, global::Fdp.Core.Entity, bool>? _whenCondPred_{id8};");
        }
    }

    private static void EmitInitializePredicates(
        CSharpEmitter e,
        List<(string Id8, string PredicateDtoJson)> ops)
    {
        e.WriteLine("public static void InitializePredicates(");
        e.WriteLine("    global::Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler predicateCompiler,");
        e.WriteLine("    global::Hrot.Blueprints.Core.Compiler.ISearchPredicateRegistry dtoRegistry)");
        e.WriteLine("{");
        e.Indent();

        foreach (var (id8, predicateJson) in ops)
        {
            // Escape the JSON for embedding in a C# string literal.
            string escaped = predicateJson
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");

            e.WriteLine($"// WhenNode ConditionMet {id8}:");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"const string dtoJson_{id8} = \"{escaped}\";");
            e.WriteLine("try");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"_whenCondDto_{id8} = global::System.Text.Json.JsonSerializer.Deserialize<");
            e.WriteLine($"    global::Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto>(dtoJson_{id8});");
            e.WriteLine($"_whenCondPred_{id8} = predicateCompiler.CompileComponentPredicate(_whenCondDto_{id8}!);");
            e.Outdent();
            e.WriteLine("}");
            e.WriteLine("catch (global::System.Exception)");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"_whenCondPred_{id8} = null;");
            e.Outdent();
            e.WriteLine("}");
            e.Outdent();
            e.WriteLine("}");
        }

        e.Outdent();
        e.WriteLine("}");
    }
```

### 6. `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Compiler\Compiler\Emit\StatementEmitter.cs`

Add a new case in `EmitOp` after the `IrOp_WhenEventFiredCheck` case, before the `default:` throw:

```csharp
            case IrOp_WhenConditionMetCheck op:
            {
                // Extract the 8-char hex id from "_when_{id8}_prev"
                const string pfx = "_when_";
                const string sfx = "_prev";
                string id8 = op.SynthFieldName.StartsWith(pfx) && op.SynthFieldName.EndsWith(sfx)
                    ? op.SynthFieldName.Substring(pfx.Length,
                        op.SynthFieldName.Length - pfx.Length - sfx.Length)
                    : op.SynthFieldName;

                e.WriteLine($"// BEGIN WhenNode {id8}: Condition Met");
                e.WriteLine($"if (_whenCondPred_{id8} != null)");
                e.WriteLine("{");
                e.Indent();
                e.WriteLine($"bool __cur_{id8} = _whenCondPred_{id8}({wv}, self);");
                e.WriteLine($"bool __prev_{id8} = {sv}.{op.SynthFieldName};");
                e.WriteLine($"{sv}.{op.SynthFieldName} = __cur_{id8};");

                if (op.OnFiredBlock.HasValue)
                    e.WriteLine($"if (__cur_{id8} && !__prev_{id8}) goto __block_{ctx.LabelForBlock(op.OnFiredBlock.Value)};");

                if (op.OnEndedBlock.HasValue)
                    e.WriteLine($"if (!__cur_{id8} && __prev_{id8}) goto __block_{ctx.LabelForBlock(op.OnEndedBlock.Value)};");

                e.Outdent();
                e.WriteLine("}");
                e.WriteLine($"// END WhenNode {id8}: Condition Met (no branch taken -> fall to out)");
                // No result value emitted — ResultValue is null; block terminator is IrTerm_Goto.
                break;
            }
```

---

## Files to Modify (Tests)

### 7. `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Compiler\Stage6_LoweringTests\WhenNodeLoweringTests.cs`

Add two new tests after the existing `Lower_EventFired_*` tests.

#### 7a. Helper: `MakeConditionMetNode`

Add after the existing `MakeValueChangedNode` helper (or at the bottom of the helpers section):

```csharp
    /// <summary>
    /// Builds a minimal WhenNode for a ConditionMet scenario with a simple PropertyMatchDto predicate.
    /// </summary>
    private static WhenNode MakeConditionMetNode(
        Guid nodeId,
        WhenEdge edges = WhenEdge.RisingEdge)
    {
        var node = new WhenNode
        {
            Id   = nodeId,
            Mode = WhenMode.ConditionMet,
            Edges = edges,
            ConditionMet = new ConditionMetPayload
            {
                Condition = new global::Fdp.Toolkit.ReplayBrowser.Search.PropertyMatchDto
                {
                    ComponentType = typeof(object),  // dummy; Stage 2 validation is skipped
                    PropertyPath  = "Value",
                    Predicate     = new global::Fdp.Toolkit.ReplayBrowser.Search.NumericPredicateDto
                    {
                        MinValue = 10.0,
                        MaxValue = double.MaxValue,
                    },
                },
            },
        };

        if ((edges & WhenEdge.RisingEdge) != 0)
            node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });
        if ((edges & WhenEdge.FallingEdge) != 0)
            node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnEnded", Direction = "Out", IsExec = true, TypeRef = new() });

        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() });
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() });

        return node;
    }
```

> **Important note on `PropertyMatchDto.ComponentType`:** `PropertyMatchDto` has a `Type ComponentType` property, not a `string ComponentTypeId`. The Stage 2 validation (which checks if the type is registered) is skipped in the `Compile()` helper, so passing `typeof(object)` as a dummy is fine for these lowering tests.

> **Check what `PropertyMatchDto` actually looks like** before writing the helper — read `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs` to get the exact property names. If `PropertyMatchDto` takes a `Type ComponentType`, use `typeof(object)`. If it takes a `string`, use `"TestComponent"`. Adjust as needed.

#### 7b. Test 1: `Lower_ConditionMet_EmitsStaticDelegateField`

```csharp
    [Fact]
    public void Lower_ConditionMet_EmitsStaticDelegateField()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var id8     = nodeId.ToString("N").Substring(0, 8);

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

        var whenNode = MakeConditionMetNode(nodeId, WhenEdge.RisingEdge);

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
            AssetId  = assetId,
            Name     = "CondMetTest",
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { graph },
        };

        var src = Compile(asset);

        Assert.NotNull(src);
        // Static delegate field
        Assert.Contains($"_whenCondPred_{id8}", src);
        // InitializePredicates method
        Assert.Contains("InitializePredicates", src);
        // Synthesized bool prev field in State struct
        Assert.Contains($"_when_{id8}_prev", src);
        // Tick-body: predicate null-check
        Assert.Contains($"_whenCondPred_{id8} != null", src);
        // Tick-body: EntityRepository cast + predicate invocation
        Assert.Contains($"_whenCondPred_{id8}(", src);
    }
```

#### 7c. Test 2: `Lower_ConditionMet_RisingFallingBoth_BothBranchesEmitted`

```csharp
    [Fact]
    public void Lower_ConditionMet_RisingFallingBoth_BothBranchesEmitted()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var id8     = nodeId.ToString("N").Substring(0, 8);

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

        var whenNode = MakeConditionMetNode(nodeId, WhenEdge.RisingEdge | WhenEdge.FallingEdge);

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
            AssetId  = assetId,
            Name     = "CondMetBothEdges",
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { graph },
        };

        var src = Compile(asset);

        Assert.NotNull(src);
        // Rising edge: current && !prev → goto fired block
        Assert.Contains($"__cur_{id8} && !__prev_{id8}", src);
        // Falling edge: !current && prev → goto ended block
        Assert.Contains($"!__cur_{id8} && __prev_{id8}", src);
        // Prev field is updated unconditionally
        Assert.Contains($"{id8}_prev = __cur_{id8}", src);
    }
```

---

## Required Imports

### In test file (`WhenNodeLoweringTests.cs`)
The test helper `MakeConditionMetNode` uses:
- `Fdp.Toolkit.ReplayBrowser.Search` types (`PropertyMatchDto`, `NumericPredicateDto`)
- `Hrot.Blueprints.Core.Assets` (already imported)

Add to using section if not already present:
```csharp
using Fdp.Toolkit.ReplayBrowser.Search;
```

---

## Key Implementation Notes

### ConditionMet vs ValueChanged branching architecture

**ValueChanged and EventFired** use the standard binary `IrTerm_Branch(condValue, trueBlock, falseBlock)` terminator. They return a `bool` result value.

**ConditionMet** uses `IrTerm_Goto(outBlock)` as the terminator. The 3-way branch (fired/ended/fallthrough) is encoded as inline `goto` statements emitted by the `StatementEmitter.EmitOp` case. The `ResultValue` of the `IrStatement` is `null`. The `goto scheduleSuccessors;` in Stage5 bypasses the `IrTerm_Branch` code while still scheduling OnFired/OnEnded/Out successors.

### `WorldVar` for ConditionMet

`EmissionContext.WorldVar` is `((global::Fdp.Core.EntityRepository)view)` for Instance dispatch. Passing this directly to `_whenCondPred_{id8}(...)` satisfies the design's `var repo = (EntityRepository)view; bool current = _whenCondPred(repo, self);` pattern.

### JSON escaping

When embedding `PredicateDtoJson` in the generated C# string literal, apply these escapes in order:
1. `\\` → `\\\\`
2. `"` → `\"`

### Stage 6 StructureHash includes ConditionMet prev fields

The bool `_when_{id8}_prev` added by `WhenLowering_Instance` for ConditionMet nodes is included in `Variables`, which flows into `StructureHash` computation in `Stage6_Lower.Run`. No extra work needed.

### `ISearchPredicateRegistry` namespace

The interface lives at `Hrot.Blueprints.Core.Compiler.ISearchPredicateRegistry` (in `Hrot.Blueprints.Compiler` project, namespace `Hrot.Blueprints.Core.Compiler`). The emitter writes the fully-qualified name `global::Hrot.Blueprints.Core.Compiler.ISearchPredicateRegistry`.

### `PropertyMatchDto` in tests

Before writing `MakeConditionMetNode`, read `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs` to verify the exact shape of `PropertyMatchDto`. Use whatever predicate type is simplest and has deterministic JSON serialization. A `PropertyMatchDto` with a dummy `ComponentType = typeof(object)` and simple `NumericPredicateDto` as nested predicate works if the type takes `Type ComponentType`. If it takes a string, adjust accordingly.

---

## Success Criteria

Run:
```
dotnet test --filter "WhenNode"
```

Expected: all WhenNode tests pass (previously 41, now 43 with the 2 new ConditionMet lowering tests).

Full suite:
```
dotnet test
```

Expected: same pass/fail pattern as before, no new regressions.

---

## Out of Scope for This Batch

- Registrar wiring (`InitializePredicates` call from `Register` method) — that is **M3-T2**
- Hot-reload coordinator extension — **M3-T2**
- ConditionMet runtime tests — **M3-T3**
- EQS Result mode — **M4**
