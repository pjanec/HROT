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

    private static void EmitFunctionGraph(CSharpEmitter e, IrAsset asset, IrGraph graph)
    {
        bool hasStatusReturn = graph.Blocks.Any(b => b.Terminator is IrTerm_ReturnStatus);
        var returnType = graph.Outputs.Count > 0
            ? CSharpType(graph.Outputs[0].Type)
            : hasStatusReturn ? "global::Hrot.Blueprints.Core.Assets.NodeStatus"
            : "void";

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
