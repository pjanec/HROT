using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

internal static class LibraryLowering
{
    public static IrAsset Apply(IrAsset asset, DiagnosticSink sink)
    {
        // Defensive double-check: Library blueprints must not contain latent ops.
        //
        // 📌 BP-82 asked whether this needs narrowing to Function graphs so a latent MACRO body is not
        // flagged. ⭐ It does not, and the reason is worth recording so nobody adds a filter that
        // silences a real error: a Macro graph never reaches the IR at all (Stage 5 skips them,
        // IrGraphKind has no Macro member), so this loop cannot see a macro declaration. A latent node
        // that a macro contributes is flagged where it actually lands — spliced into the Function
        // graph that called it, which is exactly the graph that cannot suspend.
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

        // A Library that exposes nothing at all is an authoring error.
        //
        // ⭐ BP-82 / Q25-C2 — a MACRO LIBRARY exposes something. It declares macros with no call
        // sites of its own; every call site lives in the assets that consume it, and expansion
        // happens there. Such an asset has zero Function graphs by design.
        //
        // ⚠ The count has to be carried on IrAsset because macro graphs never reach the IR: Stage 5
        // skips them and IrGraphKind has no Macro member, so "declares only macros" and "declares
        // nothing" look identical here. Without DeclaredMacroCount this rule rejected the one asset
        // shape the macro feature was built to allow.
        bool exposesFunctions = asset.Graphs.Any(g => g.Kind == IrGraphKind.Function);
        bool exposesMacros    = asset.DeclaredMacroCount > 0;

        if (!exposesFunctions && !exposesMacros)
            sink.Add(Diagnostic.Error(
                DiagnosticCodes.BP5001_LibraryHasNoFunctions,
                "This Function Library declares no Function graphs and no Macro graphs, so it exposes " +
                "nothing to call. Add a Function graph (My Blueprint panel → Functions → +) or a Macro " +
                "(Macros → +), or change the asset's dispatch to Instance if it is meant to run on an " +
                "entity.",
                asset.AssetId));

        return asset;
    }
}
