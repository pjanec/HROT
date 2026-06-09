using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;

namespace Hrot.Blueprints.Editor.Debug;

/// <summary>
/// Computes the immediate exec successor node IDs for a given node
/// by following exec-output links in a <see cref="Graph"/>.
/// Mirrors the compiler's <see cref="Hrot.Blueprints.Core.Compiler.Compiler.Stages.Stage5_Schedule"/>
/// <c>GetSingleExecSuccessor</c>/<c>GetBranchSuccessors</c> pattern.
///
/// Handles projection-only nodes (Pins = []) by rehydrating pin schemas from
/// <see cref="BuiltInNodeRegistry"/> and matching outbound link order to exec-out pin order.
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

        // 2. Get exec-output pin schemas (rehydrate if projection-only).
        var pins = node.Pins;
        if (pins.Count == 0)
        {
            // Projection-only node — rehydrate pin schemas from the registry.
            // Match by position: the i-th exec-out pin schema maps to the i-th
            // exec-out link in outbound-link order.
            var schemas = BuiltInNodeRegistry.Instance.GetStaticPins(node);
            var execOutSchemas = schemas.Where(s => s.IsExec && s.Direction == "Out").ToList();
            if (execOutSchemas.Count == 0)
                return Array.Empty<Guid>();

            // Collect all outbound links in occurrence order, keeping only
            // the first N where N = number of exec-out pin schemas.
            var allOutLinks = graph.Links.Where(l => l.FromNodeId == nodeId).ToList();
            var successors = new List<Guid>(execOutSchemas.Count);
            for (int i = 0; i < execOutSchemas.Count && i < allOutLinks.Count; i++)
                successors.Add(allOutLinks[i].ToNodeId);
            return successors;
        }

        // 3. Node has pins — get all exec-output pins.
        var execOutPins = pins.Where(p => p.IsExec && p.Direction == "Out").ToList();
        if (execOutPins.Count == 0)
            return Array.Empty<Guid>();

        // 4. For each exec-output pin, find the link and collect the target node id.
        var result = new List<Guid>(execOutPins.Count);
        foreach (var pin in execOutPins)
        {
            var link = graph.Links.FirstOrDefault(
                l => l.FromNodeId == nodeId && l.FromPinId == pin.Id);
            if (link != null)
                result.Add(link.ToNodeId);
        }

        return result;
    }
}
