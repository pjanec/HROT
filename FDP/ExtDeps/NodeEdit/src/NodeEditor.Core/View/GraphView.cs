using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;

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
}
