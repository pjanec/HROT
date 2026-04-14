using Hrot.UI.Common.Models;

namespace Hrot.UI.Common.Facades;

/// <summary>
/// Port interface for reading the ORBAT (Order of Battle) tree.
/// Provides a flat list of view-model nodes that panels use for rendering
/// the entity hierarchy without direct ECS access.
/// </summary>
public interface IOrbatDataProvider
{
    /// <summary>
    /// Returns a flat list of visible ORBAT nodes for the current scenario.
    /// </summary>
    /// <param name="filterText">
    /// Optional text filter applied to entity names. Pass <see cref="string.Empty"/> to show all nodes.
    /// </param>
    /// <param name="expandedNodes">
    /// Set of entity IDs whose children are currently expanded in the UI tree.
    /// </param>
    IReadOnlyList<OrbatNodeViewModel> GetVisibleNodes(string filterText, HashSet<int> expandedNodes);
}
