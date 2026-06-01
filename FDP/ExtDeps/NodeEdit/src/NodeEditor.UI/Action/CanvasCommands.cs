using NodeEditor.Core;
using NodeEditor.Core.Action;
using NodeEditor.Core.Commands;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using NodeEditor.UI.Find;
using System.Linq;
using System.Numerics;

namespace NodeEditor.UI.Action;

/// <summary>
/// Registers canvas-specific commands (Find, navigation) on the given
/// <see cref="EditorCommandsImpl"/>.
/// </summary>
public static class CanvasCommands
{
    /// <summary>Register all canvas commands.</summary>
    public static void Register(EditorCommandsImpl cmds, GraphView view, FindBar? findBar)
    {
        var reg = new CommandRegistration(cmds);

        reg.Add(
            CommandCatalog.FindInGraph, "Find in Graph", "Find",
            _ =>
            {
                if (findBar is not null) findBar.Open();
            },
            description: "Open the find bar to search within the current graph.",
            defaultKey: new KeyBinding(EditorKey.F, Primitives.KeyModifiers.Ctrl));

        reg.Add(
            CommandCatalog.FindInAsset, "Find in Asset", "Find",
            _ =>
            {
                if (findBar is not null)
                {
                    findBar.Scope = FindScope.Asset;
                    findBar.Open();
                }
            },
            description: "Open the find bar in asset scope.",
            defaultKey: new KeyBinding(EditorKey.F, Primitives.KeyModifiers.Ctrl | Primitives.KeyModifiers.Shift));

        reg.Add(
            CommandCatalog.FindNext, "Find Next", "Find",
            _ => findBar?.Next(),
            isEnabled: () => findBar?.Results.Count > 0,
            description: "Navigate to the next find result.",
            defaultKey: new KeyBinding(EditorKey.F3, Primitives.KeyModifiers.None));

        reg.Add(
            CommandCatalog.FindPrev, "Find Previous", "Find",
            _ => findBar?.Previous(),
            isEnabled: () => findBar?.Results.Count > 0,
            description: "Navigate to the previous find result.",
            defaultKey: new KeyBinding(EditorKey.F3, Primitives.KeyModifiers.Shift));

        reg.Add(
            CommandCatalog.AddComment, "Add Comment", "Add",
            _ => AddCommentAroundSelection(view),
            isEnabled: () => view.Selection.Nodes.Any(),
            description: "Add a comment box around the selection.",
            defaultKey: new KeyBinding(EditorKey.C, Primitives.KeyModifiers.None));
    }

    public static void AddCommentAroundSelection(GraphView view)
    {
        var selectedIds = view.Selection.Nodes.ToHashSet();
        var nodes = view.Model.Nodes.Where(n => selectedIds.Contains(n.Id)).ToList();
        if (nodes.Count == 0) return;

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (var n in nodes)
        {
            var size = n.SizeOverride ?? new Vector2(160, 64);
            if (n.Position.X < minX) minX = n.Position.X;
            if (n.Position.Y < minY) minY = n.Position.Y;
            if (n.Position.X + size.X > maxX) maxX = n.Position.X + size.X;
            if (n.Position.Y + size.Y > maxY) maxY = n.Position.Y + size.Y;
        }

        // Apply 16px padding around the enclosed AABB
        var pos = new Vector2(minX - 16f, minY - 16f);
        var sizeVec = new Vector2(maxX - minX + 32f, maxY - minY + 32f);

        var commentId = IdGenerator.NewCommentId();
        // Default to the first palette color (Blue)
        var color = new Vector4(0.29f, 0.56f, 0.88f, 1f);

        var fwd = new GraphCommand.AddComment(commentId, "New Comment", pos, sizeVec, color, true);
        var inv = new GraphCommand.RemoveComment(commentId);

        view.Execute(fwd, inv, "Add Comment");
    }
}
