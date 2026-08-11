using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Core.Compiler.Transform;

/// <summary>
/// The one definition of "this contains a latent node", shared by <c>BP1661</c> (V_MacroCallRules) and
/// by collapse legality (Q26-F).
///
/// <para>
/// ⛔ <b>Do not write a second latent-detection rule.</b> There is exactly one question — <i>can this
/// suspend?</i> — and two copies of it would drift, which is the defect shape this programme keeps
/// finding (BP-69's duplicated resolver, the LinkedToIds mirror, the four projection halves).
/// </para>
/// </summary>
internal static class MacroLatency
{
    /// <summary>The node kinds that suspend. The single source of truth for that list.</summary>
    public static bool IsLatent(Node node) =>
        node is LatentDelayNode or WaitForChannelNode or WaitForEventNode;

    /// <summary>
    /// The first latent node reachable from <paramref name="nodes"/>, following
    /// <see cref="MacroCallNode"/>s into their targets. Null when nothing can suspend.
    ///
    /// <para>
    /// ⚠ Cycle-safe on its own rather than relying on <c>BP1662</c> having already fired — a shared
    /// helper cannot assume the order its callers run in.
    /// </para>
    /// </summary>
    public static Node? FindLatentInNodes(
        IReadOnlyList<Node> nodes, IReadOnlyDictionary<Guid, Graph>? macrosById)
        => FindLatentInNodes(nodes, macrosById, new HashSet<Guid>());

    private static Node? FindLatentInNodes(
        IReadOnlyList<Node> nodes, IReadOnlyDictionary<Guid, Graph>? macrosById, HashSet<Guid> seen)
    {
        foreach (var node in nodes)
            if (IsLatent(node)) return node;

        if (macrosById is null) return null;

        foreach (var call in nodes.OfType<MacroCallNode>())
        {
            if (!Guid.TryParse(call.TargetGraphId, out var targetId)) continue;
            if (!seen.Add(targetId)) continue;
            if (!macrosById.TryGetValue(targetId, out var target)) continue;

            var found = FindLatentInNodes(target.Nodes, macrosById, seen);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>
    /// The macro-rooted form <c>BP1661</c> uses: the first latent node in a macro's body, transitively.
    /// Returns the macro it was found in alongside it, so the diagnostic can name both.
    /// </summary>
    public static (Graph Macro, Node Node)? FindTransitivelyLatentNode(
        Guid macroId, IReadOnlyDictionary<Guid, Graph> macrosById, HashSet<Guid> seen)
    {
        if (!seen.Add(macroId)) return null;
        if (!macrosById.TryGetValue(macroId, out var macro)) return null;

        foreach (var node in macro.Nodes)
            if (IsLatent(node)) return (macro, node);

        foreach (var inner in macro.Nodes.OfType<MacroCallNode>())
        {
            if (!Guid.TryParse(inner.TargetGraphId, out var innerId)) continue;
            var found = FindTransitivelyLatentNode(innerId, macrosById, seen);
            if (found is not null) return found;
        }
        return null;
    }
}
