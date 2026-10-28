// UnsavedCheckAggregator: INodeResponseAggregator for NodeOpType.CheckUnsaved (CE-4093, AQ108 D20).

using System.Collections.Generic;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐⭐ <c>CE-4093</c> (AQ108 D20) — folds the nodes' answers to <c>CheckUnsaved</c> into ONE <see cref="UnsavedCheckResult"/>: the scenario is
/// <i>changed</i> when ANY node answered anything but an explicit "unchanged" — "changed", "no-baseline" (nothing says its world is saved) or an
/// answer it cannot read (<see cref="WorldChangeCheckHandler.MeansChanged"/>: only "unchanged" means saved). ⭐ An EMPTY answer is skipped: a host with
/// no ECS world ACKs the round as NOT participating (the slave's <c>IsParticipating = false</c>), the master stores "" — it has nothing to lose.
/// <para>Returns <see langword="null"/> for a round that has no <c>CheckUnsaved</c> answers at all (any other round — the master runs every
/// aggregator on every round); a check round whose every node was silent gives "unchanged" with no nodes, as a cluster of non-participants has nothing
/// unsaved. The master's own fact (a failed save) is folded in by <see cref="ClusterMaster"/>, not here: only it knows.</para>
/// </summary>
public sealed class UnsavedCheckAggregator : INodeResponseAggregator
{
    /// <inheritdoc/>
    public NodeOpType TargetOp => NodeOpType.CheckUnsaved;

    /// <inheritdoc/>
    public object? Aggregate(IReadOnlyDictionary<int, Dictionary<NodeOpType, string>> nodeResponses)
    {
        bool asked = false;
        var changed = new List<int>();
        foreach (var (node, answers) in nodeResponses)
        {
            if (!answers.TryGetValue(NodeOpType.CheckUnsaved, out var answer)) continue;
            asked = true;
            if (string.IsNullOrWhiteSpace(answer)) continue;   // not participating (no ECS world)
            if (WorldChangeCheckHandler.MeansChanged(answer)) changed.Add(node);
        }
        if (!asked) return null;
        changed.Sort();
        return new UnsavedCheckResult(changed.Count > 0, changed.ToArray());
    }
}
