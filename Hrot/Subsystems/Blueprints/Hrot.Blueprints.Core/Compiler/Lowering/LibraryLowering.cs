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
                            "Library asset contains a latent op (Stage 2 should have caught this)."));

        // A Library with no function graphs is an authoring error.
        if (!asset.Graphs.Any(g => g.Kind == IrGraphKind.Function))
            sink.Add(Diagnostic.Error(
                DiagnosticCodes.BP5001_LibraryHasNoFunctions,
                "Library asset has no function graphs.",
                asset.AssetId));

        return asset;
    }
}
