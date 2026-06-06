using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

internal static class AiPrimitiveLowering
{
    private static readonly IrTypeRef ByteType =
        new IrTypeRef { FullName = "System.Byte", IsUnmanaged = true, SizeBytes = 1 };
    private static readonly IrTypeRef SingleType =
        new IrTypeRef { FullName = "System.Single", IsUnmanaged = true, SizeBytes = 4 };

    public static IrAsset Apply(IrAsset asset, DiagnosticSink sink)
    {
        for (int i = 0; i < asset.Graphs.Count; i++)
        {
            var graph = asset.Graphs[i];
            if (!HasAnyLatentOp(graph)) continue;

            // Ensure synthesized fields exist in WorkingState before lowering.
            asset = EnsurePhaseByteInWorkingState(asset);

            bool hasDelay = graph.Blocks
                .SelectMany(b => b.Statements)
                .Any(s => s.Operation is IrOp_LatentDelay);

            if (hasDelay)
                asset = EnsureWaitUntilTimeField(asset);

            // Lower the graph and replace it in the asset.
            var loweredGraph = WaitLowering_AiPrimitive.Apply(graph);
            asset = asset with
            {
                Graphs = asset.Graphs
                    .Select((g, idx) => idx == i ? loweredGraph : g)
                    .ToList(),
            };
        }
        return asset;
    }

    private static IrAsset EnsurePhaseByteInWorkingState(IrAsset asset)
    {
        if (asset.WorkingState.Any(f => f.Name == "__phase")) return asset;

        var phaseField = new IrField
        {
            Id                 = SynthesizedGuids.PhaseField(asset.AssetId),
            Name               = "__phase",
            Type               = ByteType,
            DefaultValueCSharp = "0",
        };

        return asset with
        {
            WorkingState = new[] { phaseField }.Concat(asset.WorkingState).ToList(),
        };
    }

    private static IrAsset EnsureWaitUntilTimeField(IrAsset asset)
    {
        if (asset.WorkingState.Any(f => f.Name == "__waitUntilTime")) return asset;

        var waitField = new IrField
        {
            Id                 = SynthesizedGuids.WaitUntilTimeField(asset.AssetId),
            Name               = "__waitUntilTime",
            Type               = SingleType,
            DefaultValueCSharp = "0f",
        };

        return asset with
        {
            WorkingState = asset.WorkingState.Concat(new[] { waitField }).ToList(),
        };
    }

    private static bool HasAnyLatentOp(IrGraph graph)
        => graph.Blocks
            .SelectMany(b => b.Statements)
            .Any(s => s.Operation is IrOp_LatentDelay or IrOp_WaitForChannel or IrOp_WaitForEvent
                                  or IrOp_InlineActionCall);
}

