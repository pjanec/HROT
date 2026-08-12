using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Emit;

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

        foreach (var graph in asset.Graphs.Where(g => g.Kind == IrGraphKind.Function))
        {
            EmitFunctionGraph(e, asset, graph);
            e.WriteLine();
        }

        e.Outdent();
        e.WriteLine("}");
    }

    /// <summary>
    /// BP-73: the C# return type for a function graph with <b>N</b> outputs — the single shared
    /// authority for all three method-DECLARATION sites (this file, <c>InstanceEmitter</c>, and
    /// <c>CSharpEmitter</c>'s library adapter), which each used to read <c>Outputs[0]</c> and silently
    /// drop the rest.
    /// <list type="bullet">
    /// <item>0 outputs → <c>void</c> (or <c>NodeStatus</c> when the graph returns a status).</item>
    /// <item>1 output → that type, <b>byte-identical to pre-BP-73 output</b>.</item>
    /// <item>N outputs → an <b>unnamed</b> ValueTuple, e.g. <c>(float, bool)</c>.</item>
    /// </list>
    /// ⚠ Unnamed on purpose. Named tuple elements would read better, but they inherit two failure
    /// modes this avoids entirely: an output whose name is not a valid C# identifier, and the
    /// positional <c>ItemN</c> collision rule (an element literally named <c>Item2</c> is illegal
    /// anywhere but position 2). <c>IrOp_TupleField</c> reads elements positionally regardless, so
    /// names would buy nothing the generated code actually uses.
    /// </summary>
    internal static string CSharpReturnType(IrGraph graph, bool hasStatusReturn)
        => graph.Outputs.Count switch
        {
            0 => hasStatusReturn ? "global::Fbt.NodeStatus" : "void",
            1 => CSharpType(graph.Outputs[0].Type),
            _ => "(" + string.Join(", ", graph.Outputs.Select(o => CSharpType(o.Type))) + ")",
        };

    /// <summary>
    /// BP-221 / BP-222 — the return type of an in-blueprint <c>Func_*</c> helper, for BOTH the
    /// declaration and the call site.
    ///
    /// <para>
    /// ⭐ <b>One function, called from three places, on purpose.</b> The two emitters declared the
    /// helper and <c>StatementEmitter</c> emitted the call, each deciding independently whether the
    /// call produces a value — and they disagreed in both directions: a zero-output graph was
    /// declared <c>void</c> and assigned anyway (<c>CS0815</c>, BP-222), and an AiPrimitive graph
    /// whose body ends in a status return was declared <c>void</c> and then returned a value
    /// (<c>CS0127</c>). Whichever way the rule moves next, it moves here.
    /// </para>
    ///
    /// <para>
    /// ⚠ The status return is DERIVED from the body, not assumed per dispatch. An Instance function
    /// graph has never carried one, so this is a no-op there; an AiPrimitive graph lowers to
    /// <c>NodeStatus</c> terminators and does.
    /// </para>
    /// </summary>
    internal static string HelperReturnType(IrGraph graph)
        => CSharpReturnType(graph, graph.Blocks.Any(b => b.Terminator is IrTerm_ReturnStatus));

    /// <summary>
    /// BP-57 / Q27-A1+E — declares this graph's function-locals as plain C# locals, initialised from
    /// their declared defaults, at the top of the emitted body.
    ///
    /// <para>
    /// ⭐⭐ <b>Emitted HERE, in the one body-emitter every graph goes through</b> — the Instance
    /// function helper, the AiPrimitive helper, the Library static method and <c>TickCore</c> all call
    /// <see cref="EmitGraphBody"/>. Declaring at each of those sites instead would have been four
    /// copies of one rule, and the fourth would have been missed exactly as <c>BP-221</c>'s helper
    /// loop was.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>The initialiser is what makes "local" mean what it says.</b> Q27-E: reset on entry, so
    /// call N+1 cannot see call N's value. A field written early would behave identically on the first
    /// call and differently on the second — which is precisely the Unreal macro-local wart Q27 exists
    /// to avoid, and why the test for this calls the function TWICE.
    /// </para>
    ///
    /// <para>
    /// ⚠ Names are prefixed <c>__loc_</c>. A designer's local may share a name with a method parameter
    /// (the graph's own inputs are parameters), a C# keyword, or another emitted symbol; the prefix
    /// makes the collision unrepresentable rather than making it someone's later bug report.
    /// </para>
    /// </summary>
    internal static void EmitLocalDeclarations(CSharpEmitter e, IrGraph graph)
    {
        if (graph.Locals.Count == 0) return;

        foreach (var local in graph.Locals)
        {
            var init = string.IsNullOrWhiteSpace(local.DefaultValueCSharp)
                ? "default"
                : local.DefaultValueCSharp;
            e.WriteLine($"{CSharpType(local.Type)} {EmissionContext.LocalName(local.Name)} = {init};");
        }
        e.WriteLine();
    }

    private static void EmitFunctionGraph(CSharpEmitter e, IrAsset asset, IrGraph graph)
    {
        bool hasStatusReturn = graph.Blocks.Any(b => b.Terminator is IrTerm_ReturnStatus);
        var returnType = CSharpReturnType(graph, hasStatusReturn);

        var paramList = string.Join(", ",
            graph.Inputs.Select(f => $"{CSharpType(f.Type)} {f.Name}"));

        e.WriteLine($"public static {returnType} {graph.Name}({paramList})");
        e.WriteLine("{");
        e.Indent();

        EmitGraphBody(e, asset, graph);

        e.Outdent();
        e.WriteLine("}");
    }

    /// <summary>Emits the block-by-block body for a graph. Sets CurrentGraph on context.</summary>
    internal static void EmitGraphBody(CSharpEmitter e, IrAsset asset, IrGraph graph)
    {
        e.Ctx.CurrentGraph = graph;
        EmitLocalDeclarations(e, graph);
        for (int i = 0; i < graph.Blocks.Count; i++)
        {
            var block = graph.Blocks[i];
            bool isEntry = block.Id == graph.Entry;
            BlockEmitter.Emit(e, block, isEntry);
        }
        e.Ctx.CurrentGraph = null;
    }

    internal static string CSharpType(IrTypeRef t) => StatementEmitter.TypeRefToCSharp(t);
}
