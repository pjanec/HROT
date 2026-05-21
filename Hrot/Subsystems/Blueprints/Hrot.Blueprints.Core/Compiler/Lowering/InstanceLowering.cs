using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

internal static class InstanceLowering
{
    public static IrAsset Apply(IrAsset asset, DiagnosticSink sink)
    {
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
}

