# BATCH-12 — TASK-CP-004: Stage 7 Emit (C# Code Generation)

## References
- **Task detail:** `.dev/blueprints-1/TASK-DETAIL.md#TASK-CP-004`
- **Compiler DD §10:** `Blueprint_Subsystem_Compiler_Detailed_Design.md` — Stage 7 Emit (§10.1–§10.10)
- **Inline Patches v1:** `Blueprint_Subsystem_Compiler_Detailed_Design_InlinePatches.md` — Q-18.1 (instanceVersion), Q-18.3 (deltaTime on custom events), Q-18.4 (class name suffix)
- **Inline Patches v2:** `Blueprint_Subsystem_Compiler_Detailed_Design_InlinePatches_v2.md` — Patch C1 (registrar uses BlueprintRegistryStaging, static HsmActionDispatcher)
- **Worked example §15 (MoveToAndFire) and §16 (HealthRegen):** in the Compiler DD — study both before coding

## Baseline
- Tests: **175 pass, 3 skip, 0 fail** (from BATCH-11)
- `Stage7_Emit.Run` throws `NotImplementedException`
- All stub files exist under `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/`:
  - `CSharpEmitter.cs` — stub (single `Emit` method throwing NotImplementedException)
  - `EmissionContext.cs` — stub (has `Asset` property + `NextLocalCounter`)
  - `DebugMapBuilder.cs` — has `Record(NodeId, GraphId, startLine, endLine)` + `Build()`
  - `Sanitizer.cs` — COMPLETE (do not modify)
- `Stage7_Emit.cs` in `Compiler/Stages/` — stub throwing NotImplementedException

## Task Scope

Implement Stage 7 end-to-end. After this batch, `BlueprintCompiler.Compile` can be used to generate valid C# source from a `BlueprintAsset`.

### Files to CREATE

1. `Hrot.Blueprints.Core/Compiler/Emit/BlockEmitter.cs`
2. `Hrot.Blueprints.Core/Compiler/Emit/StatementEmitter.cs`
3. `Hrot.Blueprints.Core/Compiler/Emit/TerminatorEmitter.cs`
4. `Hrot.Blueprints.Core/Compiler/Emit/ChannelCommandLowering.cs`
5. `Hrot.Blueprints.Core/Compiler/Emit/LibraryEmitter.cs`
6. `Hrot.Blueprints.Core/Compiler/Emit/AiPrimitiveEmitter.cs`
7. `Hrot.Blueprints.Core/Compiler/Emit/InstanceEmitter.cs`
8. `Hrot.Blueprints.Tests/Stage7Tests.cs`

### Files to MODIFY

1. `Hrot.Blueprints.Core/Compiler/Emit/CSharpEmitter.cs` — replace stub with full implementation
2. `Hrot.Blueprints.Core/Compiler/Emit/EmissionContext.cs` — add all context helpers
3. `Hrot.Blueprints.Core/Compiler/Emit/DebugMapBuilder.cs` — add `RecordNodeStart(nodeId, graphId, line)` / `RecordNodeEnd(nodeId, line)` methods alongside the existing `Record` method
4. `Hrot.Blueprints.Core/Compiler/Stages/Stage7_Emit.cs` — replace stub with implementation
5. `Hrot.Blueprints.Core/Compiler/BlueprintCompiler.cs` — wire Stage 7 (remove the NotImplementedException for Stage 7, call Stage7_Emit.Run)

---

## Step 1 — `EmissionContext.cs`

`EmissionContext` is the per-asset mutable state threaded through all emitters. Implement all helpers:

```csharp
namespace Hrot.Blueprints.Core.Compiler.Emit;

internal sealed class EmissionContext
{
    private readonly Dictionary<string, int> _counters = new();
    // Build a lookup from IrBlockId.Value → block label during construction
    private readonly Dictionary<int, string> _blockLabels;

    public IrAsset Asset { get; }
    public CompilerMode Mode { get; }

    public EmissionContext(IrAsset asset, CompilerMode mode)
    {
        Asset = asset;
        Mode = mode;
        _blockLabels = new Dictionary<int, string>();
        // Pre-populate: iterate all graphs, all blocks
        foreach (var g in asset.Graphs)
            foreach (var b in g.Blocks)
                _blockLabels[b.Id.Value] = b.Label;
    }

    /// <summary>Returns next integer suffix per prefix (e.g. "ch" → 0, 1, 2, ...).</summary>
    public string NextLocalCounter(string prefix)
    {
        _counters.TryGetValue(prefix, out int n);
        _counters[prefix] = n + 1;
        return n.ToString();
    }

    /// <summary>Block label by IrBlockId, for goto emission.</summary>
    public string LabelForBlock(IrBlockId id)
        => _blockLabels.TryGetValue(id.Value, out var lbl) ? lbl : $"block_{id.Value}";

    /// <summary>C# field name for a WorkingState or Variable by index.</summary>
    public string VarFieldName(int index)
    {
        var fields = Asset.Variables; // Instance
        if (index >= 0 && index < fields.Count)
            return fields[index].Name;
        // AiPrimitive WorkingState
        var ws = Asset.WorkingState;
        if (index >= 0 && index < ws.Count)
            return ws[index].Name;
        return $"__var_{index}";
    }

    /// <summary>C# field name for a Parameters entry by index.</summary>
    public string ParamFieldName(int index)
    {
        var ps = Asset.Parameters;
        return index >= 0 && index < ps.Count ? ps[index].Name : $"__p_{index}";
    }

    /// <summary>Custom event name by index (for Event_* method calls).</summary>
    public string CustomEventName(int index)
    {
        var evts = Asset.CustomEvents;
        return index >= 0 && index < evts.Count ? evts[index].Name : $"__customEvent_{index}";
    }

    /// <summary>Library class name for a LibraryBlueprintId.</summary>
    public string ResolveLibraryClass(int libraryBlueprintId)
    {
        // Peer classes are referenced by their generated class name in the same namespace.
        // Format: {SanitizedName}_{BlueprintId:X8}_Bp  (same naming as Q-18.4)
        // For Slice 1, we look up from CallablePeerBlueprintIds → use the id directly.
        return $"__LibBp_{libraryBlueprintId:X8}_Bp";
    }
}
```

---

## Step 2 — `CSharpEmitter.cs` (full implementation)

```csharp
namespace Hrot.Blueprints.Core.Compiler.Emit;

internal sealed class CSharpEmitter
{
    private readonly StringBuilder _sb = new();
    private readonly DebugMapBuilder _debugMap;
    private readonly EmissionContext _ctx;
    private int _indent;
    private int _currentLine = 1;

    public CSharpEmitter(EmissionContext ctx)
    {
        _ctx = ctx;
        _debugMap = new DebugMapBuilder(ctx.Asset.AssetId);
    }

    public EmissionContext Ctx => _ctx;
    public int CurrentLine => _currentLine;

    public void Write(string text)
    {
        _sb.Append(text);
        foreach (char c in text)
            if (c == '\n') _currentLine++;
    }

    public void WriteLine(string line = "")
    {
        for (int i = 0; i < _indent; i++) _sb.Append("    ");
        _sb.Append(line);
        _sb.Append('\n');
        _currentLine++;
    }

    public void Indent() => _indent++;
    public void Outdent() => _indent = Math.Max(0, _indent - 1);

    public void EmitNodeStart(IrDebugAnnotation? debug)
    {
        if (debug?.NodeId is null) return;
        _debugMap.RecordNodeStart(debug.NodeId.Value, debug.GraphId, _currentLine);
    }

    public void EmitNodeEnd(IrDebugAnnotation? debug)
    {
        if (debug?.NodeId is null) return;
        _debugMap.RecordNodeEnd(debug.NodeId.Value, _currentLine);
    }

    public (string Source, DebugMap DebugMap) Emit(IrAsset asset)
    {
        EmitFileHeader(asset);
        EmitUsings();
        WriteLine();

        switch (asset.Dispatch)
        {
            case BlueprintDispatchKind.Library:
                LibraryEmitter.EmitClass(this, asset);
                break;
            case BlueprintDispatchKind.AiPrimitive:
                AiPrimitiveEmitter.EmitClass(this, asset);
                break;
            case BlueprintDispatchKind.Instance:
                InstanceEmitter.EmitClass(this, asset);
                break;
        }

        WriteLine();
        EmitRegistrarClass(asset);
        return (_sb.ToString(), _debugMap.Build());
    }

    private void EmitFileHeader(IrAsset asset)
    {
        var className = $"{asset.SanitizedName}_{asset.BlueprintId:X8}_Bp";
        WriteLine("// <auto-generated />");
        WriteLine($"// Asset: {asset.Name} ({asset.AssetId})");
        WriteLine($"// BlueprintId: 0x{asset.BlueprintId:X8}");
        WriteLine($"// StructureHash: 0x{asset.StructureHash:X16}");
        WriteLine();
    }

    private void EmitUsings()
    {
        WriteLine("using System;");
        WriteLine("using System.Runtime.CompilerServices;");
        WriteLine("using System.Runtime.InteropServices;");
        WriteLine("using System.Numerics;");
        WriteLine("using Fdp.Core;");
        WriteLine("using Fdp.Interfaces;");
        WriteLine("using Fdp.ModuleHost.Abstractions;");
        WriteLine("using Fdp.Toolkit.Blueprints;");
        // Note: BlueprintDispatchKind lives in both assemblies — prefer the Fdp.Toolkit.Blueprints one
        // HsmActionDispatcher is used as a static call reference in registrars
    }

    private void EmitRegistrarClass(IrAsset asset)
    {
        // Per Patch C1: ALL registrars use BlueprintRegistryStaging (not BlueprintRegistry)
        // HsmActionDispatcher calls are static (no instance parameter)
        var className = $"{asset.SanitizedName}_{asset.BlueprintId:X8}_Bp";
        var registrarName = $"BlueprintRegistrar_{asset.SanitizedName}_{asset.BlueprintId:X8}_Bp";

        bool needsBehReg = asset.Hostings.Any(h =>
            h == AiPrimitiveHosting.BTreeAction || h == AiPrimitiveHosting.BTreeCondition);
        bool needsHsmCalls = asset.Hostings.Any(h =>
            h == AiPrimitiveHosting.HsmAction || h == AiPrimitiveHosting.HsmGuard);

        WriteLine("[global::Fdp.Toolkit.Blueprints.BlueprintRegistrar]");
        WriteLine($"public static class {registrarName}");
        WriteLine("{");
        Indent();

        // Build param list
        var paramParts = new List<string>
            { "global::Fdp.Toolkit.Blueprints.BlueprintRegistryStaging staging" };
        if (needsBehReg)
            paramParts.Add("global::Fdp.Toolkit.Behavior.BehaviorRegistry behReg");
        var paramSig = string.Join(", ", paramParts);

        WriteLine($"public static void Register({paramSig})");
        WriteLine("{");
        Indent();

        // Build BlueprintDefinition based on dispatch kind
        switch (asset.Dispatch)
        {
            case BlueprintDispatchKind.Library:
                EmitLibraryRegistration(className, asset);
                break;
            case BlueprintDispatchKind.AiPrimitive:
                EmitAiPrimitiveRegistration(className, asset, needsBehReg, needsHsmCalls);
                break;
            case BlueprintDispatchKind.Instance:
                EmitInstanceRegistration(className, asset);
                break;
        }

        Outdent();
        WriteLine("}");
        Outdent();
        WriteLine("}");
    }

    private void EmitLibraryRegistration(string className, IrAsset asset)
    {
        WriteLine($"staging.Add({className}.BlueprintId, new global::Fdp.Toolkit.Blueprints.BlueprintDefinition");
        WriteLine("{");
        Indent();
        WriteLine($"Name = \"{asset.Name}\",");
        WriteLine("Kind = global::Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Library,");
        WriteLine($"StructureHash = {asset.StructureHash}UL,");
        WriteLine("StateSize = 0,");
        Outdent();
        WriteLine("});");
    }

    private void EmitAiPrimitiveRegistration(string className, IrAsset asset,
        bool needsBehReg, bool needsHsmCalls)
    {
        // staging.Add(...)
        WriteLine($"staging.Add({className}.BlueprintId, new global::Fdp.Toolkit.Blueprints.BlueprintDefinition");
        WriteLine("{");
        Indent();
        WriteLine($"Name = \"{asset.Name}\",");
        WriteLine("Kind = global::Fdp.Toolkit.Blueprints.BlueprintDispatchKind.AiPrimitive,");
        WriteLine($"StructureHash = {className}.StructureHash,");
        WriteLine("StateSize = 0,");
        Outdent();
        WriteLine("});");

        // BTree hosting
        foreach (var h in asset.Hostings)
        {
            if (h == AiPrimitiveHosting.BTreeAction)
                WriteLine($"behReg.RegisterAction(\"{className}\", {className}.BTreeTick);");
            else if (h == AiPrimitiveHosting.BTreeCondition)
                WriteLine($"behReg.RegisterCondition(\"{className}\", {className}.BTreeEvaluate);");
        }

        // HSM hosting — static calls (Patch C1)
        foreach (var h in asset.Hostings)
        {
            if (h == AiPrimitiveHosting.HsmAction)
            {
                WriteLine("global::FastHSM.HsmActionDispatcher.RegisterAction(");
                Indent();
                WriteLine($"{className}.BlueprintId,");
                WriteLine($"(IntPtr)(delegate* unmanaged<void*, void*, void*, void>)");
                WriteLine($"    &{className}.HsmActivity);");
                Outdent();
            }
            else if (h == AiPrimitiveHosting.HsmGuard)
            {
                WriteLine("global::FastHSM.HsmActionDispatcher.RegisterGuard(");
                Indent();
                WriteLine($"{className}.BlueprintId,");
                WriteLine($"(IntPtr)(delegate* unmanaged<void*, void*, ushort, bool>)");
                WriteLine($"    &{className}.HsmGuard);");
                Outdent();
            }
        }
    }

    private void EmitInstanceRegistration(string className, IrAsset asset)
    {
        var eventHandlers = asset.Graphs.Where(g => g.Kind == IrGraphKind.Event).ToList();

        WriteLine($"staging.Add({className}.BlueprintId, new global::Fdp.Toolkit.Blueprints.BlueprintDefinition");
        WriteLine("{");
        Indent();
        WriteLine($"Name = \"{asset.Name}\",");
        WriteLine("Kind = global::Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance,");
        WriteLine($"StructureHash = {className}.StructureHash,");
        WriteLine($"StateSize = {className}.StateSize,");
        WriteLine($"StateClrType = typeof({className}.State),");
        WriteLine($"InitDefault = {className}.InitDefault,");
        WriteLine($"Tick = {className}.TickThunk,");
        if (eventHandlers.Count > 0)
        {
            WriteLine("EventHandlers = new global::System.Collections.Generic.Dictionary<string, global::Fdp.Toolkit.Blueprints.EventHandlerDelegate>(global::System.StringComparer.Ordinal)");
            WriteLine("{");
            Indent();
            foreach (var evtGraph in eventHandlers)
                WriteLine($"[\"{evtGraph.Name}\"] = {className}.Event_{evtGraph.Name}_Thunk,");
            Outdent();
            WriteLine("},");
        }
        Outdent();
        WriteLine("});");
    }
}
```

**Key design rules for `CSharpEmitter`:**
- `WriteLine` always prepends current indent level (4 spaces per level) then appends `\n`
- `_currentLine` is incremented once per `\n` emitted (either via `WriteLine` or embedded in `Write`)
- `EmitNodeStart`/`EmitNodeEnd` called per statement around debug annotations

---

## Step 3 — `DebugMapBuilder.cs` (extend existing)

The existing `DebugMapBuilder` has `Record(nodeId, graphId, startLine, endLine)`. Add the incremental track-open/track-close pattern that `CSharpEmitter` uses:

```csharp
// Add to existing DebugMapBuilder class:
private readonly Dictionary<Guid, (Guid GraphId, int StartLine)> _openNodes = new();

public void RecordNodeStart(Guid nodeId, Guid graphId, int line)
{
    _openNodes.TryAdd(nodeId, (graphId, line));
}

public void RecordNodeEnd(Guid nodeId, int line)
{
    if (!_openNodes.Remove(nodeId, out var info)) return;
    Record(nodeId, info.GraphId, info.StartLine, line);
}
```

Keep the existing `Record` and `Build` methods unchanged.

---

## Step 4 — `BlockEmitter.cs`

```csharp
namespace Hrot.Blueprints.Core.Compiler.Emit;

internal static class BlockEmitter
{
    /// <summary>
    /// Emits a single IrBlock as C# code.
    /// If isEntry is true, no label is emitted (entry block).
    /// If isLabelTarget is true, the block gets a goto label.
    /// </summary>
    public static void Emit(CSharpEmitter e, IrBlock block, bool isEntry)
    {
        if (!isEntry)
            e.WriteLine($"__block_{block.Label}:");

        e.WriteLine("{");
        e.Indent();
        foreach (var stmt in block.Statements)
            StatementEmitter.Emit(e, stmt);
        TerminatorEmitter.Emit(e, block.Terminator, e.Ctx);
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine();
    }
}
```

---

## Step 5 — `StatementEmitter.cs`

Implements the full dispatch over all `IrOperation` subtypes per Compiler DD §10.7.

Also handles the Stage 6 lowering ops that appear post-lowering:
- `IrOp_WriteWorkingStatePhase` → `ws.__phase = N;`
- `IrOp_ReadWorkingStatePhase` → `byte __tN = ws.__phase;`
- `IrOp_WriteWorkingStateWaitUntilTime` → `ws.__waitUntilTime = __tN;`
- `IrOp_ReadWorkingStateWaitUntilTime` → `float __tN = ws.__waitUntilTime;`
- `IrOp_WriteCursorResumeAt` → `s.Cursor.ResumeAt = N;`
- `IrOp_ReadCursorResumeAt` → `uint __tN = s.Cursor.ResumeAt;`
- `IrOp_WriteCursorInstanceVersion` → `s.Cursor.InstanceVersion = instanceVersion;`
- `IrOp_WriteCursorWaitUntilTime` → `s.Cursor.WaitUntilTime = __tN;`
- `IrOp_FieldRead` → `var __tN = __tSource.FieldName;` or typed if result type is known
- `IrOp_CheckCursorVersion` → multi-line staleness check block (see below)
- `IrOp_ReadInstanceVersion` → `uint __tN = instanceVersion;`

**`IrOp_CheckCursorVersion` lowers to:**
```csharp
if (s.Cursor.InstanceVersion != instanceVersion)
{
    s.Cursor.ResumeAt = 0;
    return;
}
```
This is a void statement (no ResultValue). It emits an inline staleness guard in the resume block.

**Full TypeRefToCSharp helper:**
```csharp
private static string TypeRefToCSharp(IrTypeRef t)
{
    if (t.IsArray)
        return TypeRefToCSharp(t.ElementType!) + "[]";
    return t.FullName switch
    {
        "System.Boolean"       => "bool",
        "System.Byte"          => "byte",
        "System.Int16"         => "short",
        "System.Int32"         => "int",
        "System.Int64"         => "long",
        "System.UInt32"        => "uint",
        "System.Single"        => "float",
        "System.Double"        => "double",
        "Fdp.Core.Entity"      => "global::Fdp.Core.Entity",
        _                      => $"global::{t.FullName}",
    };
}
```

For `IrOp_WaitForChannel`, `IrOp_WaitForEvent`, `IrOp_LatentDelay`: these should NOT reach the emitter after Stage 6 lowering. Emit `throw new InvalidOperationException("latent op reached Stage 7; should have been lowered in Stage 6")` if encountered.

---

## Step 6 — `TerminatorEmitter.cs`

Per Compiler DD §10.8:

```csharp
internal static class TerminatorEmitter
{
    public static void Emit(CSharpEmitter e, IrTerminator term, EmissionContext ctx)
    {
        switch (term)
        {
            case IrTerm_Goto t:
                e.WriteLine($"goto __block_{ctx.LabelForBlock(t.Target)};");
                break;

            case IrTerm_Branch t:
                e.WriteLine($"if (__t{t.Condition.Index})");
                e.WriteLine($"    goto __block_{ctx.LabelForBlock(t.IfTrue)};");
                e.WriteLine("else");
                e.WriteLine($"    goto __block_{ctx.LabelForBlock(t.IfFalse)};");
                break;

            case IrTerm_Return t:
                if (t.Value.HasValue)
                    e.WriteLine($"return __t{t.Value.Value.Index};");
                else
                    e.WriteLine("return;");
                break;

            case IrTerm_ReturnStatus t:
                e.WriteLine($"return global::Fdp.Toolkit.Blueprints.NodeStatus.{t.Status};");
                break;

            case IrTerm_Suspend:
                throw new InvalidOperationException(
                    "IrTerm_Suspend reached Emit stage; should have been lowered in Stage 6.");

            case IrTerm_FallThrough:
                // nothing; next block emitted sequentially
                break;

            default:
                throw new NotSupportedException(
                    $"Unsupported IrTerminator in Emit: {term.GetType().Name}");
        }
    }
}
```

---

## Step 7 — `ChannelCommandLowering.cs`

Per Compiler DD §10.9:

```csharp
internal static class ChannelCommandLowering
{
    public static void Emit(CSharpEmitter e, IrOp_ChannelCommand op)
    {
        var n = e.Ctx.NextLocalCounter("ch");
        var worldVar = DetermineWorldVar(e.Ctx.Asset.Dispatch);

        e.WriteLine($"ref var __ch_{n} = ref {worldVar}.GetComponentRW<global::{op.ChannelComponentTypeFqn}>(self);");
        e.WriteLine($"__ch_{n}.ActiveAction = {op.ActionIdConstantName};");
        if (op.ParamFields.Count > 0)
        {
            e.WriteLine("unsafe");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"fixed (byte* __paramSlot_{n} = __ch_{n}.Params)");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"*(global::{op.ParamsStructTypeFqn}*)__paramSlot_{n} = new global::{op.ParamsStructTypeFqn}");
            e.WriteLine("{");
            e.Indent();
            for (int i = 0; i < op.ParamFields.Count; i++)
            {
                var f = op.ParamFields[i];
                var sep = i == op.ParamFields.Count - 1 ? "" : ",";
                e.WriteLine($"{f.FieldName} = __t{f.Value.Index}{sep}");
            }
            e.Outdent();
            e.WriteLine("};");
            e.Outdent();
            e.WriteLine("}");
            e.Outdent();
            e.WriteLine("}");
        }
        e.WriteLine($"__ch_{n}.ActionInstanceId++;");
    }

    private static string DetermineWorldVar(BlueprintDispatchKind dispatch)
        => dispatch == BlueprintDispatchKind.AiPrimitive ? "world" : "((global::Fdp.Core.EntityRepository)view)";
}
```

---

## Step 8 — `LibraryEmitter.cs`

Per Compiler DD §10.3. Class name: `{SanitizedName}_{BlueprintId:X8}_Bp` (Q-18.4).

```csharp
internal static class LibraryEmitter
{
    public static void EmitClass(CSharpEmitter e, IrAsset asset)
    {
        var className = $"{asset.SanitizedName}_{asset.BlueprintId:X8}_Bp";

        e.WriteLine("namespace Hrot.AI.Behaviors.Generated;");
        e.WriteLine();
        e.WriteLine($"public static class {className}");
        e.WriteLine("{");
        e.Indent();

        e.WriteLine($"public const int BlueprintId = unchecked((int)0x{asset.BlueprintId:X8});");
        e.WriteLine();

        // One method per function graph
        foreach (var graph in asset.Graphs.Where(g => g.Kind == IrGraphKind.Function))
        {
            EmitFunctionGraph(e, asset, graph, className);
            e.WriteLine();
        }

        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitFunctionGraph(CSharpEmitter e, IrAsset asset, IrGraph graph, string className)
    {
        // Return type: from graph Outputs list (first output, or void)
        var returnType = graph.Outputs.Count > 0
            ? TypeRefToCSharpPublic(graph.Outputs[0].Type)
            : "void";

        // Parameters: from graph Inputs
        var paramList = string.Join(", ",
            graph.Inputs.Select(f => $"{TypeRefToCSharpPublic(f.Type)} {f.Name}"));

        e.WriteLine($"public static {returnType} {graph.Name}({paramList})");
        e.WriteLine("{");
        e.Indent();

        EmitGraphBody(e, asset, graph);

        e.Outdent();
        e.WriteLine("}");
    }

    private static string TypeRefToCSharpPublic(IrTypeRef t)
    {
        // Delegate to StatementEmitter's helper via a shared utility
        return t.FullName switch
        {
            "System.Void"  => "void",
            "System.Int32" => "int",
            "System.Single" => "float",
            "System.Boolean" => "bool",
            _ => $"global::{t.FullName}"
        };
    }

    // Emit the block-by-block body for a graph
    internal static void EmitGraphBody(CSharpEmitter e, IrAsset asset, IrGraph graph)
    {
        for (int i = 0; i < graph.Blocks.Count; i++)
        {
            var block = graph.Blocks[i];
            bool isEntry = block.Id == graph.Entry;
            BlockEmitter.Emit(e, block, isEntry);
        }
    }
}
```

---

## Step 9 — `AiPrimitiveEmitter.cs`

Per Compiler DD §10.4, Q-18.4.

Class name: `{SanitizedName}_{BlueprintId:X8}_Bp`

```csharp
internal static class AiPrimitiveEmitter
{
    public static void EmitClass(CSharpEmitter e, IrAsset asset)
    {
        var className = $"{asset.SanitizedName}_{asset.BlueprintId:X8}_Bp";

        e.WriteLine("namespace Hrot.AI.Behaviors.Generated;");
        e.WriteLine();
        e.WriteLine($"public static class {className}");
        e.WriteLine("{");
        e.Indent();

        e.WriteLine($"public const int BlueprintId = unchecked((int)0x{asset.BlueprintId:X8});");
        e.WriteLine($"public const ulong StructureHash = {asset.StructureHash}UL;");
        e.WriteLine();

        // Params struct
        EmitParamsStruct(e, asset);
        e.WriteLine();

        // WorkingState struct
        EmitWorkingStateStruct(e, asset);
        e.WriteLine();

        // InitDefaultWorkingState
        EmitInitDefault(e, asset, className);
        e.WriteLine();

        // TickCore method
        EmitTickCore(e, asset, className);
        e.WriteLine();

        // Thunk methods for each hosting
        EmitThunks(e, asset, className);

        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitParamsStruct(CSharpEmitter e, IrAsset asset)
    {
        e.WriteLine("[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
        e.WriteLine("public struct Params");
        e.WriteLine("{");
        e.Indent();
        foreach (var f in asset.Parameters)
            e.WriteLine($"public {CSharpType(f.Type)} {f.Name};");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitWorkingStateStruct(CSharpEmitter e, IrAsset asset)
    {
        e.WriteLine("[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
        e.WriteLine("public struct WorkingState");
        e.WriteLine("{");
        e.Indent();
        foreach (var f in asset.WorkingState)
            e.WriteLine($"public {CSharpType(f.Type)} {f.Name};");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitInitDefault(CSharpEmitter e, IrAsset asset, string className)
    {
        e.WriteLine($"private static unsafe void InitDefaultWorkingState(WorkingState* dst)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("*dst = default;");
        foreach (var f in asset.WorkingState.Where(f => !string.IsNullOrEmpty(f.DefaultValueCSharp) && f.DefaultValueCSharp != "0" && f.DefaultValueCSharp != "default"))
            e.WriteLine($"dst->{f.Name} = {f.DefaultValueCSharp};");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitTickCore(CSharpEmitter e, IrAsset asset, string className)
    {
        e.WriteLine("public static global::Fdp.Toolkit.Blueprints.NodeStatus TickCore(");
        e.Indent();
        e.WriteLine("ref Params p,");
        e.WriteLine("ref WorkingState ws,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        e.WriteLine("global::Fdp.Core.EntityRepository world,");
        e.WriteLine("float time)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();

        // Emit all graphs (there should be exactly one AiPrimitiveMain graph)
        var mainGraph = asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.AiPrimitiveMain)
            ?? asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.Function);

        if (mainGraph != null)
            LibraryEmitter.EmitGraphBody(e, asset, mainGraph);
        else
            e.WriteLine("return global::Fdp.Toolkit.Blueprints.NodeStatus.Failure;");

        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitThunks(CSharpEmitter e, IrAsset asset, string className)
    {
        foreach (var hosting in asset.Hostings)
        {
            switch (hosting)
            {
                case AiPrimitiveHosting.BTreeAction:
                    EmitBTreeActionThunk(e, className);
                    e.WriteLine();
                    break;
                case AiPrimitiveHosting.BTreeCondition:
                    EmitBTreeConditionThunk(e, className);
                    e.WriteLine();
                    break;
                case AiPrimitiveHosting.HsmAction:
                    EmitHsmActivityThunk(e, className);
                    e.WriteLine();
                    break;
                case AiPrimitiveHosting.HsmGuard:
                    EmitHsmGuardThunk(e, className);
                    e.WriteLine();
                    break;
                case AiPrimitiveHosting.BlueprintCall:
                    EmitBlueprintCallThunk(e, className);
                    e.WriteLine();
                    break;
            }
        }
    }

    private static void EmitBTreeActionThunk(CSharpEmitter e, string className)
    {
        e.WriteLine("public static global::Fdp.Toolkit.Blueprints.NodeStatus BTreeTick(");
        e.Indent();
        e.WriteLine("ref global::Fdp.Toolkit.Behavior.BrainBlackboard bb,");
        e.WriteLine("ref global::Fdp.Toolkit.Behavior.BehaviorTreeState state,");
        e.WriteLine("ref global::Fdp.Toolkit.Behavior.BTreeContext ctx,");
        e.WriteLine("int paramIndex)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"ref var p = ref global::System.Runtime.CompilerServices.Unsafe.As<byte, Params>(");
        e.WriteLine($"    ref bb.BehaviorParameters[paramIndex * global::System.Runtime.CompilerServices.Unsafe.SizeOf<Params>()]);");
        e.WriteLine("ref var bb1024 = ref ctx.World.GetComponentRW<global::Fdp.Toolkit.Blueprints.Blackboard1024>(ctx.Self);");
        e.WriteLine("unsafe");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("fixed (byte* memory = bb1024.Memory)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("ulong storedHash = *(ulong*)memory;");
        e.WriteLine($"if (storedHash != StructureHash)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"global::System.Runtime.CompilerServices.Unsafe.InitBlock(memory, 0, (uint)global::System.Runtime.CompilerServices.Unsafe.SizeOf<global::Fdp.Toolkit.Blueprints.Blackboard1024>());");
        e.WriteLine("*(ulong*)memory = StructureHash;");
        e.WriteLine("InitDefaultWorkingState((WorkingState*)(memory + 8));");
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine("ref var ws = ref global::System.Runtime.CompilerServices.Unsafe.AsRef<WorkingState>(memory + 8);");
        e.WriteLine("return TickCore(ref p, ref ws, ctx.Self, ctx.World, ctx.World.Time);");
        e.Outdent();
        e.WriteLine("}");
        e.Outdent();
        e.WriteLine("}");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitBTreeConditionThunk(CSharpEmitter e, string className)
    {
        // Same as BTreeTick but method name is BTreeEvaluate and returns bool
        e.WriteLine("public static bool BTreeEvaluate(");
        e.Indent();
        e.WriteLine("ref global::Fdp.Toolkit.Behavior.BrainBlackboard bb,");
        e.WriteLine("ref global::Fdp.Toolkit.Behavior.BehaviorTreeState state,");
        e.WriteLine("ref global::Fdp.Toolkit.Behavior.BTreeContext ctx,");
        e.WriteLine("int paramIndex)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"ref var p = ref global::System.Runtime.CompilerServices.Unsafe.As<byte, Params>(");
        e.WriteLine($"    ref bb.BehaviorParameters[paramIndex * global::System.Runtime.CompilerServices.Unsafe.SizeOf<Params>()]);");
        e.WriteLine("ref var bb1024 = ref ctx.World.GetComponentRW<global::Fdp.Toolkit.Blueprints.Blackboard1024>(ctx.Self);");
        e.WriteLine("unsafe");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("fixed (byte* memory = bb1024.Memory)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("ulong storedHash = *(ulong*)memory;");
        e.WriteLine("if (storedHash != StructureHash)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("global::System.Runtime.CompilerServices.Unsafe.InitBlock(memory, 0, (uint)global::System.Runtime.CompilerServices.Unsafe.SizeOf<global::Fdp.Toolkit.Blueprints.Blackboard1024>());");
        e.WriteLine("*(ulong*)memory = StructureHash;");
        e.WriteLine("InitDefaultWorkingState((WorkingState*)(memory + 8));");
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine("ref var ws = ref global::System.Runtime.CompilerServices.Unsafe.AsRef<WorkingState>(memory + 8);");
        e.WriteLine("return TickCore(ref p, ref ws, ctx.Self, ctx.World, ctx.World.Time) == global::Fdp.Toolkit.Blueprints.NodeStatus.Success;");
        e.Outdent();
        e.WriteLine("}");
        e.Outdent();
        e.WriteLine("}");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitHsmActivityThunk(CSharpEmitter e, string className)
    {
        e.WriteLine("[global::System.Runtime.InteropServices.UnmanagedCallersOnly]");
        e.WriteLine("public static unsafe void HsmActivity(void* instance, void* context, void* writer)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("var bridge = (global::FastHSM.HsmKernelBridge*)context;");
        e.WriteLine("var world = (global::Fdp.Core.EntityRepository)global::System.Runtime.InteropServices.GCHandle.FromIntPtr(bridge->WorldHandle).Target!;");
        e.WriteLine("ref var p = ref *(Params*)instance;");
        e.WriteLine("ref var bb1024 = ref world.GetComponentRW<global::Fdp.Toolkit.Blueprints.Blackboard1024>(bridge->Self);");
        e.WriteLine("fixed (byte* memory = bb1024.Memory)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("if (*(ulong*)memory != StructureHash)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("global::System.Runtime.CompilerServices.Unsafe.InitBlock(memory, 0, (uint)global::System.Runtime.CompilerServices.Unsafe.SizeOf<global::Fdp.Toolkit.Blueprints.Blackboard1024>());");
        e.WriteLine("*(ulong*)memory = StructureHash;");
        e.WriteLine("InitDefaultWorkingState((WorkingState*)(memory + 8));");
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine("ref var ws = ref global::System.Runtime.CompilerServices.Unsafe.AsRef<WorkingState>(memory + 8);");
        e.WriteLine("TickCore(ref p, ref ws, bridge->Self, world, world.Time);");
        e.Outdent();
        e.WriteLine("}");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitHsmGuardThunk(CSharpEmitter e, string className)
    {
        e.WriteLine("[global::System.Runtime.InteropServices.UnmanagedCallersOnly]");
        e.WriteLine("public static unsafe bool HsmGuard(void* instance, void* context, ushort eventId)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("var bridge = (global::FastHSM.HsmKernelBridge*)context;");
        e.WriteLine("var world = (global::Fdp.Core.EntityRepository)global::System.Runtime.InteropServices.GCHandle.FromIntPtr(bridge->WorldHandle).Target!;");
        e.WriteLine("ref var p = ref *(Params*)instance;");
        e.WriteLine("ref var bb1024 = ref world.GetComponentRW<global::Fdp.Toolkit.Blueprints.Blackboard1024>(bridge->Self);");
        e.WriteLine("fixed (byte* memory = bb1024.Memory)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("if (*(ulong*)memory != StructureHash)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("global::System.Runtime.CompilerServices.Unsafe.InitBlock(memory, 0, (uint)global::System.Runtime.CompilerServices.Unsafe.SizeOf<global::Fdp.Toolkit.Blueprints.Blackboard1024>());");
        e.WriteLine("*(ulong*)memory = StructureHash;");
        e.WriteLine("InitDefaultWorkingState((WorkingState*)(memory + 8));");
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine("ref var ws = ref global::System.Runtime.CompilerServices.Unsafe.AsRef<WorkingState>(memory + 8);");
        e.WriteLine("return TickCore(ref p, ref ws, bridge->Self, world, world.Time) == global::Fdp.Toolkit.Blueprints.NodeStatus.Success;");
        e.Outdent();
        e.WriteLine("}");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitBlueprintCallThunk(CSharpEmitter e, string className)
    {
        e.WriteLine("public static global::Fdp.Toolkit.Blueprints.NodeStatus Call(");
        e.Indent();
        e.WriteLine("ref Params p,");
        e.WriteLine("ref WorkingState ws,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        e.WriteLine("global::Fdp.Core.EntityRepository world,");
        e.WriteLine("float time)");
        e.Outdent();
        e.WriteLine("    => TickCore(ref p, ref ws, self, world, time);");
    }

    private static string CSharpType(IrTypeRef t) =>
        t.FullName switch
        {
            "System.Byte"    => "byte",
            "System.Int32"   => "int",
            "System.Single"  => "float",
            "System.Double"  => "double",
            "System.Boolean" => "bool",
            "Fdp.Core.Entity" => "global::Fdp.Core.Entity",
            _ => $"global::{t.FullName}",
        };
}
```

---

## Step 10 — `InstanceEmitter.cs`

Per Compiler DD §10.5, Q-18.1 (`uint instanceVersion` in Tick signature), Q-18.3 (`float deltaTime` in Event methods).

```csharp
internal static class InstanceEmitter
{
    public static void EmitClass(CSharpEmitter e, IrAsset asset)
    {
        var className = $"{asset.SanitizedName}_{asset.BlueprintId:X8}_Bp";

        e.WriteLine("namespace Hrot.AI.Behaviors.Generated;");
        e.WriteLine();
        e.WriteLine($"public static class {className}");
        e.WriteLine("{");
        e.Indent();

        e.WriteLine($"public const int BlueprintId = unchecked((int)0x{asset.BlueprintId:X8});");
        e.WriteLine($"public const ulong StructureHash = {asset.StructureHash}UL;");
        e.WriteLine();

        // State struct
        EmitStateStruct(e, asset);
        e.WriteLine();

        // VarIds class
        EmitVarIds(e, asset);
        e.WriteLine();

        // StateSize
        e.WriteLine("public static int StateSize => global::System.Runtime.CompilerServices.Unsafe.SizeOf<State>();");
        e.WriteLine();

        // InitDefault
        EmitInitDefault(e, asset, className);
        e.WriteLine();

        // Event methods (per Event graphs)
        foreach (var evtGraph in asset.Graphs.Where(g => g.Kind == IrGraphKind.Event))
        {
            EmitEventMethod(e, asset, evtGraph, className);
            e.WriteLine();
        }

        // Tick method (always present)
        EmitTickMethod(e, asset, className);
        e.WriteLine();

        // TickThunk (to match TickDelegate: includes uint instanceVersion per Q-18.1)
        EmitTickThunk(e, className);
        e.WriteLine();

        // Event thunks
        foreach (var evtGraph in asset.Graphs.Where(g => g.Kind == IrGraphKind.Event))
        {
            EmitEventThunk(e, evtGraph, className);
            e.WriteLine();
        }

        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitStateStruct(CSharpEmitter e, IrAsset asset)
    {
        e.WriteLine("[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
        e.WriteLine("public struct State");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("public global::Fdp.Toolkit.Blueprints.BlueprintLatentCursor Cursor;  // first 16 bytes");
        foreach (var f in asset.Variables)
            e.WriteLine($"public {CSharpType(f.Type)} {f.Name};");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitVarIds(CSharpEmitter e, IrAsset asset)
    {
        e.WriteLine("public static class VarIds");
        e.WriteLine("{");
        e.Indent();
        foreach (var v in asset.Variables)
            e.WriteLine($"public const string {v.Name} = \"{v.Id}\";");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitInitDefault(CSharpEmitter e, IrAsset asset, string className)
    {
        e.WriteLine("public static void InitDefault(global::System.Span<byte> stateBytes)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("ref var s = ref global::System.Runtime.CompilerServices.Unsafe.As<byte, State>(");
        e.WriteLine("    ref global::System.Runtime.InteropServices.MemoryMarshal.GetReference(stateBytes));");
        e.WriteLine("s = default;");
        foreach (var v in asset.Variables.Where(f => !string.IsNullOrEmpty(f.DefaultValueCSharp) && f.DefaultValueCSharp != "0" && f.DefaultValueCSharp != "default"))
            e.WriteLine($"s.{v.Name} = {v.DefaultValueCSharp};");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitEventMethod(CSharpEmitter e, IrAsset asset, IrGraph evtGraph, string className)
    {
        // Q-18.3: includes float deltaTime
        // Additional parameters from catalog/custom event decl (from graph Inputs)
        var extraParams = evtGraph.Inputs.Select(f => $"{CSharpType(f.Type)} {f.Name}");
        var extraParamStr = evtGraph.Inputs.Count > 0 ? ", " + string.Join(", ", extraParams) : "";

        e.WriteLine($"public static void Event_{evtGraph.Name}(");
        e.Indent();
        e.WriteLine("ref State s,");
        e.WriteLine("global::Fdp.Interfaces.ISimulationView view,");
        e.WriteLine("global::Fdp.ModuleHost.Abstractions.IEntityCommandBuffer ecb,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        e.WriteLine("float time,");
        e.WriteLine($"float deltaTime{extraParamStr})");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        LibraryEmitter.EmitGraphBody(e, asset, evtGraph);
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitTickMethod(CSharpEmitter e, IrAsset asset, string className)
    {
        // Q-18.1: includes uint instanceVersion as last parameter
        e.WriteLine("public static void Tick(");
        e.Indent();
        e.WriteLine("ref State s,");
        e.WriteLine("global::Fdp.Interfaces.ISimulationView view,");
        e.WriteLine("global::Fdp.ModuleHost.Abstractions.IEntityCommandBuffer ecb,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        e.WriteLine("float time,");
        e.WriteLine("float deltaTime,");
        e.WriteLine("uint instanceVersion)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();

        // Look for the main tick graph (kind=Function named "Tick" or first Function)
        var tickGraph = asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.Function && g.Name == "Tick")
            ?? asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.Function);

        if (tickGraph != null)
            LibraryEmitter.EmitGraphBody(e, asset, tickGraph);
        // else: empty tick body (valid for instance with no tick graph)

        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitTickThunk(CSharpEmitter e, string className)
    {
        // TickDelegate signature per Q-18.1: includes uint instanceVersion
        e.WriteLine("private static void TickThunk(");
        e.Indent();
        e.WriteLine("global::System.Span<byte> bytes,");
        e.WriteLine("global::Fdp.Interfaces.ISimulationView view,");
        e.WriteLine("global::Fdp.ModuleHost.Abstractions.IEntityCommandBuffer ecb,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        e.WriteLine("float time,");
        e.WriteLine("float deltaTime,");
        e.WriteLine("uint instanceVersion)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("ref var s = ref global::System.Runtime.CompilerServices.Unsafe.As<byte, State>(");
        e.WriteLine("    ref global::System.Runtime.InteropServices.MemoryMarshal.GetReference(bytes));");
        e.WriteLine("Tick(ref s, view, ecb, self, time, deltaTime, instanceVersion);");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitEventThunk(CSharpEmitter e, IrGraph evtGraph, string className)
    {
        // EventHandlerDelegate signature: (Span<byte>, ISimView, IECB, Entity, float, float, ReadOnlySpan<byte>)
        e.WriteLine($"private static void Event_{evtGraph.Name}_Thunk(");
        e.Indent();
        e.WriteLine("global::System.Span<byte> bytes,");
        e.WriteLine("global::Fdp.Interfaces.ISimulationView view,");
        e.WriteLine("global::Fdp.ModuleHost.Abstractions.IEntityCommandBuffer ecb,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        e.WriteLine("float time,");
        e.WriteLine("float deltaTime,");
        e.WriteLine("global::System.ReadOnlySpan<byte> payload)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("ref var s = ref global::System.Runtime.CompilerServices.Unsafe.As<byte, State>(");
        e.WriteLine("    ref global::System.Runtime.InteropServices.MemoryMarshal.GetReference(bytes));");
        // For Slice 1: payload deserialization not yet implemented; just call the event handler with no custom args
        e.WriteLine($"Event_{evtGraph.Name}(ref s, view, ecb, self, time, deltaTime);");
        e.Outdent();
        e.WriteLine("}");
    }

    private static string CSharpType(IrTypeRef t) =>
        t.FullName switch
        {
            "System.Byte"    => "byte",
            "System.Int32"   => "int",
            "System.Single"  => "float",
            "System.Double"  => "double",
            "System.Boolean" => "bool",
            "Fdp.Core.Entity" => "global::Fdp.Core.Entity",
            _ => $"global::{t.FullName}",
        };
}
```

---

## Step 11 — `Stage7_Emit.cs` (wire everything up)

```csharp
internal static class Stage7_Emit
{
    public static (string GeneratedSource, DebugMap DebugMap) Run(
        IrAsset asset, CompilerMode mode, DiagnosticSink sink)
    {
        var ctx = new EmissionContext(asset, mode);
        var emitter = new CSharpEmitter(ctx);
        return emitter.Emit(asset);
    }
}
```

---

## Step 12 — `BlueprintCompiler.cs` (wire Stage 7)

Currently `BlueprintCompiler.cs` ends with:
```csharp
throw new NotImplementedException("Stage 7 not yet implemented (CP-004)");
```

Replace that with:
```csharp
var (generatedSource, debugMap) = Stage7_Emit.Run(irAsset, options.Mode, sink);

if (sink.HasErrors)
    return CompileResult.Failed(sink.Diagnostics.ToList());

return CompileResult.Success(
    generatedSource: generatedSource,
    blueprintId: irAsset.BlueprintId,
    structureHash: irAsset.StructureHash,
    debugMap: debugMap,
    diagnostics: sink.Diagnostics.ToList(),
    canonicalAsset: asset);
```

> **Note:** Check the existing `CompileResult` factory methods in the code to match what already exists. If `CompileResult.Success(...)` doesn't exist, use whatever the correct constructor/factory pattern is.

---

## Step 13 — Tests: `Stage7Tests.cs`

Write tests in `Hrot.Blueprints.Tests/Stage7Tests.cs`. Use `BlueprintAssetBuilder` for asset construction and the real `BlueprintCompiler` pipeline (Stages 1-7).

The tests must run through the full pipeline (compile via `BlueprintCompiler.Compile`) to verify end-to-end Stage 7 output.

### SC1: Library emission structural test
Build a Library asset with one function graph. Run through full pipeline. Assert:
- `result.GeneratedSource` contains `"public static class"` + `SanitizedName_*_Bp`
- Contains `"public const int BlueprintId"`
- Contains the registrar class name `"BlueprintRegistrar_"`
- Contains `"public static void Register(global::Fdp.Toolkit.Blueprints.BlueprintRegistryStaging staging)"`
- Does NOT contain `"BlueprintRegistry registry"` (old signature)

### SC2: AiPrimitive emission test — class name and registrar
Build an AiPrimitive asset with BTreeAction + HsmAction hostings. Run pipeline. Assert:
- Class name contains `"_Bp"` suffix with 8-char hex (Q-18.4) 
- `result.GeneratedSource` contains `"Params"` struct
- `result.GeneratedSource` contains `"WorkingState"` struct
- `result.GeneratedSource` contains `"TickCore"`
- `result.GeneratedSource` contains `"BTreeTick"`
- `result.GeneratedSource` contains `"HsmActivity"`
- Registrar `Register` method has parameter `"BehaviorRegistry behReg"` (has BTree hosting)
- Registrar contains static `"HsmActionDispatcher.RegisterAction"` call (no instance param)
- Does NOT contain `"HsmActionDispatcher hsmDispatcher"` parameter

### SC3: Instance emission test — Tick signature includes instanceVersion
Build an Instance asset with at least one Variable. Run pipeline. Assert:
- `result.GeneratedSource` contains `"State"` struct
- Contains `"BlueprintLatentCursor Cursor"`
- Tick method signature contains `"uint instanceVersion"` (Q-18.1)
- `TickThunk` method contains `"uint instanceVersion"` in its signature
- `result.GeneratedSource` contains `"StateSize"`

### SC4: Determinism test
Compile the same Library asset twice. Assert `result1.GeneratedSource == result2.GeneratedSource`.

### SC5: `IrTerm_Suspend` in lowered IR throws `InvalidOperationException`
Manually construct an `IrAsset` (skip the full pipeline) that has a block with `IrTerm_Suspend` as its terminator. Call `Stage7_Emit.Run` on it and assert it throws `InvalidOperationException` with message containing `"should have been lowered"`.

### SC6: Class name format (Q-18.4)
For an asset named `"MoveToAndFire"` with a specific Guid, run pipeline. Assert the class name in generated source is `"MoveToAndFire_XXXXXXXX_Bp"` where XXXXXXXX is the 8-char hex BlueprintId.

### SC7: Instance with custom event — deltaTime in signature
Build Instance with a custom event named `"OnHit"`. Run pipeline. Assert the generated source contains:
- Method `"Event_OnHit("` 
- With `"float deltaTime"` parameter (Q-18.3)

---

## Implementation Notes

### Handling `IrOp_ReadInputArg`
For Library function graphs, input args are the graph's `Inputs` fields. Emit as `var __tN = arg{index};` or simply forward from method parameters. The method parameter names come from `graph.Inputs[argIndex].Name`.

### Handling unresolved / unknown ops in Stage 6 lowering IR
The following ops from Stage 6 appear in the lowered IR and must be handled in `StatementEmitter`:
- `IrOp_WriteWorkingStatePhase(int)` → `ws.__phase = {N};`
- `IrOp_ReadWorkingStatePhase` → `byte __t{idx} = ws.__phase;`
- `IrOp_WriteWorkingStateWaitUntilTime(IrValue)` → `ws.__waitUntilTime = __t{N};`
- `IrOp_ReadWorkingStateWaitUntilTime` → `float __t{idx} = ws.__waitUntilTime;`
- `IrOp_WriteCursorResumeAt(int)` → `s.Cursor.ResumeAt = {N};`
- `IrOp_ReadCursorResumeAt` → `uint __t{idx} = s.Cursor.ResumeAt;`
- `IrOp_WriteCursorInstanceVersion` → `s.Cursor.InstanceVersion = instanceVersion;`
- `IrOp_WriteCursorWaitUntilTime(IrValue)` → `s.Cursor.WaitUntilTime = __t{N};`
- `IrOp_FieldRead(IrValue source, string fieldName, IrTypeRef type)` → `var __t{idx} = __t{source.Index}.{fieldName};`
- `IrOp_CheckCursorVersion` (no result) → emit the staleness guard block
- `IrOp_ReadInstanceVersion` → `uint __t{idx} = instanceVersion;`

### World variable name
- For AiPrimitive `TickCore`: world variable is named `world` (typed `EntityRepository`)
- For Instance `Tick`: world variable needs to be `(global::Fdp.Core.EntityRepository)view`

### Avoiding compilation failures
The generated source must compile cleanly. To avoid type resolution issues:
- Use `global::` qualifier prefix for all external types
- Avoid using `var` for ref/pointer types — be explicit
- `NodeStatus` enum values: `NodeStatus.Success`, `NodeStatus.Failure`, `NodeStatus.Running`

### `IrOp_PollEngineEvent`
This op appears in the IR for Instance dispatch with engine event graphs. Emit as an event poll loop in the `Tick` method body. For Slice 1, this op can emit a no-op or a simple poll:
```csharp
// Engine event poll for {EventTypeFqn}
var __evts_{n} = view.ReadEvents<global::{EventTypeFqn}>();
for (int __i_{n} = 0; __i_{n} < __evts_{n}.Count; __i_{n}++)
{
    var __e_{n} = __evts_{n}[__i_{n}];
    // target field filter
    Event_{HandlerGraphId.ToString("N")}(ref s, view, ecb, self, time, deltaTime);
}
```
Note: the handler graph ID is stored in the op — look it up in `asset.Graphs` to get the graph name.

---

## Build and Test Commands

After implementation:

```
cd d:\WORK\IOS-IG-SimHost-FDP
dotnet build IOS-IG-SimHost.sln
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj --no-build
```

**Expected baseline preservation:** All 175 existing tests still pass.
**Expected new tests:** 7 new Stage7 tests (SC1-SC7), all passing.
**Expected total:** 182 pass, 3 skip, 0 fail (minimum).

---

## Output Questions for Batch Report

1. What is the exact class name generated for a Library asset named "MathUtils"?
2. Does the AiPrimitive `Register` method contain `BlueprintRegistryStaging` (not `BlueprintRegistry`)?
3. Does the Instance `Tick` method signature include `uint instanceVersion` as the last parameter?
4. List any `IrOperation` subtypes that were NOT handled in `StatementEmitter` and what fallback was used.
5. Were SC1-SC7 all verified by passing tests?
