using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Core.View;

/// <summary>
/// Top-level aggregator for a single graph being edited.
/// Holds references to the host (read-only model + services), and owns the editor-side
/// transient state (viewport, selection, interaction). Hands itself to the UI layer.
/// Editor mutations always go through <see cref="Commands"/>; the editor never writes to <see cref="Model"/> directly.
/// </summary>
public sealed class GraphView
{
    /// <summary>Host-provided read-only view of the graph data.</summary>
    public IGraphModel Model { get; }

    /// <summary>Host command sink. All mutations go here.</summary>
    public IGraphCommandSink Commands { get; }

    /// <summary>Connection validation rules.</summary>
    public ILinkValidator Validator { get; }

    /// <summary>Type system (colors, compatibility, cast resolution).</summary>
    public ITypeSystem TypeSystem { get; }

    /// <summary>Node catalog (right-click menu, contextual picker, search).</summary>
    public INodeCatalog Catalog { get; }

    /// <summary>Host services bag (clipboard, icons, diagnostics, debug session, theme, picker registry, input).</summary>
    public IEditorHostServices Host { get; }

    /// <summary>Viewport (pan/zoom).</summary>
    public ViewportState Viewport { get; }

    /// <summary>Selection set.</summary>
    public SelectionState Selection { get; }

    /// <summary>Transient interaction state.</summary>
    public InteractionState Interaction { get; }

    /// <summary>
    /// Undo/redo stack. Owned by the editor (not the host) so the editor can group
    /// multi-step authoring actions into single user-visible operations.
    /// </summary>
    public UndoStack Undo { get; }

    public GraphView(
        IGraphModel model,
        IGraphCommandSink commands,
        ILinkValidator validator,
        ITypeSystem typeSystem,
        INodeCatalog catalog,
        IEditorHostServices host)
    {
        Model = model;
        Commands = commands;
        Validator = validator;
        TypeSystem = typeSystem;
        Catalog = catalog;
        Host = host;
        Viewport = new ViewportState();
        Selection = new SelectionState();
        Interaction = new InteractionState();
        Undo = new UndoStack(commands);
    }

    /// <summary>
    /// Convenience: apply a command through the undo stack, recording the supplied inverse.
    /// Callers snapshot inverse state <em>before</em> calling Execute.
    /// </summary>
    public GraphCommandResult Execute(GraphCommand forward, GraphCommand inverse, string label)
        => Undo.ApplyAndRecord(forward, inverse, label);

    /// <summary>Undo the most recent operation (if any).</summary>
    public void UndoLast() => Undo.Undo();

    /// <summary>Redo the most recently undone operation (if any).</summary>
    public void RedoLast() => Undo.Redo();

    // ── Container transform helpers (TASK-NEC-02) ─────────────────────────────

    /// <summary>
    /// Returns the position of the node's top-left corner in canvas-absolute coordinates.
    /// For root-level nodes (ParentContainerId == null) this equals INodeModel.Position.
    /// For children of containers, walks the ancestor chain accumulating offsets.
    /// Returns Vector2.Zero if the node is not found.
    /// </summary>
    public Vector2 NodeCanvasPosition(NodeId id)
    {
        var node = Model.FindNode(id);
        if (node == null) return Vector2.Zero;

        if (node.ParentContainerId == null)
            return node.Position;

        var parent = Model.FindNode(node.ParentContainerId.Value);
        if (parent?.AsContainer() is not { } container)
            return node.Position; // parent not an active container; treat as root

        var parentCanvas = NodeCanvasPosition(parent.Id);
        var interiorOrigin = parentCanvas + new Vector2(
            container.Padding.Left,
            Host.Theme.NodeHeaderHeight + container.Padding.Top);

        float regionOffsetY = 0f;
        if (container.Regions.Count > 0)
        {
            int rIdx = container.GetRegionIndexForChild(id);
            if (rIdx > 0)
            {
                float[] regionHeights = new float[container.Regions.Count];
                for (int i = 0; i < regionHeights.Length; i++) regionHeights[i] = 60f;

                foreach (var childId in container.ChildNodeIds)
                {
                    var childNode = Model.FindNode(childId);
                    if (childNode == null) continue;
                    int cRIdx = container.GetRegionIndexForChild(childId);
                    if (cRIdx >= 0 && cRIdx < regionHeights.Length)
                    {
                        var size = childNode.SizeOverride ?? new Vector2(160, 64);
                        regionHeights[cRIdx] = Math.Max(regionHeights[cRIdx], childNode.Position.Y + size.Y);
                    }
                }

                for (int i = 0; i < rIdx; i++) regionOffsetY += regionHeights[i];
            }
        }

        return interiorOrigin + new Vector2(node.Position.X, node.Position.Y + regionOffsetY);
    }

    /// <summary>
    /// Returns the node's local position (INodeModel.Position).
    /// For root nodes this is canvas-absolute; for container children it is parent-local.
    /// Returns Vector2.Zero if the node is not found.
    /// </summary>
    public Vector2 NodeLocalPosition(NodeId id) =>
        Model.FindNode(id)?.Position ?? Vector2.Zero;

    /// <summary>
    /// Returns the parent container ID for the node, or null if the node is at root level.
    /// Returns null if the node is not found.
    /// </summary>
    public NodeId? GetParentContainer(NodeId id) =>
        Model.FindNode(id)?.ParentContainerId;
}
