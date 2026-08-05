using NodeEditor.Core;
using NodeEditor.Core.Action;
using NodeEditor.Core.Commands;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.UI.Action;

/// <summary>
/// Registers view-related commands (ZoomIn, ZoomOut, ZoomReset, FrameAll, FrameSelection)
/// on the given <see cref="EditorCommandsImpl"/>.
/// </summary>
public static class ViewCommands
{
    private const float ZoomStep = 1.25f;

    /// <summary>Register all view commands.</summary>
    public static void Register(EditorCommandsImpl cmds, GraphView view)
    {
        var reg = new CommandRegistration(cmds);

        // BP-19: the overview minimap. CommandCatalog has declared this id all along with no
        // implementation; the flag lives on ViewportState so this registrar can reach it.
        reg.Add(
            CommandCatalog.ToggleMinimap, "Toggle Minimap", "View",
            _ => view.Viewport.ShowMinimap = !view.Viewport.ShowMinimap,
            isChecked: () => view.Viewport.ShowMinimap,
            description: "Shows or hides the graph overview minimap.");

        reg.Add(
            CommandCatalog.ZoomIn, "Zoom In", "View",
            _ => view.Viewport.ZoomAt(view.Viewport.CanvasScreenOrigin + view.Viewport.CanvasScreenSize * 0.5f, ZoomStep),
            description: "Zoom in on the canvas.",
            defaultKey: new KeyBinding(EditorKey.Equals, Primitives.KeyModifiers.Ctrl));

        reg.Add(
            CommandCatalog.ZoomOut, "Zoom Out", "View",
            _ => view.Viewport.ZoomAt(view.Viewport.CanvasScreenOrigin + view.Viewport.CanvasScreenSize * 0.5f, 1f / ZoomStep),
            description: "Zoom out of the canvas.",
            defaultKey: new KeyBinding(EditorKey.Minus, Primitives.KeyModifiers.Ctrl));

        reg.Add(
            CommandCatalog.ZoomReset, "Reset Zoom", "View",
            _ => view.Viewport.Reset(),
            description: "Reset zoom to 100%.");

        reg.Add(
            CommandCatalog.FrameAll, "Frame All", "View",
            _ => FrameAll(view),
            isEnabled: () => view.Model.Nodes.Count > 0,
            description: "Frame all nodes in the viewport.",
            defaultKey: new KeyBinding(EditorKey.F, Primitives.KeyModifiers.None));

        reg.Add(
            CommandCatalog.FrameSelection, "Frame Selection", "View",
            _ => FrameSelection(view),
            isEnabled: () => !view.Selection.IsEmpty,
            description: "Frame the selected nodes.",
            defaultKey: new KeyBinding(EditorKey.F, Primitives.KeyModifiers.Shift));
    }

    private static void FrameAll(GraphView view)
    {
        if (view.Model.Nodes.Count == 0) return;
        var rect = BoundsOfNodes(view.Model.Nodes);
        view.Viewport.FrameRect(rect);
    }

    private static void FrameSelection(GraphView view)
    {
        var selectedNodeIds = view.Selection.Nodes.ToHashSet();
        var nodes = view.Model.Nodes.Where(n => selectedNodeIds.Contains(n.Id)).ToList();
        if (nodes.Count == 0) return;
        var rect = BoundsOfNodes(nodes);
        view.Viewport.FrameRect(rect);
    }

    private static Primitives.RectF BoundsOfNodes(IEnumerable<Core.Interfaces.INodeModel> nodes)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (var n in nodes)
        {
            var size = n.SizeOverride ?? new System.Numerics.Vector2(160, 64);
            if (n.Position.X < minX) minX = n.Position.X;
            if (n.Position.Y < minY) minY = n.Position.Y;
            if (n.Position.X + size.X > maxX) maxX = n.Position.X + size.X;
            if (n.Position.Y + size.Y > maxY) maxY = n.Position.Y + size.Y;
        }

        return new Primitives.RectF(new Vector2(minX, minY), new Vector2(maxX - minX, maxY - minY));
    }
}
