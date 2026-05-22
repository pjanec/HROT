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

    /// <summary>Draw all nodes and their inline editors.</summary>
    public void DrawAll(
        GraphView view,
        ImDrawListPtr dl,
        Dictionary<NodeId, RectF> nodeScreenRects,
        Dictionary<PinId, Vector2> pinPositions,
        HashSet<PinId> connectedInputPins)
    {
        var theme = view.Host.Theme;
        float zoom  = view.Viewport.Zoom;
        float corner = theme.NodeCornerRadius * zoom;
        float border = theme.NodeBorderThickness * zoom;

        foreach (var node in view.Model.Nodes)
        {
            if (!nodeScreenRects.TryGetValue(node.Id, out var rect)) continue;

            var pMin = rect.Min;
            var pMax = rect.Min + rect.Size;

            // Body background
            dl.AddRectFilled(pMin, pMax, ImGui.GetColorU32(new Vector4(0.18f, 0.18f, 0.18f, 0.95f)), corner);

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

            // Pins
            _pins.DrawNodePins(view, dl, node, pinPositions, connectedInputPins);

            // Inline default-value editors
            if (!view.Viewport.IsLowZoom)
                DrawInlineEditors(view, node, nodeScreenRects, pinPositions, connectedInputPins, zoom);
        }
    }

    // ── private ───────────────────────────────────────────────────────────────

    private static void DrawTitle(
        ImDrawListPtr dl,
        INodeModel node,
        Vector2 pMin, Vector2 pMax,
        float headerH, IEditorTheme theme, float zoom)
    {
        uint textColor = ImGui.GetColorU32(theme.TextDefault);
        var titleSize  = ImGui.CalcTextSize(node.Title);
        float centerX  = pMin.X + (pMax.X - pMin.X - titleSize.X) * 0.5f;
        float centerY  = pMin.Y + (headerH - titleSize.Y) * 0.5f;
        dl.AddText(new Vector2(MathF.Max(pMin.X + 4f, centerX), centerY), textColor, node.Title);
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
        bool hovered  = view.Interaction.Hover is { Kind: HoverKind.Node } h && h.Node == node.Id;

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

        if ((node.State & NodeState.Executing) != 0 || (debug?.CurrentlyExecutingNode == node.Id) == true)
        {
            dl.AddRect(pMin, pMax, ImGui.GetColorU32(new Vector4(1f, 0.9f, 0.1f, 1f)),
                corner, ImDrawFlags.None, border + 2f);
        }
        else if ((node.State & NodeState.Error) != 0)
        {
            dl.AddRect(pMin, pMax, ImGui.GetColorU32(theme.ErrorColor),
                corner, ImDrawFlags.None, border + 1f);
        }
        else if ((node.State & NodeState.Warning) != 0)
        {
            dl.AddRect(pMin, pMax, ImGui.GetColorU32(theme.WarningColor),
                corner, ImDrawFlags.None, border + 1f);
        }

        if ((node.State & NodeState.Disabled) != 0)
        {
            dl.AddRectFilled(pMin, pMax,
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.5f)), corner);
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

        float editorWidthPx = EditorWidthGu * zoom;
        float padPx = EditorHorizPadGu * zoom;

        // Editor appears to the right of the pin label, left-aligned inside the node body.
        // Position: node right side minus editor width minus horizontal padding.
        float editorX = nodeRect.Min.X + nodeRect.Size.X * 0.5f;

        foreach (var pin in visibleInputPins)
        {
            if (!pinPositions.TryGetValue(pin.Id, out var pinScreenPos)) continue;

            var editor = view.TypeSystem.GetDefaultEditor(pin.Type!.Value);
            if (editor == null) continue;

            var editorPos = new Vector2(editorX, pinScreenPos.Y - ImGui.GetFontSize() * 0.5f);
            float editorHeight = ImGui.GetFontSize() + 2f;

            using var scope = new ImGuiPushIdScope(pin.Id.GetHashCode());
            ImGui.SetCursorScreenPos(editorPos);
            ImGui.PushItemWidth(editorWidthPx);

            var currentValue = pin.Default!.Value;
            var ctx = new DefaultEditorContext(
                Pin: pin.Id,
                Type: pin.Type!.Value,
                MaxWidth: editorWidthPx,
                IsReadOnly: false,
                Metadata: pin.Default.Metadata);

            bool changed = editor.Draw(ref currentValue, ctx, out bool committed);

            ImGui.PopItemWidth();

            if (committed)
            {
                view.Commands.Apply(new GraphCommand.SetPinDefault(pin.Id, currentValue));
            }
        }
    }
}
