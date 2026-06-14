using System.Collections.Generic;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Host-supplied provider that returns context menu items for a right-clicked node.
/// Registered via <see cref="IEditorHostServices.NodeContextMenu"/>.
/// If no provider is registered, the node context menu contains only the built-in items.
/// </summary>
public interface INodeContextMenuProvider
{
    /// <summary>Extra context-menu items for a right-clicked node (and the current multi-selection). Empty = none.</summary>
    IReadOnlyList<ContextMenuItem> GetItemsFor(NodeId node, IReadOnlyList<NodeId> selection);
}
