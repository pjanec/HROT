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
        // BP-57 / Q27-A3 — BEFORE the wait lowering, because the reset statement goes into the
        // graph's CURRENT entry block and WaitLowering repoints Entry at its dispatch block.
        asset = LocalStorage.PromoteSuspendingGraphLocals(asset);

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

        // APPEND (not prepend) __phase. Stage5 already baked IrOp_ReadVariable/WriteVariable indices
        // positionally against the pre-lowering WorkingState list; prepending __phase at index 0 would
        // shift every real field by +1, so Stage7's index->name resolution (EmissionContext.VarFieldName)
        // would emit the WRONG field for every WorkingState access (off-by-one) whenever a graph has BOTH
        // a non-empty WorkingState AND a latent op (WaitForChannel/Delay/... -- the only case that reaches
        // here). Appending keeps real fields at their original indices; __phase is only ever accessed by
        // NAME (`ws.__phase` in StatementEmitter / dispatch), so its position within the struct is
        // immaterial. (Latent-with-empty-WorkingState assets, e.g. ReverseToBaseline, are unaffected --
        // append == prepend when __phase is the only field.)
        return asset with
        {
            WorkingState = asset.WorkingState.Concat(new[] { phaseField }).ToList(),
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

    // ⭐ One predicate, three call sites — see LocalStorage.CanSuspend for why a second copy of this
    // list is the defect shape this programme keeps finding.
    private static bool HasAnyLatentOp(IrGraph graph) => LocalStorage.CanSuspend(graph);
}

