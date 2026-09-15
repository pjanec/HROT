using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Transform;

namespace Hrot.Blueprints.Core.Compiler.Stages;

/// <summary>
/// BP-81 / Q25-B1 — splices every <see cref="MacroCallNode"/>'s target body into its host graph, then
/// deletes the call. After this stage no macro call exists and no later stage needs to know macros
/// were ever there.
///
/// <para>
/// <b>Where it runs, and why that placement is load-bearing.</b> Between <c>Stage2_Validate</c> and
/// <c>Stage3_Normalize</c> — specifically <b>after</b> Stage 2's <c>if (sink.HasErrors) return</c>
/// gate. Expansion therefore never runs on a graph Stage 2 rejected, which is what lets
/// <c>MacroExpander.Expand</c> ASSUME a resolvable, acyclic target (<c>BP1660</c>/<c>BP1662</c>)
/// instead of defending against null on every rule.
/// </para>
///
/// <para>
/// <b>Fixpoint, not recursion.</b> A macro body containing another macro call is spliced in whole;
/// the clone of the inner call then sits in the host and is picked up next round. So nesting falls
/// out for free and <b>the round counter IS the depth cap</b> — there is no second mechanism.
/// </para>
///
/// <para>
/// ⚠ Only non-<see cref="GraphKind.Macro"/> graphs are expanded. Macro declarations are left exactly
/// as authored: they are never compilation targets (Stage 5 skips them), rewriting them would mutate
/// a declaration shared by every call site, and nesting resolves through the host anyway.
/// </para>
///
/// <para>
/// ⭐ <b>BP-76 / Batch 36: the splice itself no longer lives here.</b> This class is the <i>pass</i> —
/// the fixpoint over an asset, the depth cap, the wholesale mirror rebuild. The one-call splice moved
/// to the public <see cref="MacroExpander"/> so the editor's <c>Expand Node</c> can reach it: this
/// assembly's <c>InternalsVisibleTo</c> does not list <c>.Editor</c>, and a second implementation of
/// five splice rules is exactly the shape <c>BP-69</c> recorded when <c>ResolveCustomEventDecl</c> was
/// duplicated across this boundary and the two copies drifted.
/// </para>
/// </summary>
internal static class Stage2_5_ExpandMacros
{
    /// <summary>
    /// Rounds before <c>BP1665</c>. A genuine macro cycle is caught upstream by <c>BP1662</c>, so this
    /// only ever trips on pathological nesting depth — which is exactly what the message says, and why
    /// pulling BP1662 forward mattered: without it this error would blame depth for a loop.
    /// </summary>
    private const int MaxRounds = 16;

    public static BlueprintAsset Run(BlueprintAsset asset, ValidationContext ctx)
    {
        // Cheap exit for the overwhelmingly common case: no macro call anywhere in the asset.
        if (!asset.Graphs.Any(g => g.Kind != GraphKind.Macro && g.Nodes.OfType<MacroCallNode>().Any()))
            return asset;

        var macroById = asset.Graphs
            .Where(g => g.Kind == GraphKind.Macro)
            .ToDictionary(g => g.Id);

        foreach (var host in asset.Graphs.Where(g => g.Kind != GraphKind.Macro))
        {
            int round = 0;
            while (true)
            {
                var calls = host.Nodes.OfType<MacroCallNode>().ToList();
                if (calls.Count == 0) break;

                if (++round > MaxRounds)
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1665,
                        $"Macro expansion in graph '{host.Name}' exceeded {MaxRounds} rounds. Macro "
                        + "bodies are spliced one nesting level per round, so this means macros are "
                        + "nested more than "
                        + $"{MaxRounds} deep. (A macro call CYCLE is reported separately as BP1662 "
                        + "and is not the cause here.)",
                        asset.AssetId, host.Id));
                    break;
                }

                foreach (var call in calls)
                {
                    // BP1660 guarantees this resolves; if a caller ran this stage without Stage 2's
                    // gate, skip rather than throw -- an unexpanded call is still caught by BP1668.
                    if (!Guid.TryParse(call.TargetGraphId, out var targetId)
                        || !macroById.TryGetValue(targetId, out var macro))
                        continue;

                    MacroExpander.Expand(asset, host, call, macro, ctx.Diagnostics.Add);
                }
            }
        }

        // ⭐ The LinkedToIds mirror is rebuilt WHOLESALE, once, rather than patched at each rewire.
        // It is a denormalised copy of the link list, and the class of defect this programme keeps
        // finding is a denormalised copy that no test compares against its source. A full rebuild is
        // O(links), cannot drift, and cannot be got subtly wrong by a rule that forgot one endpoint.
        foreach (var host in asset.Graphs.Where(g => g.Kind != GraphKind.Macro))
            MacroExpander.RebuildLinkedToIds(host);

        return asset;
    }
}
