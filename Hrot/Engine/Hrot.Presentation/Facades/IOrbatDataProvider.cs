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

    /// <summary>
    /// ⭐ CE-4213 (DESIGN_Entity_Hierarchy_Views HV-2) — the relation the tree is built on (the panel's [Command | Structure] switch).
    /// Default <see cref="EntityTreeRelation.Command"/>; a provider without other relations ignores the setter.
    /// </summary>
    EntityTreeRelation Relation { get => EntityTreeRelation.Command; set { } }
}
