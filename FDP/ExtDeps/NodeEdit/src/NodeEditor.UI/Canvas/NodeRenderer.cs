using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using NodeEditor.UI.Util;

namespace NodeEditor.UI.Canvas;

/// <summary>
/// Draws node bodies: header strip, title, subtitle, pin glyphs/labels,
/// and inline default-value editors for unconnected input data pins.
/// Applies selection, error, warning, and debug-execution outlines.
/// </summary>
internal sealed class NodeRenderer
{
    private const float EditorWidthGu    = 80f;
    private const float EditorHorizPadGu = 4f;

    private readonly PinRenderer _pins = new();

    /// <summary>Draw the culled visible nodes and their inline editors. Returns true if a node body was clicked.</summary>
    public bool DrawAll(
        GraphView view,
        ImDrawListPtr dl,
        Dictionary<NodeId, RectF> nodeScreenRects,
        Dictionary<PinId, Vector2> pinPositions,
        HashSet<PinId> connectedInputPins,
        HashSet<NodeId> visibleNodes)
    {
        var theme = view.Host.Theme;
        float zoom  = view.Viewport.Zoom;
        float corner = theme.NodeCornerRadius * zoom;
        float border = theme.NodeBorderThickness * zoom;
        bool isNodeBgActive = false;

        // Pass 1: draw unselected, resting nodes in stable model order.
        foreach (var node in view.Model.Nodes)
        {
            if (!visibleNodes.Contains(node.Id)) continue;
            if (node.IsContainerNode()) continue; // containers drawn by ContainerRenderer

            bool isSelected = view.Selection.Contains(SelectionEntry.OfNode(node.Id));
            bool isDragged  = view.Interaction.DragOverridePositions.ContainsKey(node.Id);
            if (isSelected || isDragged) continue;
            isNodeBgActive |= RenderSingleNode(view, dl, node.Id, nodeScreenRects, pinPositions, connectedInputPins,
                theme, zoom, corner, border);
        }

        // Pass 2: draw selected or dragged nodes on top.
        foreach (var node in view.Model.Nodes)
        {
            if (!visibleNodes.Contains(node.Id)) continue;
            if (node.IsContainerNode()) continue; // containers drawn by ContainerRenderer

            bool isSelected = view.Selection.Contains(SelectionEntry.OfNode(node.Id));
            bool isDragged  = view.Interaction.DragOverridePositions.ContainsKey(node.Id);
            if (!isSelected && !isDragged) continue;
            isNodeBgActive |= RenderSingleNode(view, dl, node.Id, nodeScreenRects, pinPositions, connectedInputPins,
                theme, zoom, corner, border);
        }

        return isNodeBgActive;
    }

    // -- private ----------------------------------------------------------------

    private bool RenderSingleNode(
        GraphView view,
        ImDrawListPtr dl,
        NodeId nodeId,
        Dictionary<NodeId, RectF> nodeScreenRects,
        Dictionary<PinId, Vector2> pinPositions,
        HashSet<PinId> connectedInputPins,
        IEditorTheme theme,
        float zoom,
        float corner,
        float border)
    {
        var node = view.Model.FindNode(nodeId);
        if (node == null) return false;
        if (!nodeScreenRects.TryGetValue(nodeId, out var rect)) return false;

        var pMin = rect.Min;
        var pMax = rect.Min + rect.Size;

        // Body background
        dl.AddRectFilled(pMin, pMax, ImGui.GetColorU32(new Vector4(0.18f, 0.18f, 0.18f, 0.95f)), corner);

        // Submit an interaction blocker for this node body.
        // SetNextItemAllowOverlap permits widgets submitted after this (this node's own inline
        // editors) to capture input, while still occluding widgets rendered in prior passes
        // (editors of nodes underneath).
        ImGui.SetCursorScreenPos(pMin);
        ImGui.SetNextItemAllowOverlap();
        ImGui.InvisibleButton($"##node_bg_{nodeId.Value}", pMax - pMin);
        bool isBgActive = ImGui.IsItemActive();

        // Header strip
        float headerH = theme.NodeHeaderHeight * zoom;
        var headerColor = theme.GetCategoryHeaderColor(node.Category);
        dl.AddRectFilled(pMin, new Vector2(pMax.X, pMin.Y + headerH), ImGui.GetColorU32(headerColor),
            corner, ImDrawFlags.RoundCornersTop);

        // Node state overlay (executing, disabled, error, warning)
        DrawStateOverlay(dl, view, node, pMin, pMax, corner, border, theme);

        // Selection / hover outline
        DrawOutlines(dl, view, node, pMin, pMax, corner, border, theme);

        // Title text
        if (!view.Viewport.IsLowZoom)
        {
            DrawTitle(dl, node, pMin, pMax, headerH, theme, zoom);
        }

        // Pins - skip entirely in low-zoom mode (no sub-pixel glyphs submitted to ImGui).
        if (!view.Viewport.IsLowZoom)
            _pins.DrawNodePins(view, dl, node, pinPositions, connectedInputPins);

        // Inline default-value editors
        if (!view.Viewport.IsLowZoom)
            DrawInlineEditors(view, node, nodeScreenRects, pinPositions, connectedInputPins, zoom);

        return isBgActive;
    }

    private static void DrawTitle(
        ImDrawListPtr dl,
        INodeModel node,
        Vector2 pMin, Vector2 pMax,
        float headerH, IEditorTheme theme, float zoom)
    {
        uint textColor = ImGui.GetColorU32(theme.TextDefault);
        float targetFontSize = ImGui.GetFontSize() * zoom;
        nint fontPtr = theme.GetFontForSize(targetFontSize);
        bool useFont = fontPtr != 0;

        unsafe
        {
            if (useFont) ImGui.PushFont(new ImFontPtr((ImFont*)(void*)fontPtr));
        }

        var font = ImGui.GetFont();
        var titleSize = font.CalcTextSizeA(targetFontSize, float.MaxValue, 0f, node.Title);
        float centerX = pMin.X + (pMax.X - pMin.X - titleSize.X) * 0.5f;
        float centerY = pMin.Y + (headerH - titleSize.Y) * 0.5f;
        dl.AddText(font, targetFontSize, new Vector2(MathF.Max(pMin.X + 4f * zoom, centerX), centerY), textColor, node.Title);

        if (useFont) ImGui.PopFont();
    }

    private static void DrawOutlines(
        ImDrawListPtr dl,
        GraphView view,
        INodeModel node,
        Vector2 pMin, Vector2 pMax,
        float corner, float border,
        IEditorTheme theme)
    {
        bool selected = view.Selection.Contains(SelectionEntry.OfNode(node.Id));

        // A node is considered hovered when the cursor is over its body OR over
        // any of its own pins (pins have higher hit priority, but the node border
        // must remain highlighted throughout the entire node area).
        bool hovered = view.Interaction.Hover.Kind switch
        {
            HoverKind.Node => view.Interaction.Hover.Node == node.Id,
            HoverKind.Pin  => view.Model.FindPin(view.Interaction.Hover.Pin)?.OwnerNodeId == node.Id,
            _              => false,
        };

        if (selected)
        {
            uint selColor = view.Selection.Items.Count == 1 &&
                            view.Selection.Contains(SelectionEntry.OfNode(node.Id))
                ? ImGui.GetColorU32(theme.PrimarySelectionAccent)
                : ImGui.GetColorU32(theme.SelectionAccent);
            dl.AddRect(pMin, pMax, selColor, corner, ImDrawFlags.None, border + 1f);
        }
        else if (hovered)
        {
            dl.AddRect(pMin, pMax, ImGui.GetColorU32(new Vector4(1, 1, 1, 0.25f)), corner, ImDrawFlags.None, border);
        }
        else
        {
            dl.AddRect(pMin, pMax, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.5f)), corner, ImDrawFlags.None, border);
        }
    }

    private static void DrawStateOverlay(
        ImDrawListPtr dl,
        GraphView view,
        INodeModel node,
        Vector2 pMin, Vector2 pMax,
        float corner, float border,
        IEditorTheme theme)
    {
        var debug = view.Host.Debug;
        bool isExecuting = (node.State & NodeState.Executing) != 0 || debug?.CurrentlyExecutingNode == node.Id;
        bool isRecentlyExecuted = (node.State & NodeState.RecentlyExecuted) != 0 || debug?.RecentlyExecutedNodes.Contains(node.Id) == true;

        if (isExecuting)
        {
            // Architecturally critical: 2 Hz sine pulse for currently executing node
            float time = (float)ImGui.GetTime();
            float pulseAlpha = 0.5f + 0.5f * MathF.Sin(time * MathF.PI * 4f);
            
            // Header glow overlay
            float headerH = theme.NodeHeaderHeight * view.Viewport.Zoom;
            dl.AddRectFilled(pMin, new Vector2(pMax.X, pMin.Y + headerH), ImGui.GetColorU32(new Vector4(1f, 0.9f, 0.1f, pulseAlpha * 0.4f)), corner, ImDrawFlags.RoundCornersTop);
            
            // Pulsing outline
            dl.AddRect(pMin, pMax, ImGui.GetColorU32(new Vector4(1f, 0.9f, 0.1f, pulseAlpha)), corner, ImDrawFlags.None, border + 2f);
        }
        else if (isRecentlyExecuted)
        {
            // Recently executed afterglow
            dl.AddRect(pMin, pMax, ImGui.GetColorU32(new Vector4(1f, 0.6f, 0.1f, 0.8f)), corner, ImDrawFlags.None, border + 1.5f);
        }
        else if ((node.State & NodeState.Error) != 0)
        {
            dl.AddRect(pMin, pMax, ImGui.GetColorU32(theme.ErrorColor), corner, ImDrawFlags.None, border + 1f);
        }
        else if ((node.State & NodeState.Warning) != 0)
        {
            dl.AddRect(pMin, pMax, ImGui.GetColorU32(theme.WarningColor), corner, ImDrawFlags.None, border + 1f);
        }

        if ((node.State & NodeState.Disabled) != 0)
        {
            dl.AddRectFilled(pMin, pMax, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.5f)), corner);
        }

        // Breakpoint marker (16x16 red circle on the left edge of the header)
        if (debug?.Breakpoints.Contains(node.Id) == true)
        {
            float headerH = theme.NodeHeaderHeight * view.Viewport.Zoom;
            var bpCenter = pMin + new Vector2(0f, headerH * 0.5f);
            dl.AddCircleFilled(bpCenter, 8f * view.Viewport.Zoom, ImGui.GetColorU32(new Vector4(0.9f, 0.1f, 0.1f, 1f)));
        }
    }

    private void DrawInlineEditors(
        GraphView view,
        INodeModel node,
        Dictionary<NodeId, RectF> nodeScreenRects,
        Dictionary<PinId, Vector2> pinPositions,
        HashSet<PinId> connectedInputPins,
        float zoom)
    {
        if (!nodeScreenRects.TryGetValue(node.Id, out var nodeRect)) return;

        var visibleInputPins = node.Pins
            .Where(p => p.Direction == PinDirection.Input
                     && p.Kind == PinKind.Data
                     && p.Default != null
                     && !connectedInputPins.Contains(p.Id)
                     && (!p.IsAdvanced || node.ShowAdvancedPins))
            .ToList();
        if (visibleInputPins.Count == 0) return;

        float targetFontSize = ImGui.GetFontSize() * zoom;
        nint fontPtr = view.Host.Theme.GetFontForSize(targetFontSize);
        bool useFont = fontPtr != 0;

        unsafe
        {
            if (useFont) ImGui.PushFont(new ImFontPtr((ImFont*)(void*)fontPtr));
        }

        // The resolved ladder face is baked at a fixed size (e.g. 16px), so ImGui widgets would
        // render at that size — bigger than targetFontSize and, with unscaled FramePadding, taller
        // than the layout's PinRowHeightGu*zoom slot (→ vertical overlap when zoomed out). Scale
        // the window font so widgets render at exactly targetFontSize, and scale FramePadding by
        // zoom so the frame height tracks the row slot. Both are reset before PopFont below.
        float faceSize = ImGui.GetFontSize();
        if (faceSize > 0f) ImGui.SetWindowFontScale(targetFontSize / faceSize);
        // Vertical padding 2 (not 3) keeps the frame height ~18*zoom inside the 24*zoom row slot
        // with a clear ~3px gap each side, so adjacent editors never touch.
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f * zoom, 2f * zoom));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2f * zoom);

        float pinCenterX = nodeRect.Min.X + CanvasLayoutBuilder.NodeHorizPadGu * zoom;
        float maxLabelWidthPx = 0f;
        float maxOutputWidthPx = 0f;
        var font = ImGui.GetFont();

        foreach (var p in node.Pins)
        {
            if (p.IsAdvanced && !node.ShowAdvancedPins) continue;

            float labelWidth = 0f;
            if (!string.IsNullOrEmpty(p.Label))
                labelWidth = font.CalcTextSizeA(targetFontSize, float.MaxValue, 0f, p.Label).X;

            if (p.Direction == PinDirection.Input)
            {
                if (pinPositions.TryGetValue(p.Id, out var pos))
                    pinCenterX = pos.X;

                if (labelWidth > maxLabelWidthPx) maxLabelWidthPx = labelWidth;
            }
            else
            {
                float outWidth = (20f * zoom) + labelWidth; // 20f matches layout glyph budget
                if (outWidth > maxOutputWidthPx) maxOutputWidthPx = outWidth;
            }
        }

        float editorX = pinCenterX + (10f * zoom) + maxLabelWidthPx + (CanvasLayoutBuilder.EditorHorizPadGu * zoom);
        float rightLimitX = nodeRect.Max.X - (CanvasLayoutBuilder.NodeHorizPadGu * zoom) - maxOutputWidthPx - (12f * zoom);
        float editorWidthPx = MathF.Max(rightLimitX - editorX, 40f * zoom);

        foreach (var pin in visibleInputPins)
        {
            if (!pinPositions.TryGetValue(pin.Id, out var pinScreenPos)) continue;

            var editor = view.TypeSystem.GetDefaultEditor(pin.Type!.Value);
            if (editor == null) continue;

            // Center the widget on the pin using its actual frame height (font + scaled padding).
            var editorPos = new Vector2(editorX, pinScreenPos.Y - ImGui.GetFrameHeight() * 0.5f);

            using var scope = new ImGuiPushIdScope(pin.Id.Value.ToString());
            ImGui.SetCursorScreenPos(editorPos);
            ImGui.PushItemWidth(editorWidthPx);

            var currentValue = view.Interaction.PinDragOverrides.TryGetValue(pin.Id, out var ovr)
                ? ovr
                : pin.Default!.Value;
            var ctx = new DefaultEditorContext(
                Pin: pin.Id,
                Type: pin.Type!.Value,
                MaxWidth: editorWidthPx,
                IsReadOnly: false,
                Metadata: pin.Default!.Metadata);

            bool changed = editor.Draw(ref currentValue, ctx, out bool committed);

            ImGui.PopItemWidth();

            if (committed)
            {
                view.Interaction.PinDragOverrides.Remove(pin.Id);
                if (!Equals(currentValue, pin.Default!.Value))
                {
                    var cb = new CommandBuilder(view.Model);
                    var (fwd, inv) = cb.SetPinDefault(pin.Id, currentValue);
                    view.Execute(fwd, inv, "Set Pin Default");
                }
            }
            else if (changed)
            {
                view.Interaction.PinDragOverrides[pin.Id] = currentValue;
            }
            else if (!ImGui.IsAnyItemActive())
            {
                // Clean up orphaned drag overrides if the user cancels via Escape
                view.Interaction.PinDragOverrides.Remove(pin.Id);
            }
        }

        ImGui.PopStyleVar(2);          // FrameRounding, FramePadding
        ImGui.SetWindowFontScale(1f);  // restore the window's font scale

        if (useFont)
            ImGui.PopFont();
    }
}
