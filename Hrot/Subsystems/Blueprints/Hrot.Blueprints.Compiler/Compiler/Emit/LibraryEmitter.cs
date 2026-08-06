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
