using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Emit;

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

        EmitStateStruct(e, asset);
        e.WriteLine();

        EmitVarIds(e, asset);
        e.WriteLine();

        var condMetOps = CollectConditionMetOps(asset);
        if (condMetOps.Count > 0)
        {
            e.WriteLine();
            EmitConditionMetFields(e, condMetOps);
            e.WriteLine();
            EmitInitializePredicates(e, condMetOps);
        }

        var eqsOps = CollectEqsResultOps(asset);
        if (eqsOps.Count > 0)
        {
            e.WriteLine();
            EmitEqsResultPrevStateStructs(e, eqsOps);
            EmitEqsConstFields(e, eqsOps);
        }

        var readEqsOps = CollectReadEqsResultOps(asset);
        if (readEqsOps.Count > 0)
        {
            e.WriteLine();
            EmitReadEqsResultHelpers(e, readEqsOps);
        }

        var scoreDecisionOps = CollectScoreDecisionOps(asset);
        if (scoreDecisionOps.Count > 0)
        {
            e.WriteLine();
            EmitScoreDecisionHelpers(e, scoreDecisionOps);
        }

        var readRankedResultOps = CollectReadRankedResultOps(asset);
        if (readRankedResultOps.Count > 0)
        {
            e.WriteLine();
            EmitReadRankedResultHelpers(e, readRankedResultOps);
        }

        e.WriteLine("public static int StateSize => global::System.Runtime.CompilerServices.Unsafe.SizeOf<State>();");
        e.WriteLine();

        EmitInitDefault(e, asset);
        e.WriteLine();

        foreach (var evtGraph in asset.Graphs.Where(g => g.Kind == IrGraphKind.Event))
        {
            EmitEventMethod(e, asset, evtGraph);
            e.WriteLine();
        }

        EmitTickMethod(e, asset);
        e.WriteLine();

        // Emit private helper methods for each non-Tick Function graph (BATCH-03A).
        var tickGraph = asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.Function && g.Name == "Tick")
            ?? asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.Function);
        foreach (var fg in asset.Graphs.Where(g => g.Kind == IrGraphKind.Function && g != tickGraph))
        {
            EmitInstanceFunctionMethod(e, asset, fg);
            e.WriteLine();
        }

        EmitTickThunk(e);
        e.WriteLine();

        foreach (var evtGraph in asset.Graphs.Where(g => g.Kind == IrGraphKind.Event))
        {
            EmitEventThunk(e, evtGraph);
            e.WriteLine();
        }

        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitStateStruct(CSharpEmitter e, IrAsset asset)
    {
        EmitListWrappers(e, asset.Variables);
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

    /// <summary>
    /// FC-2/LV-1 (Q#19-B, review F4) -- emits the PER-CLASS nested fixed-list wrapper structs for
    /// every list-typed field, deduped per (element, capacity): an `[InlineArray(N)]` buffer +
    /// a `{ int Count; Buffer Items; }` wrapper whose name matches the IrTypeRef's synthesized
    /// `__List_{Elem}_{N}` FullName (TypeRefToCSharp emits `_`-prefixed names bare, so the State
    /// field resolves to THIS nested type). Nested-per-class -- never a top-level shared type --
    /// because the generator emits per `.bp.json` and two blueprints sharing (Elem,N) would
    /// otherwise collide with CS0101 (review F4; the future shared-type migration is a cross-file
    /// Collect() pass that needs no asset changes -- assets never name the wrapper).
    /// </summary>
    internal static void EmitListWrappers(CSharpEmitter e, IReadOnlyList<IrField> fields)
    {
        var emitted = new HashSet<string>();
        foreach (var f in fields)
        {
            var t = f.Type;
            if (t.Capacity <= 0 || t.ElementType is null) continue;
            if (!emitted.Add(t.FullName)) continue;

            string elemCs  = StatementEmitter.TypeRefToCSharp(t.ElementType);
            string bufName = "__Buf" + t.FullName.Substring("__List".Length);
            e.WriteLine($"[global::System.Runtime.CompilerServices.InlineArray({t.Capacity})]");
            e.WriteLine($"public struct {bufName}");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"private {elemCs} _e0;");
            e.Outdent();
            e.WriteLine("}");
            e.WriteLine("[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
            e.WriteLine($"public struct {t.FullName}");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine("public int Count;");
            e.WriteLine($"public {bufName} Items;");
            e.Outdent();
            e.WriteLine("}");
        }
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

    private static void EmitInitDefault(CSharpEmitter e, IrAsset asset)
    {
        e.WriteLine("public static void InitDefault(global::System.Span<byte> stateBytes)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("ref var s = ref global::System.Runtime.CompilerServices.Unsafe.As<byte, State>(");
        e.WriteLine("    ref global::System.Runtime.InteropServices.MemoryMarshal.GetReference(stateBytes));");
        e.WriteLine("s = default;");
        foreach (var v in asset.Variables.Where(f =>
            !string.IsNullOrEmpty(f.DefaultValueCSharp) &&
            f.DefaultValueCSharp != "0" &&
            f.DefaultValueCSharp != "default"))
        {
            e.WriteLine($"s.{v.Name} = {v.DefaultValueCSharp};");
        }
        // FC-2/LV-1 (Q#19-B): declared initial length seeds Count over the already-zeroed slots
        // (preallocation is free for blittable elements -- default(T) is all-zero bytes). This is
        // the PARTIAL init the whole-field DefaultValueCSharp path cannot express (review F2).
        foreach (var v in asset.Variables.Where(f => f.Type.Capacity > 0 && f.Type.InitialLength > 0))
        {
            e.WriteLine($"s.{v.Name}.Count = {v.Type.InitialLength};");
        }
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitEventMethod(CSharpEmitter e, IrAsset asset, IrGraph evtGraph)
    {
        // Extra parameters come from graph Inputs (event payload fields).
        var extraParams = evtGraph.Inputs.Select(f => $"{CSharpType(f.Type)} {f.Name}");
        var extraParamStr = evtGraph.Inputs.Count > 0 ? ", " + string.Join(", ", extraParams) : "";

        e.WriteLine($"public static void Event_{evtGraph.Name}(");
        e.Indent();
        e.WriteLine("ref State s,");
        e.WriteLine("global::Fdp.ModuleHost.Abstractions.ISimulationView view,");
        e.WriteLine("global::Fdp.Interfaces.IEntityCommandBuffer ecb,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        e.WriteLine($"float time{extraParamStr})");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        LibraryEmitter.EmitGraphBody(e, asset, evtGraph);
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitTickMethod(CSharpEmitter e, IrAsset asset)
    {
        // Q-18.1: includes uint instanceVersion as last parameter
        e.WriteLine("public static void Tick(");
        e.Indent();
        e.WriteLine("ref State s,");
        e.WriteLine("global::Fdp.ModuleHost.Abstractions.ISimulationView view,");
        e.WriteLine("global::Fdp.Interfaces.IEntityCommandBuffer ecb,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        e.WriteLine("float time,");
        e.WriteLine("float deltaTime,");
        e.WriteLine("uint instanceVersion)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();

        var tickGraph = asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.Function && g.Name == "Tick")
            ?? asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.Function);

        if (tickGraph != null)
        {
            LibraryEmitter.EmitGraphBody(e, asset, tickGraph);
        }

        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitTickThunk(CSharpEmitter e)
    {
        // TickDelegate signature per Q-18.1: includes uint instanceVersion
        e.WriteLine("public static void TickThunk(");
        e.Indent();
        e.WriteLine("global::System.Span<byte> bytes,");
        e.WriteLine("global::Fdp.ModuleHost.Abstractions.ISimulationView view,");
        e.WriteLine("global::Fdp.Interfaces.IEntityCommandBuffer ecb,");
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

    /// <summary>
    /// Emits a private static helper method for an in-blueprint Function graph (BATCH-03A).
    /// Mirrors LibraryEmitter.EmitFunctionGraph but prepends the 7 context parameters
    /// (ref State s, ISimulationView view, IEntityCommandBuffer ecb, Entity self,
    ///  float time, float deltaTime, uint instanceVersion) so that ops like
    /// IrOp_Self/IrOp_Time/IrOp_WriteVariable etc. resolve correctly inside the body.
    /// </summary>
    private static void EmitInstanceFunctionMethod(CSharpEmitter e, IrAsset asset, IrGraph graph)
    {
        // BP-73: N outputs come back as a ValueTuple carrier; 1 output is unchanged.
        // An Instance function graph has no NodeStatus return, hence hasStatusReturn: false.
        var retType = LibraryEmitter.CSharpReturnType(graph, hasStatusReturn: false);

        var sanitized = Sanitizer.SanitizeName(graph.Name);

        // Build the extra input parameters after the 7 context params.
        var extraParams = graph.Inputs.Count > 0
            ? ", " + string.Join(", ", graph.Inputs.Select(f => $"{CSharpType(f.Type)} {f.Name}"))
            : "";

        e.WriteLine($"private static {retType} Func_{sanitized}(");
        e.Indent();
        e.WriteLine("ref State s,");
        e.WriteLine("global::Fdp.ModuleHost.Abstractions.ISimulationView view,");
        e.WriteLine("global::Fdp.Interfaces.IEntityCommandBuffer ecb,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        e.WriteLine("float time,");
        e.WriteLine("float deltaTime,");
        e.WriteLine($"uint instanceVersion{extraParams})");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        LibraryEmitter.EmitGraphBody(e, asset, graph);
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitEventThunk(CSharpEmitter e, IrGraph evtGraph)
    {
        // EventHandlerDelegate signature: (Span<byte>, ISimView, IECB, Entity, float, float, ReadOnlySpan<byte>)
        e.WriteLine($"public static void Event_{evtGraph.Name}_Thunk(");
        e.Indent();
        e.WriteLine("global::System.Span<byte> bytes,");
        e.WriteLine("global::Fdp.ModuleHost.Abstractions.ISimulationView view,");
        e.WriteLine("global::Fdp.Interfaces.IEntityCommandBuffer ecb,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        e.WriteLine("float time,");
        e.WriteLine("float deltaTime,");
        e.WriteLine("global::System.ReadOnlySpan<byte> payload)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("ref var s = ref global::System.Runtime.CompilerServices.Unsafe.As<byte, State>(");
        e.WriteLine("    ref global::System.Runtime.InteropServices.MemoryMarshal.GetReference(bytes));");
        // Q#14: when the Event graph carries an event identity (EventTypeFqn) and has inputs, reinterpret the
        // dispatched payload span as that struct and pass each field to the handler. Otherwise fall back to
        // the legacy default stub (byte-identical for legacy Event graphs with no identity).
        // Q#14 (3d): the Self filter needs the reinterpreted payload even when the handler takes no inputs,
        // so reinterpret __ev whenever we have an event identity AND (payload fields OR a Self filter).
        bool hasFqn      = !string.IsNullOrEmpty(evtGraph.EventTypeFqn);
        bool selfFilter  = evtGraph.TargetFilterSelf && !string.IsNullOrEmpty(evtGraph.TargetFieldName);
        bool reinterpret = hasFqn && (evtGraph.Inputs.Count > 0 || selfFilter);

        string args;
        if (reinterpret)
        {
            e.WriteLine($"ref readonly var __ev = ref global::System.Runtime.CompilerServices.Unsafe.As<byte, global::{evtGraph.EventTypeFqn}>(");
            e.WriteLine("    ref global::System.Runtime.InteropServices.MemoryMarshal.GetReference(payload));");
            // Self/Any: skip this subscriber unless the event's target field names THIS entity.
            if (selfFilter)
                e.WriteLine($"if (__ev.{evtGraph.TargetFieldName} != self) return;");
            args = evtGraph.Inputs.Count > 0
                ? ", " + string.Join(", ", evtGraph.Inputs.Select(f => $"__ev.{f.Name}"))
                : "";
        }
        else
        {
            args = evtGraph.Inputs.Count > 0
                ? ", " + string.Join(", ", evtGraph.Inputs.Select(f => $"default({CSharpType(f.Type)})"))
                : "";
        }
        e.WriteLine($"Event_{evtGraph.Name}(ref s, view, ecb, self, time{args});");
        e.Outdent();
        e.WriteLine("}");
    }

    private static string CSharpType(IrTypeRef t) => StatementEmitter.TypeRefToCSharp(t);

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

    private static void EmitEqsResultPrevStateStructs(CSharpEmitter e, List<IrOp_WhenEqsResultCheck> ops)
    {
        foreach (var op in ops)
        {
            e.WriteLine($"[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
            e.WriteLine($"public struct {op.SynthStructTypeName}");
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

    private static List<IrOp_ReadEqsResult> CollectReadEqsResultOps(IrAsset asset)
    {
        var result = new List<IrOp_ReadEqsResult>();
        var seen   = new HashSet<string>();
        foreach (var graph in asset.Graphs)
        foreach (var block in graph.Blocks)
        foreach (var stmt  in block.Statements)
        {
            if (stmt.Operation is not IrOp_ReadEqsResult op) continue;
            if (!seen.Add(op.NodeId8)) continue;
            result.Add(op);
        }
        return result;
    }

    private static void EmitReadEqsResultHelpers(CSharpEmitter e, List<IrOp_ReadEqsResult> ops)
    {
        foreach (var op in ops)
        {
            // Emit the result struct
            e.WriteLine($"[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
            e.WriteLine($"private struct {op.ResultStructTypeName}");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine("public bool  IsReady;");
            e.WriteLine("public int   ResultCount;");
            e.WriteLine("public global::Fdp.Core.Entity Entity;");
            e.WriteLine("public global::System.Numerics.Vector2 Position;");
            e.WriteLine("public float Score;");
            e.Outdent();
            e.WriteLine("}");
            e.WriteLine();

            // Emit the helper method
            e.WriteLine($"[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            e.WriteLine($"private static {op.ResultStructTypeName} ReadEqsResult_{op.NodeId8}(");
            e.Indent();
            e.WriteLine($"ref State s,");
            e.WriteLine($"global::Fdp.ModuleHost.Abstractions.ISimulationView view,");
            e.WriteLine($"int resultIndex)");
            e.Outdent();
            e.WriteLine("{");
            e.Indent();

            e.WriteLine($"var result = default({op.ResultStructTypeName});");
            e.WriteLine();
            e.WriteLine($"ref readonly var handle = ref s.{op.SensorVariableName};");
            e.WriteLine($"if (!view.IsAlive(handle.ChildId))");
            e.Indent();
            e.WriteLine("return result;");
            e.Outdent();
            e.WriteLine();
            e.WriteLine($"if (!view.HasComponent<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(handle.ChildId))");
            e.Indent();
            e.WriteLine("return result;");
            e.Outdent();
            e.WriteLine();
            e.WriteLine($"ref readonly var buffer = ref view.GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(handle.ChildId);");
            e.WriteLine($"if (!buffer.IsReady)");
            e.Indent();
            e.WriteLine("return result;");
            e.Outdent();
            e.WriteLine();
            e.WriteLine("var results = buffer.GetSpanRO();");
            e.WriteLine("result.IsReady = true;");
            e.WriteLine("result.ResultCount = buffer.Count;");
            e.WriteLine();
            e.WriteLine("if (buffer.Count == 0)");
            e.Indent();
            e.WriteLine("return result;");
            e.Outdent();
            e.WriteLine();
            e.WriteLine("int idx = global::System.Math.Clamp(resultIndex, 0, buffer.Count - 1);");
            e.WriteLine("var picked = results[idx];");
            e.WriteLine("result.Entity   = new global::Fdp.Core.Entity((ulong)picked.EntityId);");
            e.WriteLine("result.Position = new global::System.Numerics.Vector2(picked.PositionX, picked.PositionY);");
            e.WriteLine("result.Score    = picked.Score;");
            e.WriteLine("return result;");

            e.Outdent();
            e.WriteLine("}");
            e.WriteLine();
        }
    }

    private static List<IrOp_ScoreDecision> CollectScoreDecisionOps(IrAsset asset)
    {
        var result = new List<IrOp_ScoreDecision>();
        var seen   = new HashSet<string>();
        foreach (var graph in asset.Graphs)
        foreach (var block in graph.Blocks)
        foreach (var stmt  in block.Statements)
        {
            if (stmt.Operation is not IrOp_ScoreDecision op) continue;
            if (!seen.Add(op.NodeId8)) continue;
            result.Add(op);
        }
        return result;
    }

    private static List<IrOp_ReadRankedResult> CollectReadRankedResultOps(IrAsset asset)
    {
        var result = new List<IrOp_ReadRankedResult>();
        var seen   = new HashSet<string>();
        foreach (var graph in asset.Graphs)
        foreach (var block in graph.Blocks)
        foreach (var stmt  in block.Statements)
        {
            if (stmt.Operation is not IrOp_ReadRankedResult op) continue;
            if (!seen.Add(op.NodeId8)) continue;
            result.Add(op);
        }
        return result;
    }

    private static void EmitScoreDecisionHelpers(CSharpEmitter e, List<IrOp_ScoreDecision> ops)
    {
        foreach (var op in ops)
        {
            e.WriteLine($"[global::System.Runtime.CompilerServices.MethodImpl(" +
                        $"global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            e.WriteLine($"private static byte ScoreDecision_{op.NodeId8}(");
            e.Indent();
            e.WriteLine($"global::Fdp.ModuleHost.Abstractions.ISimulationView view,");
            e.WriteLine($"global::Fdp.Core.Entity self,");
            e.WriteLine($"float time)");
            e.Outdent();
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"uint tick = (uint)(time * 60f);");
            e.WriteLine($"return global::Fdp.Toolkit.Utility.Integration.UtilityBlueprintBridge" +
                        $".ScoreDecision(view, self, {op.DecisionIdLiteral}, tick);");
            e.Outdent();
            e.WriteLine("}");
            e.WriteLine();
        }
    }

    private static void EmitReadRankedResultHelpers(CSharpEmitter e, List<IrOp_ReadRankedResult> ops)
    {
        foreach (var op in ops)
        {
            // Emit the result struct
            e.WriteLine($"[global::System.Runtime.InteropServices.StructLayout(" +
                        $"global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
            e.WriteLine($"private struct {op.ResultStructTypeName}");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine("public bool  IsValid;");
            e.WriteLine("public long  Entity;");
            e.WriteLine("public float Score;");
            e.Outdent();
            e.WriteLine("}");
            e.WriteLine();

            // Emit the helper method
            e.WriteLine($"[global::System.Runtime.CompilerServices.MethodImpl(" +
                        $"global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            e.WriteLine($"private static {op.ResultStructTypeName} ReadRankedResult_{op.NodeId8}(");
            e.Indent();
            e.WriteLine($"global::Fdp.ModuleHost.Abstractions.ISimulationView view,");
            e.WriteLine($"global::Fdp.Core.Entity self)");
            e.Outdent();
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"var result = default({op.ResultStructTypeName});");
            e.WriteLine($"var (handle, score, isValid) = " +
                        $"global::Fdp.Toolkit.Utility.Integration.UtilityBlueprintBridge" +
                        $".ReadRankedResult(view, self, {op.RankLiteral});");
            e.WriteLine("result.IsValid = isValid;");
            e.WriteLine("result.Entity  = handle;");
            e.WriteLine("result.Score   = score;");
            e.WriteLine("return result;");
            e.Outdent();
            e.WriteLine("}");
            e.WriteLine();
        }
    }
}
