# WHEN-BATCH-08 — EQS Result mode: IR + Stage 5 + Stage 6 + Emission (M4-T1, M4-T2)

**Tasks covered:** WHEN-M4-T1, WHEN-M4-T2  
**References:** [TASK-DETAIL.md M4-T1](../TASK-DETAIL.md#when-m4-t1--eqs-result-mode--common-scaffolding), [M4-T2](../TASK-DETAIL.md#when-m4-t2--eqs-result-mode--firstready-topchanged-scorecrossed-becomesstale-triggers), [DESIGN §6](../When_Reactivity_Iteration_Design_v2_2.md)

---

## Context

M3 is complete (49 WhenNode tests passing). The EQS-2 hard dependency is satisfied:
- `EqsSensorHandle` is in `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsSensorHandle.cs` (namespace `FDP.Eqs`)
- `EqsCognitiveBuffer`, `EqsSensor`, `EqsResult` are in `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs` (namespace `Fdp.Toolkit.Spatial.Eqs`)
- `LastUpdateTimeSeconds` is a `float` field on `EqsCognitiveBuffer`
- `GetSpanRO()` returns `ReadOnlySpan<EqsResult>` on `EqsCognitiveBuffer`

Currently, `WhenMode.EqsResult` falls into the `default:` case in `Stage5_Schedule.ScheduleWhenNode` and emits `IrOp_Const("false", ...)` (a stub). This batch replaces that stub with full implementation.

---

## Files to Modify

1. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Ir/IrOperation.cs`
2. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs`
3. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Lowering/WhenLowering_Instance.cs`
4. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/InstanceEmitter.cs`
5. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/StatementEmitter.cs`

---

## Files to Create

6. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage6_LoweringTests/WhenNodeEqsLoweringTests.cs`

---

## Implementation Steps

### Step 1 — Add `IrOp_WhenEqsResultCheck` to `IrOperation.cs`

Append after the existing `IrOp_WhenConditionMetCheck` record:

```csharp
/// <summary>
/// Emitted by Stage 5 for a WhenNode in EqsResult mode.
/// Stage 7 emits the EQS child-entity read + trigger-specific comparison inline.
/// No result value (ResultValue = null); branching is done inline with goto statements,
/// same as IrOp_WhenConditionMetCheck.
/// The block terminator must be IrTerm_Goto(outBlock).
/// </summary>
public sealed record IrOp_WhenEqsResultCheck(
    /// <summary>Name of the EqsSensorHandle variable field in the State struct (e.g. "CoverQuery").</summary>
    string SensorVariableName,
    /// <summary>EQS trigger kind: "TopChanged", "FirstReady", "ScoreCrossed", "BecomesStale".</summary>
    string Trigger,
    /// <summary>Name of the synthesized prev-state field in the State struct (e.g. "_when_a3f7c218_prev").</summary>
    string SynthFieldName,
    /// <summary>Name of the prev-state struct type local to the generated class (e.g. "_WhenEqsTopChanged_a3f7c218_PrevState").</summary>
    string SynthStructTypeName,
    /// <summary>Size in bytes of the synthesized struct (used for StructureHash contributions).</summary>
    int SynthStructSizeBytes,
    /// <summary>Score threshold as C# float literal (e.g. "0.5f"). Null for non-ScoreCrossed triggers.</summary>
    string? ScoreThresholdLiteral,
    /// <summary>Max age in seconds as C# float literal (e.g. "3.0f"). Null for non-BecomesStale triggers.</summary>
    string? MaxAgeLiteral,
    /// <summary>Block to goto when condition fires (RisingEdge). Null if no RisingEdge.</summary>
    IrBlockId? OnFiredBlock,
    /// <summary>Block to goto when condition ends (FallingEdge). Null if no FallingEdge.</summary>
    IrBlockId? OnEndedBlock
) : IrOperation;
```

### Step 2 — Add `WhenMode.EqsResult` case in `Stage5_Schedule.cs`

In `ScheduleWhenNode`, replace the `default:` stub comment with a proper `case WhenMode.EqsResult:` **before** the `default:` case.

The EqsResult mode follows the same structural pattern as `ConditionMet`:
- No result value (uses `IrTerm_Goto(outBlock)` instead of `IrTerm_Branch`)
- Branching is embedded inline in the op emit
- Uses `goto scheduleSuccessors` to skip the standard Branch code

Derive synth field/struct names from the node ID:

```csharp
case WhenMode.EqsResult:
{
    var er = wn.EqsResult;
    if (er is null) break; // BP2002 already reported

    string trigger = er.Trigger.ToString(); // "FirstReady", "TopChanged", "ScoreCrossed", "BecomesStale"

    // Determine struct shape from trigger
    string structTypeName = $"_WhenEqs{trigger}_{idShort}_PrevState";
    int structSizeBytes = trigger switch
    {
        "TopChanged"    => 16, // uint LastEvaluatedEpoch + long PrevTopId + float PrevTopScore
        "FirstReady"    => 4,  // uint LastEvaluatedEpoch
        "ScoreCrossed"  => 8,  // uint LastEvaluatedEpoch + float PrevTopScore
        "BecomesStale"  => 4,  // float PrevStaleCheckTime
        _               => 8,
    };

    string? scoreThreshold = trigger == "ScoreCrossed"
        ? $"{er.ScoreThreshold.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}f"
        : null;
    string? maxAge = trigger == "BecomesStale"
        ? $"{er.MaxAgeSeconds.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}f"
        : null;

    bb.Statements.Add(new IrStatement
    {
        ResultValue = null,
        Operation   = new IrOp_WhenEqsResultCheck(
            SensorVariableName:   er.SensorVariableName,
            Trigger:              trigger,
            SynthFieldName:       synthFieldName,
            SynthStructTypeName:  structTypeName,
            SynthStructSizeBytes: structSizeBytes,
            ScoreThresholdLiteral: scoreThreshold,
            MaxAgeLiteral:        maxAge,
            OnFiredBlock:         hasFired ? onFiredBlock : null,
            OnEndedBlock:         hasEnded ? onEndedBlock : null),
        Debug = debug,
    });

    bb.Terminator = new IrTerm_Goto(outBlock) { Debug = debug };
    goto scheduleSuccessors;
}
```

> **Note:** `synthFieldName` is already computed at the top of `ScheduleWhenNode` from the node id (`idShort`). Check the existing code to see where it's declared — it's used by ConditionMet and should be available. Look for `string synthFieldName` near the top of `ScheduleWhenNode`.

### Step 3 — Update `WhenLowering_Instance.cs`

Add a new case for `IrOp_WhenEqsResultCheck` after the existing `IrOp_WhenConditionMetCheck` case:

```csharp
else if (stmt.Operation is IrOp_WhenEqsResultCheck eqs)
{
    if (!seen.Add(eqs.SynthFieldName)) continue;
    var fieldId = SynthesizedGuids.WhenPrevField(asset.AssetId,
        DeriveNodeIdFromFieldName(eqs.SynthFieldName));
    toAdd.Add(new IrField
    {
        Id                 = fieldId,
        Name               = eqs.SynthFieldName,
        Type               = new IrTypeRef
        {
            FullName    = eqs.SynthStructTypeName, // local generated type (starts with '_')
            IsUnmanaged = true,
            SizeBytes   = eqs.SynthStructSizeBytes,
        },
        DefaultValueCSharp = "default",
    });
}
```

### Step 4 — Fix `TypeRefToCSharp` in `StatementEmitter.cs`

The `_WhenEqsXxx_PrevState` struct names start with `_`. The existing `_` switch arm in `TypeRefToCSharp` would prepend `global::` which is wrong for local generated types. Add a guard before the final `_` case:

In `TypeRefToCSharp`, replace:
```csharp
_                => $"global::{t.FullName}",
```
with:
```csharp
_ when t.FullName.StartsWith("_") => t.FullName, // local generated type (synthesized struct)
_                                  => $"global::{t.FullName}",
```

This ensures `_WhenEqsTopChanged_a3f7c218_PrevState` is emitted as-is (no `global::` prefix).

### Step 5 — Add EQS struct + op emission to `InstanceEmitter.cs`

**5a.** Add a `CollectEqsResultOps` helper (similar to `CollectConditionMetOps`):

```csharp
private static List<IrOp_WhenEqsResultCheck> CollectEqsResultOps(IrAsset asset)
{
    var result = new List<IrOp_WhenEqsResultCheck>();
    var seen   = new HashSet<string>();
    foreach (var graph in asset.Graphs)
    foreach (var block in graph.Blocks)
    foreach (var stmt  in block.Statements)
    {
        if (stmt.Operation is not IrOp_WhenEqsResultCheck op) continue;
        if (!seen.Add(op.SynthFieldName)) continue;
        result.Add(op);
    }
    return result;
}
```

**5b.** Add `EmitEqsResultPrevStateStructs` method:

This emits the nested private struct definitions for each EQS trigger. Place after `EmitConditionMetFields` / `EmitInitializePredicates`.

```csharp
private static void EmitEqsResultPrevStateStructs(CSharpEmitter e, List<IrOp_WhenEqsResultCheck> ops)
{
    foreach (var op in ops)
    {
        e.WriteLine($"[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
        e.WriteLine($"private struct {op.SynthStructTypeName}");
        e.WriteLine("{");
        e.Indent();
        switch (op.Trigger)
        {
            case "TopChanged":
                e.WriteLine("public uint  LastEvaluatedEpoch;");
                e.WriteLine("public long  PrevTopId;");
                e.WriteLine("public float PrevTopScore;");
                break;
            case "FirstReady":
                e.WriteLine("public uint LastEvaluatedEpoch;");
                break;
            case "ScoreCrossed":
                e.WriteLine("public uint  LastEvaluatedEpoch;");
                e.WriteLine("public float PrevTopScore;");
                break;
            case "BecomesStale":
                e.WriteLine("public float PrevStaleCheckTime;");
                break;
        }
        e.Outdent();
        e.WriteLine("}");
    }
}
```

**5c.** Add `EmitEqsConstFields` for ScoreCrossed/BecomesStale const fields:

```csharp
private static void EmitEqsConstFields(CSharpEmitter e, List<IrOp_WhenEqsResultCheck> ops)
{
    foreach (var op in ops)
    {
        if (op.ScoreThresholdLiteral is not null)
        {
            var id8 = ExtractId8FromSynthFieldName(op.SynthFieldName);
            e.WriteLine($"private const float _whenScoreThreshold_{id8} = {op.ScoreThresholdLiteral};");
        }
        if (op.MaxAgeLiteral is not null)
        {
            var id8 = ExtractId8FromSynthFieldName(op.SynthFieldName);
            e.WriteLine($"private const float _whenMaxAge_{id8} = {op.MaxAgeLiteral};");
        }
    }
}

private static string ExtractId8FromSynthFieldName(string synthFieldName)
{
    // "_when_<id8>_prev" -> "<id8>"
    const string prefix = "_when_";
    const string suffix = "_prev";
    if (synthFieldName.StartsWith(prefix) && synthFieldName.EndsWith(suffix))
        return synthFieldName.Substring(prefix.Length,
            synthFieldName.Length - prefix.Length - suffix.Length);
    return synthFieldName;
}
```

**5d.** Wire the new methods into `EmitClass`:

After the existing `condMetOps` block (around line 35 in `InstanceEmitter.EmitClass`), add:

```csharp
var eqsOps = CollectEqsResultOps(asset);
if (eqsOps.Count > 0)
{
    e.WriteLine();
    EmitEqsResultPrevStateStructs(e, eqsOps);
    EmitEqsConstFields(e, eqsOps);
}
```

### Step 6 — Add EQS trigger emission to `StatementEmitter.cs`

Add a `case IrOp_WhenEqsResultCheck op:` in the WhenNode section of `EmitStatement`. The emitter uses `sv` for the State struct variable name and `wv` for the view variable name (check existing cases for the exact local variable names).

Implement following the canonical templates from DESIGN §6.5–6.8. Use `sv` for state variable, `wv` for view variable.

**Important naming conventions from existing emitter:**
- `sv` = the local name for the `ref State` (check the existing `case IrOp_WhenConditionMetCheck` to confirm variable names used)  
- `wv` = view variable (check the existing usage in `IrOp_WhenValueChangedCheck`)
- `idx` = the statement result index (used in variable names like `__t{idx}`)

Since `IrOp_WhenEqsResultCheck` has `ResultValue = null`, the `idx` parameter will be `-1`. The op emits inline, similar to `IrOp_WhenConditionMetCheck`.

```csharp
case IrOp_WhenEqsResultCheck op:
{
    var id8 = ExtractId8FromFieldName(op.SynthFieldName); // same helper as InstanceEmitter

    // BEGIN comment
    e.WriteLine($"// BEGIN WhenNode {id8}: EqsResult / {op.Trigger} / {(op.OnFiredBlock.HasValue ? "RisingEdge" : "")}{(op.OnEndedBlock.HasValue ? "FallingEdge" : "")}");
    e.WriteLine("{");
    e.Indent();

    // Common pre-flow: handle + liveness guard + component reads
    e.WriteLine($"ref var prev = ref {sv}.{op.SynthFieldName};");
    e.WriteLine($"ref readonly var handle = ref {sv}.{op.SensorVariableName};");
    e.WriteLine();
    e.WriteLine($"if (!{wv}.IsAlive(handle.ChildId))");
    e.Indent();
    e.WriteLine($"goto whenNode_{id8}_end;");
    e.Outdent();
    e.WriteLine();

    // Trigger-specific emission
    switch (op.Trigger)
    {
        case "TopChanged":
            EmitEqsTopChanged(e, op, id8, wv, sv);
            break;
        case "FirstReady":
            EmitEqsFirstReady(e, op, id8, wv, sv);
            break;
        case "ScoreCrossed":
            EmitEqsScoreCrossed(e, op, id8, wv, sv);
            break;
        case "BecomesStale":
            EmitEqsBecomesStale(e, op, id8, wv, sv);
            break;
    }

    e.WriteLine();
    e.WriteLine($"whenNode_{id8}_end: ;");
    e.Outdent();
    e.WriteLine("}");
    e.WriteLine($"// END WhenNode {id8}");
    break;
}
```

**`EmitEqsTopChanged` helper** (follows DESIGN §6.5 exactly):

```csharp
private static void EmitEqsTopChanged(CSharpEmitter e, IrOp_WhenEqsResultCheck op, string id8, string wv, string sv)
{
    e.WriteLine($"ref readonly var sensor = ref {wv}.GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsSensor>(handle.ChildId);");
    e.WriteLine($"ref readonly var buffer = ref {wv}.GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(handle.ChildId);");
    e.WriteLine();
    e.WriteLine($"if (sensor.Epoch != prev.LastEvaluatedEpoch)");
    e.WriteLine("{");
    e.Indent();
    e.WriteLine($"if (buffer.IsReady)");
    e.WriteLine("{");
    e.Indent();
    e.WriteLine($"var results = buffer.GetSpanRO();");
    e.WriteLine($"if (results.Length > 0)");
    e.WriteLine("{");
    e.Indent();
    e.WriteLine($"var top = results[0];");
    e.WriteLine($"long currentTopId = top.EntityId != 0L");
    e.WriteLine($"    ? top.EntityId");
    e.WriteLine($"    : global::System.HashCode.Combine(top.PositionX, top.PositionY);");
    e.WriteLine();
    e.WriteLine($"if (currentTopId != prev.PrevTopId && prev.LastEvaluatedEpoch != 0)");
    e.WriteLine("{");
    e.Indent();
    if (op.OnFiredBlock.HasValue)
        e.WriteLine($"goto {GotoLabel(op.OnFiredBlock.Value)};");
    e.Outdent();
    e.WriteLine("}");
    e.WriteLine();
    e.WriteLine($"prev.PrevTopId    = currentTopId;");
    e.WriteLine($"prev.PrevTopScore = top.Score;");
    e.Outdent();
    e.WriteLine("}");
    e.WriteLine("else");
    e.WriteLine("{");
    e.Indent();
    e.WriteLine($"prev.PrevTopId    = 0L;");
    e.WriteLine($"prev.PrevTopScore = 0f;");
    e.Outdent();
    e.WriteLine("}");
    e.Outdent();
    e.WriteLine("}");
    e.WriteLine($"prev.LastEvaluatedEpoch = sensor.Epoch;");
    e.Outdent();
    e.WriteLine("}");
}
```

**`EmitEqsFirstReady` helper** (follows DESIGN §6.6):

```csharp
private static void EmitEqsFirstReady(CSharpEmitter e, IrOp_WhenEqsResultCheck op, string id8, string wv, string sv)
{
    e.WriteLine($"ref readonly var sensor = ref {wv}.GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsSensor>(handle.ChildId);");
    e.WriteLine($"ref readonly var buffer = ref {wv}.GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(handle.ChildId);");
    e.WriteLine();
    e.WriteLine($"if (sensor.Epoch != prev.LastEvaluatedEpoch)");
    e.WriteLine("{");
    e.Indent();
    e.WriteLine($"if (buffer.IsReady && prev.LastEvaluatedEpoch == 0)");
    e.WriteLine("{");
    e.Indent();
    if (op.OnFiredBlock.HasValue)
        e.WriteLine($"goto {GotoLabel(op.OnFiredBlock.Value)};");
    e.Outdent();
    e.WriteLine("}");
    e.WriteLine($"prev.LastEvaluatedEpoch = sensor.Epoch;");
    e.Outdent();
    e.WriteLine("}");
}
```

**`EmitEqsScoreCrossed` helper** (follows DESIGN §6.7):

```csharp
private static void EmitEqsScoreCrossed(CSharpEmitter e, IrOp_WhenEqsResultCheck op, string id8, string wv, string sv)
{
    e.WriteLine($"ref readonly var sensor = ref {wv}.GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsSensor>(handle.ChildId);");
    e.WriteLine($"ref readonly var buffer = ref {wv}.GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(handle.ChildId);");
    e.WriteLine();
    e.WriteLine($"if (sensor.Epoch != prev.LastEvaluatedEpoch)");
    e.WriteLine("{");
    e.Indent();
    e.WriteLine($"if (buffer.IsReady)");
    e.WriteLine("{");
    e.Indent();
    e.WriteLine($"var results = buffer.GetSpanRO();");
    e.WriteLine($"if (results.Length > 0)");
    e.WriteLine("{");
    e.Indent();
    e.WriteLine($"float currentScore = results[0].Score;");
    e.WriteLine($"bool wasAbove = prev.PrevTopScore >= _whenScoreThreshold_{id8};");
    e.WriteLine($"bool isAbove  = currentScore      >= _whenScoreThreshold_{id8};");
    e.WriteLine();
    e.WriteLine($"if (!wasAbove && isAbove && prev.LastEvaluatedEpoch != 0)");
    e.WriteLine("{");
    e.Indent();
    if (op.OnFiredBlock.HasValue)
        e.WriteLine($"goto {GotoLabel(op.OnFiredBlock.Value)};");
    e.Outdent();
    e.WriteLine("}");
    if (op.OnEndedBlock.HasValue)
    {
        e.WriteLine($"else if (wasAbove && !isAbove && prev.LastEvaluatedEpoch != 0)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"goto {GotoLabel(op.OnEndedBlock.Value)};");
        e.Outdent();
        e.WriteLine("}");
    }
    e.WriteLine();
    e.WriteLine($"prev.PrevTopScore = currentScore;");
    e.Outdent();
    e.WriteLine("}");
    e.Outdent();
    e.WriteLine("}");
    e.WriteLine($"prev.LastEvaluatedEpoch = sensor.Epoch;");
    e.Outdent();
    e.WriteLine("}");
}
```

**`EmitEqsBecomesStale` helper** (follows DESIGN §6.8 — no epoch gate):

```csharp
private static void EmitEqsBecomesStale(CSharpEmitter e, IrOp_WhenEqsResultCheck op, string id8, string wv, string sv)
{
    // BecomesStale: no sensor Epoch gate, no EqsSensor component read
    e.WriteLine($"ref readonly var buffer = ref {wv}.GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(handle.ChildId);");
    e.WriteLine();
    e.WriteLine($"if (buffer.IsReady)");
    e.WriteLine("{");
    e.Indent();
    e.WriteLine($"float age     = time - buffer.LastUpdateTimeSeconds;");
    e.WriteLine($"float prevAge = time - prev.PrevStaleCheckTime;");
    e.WriteLine();
    e.WriteLine($"bool wasStale = prevAge > _whenMaxAge_{id8};");
    e.WriteLine($"bool isStale  = age     > _whenMaxAge_{id8};");
    e.WriteLine();
    e.WriteLine($"if (!wasStale && isStale)");
    e.WriteLine("{");
    e.Indent();
    if (op.OnFiredBlock.HasValue)
        e.WriteLine($"goto {GotoLabel(op.OnFiredBlock.Value)};");
    e.Outdent();
    e.WriteLine("}");
    if (op.OnEndedBlock.HasValue)
    {
        e.WriteLine($"else if (wasStale && !isStale)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"goto {GotoLabel(op.OnEndedBlock.Value)};");
        e.Outdent();
        e.WriteLine("}");
    }
    e.WriteLine();
    e.WriteLine($"prev.PrevStaleCheckTime = buffer.LastUpdateTimeSeconds;");
    e.Outdent();
    e.WriteLine("}");
}
```

**Helper functions needed in `StatementEmitter`:**

```csharp
private static string ExtractId8FromFieldName(string synthFieldName)
{
    const string prefix = "_when_";
    const string suffix = "_prev";
    if (synthFieldName.StartsWith(prefix) && synthFieldName.EndsWith(suffix))
        return synthFieldName.Substring(prefix.Length,
            synthFieldName.Length - prefix.Length - suffix.Length);
    return synthFieldName;
}

private static string GotoLabel(IrBlockId blockId)
    => $"block_{blockId.Value:N}"; // or whatever the existing label naming convention is
```

> **IMPORTANT:** Before implementing the goto label format, look at the existing `IrOp_WhenConditionMetCheck` emission in `StatementEmitter.cs` to see the exact format of `goto` labels used there (they might be `block_{id}` or something else). Use the same convention.

---

## Tests to Create

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage6_LoweringTests/WhenNodeEqsLoweringTests.cs`.

Use the existing `WhenNodeLoweringTests.cs` as structural reference (it shows how to call `Compile(asset)` and search the returned C# string).

The test class needs:
- Same `using` statements as `WhenNodeLoweringTests.cs`
- Same `DefaultOptions()` helper
- Same `Compile(asset)` helper

### Tests

```csharp
[Fact]
public void Lower_EqsResult_UsesChildEntityRead()
{
    // Build an Instance asset with a WhenNode in EqsResult/TopChanged mode.
    // SensorVariableName = "CoverQuery" — add this as an EqsSensorHandle variable.
    // Assert the emitted C# contains "GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(handle.ChildId)"
    // and NOT "GetComponentRO<...>(self)".
}

[Fact]
public void Lower_EqsResult_LivenessGuardPrecedesReads()
{
    // Same asset as above.
    // Assert the emitted C# contains "view.IsAlive(handle.ChildId)" and that it
    // appears BEFORE "GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer".
    // (Check indexOf order in the source string.)
}

[Fact]
public void Lower_EqsResult_TopChanged_UsesGetSpanRO()
{
    // Build Instance asset with WhenNode in EqsResult/TopChanged mode.
    // Assert emitted source contains "buffer.GetSpanRO()" (not direct index access).
}

[Fact]
public void Lower_EqsResult_TopChanged_EpochGated()
{
    // Assert emitted source contains "sensor.Epoch != prev.LastEvaluatedEpoch"
    // as the first conditional after the liveness guard.
}

[Fact]
public void Lower_EqsResult_PositionalHash_OnTheFly()
{
    // TopChanged trigger; assert source contains:
    // "top.EntityId != 0L" and "HashCode.Combine(top.PositionX, top.PositionY)"
}

[Fact]
public void Lower_EqsResult_FirstReady_DistinctStateStruct()
{
    // Build Instance with WhenNode in EqsResult/FirstReady mode.
    // Assert the emitted State struct field uses a _WhenEqsFirstReady_ prefix struct type.
    // Assert the struct type contains only "LastEvaluatedEpoch" (not PrevTopId or PrevTopScore).
    // Check SizeBytes contribution: FirstReady state is 4 bytes.
    // Hint: look at the emitted struct definition in source for "struct _WhenEqsFirstReady_".
}

[Fact]
public void Lower_EqsResult_ScoreCrossed_EmitsConstThreshold()
{
    // Build Instance with WhenNode in EqsResult/ScoreCrossed mode, ScoreThreshold = 0.75f.
    // Assert source contains "private const float _whenScoreThreshold_" and "0.75".
}

[Fact]
public void Lower_EqsResult_BecomesStale_UsesSimTime()
{
    // Build Instance with WhenNode in EqsResult/BecomesStale mode, MaxAgeSeconds = 3.0f.
    // Assert source contains "time - buffer.LastUpdateTimeSeconds" (not "tick").
    // Assert source contains "private const float _whenMaxAge_" and "3".
}

[Fact]
public void Lower_EqsResult_BecomesStale_NotEpochGated()
{
    // Build Instance with WhenNode in EqsResult/BecomesStale mode.
    // Assert source does NOT contain "sensor.Epoch" (BecomesStale skips the epoch gate).
}

[Fact]
public void Lower_StructureHashDiffersWithEqsResult()
{
    // Build two assets: one without WhenNode, one with EqsResult/TopChanged WhenNode.
    // Assert the two assets have different StructureHash values.
    // (Use Stage6_Lower.Run to get the lowered asset and compare asset.StructureHash.)
}
```

### Building the Test Asset

For each test, you need an Instance `BlueprintAsset` with:
1. A variable for the sensor handle (type `FDP.Eqs.EqsSensorHandle`)
2. A `WhenNode` with `Mode = WhenMode.EqsResult`, `EqsResult = new EqsResultPayload { SensorVariableName = "CoverQuery", Trigger = EqsTrigger.TopChanged, ... }`
3. An `EventEntryNode` wired to the `WhenNode`
4. Pin layout matching what Stage 5 expects: ExecIn pin on WhenNode, Out exec pin, OnFired pin (if RisingEdge)

Look at `WhenNodeLoweringTests.cs` method `MakeValueChangedNode` to see how exec pins are set up. Create a parallel `MakeEqsResultNode` helper:

```csharp
private static WhenNode MakeEqsResultNode(Guid nodeId, EqsTrigger trigger, string sensorVarName,
    WhenEdge edges = WhenEdge.RisingEdge, float scoreThreshold = 0f, float maxAge = 5f)
{
    var node = new WhenNode
    {
        Id    = nodeId,
        Mode  = WhenMode.EqsResult,
        Edges = edges,
        EqsResult = new EqsResultPayload
        {
            SensorVariableName = sensorVarName,
            Trigger            = trigger,
            ScoreThreshold     = scoreThreshold,
            MaxAgeSeconds      = maxAge,
        },
    };
    node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() });
    if ((edges & WhenEdge.RisingEdge)  != 0)
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });
    if ((edges & WhenEdge.FallingEdge) != 0)
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnEnded", Direction = "Out", IsExec = true, TypeRef = new() });
    node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() });
    return node;
}
```

The EqsSensorHandle variable for the asset:
```csharp
var sensorVar = new BlueprintVariable
{
    Id      = Guid.NewGuid(),
    Name    = "CoverQuery",
    Type    = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
};
```

Build the full `BlueprintAsset` (Dispatch = Instance) with graph containing EntryNode → WhenNode.  
Look at existing tests in `WhenNodeLoweringTests.cs` to see the full asset construction pattern.

---

## What NOT to do

- Do NOT implement `ReadEqsResultNode` or `SpawnEqsSensorNode` lowering — that's WHEN-BATCH-09.
- Do NOT add runtime tests — that's WHEN-BATCH-10.
- Do NOT modify the `SpawnEqsSensorNode` validator (BP2032 instance-id collision) — out of scope for this batch.

---

## Implementation Notes

### Goto label convention
Before implementing `GotoLabel(IrBlockId)`, look at the existing `IrOp_WhenConditionMetCheck` case in `StatementEmitter.cs` to see what goto label format is already used. Copy that exact format.

### `sv` and `wv` variable names in StatementEmitter
These are local variables in the `EmitStatement` method that hold the state struct variable name and view variable name respectively. Look at the `IrOp_WhenValueChangedCheck` and `IrOp_WhenConditionMetCheck` cases to find them. They're typically named something like `sv` and `wv` or similar. Use whatever names the existing emitter uses.

### `time` variable in BecomesStale
The `Tick` method signature has `float time` as the 5th parameter. The StatementEmitter has access to the method parameter names through its context. Look at how existing emitted code references `time` (for latent delays or similar patterns).

### HashCode.Combine return type
`HashCode.Combine(float, float)` returns `int`, but we cast it to `long` implicitly for `currentTopId`. The emitted code should compile without an explicit cast since `int` is implicitly convertible to `long`. Use `(long)global::System.HashCode.Combine(top.PositionX, top.PositionY)` to be explicit.

### FallingEdge for TopChanged
The DESIGN §6.5 canonical example only shows RisingEdge. For FallingEdge, emit an `else if (currentTopId != prev.PrevTopId && prev.LastEvaluatedEpoch != 0)` — no, actually TopChanged is inherently a "changed" event. The OnEnded case for TopChanged would be: "top identity changed from previous non-zero" which is the same logic. For simplicity: TopChanged only has RisingEdge in the DESIGN. If `OnEndedBlock` is set (FallingEdge), follow the same guard pattern but target `OnEndedBlock`. The validator (BP2014 or similar) should prevent FallingEdge on TopChanged, so treat it as a no-op in Stage 6.

---

## Build & Test

```
dotnet build Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName~WhenNodeEqs" -v normal
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName~WhenNode" -v normal
```

All pre-existing WhenNode tests (49) must still pass.

---

## Commit

```
git -C d:\WORK\IOS-IG-SimHost-FDP add -A
git -C d:\WORK\IOS-IG-SimHost-FDP commit -m "WHEN-BATCH-08: EQS Result mode IR + Stage5 + Stage6 + emission (M4-T1, M4-T2)"
```
