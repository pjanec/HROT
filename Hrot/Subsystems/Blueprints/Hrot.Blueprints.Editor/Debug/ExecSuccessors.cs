using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Debug;

/// <summary>
/// Computes the immediate exec successor node IDs for a given node
/// by following exec-output links in a <see cref="Graph"/>.
/// Mirrors the compiler's <see cref="Hrot.Blueprints.Core.Compiler.Compiler.Stages.Stage5_Schedule"/>
/// <c>GetSingleExecSuccessor</c>/<c>GetBranchSuccessors</c> pattern.
///
/// Used by CF-6 stepping to determine where to set temporary breakpoints.
/// </summary>
public static class ExecSuccessors
{
    /// <summary>
    /// Returns all immediate exec successor node IDs for <paramref name="nodeId"/>
    /// in <paramref name="graph"/>.  Follows all exec-output pins through links.
    /// </summary>
    /// <returns>
    /// An ordered list of successor node Guids.  Multi-successor nodes (Branch,
    /// When, Sequence) return all immediate successors.  Terminal nodes (Return)
    /// and unlinked exec-out pins return empty.
    /// </returns>
    public static IReadOnlyList<Guid> GetSuccessors(Graph graph, Guid nodeId)
    {
        // 1. Find the node in the graph.
        var node = graph.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node == null)
            return Array.Empty<Guid>();

        // 2. Get all exec-output pins.
        var execOutPins = node.Pins.Where(p => p.IsExec && p.Direction == "Out").ToList();
        if (execOutPins.Count == 0)
            return Array.Empty<Guid>();

        // 3. For each exec-output pin, find the link and collect the target node id.
        var successors = new List<Guid>(execOutPins.Count);
        foreach (var pin in execOutPins)
        {
            var link = graph.Links.FirstOrDefault(
                l => l.FromNodeId == nodeId && l.FromPinId == pin.Id);
            if (link != null)
                successors.Add(link.ToNodeId);
        }

        return successors;
    }
}
