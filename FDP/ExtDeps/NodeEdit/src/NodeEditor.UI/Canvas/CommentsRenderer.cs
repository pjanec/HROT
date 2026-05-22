using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using NodeEditor.UI.Util;

namespace NodeEditor.UI.Canvas;

/// <summary>
/// Renders comment boxes onto the canvas draw list.
/// Handles background bodies (pre-node pass) and foreground header strips + rename overlay (post-node pass).
/// </summary>
internal static class CommentsRenderer
{
    private const float HeaderHeight      = 20f;
    private const float HandleRadius      = 5f;
    private const float HandleHitRadius   = 8f;
    private const float BodyAlpha         = 0.15f;
    private const float SelectedThickness = 2.5f;
    private const float NormalThickness   = 1.0f;

    /// <summary>
    /// Draw comment bodies (filled rects) for the back pass (behind nodes).
    /// </summary>
    public static void RenderBackground(ImDrawListPtr dl, GraphView view)
    {
        foreach (var comment in SortedComments(view))
        {
            var (min, max) = CommentScreenRect(comment, view);
            var bodyColor  = comment.Color with { W = BodyAlpha };
            dl.AddRectFilled(min, max, ImGui.GetColorU32(bodyColor), 4f);
        }
    }

    /// <summary>
    /// Draw comment headers, outlines, resize handles, and optional inline rename fields.
    /// Call after nodes have been rendered (foreground pass).
    /// </summary>
    public static void RenderForeground(ImDrawListPtr dl, GraphView view)
    {
        foreach (var comment in SortedComments(view))
        {
            var (min, max)   = CommentScreenRect(comment, view);
            float headerH    = HeaderHeight * view.Viewport.Zoom;
            var headerMax    = new Vector2(max.X, min.Y + headerH);
            bool selected    = view.Selection.Contains(SelectionEntry.OfComment(comment.Id));
            float thickness  = selected ? SelectedThickness : NormalThickness;

            // Outline
            var outlineColor = selected
                ? new Vector4(1f, 0.7f, 0.0f, 1f)
                : comment.Color with { W = 0.8f };
            dl.AddRect(min, max, ImGui.GetColorU32(outlineColor), 4f, ImDrawFlags.None, thickness);

            // Header fill
            var headerFill = comment.Color with { W = 0.7f };
            dl.AddRectFilled(min, headerMax, ImGui.GetColorU32(headerFill), 4f,
                ImDrawFlags.RoundCornersTop);

            // Resize handles (8 corner + edge handles)
            RenderResizeHandles(dl, min, max, selected);

            // Title or rename field
            bool isRenaming = view.Interaction.RenamingComment == comment.Id;
            if (isRenaming)
            {
                RenderRenameField(view, comment, min, headerMax);
            }
            else
            {
                var textColor = new Vector4(1f, 1f, 1f, 0.9f);
                dl.AddText(min + new Vector2(6f, (headerH - ImGui.GetTextLineHeight()) * 0.5f),
                    ImGui.GetColorU32(textColor), comment.Text.Split('\n')[0]);
            }
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static void RenderResizeHandles(ImDrawListPtr dl, Vector2 min, Vector2 max, bool selected)
    {
        if (!selected) return; // only show handles when selected

        var handleColor = ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 0.9f));
        float r = HandleRadius;

        // 8 handle positions: TL, TC, TR, ML, MR, BL, BC, BR
        var cx = (min.X + max.X) * 0.5f;
        var cy = (min.Y + max.Y) * 0.5f;
        Vector2[] handles =
        {
            new(min.X, min.Y), new(cx,    min.Y), new(max.X, min.Y),
            new(min.X, cy),                        new(max.X, cy),
            new(min.X, max.Y), new(cx,    max.Y), new(max.X, max.Y),
        };

        foreach (var h in handles)
            dl.AddCircleFilled(h, r, handleColor);
    }

    private static void RenderRenameField(GraphView view, ICommentModel comment, Vector2 min, Vector2 headerMax)
    {
        // Position the InputText over the header strip
        ImGui.SetCursorScreenPos(min + new Vector2(4f, 2f));
        ImGui.PushItemWidth(headerMax.X - min.X - 8f);

        using (new ImGuiPushIdScope(comment.Id.Value.ToString()))
        {
            var buf = comment.Text;
            if (ImGui.InputText("##rename", ref buf, 512,
                ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
            {
                // Commit: dispatch UpdateComment
                view.Interaction.RenamingComment = null;
            }
            else if (!ImGui.IsItemActive() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                // Focus lost → commit
                view.Interaction.RenamingComment = null;
            }
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                view.Interaction.RenamingComment = null;
            }
        }

        ImGui.PopItemWidth();
    }

    private static (Vector2 Min, Vector2 Max) CommentScreenRect(ICommentModel comment, GraphView view)
    {
        // Check for drag override position
        var pos = view.Interaction.CommentDragOverridePositions.TryGetValue(comment.Id, out var dragPos)
            ? dragPos
            : comment.Position;

        var min = view.Viewport.GraphToScreen(pos);
        var max = view.Viewport.GraphToScreen(pos + comment.Size);
        return (min, max);
    }

    private static IOrderedEnumerable<ICommentModel> SortedComments(GraphView view)
        => view.Model.Comments.OrderBy(c => c.ZOrder);
}
