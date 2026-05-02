namespace Hrot.UI.Common.Models;

/// <summary>
/// Flat view-model node representing a single entity in the ORBAT (Order of Battle) tree.
/// The <see cref="Depth"/> field drives ImGui indentation; children are rendered by
/// iterating the flat list and comparing depths.
/// </summary>
/// <param name="EntityId">The network entity ID.</param>
/// <param name="Name">The display name of the entity.</param>
/// <param name="Depth">Zero-based tree depth used for UI indentation.</param>
/// <param name="HasChildren">Whether this node has child nodes in the hierarchy.</param>
/// <param name="IsPendingDelete">Whether the entity is marked for deletion and should render grayed out.</param>
/// <param name="CanAcceptSubordinates">Whether this node represents a commanding entity that can accept subordinate assignments.</param>
public sealed record OrbatNodeViewModel(
    int EntityId,
    string Name,
    int Depth,
    bool HasChildren,
    bool IsPendingDelete,
    bool CanAcceptSubordinates);
