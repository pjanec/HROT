using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

internal static class LibraryLowering
{
    public static IrAsset Apply(IrAsset asset, DiagnosticSink sink)
    {
        // Defensive double-check: Library blueprints must not contain latent ops.
        foreach (var g in asset.Graphs)
            foreach (var b in g.Blocks)
                foreach (var s in b.Statements)
                    if (s.Operation is IrOp_LatentDelay or IrOp_WaitForChannel or IrOp_WaitForEvent)
                        sink.Add(Diagnostic.Error(
                            DiagnosticCodes.BP9001_InternalLibraryLatent,
                            $"A Function Library cannot contain latent nodes such as Delay or Wait For Channel: " +
                            $"its graphs compile to plain static methods, which have nowhere to suspend. " +
                            $"Remove the latent node from graph '{g.Name}', or move this logic into an Event " +
                            $"graph on an Instance blueprint. (Reported during lowering — validation normally " +
                            $"reports this first; please report it if it did not.)",
                            asset.AssetId, g.Id));

        // A Library with no function graphs is an authoring error.
        if (!asset.Graphs.Any(g => g.Kind == IrGraphKind.Function))
            sink.Add(Diagnostic.Error(
                DiagnosticCodes.BP5001_LibraryHasNoFunctions,
                "This Function Library declares no Function graphs, so it exposes nothing to call. " +
                "Add a Function graph (My Blueprint panel → Functions → +), or change the asset's " +
                "dispatch to Instance if it is meant to run on an entity.",
                asset.AssetId));

        return asset;
    }
}
