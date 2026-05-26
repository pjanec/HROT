using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Registry mapping node types to their corresponding IBlueprintNodeDrawer implementations.
/// Populated at editor startup via BlueprintEditorBootstrap.
/// </summary>
public sealed class BlueprintNodeDrawerRegistry
{
    private readonly Dictionary<Type, IBlueprintNodeDrawer> _drawers = new();

    /// <summary>
    /// Register a drawer for a specific node type.
    /// </summary>
    public void Register(Type nodeType, IBlueprintNodeDrawer drawer)
    {
        ArgumentNullException.ThrowIfNull(nodeType);
        ArgumentNullException.ThrowIfNull(drawer);
        _drawers[nodeType] = drawer;
    }

    /// <summary>
    /// Attempt to retrieve a drawer for the given node type.
    /// Returns true if a drawer is registered for this exact type.
    /// </summary>
    public bool TryGet(Type nodeType, out IBlueprintNodeDrawer? drawer)
    {
        return _drawers.TryGetValue(nodeType, out drawer);
    }

    /// <summary>
    /// Find a drawer that can handle the given node instance.
    /// Checks the node's exact type first, then calls Handles() on all drawers.
    /// Returns null if no drawer can handle this node.
    /// </summary>
    public IBlueprintNodeDrawer? GetDrawerFor(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        // Fast path: exact type match
        if (_drawers.TryGetValue(node.GetType(), out var drawer))
            return drawer;

        // Fallback: ask each drawer if it handles this node
        foreach (var d in _drawers.Values)
        {
            if (d.Handles(node))
                return d;
        }

        return null;
    }
}
