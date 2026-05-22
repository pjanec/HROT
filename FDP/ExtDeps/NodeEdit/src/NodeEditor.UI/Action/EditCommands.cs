using NodeEditor.Core;
using NodeEditor.Core.Action;
using NodeEditor.Core.Commands;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Action;

/// <summary>
/// Registers edit-related commands (Undo, Redo, Delete, SelectAll, etc.)
/// on the given <see cref="EditorCommandsImpl"/>.
/// </summary>
public static class EditCommands
{
    /// <summary>Register all edit commands.</summary>
    public static void Register(EditorCommandsImpl cmds, GraphView view)
    {
        var reg = new CommandRegistration(cmds);

        reg.Add(
            CommandCatalog.Undo, "Undo", "Edit",
            _ => view.UndoLast(),
            isEnabled: () => view.Undo.CanUndo,
            description: "Undo the last operation.",
            iconKey: "icon.undo",
            defaultKey: new KeyBinding(EditorKey.Z, KeyModifiers.Ctrl));

        reg.Add(
            CommandCatalog.Redo, "Redo", "Edit",
            _ => view.RedoLast(),
            isEnabled: () => view.Undo.CanRedo,
            description: "Redo the next operation.",
            iconKey: "icon.redo",
            defaultKey: new KeyBinding(EditorKey.Z, KeyModifiers.Ctrl | KeyModifiers.Shift));

        reg.Add(
            CommandCatalog.DeleteSelection, "Delete", "Edit",
            _ => DeleteSelected(view),
            isEnabled: () => !view.Selection.IsEmpty,
            description: "Delete the current selection.",
            defaultKey: new KeyBinding(EditorKey.Delete, KeyModifiers.None));

        reg.Add(
            CommandCatalog.SelectAll, "Select All", "Edit",
            _ => SelectAll(view),
            isEnabled: () => view.Model.Nodes.Count > 0,
            description: "Select every entity in the current graph.",
            defaultKey: new KeyBinding(EditorKey.A, KeyModifiers.Ctrl));

        reg.Add(
            CommandCatalog.SelectNone, "Deselect All", "Edit",
            _ => view.Selection.Clear(),
            isEnabled: () => !view.Selection.IsEmpty,
            description: "Clear the current selection.",
            defaultKey: new KeyBinding(EditorKey.Escape, KeyModifiers.None));
    }

    private static void DeleteSelected(GraphView view)
    {
        // Collect IDs to delete
        var nodeIds = view.Selection.Nodes.ToList();
        var linkIds = view.Selection.Links.ToList();

        if (nodeIds.Count == 0 && linkIds.Count == 0)
            return;

        GraphCommand forward;
        GraphCommand inverse;

        if (nodeIds.Count > 0 && linkIds.Count > 0)
        {
            forward = new GraphCommand.Batch("Delete Selection",
                new GraphCommand[]
                {
                    new GraphCommand.RemoveLinks(linkIds),
                    new GraphCommand.RemoveNodes(nodeIds),
                });
        }
        else if (nodeIds.Count > 0)
        {
            forward = new GraphCommand.RemoveNodes(nodeIds);
        }
        else
        {
            forward = new GraphCommand.RemoveLinks(linkIds);
        }

        // Inverse is a no-op placeholder (host stores full undo history)
        inverse = new GraphCommand.Batch("restore-delete", Array.Empty<GraphCommand>());
        view.Execute(forward, inverse, "Delete Selection");
        view.Selection.Clear();
    }

    private static void SelectAll(GraphView view)
    {
        view.Selection.Clear();
        foreach (var node in view.Model.Nodes)
            view.Selection.Add(SelectionEntry.OfNode(node.Id));
    }
}
