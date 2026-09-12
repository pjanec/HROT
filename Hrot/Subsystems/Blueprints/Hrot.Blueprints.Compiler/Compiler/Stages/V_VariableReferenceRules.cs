using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Core.Compiler.Stages;

/// <summary>
/// <c>BP1670</c> — a <see cref="GetVariableNode"/>/<see cref="SetVariableNode"/> whose
/// <c>VariableId</c> resolves to nothing.
///
/// <para>
/// ⚠⚠ <b>Today this compiles clean and emits invalid C#.</b> <c>Stage5.FindLocalIndex</c> misses,
/// <c>FindVariableIndex</c> misses, both return -1, and <c>EmissionContext.VarFieldName(-1)</c> falls
/// through to <c>$"__var_{index}"</c> — literally <c>s.__var_-1 = …</c>. The solution build then breaks
/// with an unintelligible <c>CS</c> error naming a generated file, and no BP diagnostic names the node.
/// </para>
///
/// <para>
/// ⭐ <b>Pre-existing, but BP-57 makes it reachable.</b> Deleting a local leaves every Get/Set that
/// targeted it dangling — <c>BP-225</c> established that delete/rename maintenance is exactly where
/// this lands — so the rail has to exist before the delete gesture does.
/// </para>
///
/// <para>
/// ⛔⛔ <b>Scoped to Get/SetVariableNode, and the scope is load-bearing.</b>
/// <c>GetSharedNode</c>/<c>SetSharedNode</c> also carry a <c>VariableId</c>, but it is a name-keyed
/// shared-state slot resolved at RUNTIME (<c>BlueprintSharedState.TryGetShared</c>) and never passed to
/// <c>FindVariableIndex</c> at all. The shipped corpus holds <b>61</b> such references — the literals
/// <c>"state"</c> and <c>"rally"</c> — and a rail generalised to "any node with a VariableId" would
/// reject six shipped assets on a mechanism that works correctly.
/// </para>
///
/// <para>
/// 📌 <b>Measured before it was built.</b> Across the 58 shipped <c>.bp.json</c> assets, all 103
/// Get/SetVariable references resolve (46 to <c>Variables</c>, 57 to <c>WorkingState</c>, 0 to
/// <c>Parameters</c>); there are no dangling GUIDs and no bare literals on those two node kinds. This
/// rail refuses nothing that ships today.
/// </para>
/// </summary>
internal sealed class V_VariableReferenceRules : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        foreach (var graph in asset.Graphs)
        {
            // ⚠ A Macro graph's Get/Set nodes are validated where they are SPLICED, against the host's
            // scope — the macro's own graph has none. BP1669 already refuses a macro body that reaches
            // for a local; reporting an unresolvable asset reference twice (here and at each call site)
            // would name the same defect once per caller.
            if (graph.Kind == GraphKind.Macro) continue;

            foreach (var node in graph.Nodes)
            {
                var rawId = node switch
                {
                    GetVariableNode gv => gv.VariableId,
                    SetVariableNode sv => sv.VariableId,
                    _                  => null,
                };
                if (rawId is null) continue;
                if (Resolves(rawId, asset, graph)) continue;

                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1670,
                    $"Graph '{graph.Name}' reads or writes a variable '{rawId}' that does not exist. "
                    + "It matches no asset variable, working-state field or parameter, and no local "
                    + "declared by this graph — most often because the variable it pointed at was "
                    + "deleted or renamed away. Retarget the node at an existing variable, or delete "
                    + "it.",
                    asset.AssetId, graph.Id, node.Id));
            }
        }
    }

    /// <summary>
    /// ⭐ <b>Mirrors <c>Stage5</c>'s resolution exactly, in its order</b>: this graph's locals by id
    /// (<c>FindLocalIndex</c> — id only, deliberately), then the asset's three lists by id, then the
    /// same three by NAME.
    ///
    /// <para>
    /// ⚠ The name fallback is not a nicety to drop here: a <c>VariableId</c> is not always a GUID, and
    /// <c>FindVariableIndex</c> resolves a bare name against the asset lists. A rail that only accepted
    /// GUIDs would refuse graphs that compile correctly today.
    /// </para>
    /// </summary>
    private static bool Resolves(string rawId, BlueprintAsset asset, Graph graph)
    {
        var idStr = rawId.StartsWith("var:", StringComparison.OrdinalIgnoreCase)
            ? rawId.Substring(4)
            : rawId;

        if (Guid.TryParse(idStr, out var guid))
        {
            if (graph.LocalVariables.Any(v => v.Id == guid)) return true;
            if (AssetDecls(asset).Any(v => v.Id == guid))    return true;
        }

        return AssetDecls(asset).Any(v => v.Name == idStr);
    }

    /// <summary>
    /// The asset's declarations as (id, name) pairs.
    ///
    /// <para>
    /// <b>U-11.</b> This used to concatenate three projections, with the comment *"<c>Parameters</c> is
    /// a different declaration TYPE from the other two, which is why this projects rather than
    /// concatenating"* — ⭐ <b>which is exactly the difference <c>BlueprintDeclaration</c> removes.</b>
    /// </para>
    ///
    /// <para>
    /// ⚠ <c>Declarations</c> enumerates in STORAGE order rather than the resolution order the three
    /// concats happened to produce. Safe here, and checked: both callers use <c>.Any(…)</c>, so the
    /// sequence is a set membership test and its order is not observable.
    /// </para>
    /// </summary>
    private static IEnumerable<(Guid Id, string Name)> AssetDecls(BlueprintAsset asset)
        => asset.Declarations.Select(d => (d.Id, d.Name));
}
