using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

internal static class InstanceLowering
{
    public static IrAsset Apply(IrAsset asset, DiagnosticSink sink)
    {
        // Add synthesized _when_xxx_prev fields for ValueChanged WhenNodes.
        asset = WhenLowering_Instance.Apply(asset);

        // BP-57 / Q27-A3 — BEFORE the wait lowering, because the reset statement goes into the
        // graph's CURRENT entry block and WaitLowering repoints Entry at its dispatch block.
        asset = LocalStorage.PromoteSuspendingGraphLocals(asset);

        var newGraphs = new List<IrGraph>(asset.Graphs.Count);
        foreach (var graph in asset.Graphs)
            newGraphs.Add(LocalStorage.CanSuspend(graph) ? WaitLowering_Instance.Apply(graph) : graph);
        return asset with { Graphs = newGraphs };
    }
}

