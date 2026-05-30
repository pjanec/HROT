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
            defaultKey: new KeyBinding(EditorKey.Y, KeyModifiers.Ctrl));

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
        var sel = view.Selection;
        if (sel.IsEmpty) return;

        var fwds = new List<GraphCommand>();
        var invs = new List<GraphCommand>();

        // 1. Reroutes
        var reroutes = sel.Reroutes.ToList();
        foreach (var r in reroutes)
        {
            var link = view.Model.FindLink(r.LinkId);
            if (link != null && r.WaypointIndex >= 0 && r.WaypointIndex < link.Waypoints.Count)
            {
                fwds.Add(new GraphCommand.RemoveReroute(r.LinkId, r.WaypointIndex));
                invs.Add(new GraphCommand.InsertReroute(r.LinkId, link.Waypoints[r.WaypointIndex]));
            }
        }

        var nodes = sel.Nodes.ToList();
        var explicitLinks = sel.Links.ToList();

        // Identify implicitly deleted links (connected to nodes being deleted)
        var implicitLinks = view.Model.Links
            .Where(l => nodes.Any(nid =>
                view.Model.FindPin(l.FromPin)?.OwnerNodeId == nid ||
                view.Model.FindPin(l.ToPin)?.OwnerNodeId == nid))
            .Select(l => l.Id);

        var allLinksToRemove = explicitLinks.Union(implicitLinks).ToList();

        // 2. Links
        if (allLinksToRemove.Count > 0)
        {
            fwds.Add(new GraphCommand.RemoveLinks(allLinksToRemove));
            var addLinks = new List<GraphCommand>();
            foreach (var lid in allLinksToRemove)
            {
                var l = view.Model.FindLink(lid);
                if (l != null) addLinks.Add(new GraphCommand.AddLink(l.Id, l.FromPin, l.ToPin));
            }
            if (addLinks.Count > 0) invs.Add(new GraphCommand.Batch("Restore Links", addLinks));
        }

        // 3. Nodes
        if (nodes.Count > 0)
        {
            fwds.Add(new GraphCommand.RemoveNodes(nodes));
            var addNodes = new List<GraphCommand>();
            foreach (var nid in nodes)
            {
                var n = view.Model.FindNode(nid);
                if (n != null)
                {
                    var props = new Dictionary<string, object?>
                    {
                        ["PinIds"] = n.Pins.Select(p => p.Id).ToList()
                    };
                    addNodes.Add(new GraphCommand.AddNode(n.Id, n.Kind, n.Position, props));
                }
            }
            if (addNodes.Count > 0) invs.Add(new GraphCommand.Batch("Restore Nodes", addNodes));
        }

        // 4. Comments
        var comments = sel.Comments.ToList();
        foreach (var cid in comments)
        {
            var c = view.Model.Comments.FirstOrDefault(x => x.Id == cid);
            if (c != null)
            {
                fwds.Add(new GraphCommand.RemoveComment(cid));
                invs.Add(new GraphCommand.AddComment(c.Id, c.Text, c.Position, c.Size, c.Color, c.MoveWithContents));
            }
        }

        if (fwds.Count > 0)
        {
            // Architecturally critical: Inverses must be executed in reverse order.
            // Reversing ensures nodes are restored before links attempt to connect to them.
            invs.Reverse();

            var forwardBatch = new GraphCommand.Batch("Delete Selection", fwds);
            var inverseBatch = new GraphCommand.Batch("Restore Selection", invs);

            view.Execute(forwardBatch, inverseBatch, "Delete Selection");
        }

        view.Selection.Clear();
    }

    private static void SelectAll(GraphView view)
    {
        view.Selection.Clear();
        foreach (var node in view.Model.Nodes)
            view.Selection.Add(SelectionEntry.OfNode(node.Id));
    }
}
