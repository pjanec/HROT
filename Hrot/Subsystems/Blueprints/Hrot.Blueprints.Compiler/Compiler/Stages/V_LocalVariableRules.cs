using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Core.Compiler.Stages;

/// <summary>
/// BP-57 / Q27 — the two rails around function-local variables.
///
/// <para>
/// ⭐ Both are about the same fact from different directions: <b>a local is scoped to a graph, and a
/// macro is not a graph after expansion.</b> <c>BP1664</c> refuses a macro that <i>declares</i> one;
/// <c>BP1669</c> refuses a macro body that <i>references</i> one.
/// </para>
/// </summary>
internal sealed class V_LocalVariableRules : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        ValidateMacroDeclaresNoLocal(asset, ctx);
        ValidateMacroReferencesNoLocal(asset, ctx);
    }

    /// <summary>
    /// <c>BP1664</c> — a <see cref="GraphKind.Macro"/> graph may not declare a local.
    ///
    /// <para>
    /// ⭐ <b>Q27-B: this is an incoherence we report, not a policy we impose.</b> A macro is spliced;
    /// after expansion it does not exist as a graph and its nodes are the host's. A macro-local
    /// therefore has nothing to be scoped to — there is no "this invocation of the macro" for it to
    /// reset per, because there is no invocation at all, only inlined nodes.
    /// </para>
    ///
    /// <para>
    /// 📌 Unreal ships macro locals and they are broken in exactly this way: they land in the host's
    /// scope and never reset per call. ⭐ We refuse the construct they regret rather than reproducing
    /// it and documenting the surprise.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>This code was reserved and unbuildable for six batches</b> — <c>Graph</c> had no
    /// <c>LocalVariables</c> at all, so the rail had nothing to check. It is buildable now because
    /// this batch added the field.
    /// </para>
    /// </summary>
    private static void ValidateMacroDeclaresNoLocal(BlueprintAsset asset, ValidationContext ctx)
    {
        foreach (var macro in asset.Graphs.Where(g => g.Kind == GraphKind.Macro))
        {
            foreach (var local in macro.LocalVariables)
            {
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1664,
                    $"Macro '{macro.Name}' declares the local variable '{local.Name}', which a macro "
                    + "cannot have: a macro is spliced into its call site, so after expansion it is "
                    + "not a graph and its nodes belong to the host — there is nothing for a "
                    + "macro-local to be scoped to. Declare the local on the graph that CALLS this "
                    + "macro, or make it an asset variable if the value must persist.",
                    asset.AssetId, macro.Id));
            }
        }
    }

    /// <summary>
    /// <c>BP1669</c> — a macro body may not reference a local declared by any graph.
    ///
    /// <para>
    /// ⭐⭐ <b>Why refusal rather than cross-host resolution.</b> After splicing, a macro's nodes are
    /// host nodes and see the host's scope — which is what "macros inherit the host's variables"
    /// means and is correct. ⚠ <b>But a macro is called from many hosts.</b> A body referencing a
    /// local would resolve against whichever host it happened to be spliced into: expanding cleanly
    /// in one graph and referencing a non-existent local in another, from one authored macro.
    /// </para>
    ///
    /// <para>
    /// ⛔ <b>The only mechanism that could work across hosts is name matching</b>, because a local's
    /// id belongs to the graph that declared it and a macro cannot hold ids for graphs it has never
    /// seen. ⭐ That is precisely the fallback <c>Stage5.FindLocalIndex</c> refuses to have: an
    /// id-miss degrading into a name match is how a local reference silently becomes an asset-variable
    /// read — the wrong storage, not merely the wrong value. Building a cross-host name resolver to
    /// support this would re-open, as a feature, the hazard the resolution design closes.
    /// </para>
    ///
    /// <para>
    /// ⇒ ⭐ <b>A smaller honest rule beats a resolution scheme nobody can predict.</b> If a macro needs
    /// a scratch value, it has its own body to keep it in; if it needs to share one with the host,
    /// that is an asset variable, and saying so is a better answer than a reference whose meaning
    /// depends on the caller.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Fires on the macro's OWN node, not at the call site — a deliberate divergence.</b>
    /// <c>BP1661</c> reports at the call site because there the BODY is fine and only the call is
    /// wrong (a latent macro is legal; calling it from a Function graph is not), so the call node is
    /// the only thing the designer can act on. Here the body is wrong <b>in every host</b>, the
    /// offending node is one a designer placed and can open, and reporting once at the macro beats
    /// reporting once per call site for a defect that is not the call's fault.
    /// </para>
    /// </summary>
    private static void ValidateMacroReferencesNoLocal(BlueprintAsset asset, ValidationContext ctx)
    {
        // Every local declared anywhere in the asset, by id. ⚠ Includes locals on macro graphs, which
        // BP1664 has already rejected — reporting both is right: the declaration and the reference are
        // two separate things to fix.
        var localsById = new Dictionary<Guid, (string Graph, string Name)>();
        foreach (var graph in asset.Graphs)
            foreach (var local in graph.LocalVariables)
                localsById[local.Id] = (graph.Name, local.Name);

        if (localsById.Count == 0) return;

        foreach (var macro in asset.Graphs.Where(g => g.Kind == GraphKind.Macro))
        {
            foreach (var node in macro.Nodes)
            {
                var rawId = node switch
                {
                    GetVariableNode gv => gv.VariableId,
                    SetVariableNode sv => sv.VariableId,
                    _                  => null,
                };
                if (rawId is null) continue;

                var idStr = rawId.StartsWith("var:", StringComparison.OrdinalIgnoreCase)
                    ? rawId.Substring(4)
                    : rawId;
                if (!Guid.TryParse(idStr, out var id)) continue;
                if (!localsById.TryGetValue(id, out var local)) continue;

                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1669,
                    $"Macro '{macro.Name}' reads or writes the local variable '{local.Name}' declared "
                    + $"by graph '{local.Graph}'. A macro is spliced into every graph that calls it, "
                    + "so a reference to one graph's local would resolve there and be missing "
                    + "everywhere else — the same macro would expand cleanly in one graph and break "
                    + "in another. Use an asset variable if the value must be shared with the caller, "
                    + "or keep the value inside the macro body.",
                    asset.AssetId, macro.Id, node.Id));
            }
        }
    }
}
