using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.Spatial;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Canvas;

/// <summary>
/// Renders attachment pills (or low-zoom bars) above their host nodes.
/// Call DrawAll after node rendering is complete for the current frame.
/// </summary>
internal sealed class AttachmentRenderer
{
    // NEA-10: below this zoom level, draw a single colored bar instead of pills.
    private const float LowZoomThreshold = 0.5f;
    // Height of the low-zoom bar in screen pixels.
    private const float LowZoomBarHeight = 3f;

    /// <summary>
    /// Draw all attachment pills for nodes that have attachments.
    /// At zoom below LowZoomThreshold, draws a 3 px colored bar instead.
    /// </summary>
    public void DrawAll(
        GraphView view,
        ImDrawListPtr dl,
        Dictionary<NodeId, AttachmentLayout> attachmentLayouts,
        Dictionary<NodeId, RectF> nodeScreenRects)
    {
        if (attachmentLayouts.Count == 0) return;

        float zoom = view.Viewport.Zoom;
        bool lowZoom = zoom < LowZoomThreshold;
        var theme = view.Host.Theme;

        foreach (var (nodeId, layout) in attachmentLayouts)
        {
            if (!nodeScreenRects.TryGetValue(nodeId, out var nodeRect)) continue;
            var attachments = view.Model.GetAttachmentsForNode(nodeId);
            if (attachments.Count == 0) continue;

            if (lowZoom)
                DrawLowZoomBar(dl, nodeRect, attachments, theme);
            else
                DrawPills(dl, nodeRect, layout, attachments, view);
        }
    }

    // ── Low-zoom bar (NEA-10) ─────────────────────────────────────────────────

    private static void DrawLowZoomBar(
        ImDrawListPtr dl,
        RectF nodeRect,
        IReadOnlyList<IAttachmentModel> attachments,
        IEditorTheme theme)
    {
        // Single 3 px bar above the host, colored by the leftmost attachment category.
        IAttachmentModel? leftmost = null;
        foreach (var a in attachments)
        {
            if (leftmost == null
                || a.StackIndex < leftmost.StackIndex
                || (a.StackIndex == leftmost.StackIndex
                    && a.Id.Value.CompareTo(leftmost.Id.Value) < 0))
                leftmost = a;
        }
        if (leftmost == null) return;

        var color = GetCategoryColor(leftmost.Category, theme);
        var barMin = new Vector2(nodeRect.Min.X, nodeRect.Min.Y - LowZoomBarHeight);
        var barMax = new Vector2(nodeRect.Min.X + nodeRect.Size.X, nodeRect.Min.Y);
        dl.AddRectFilled(barMin, barMax, ImGui.GetColorU32(color));
    }

    // ── Normal-zoom pills (NEA-05) ────────────────────────────────────────────

    private static void DrawPills(
        ImDrawListPtr dl,
        RectF nodeRect,
        AttachmentLayout layout,
        IReadOnlyList<IAttachmentModel> attachments,
        GraphView view)
    {
        var theme = view.Host.Theme;
        float zoom = view.Viewport.Zoom;

        // Setup font scaling (matches node title rendering).
        float targetFontSize = ImGui.GetFontSize() * zoom;
        nint fontPtr = theme.GetFontForSize(targetFontSize);
        bool useFont = fontPtr != 0;

        unsafe
        {
            if (useFont) ImGui.PushFont(new ImFontPtr((ImFont*)(void*)fontPtr));
        }

        var font = ImGui.GetFont();

        // Build lookup table so we can find a model given its id.
        var modelMap = new Dictionary<AttachmentId, IAttachmentModel>(attachments.Count);
        foreach (var a in attachments)
            modelMap[a.Id] = a;

        foreach (var (id, placement) in layout.Placements)
        {
            if (!modelMap.TryGetValue(id, out var model)) continue;

            // Apply zoom to layout geometry.
            var pillMin = nodeRect.Min + placement.TopLeft * zoom;
            var pillMax = pillMin + placement.Size * zoom;

            float cornerRadius = theme.AttachmentCornerRadius * zoom;

            var bgColor = GetCategoryColor(model.Category, theme);
            if ((model.State & AttachmentState.Disabled) != 0)
                bgColor = bgColor with { W = 0.6f };

            dl.AddRectFilled(pillMin, pillMax, ImGui.GetColorU32(bgColor), cornerRadius);

            // State outlines drawn on top of fill.
            if ((model.State & AttachmentState.Selected) != 0)
                dl.AddRect(pillMin, pillMax, ImGui.GetColorU32(theme.SelectionAccent), cornerRadius, ImDrawFlags.None, 2f * zoom);
            else if ((model.State & AttachmentState.Error) != 0)
                dl.AddRect(pillMin, pillMax, ImGui.GetColorU32(theme.ErrorColor), cornerRadius, ImDrawFlags.None, 1f * zoom);
            else if ((model.State & AttachmentState.Warning) != 0)
                dl.AddRect(pillMin, pillMax, ImGui.GetColorU32(theme.WarningColor), cornerRadius, ImDrawFlags.None, 1f * zoom);

            // Text content: optional glyph then optional label.
            float textLineH = font.CalcTextSizeA(targetFontSize, float.MaxValue, 0f, "A").Y;
            float textY = pillMin.Y + (placement.Size.Y * zoom - textLineH) * 0.5f;
            float textX = pillMin.X + AttachmentLayoutEngine.PillPaddingH * zoom;
            uint textColor = ImGui.GetColorU32(theme.TextDefault);

            if (!string.IsNullOrEmpty(model.Glyph))
            {
                dl.AddText(font, targetFontSize, new Vector2(textX, textY), textColor, model.Glyph);
                textX += font.CalcTextSizeA(targetFontSize, float.MaxValue, 0f, model.Glyph).X;
                if (!string.IsNullOrEmpty(model.Label))
                    textX += 4f * zoom;
            }
            if (!string.IsNullOrEmpty(model.Label))
                dl.AddText(font, targetFontSize, new Vector2(textX, textY), textColor, model.Label);
        }

        if (useFont) ImGui.PopFont();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Vector4 GetCategoryColor(AttachmentCategory category, IEditorTheme theme) =>
        category switch
        {
            AttachmentCategory.Decorator => theme.AttachmentDecoratorColor,
            AttachmentCategory.Flag      => theme.AttachmentFlagColor,
            AttachmentCategory.Pure      => theme.AttachmentPureColor,
            _                            => theme.AttachmentCustomColor,
        };
}
