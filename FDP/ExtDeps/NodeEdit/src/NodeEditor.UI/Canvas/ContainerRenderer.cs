using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Canvas;

/// <summary>
/// Renders container node bodies (fill, header strip, title, outline, selection highlight).
/// Called in two places relative to the wire pass:
///   - Before wires: DrawBackground draws fills, headers, and outlines of all containers.
///   Container children (regular nodes and nested containers) are drawn by the
///   normal NodeRenderer pass which runs after wires.
/// </summary>
internal sealed class ContainerRenderer
{
    // Corner radius for containers is slightly larger than regular nodes (6 px vs 4 px).
    private const float ContainerCornerRadiusPx = 6f;
    // Outline thickness in screen pixels.
    private const float OutlinePx = 2f;
    // Horizontal left-pad for the collapse glyph.
    private const float ChevronLeftPad = 6f;
    // Horizontal pad between chevron and title.
    private const float TitleLeftPad = 18f;

    /// <summary>
    /// Draw pass 3: fills, headers, and outlines for ALL container nodes, outermost first.
    /// Must be called BEFORE the wire pass so container fills sit behind wires.
    /// </summary>
    public void DrawBackground(
        GraphView view,
        ImDrawListPtr dl,
        CanvasLayout layout,
        HashSet<NodeId> visibleNodeIds)
    {
        float zoom     = view.Viewport.Zoom;
        float headerHt = view.Host.Theme.NodeHeaderHeight * zoom;
        float corner   = ContainerCornerRadiusPx * zoom;

        // Only seed from root containers; recursion handles nesting depth.
        foreach (var node in view.Model.Nodes)
        {
            if (node.ParentContainerId != null) continue;
            if (node.AsContainer() is { } container)
                DrawContainerRecursive(view, dl, container, layout, visibleNodeIds, zoom, headerHt, corner);
        }
    }

    // ── private ───────────────────────────────────────────────────────────────

    private void DrawContainerRecursive(
        GraphView view,
        ImDrawListPtr dl,
        IContainerNodeModel container,
        CanvasLayout layout,
        HashSet<NodeId> visibleNodeIds,
        float zoom,
        float headerHt,
        float corner)
    {
        // Draw this container's own background before its nested children.
        DrawSingleContainer(view, dl, container, layout, zoom, headerHt, corner);

        // Recurse into child containers (outer-before-inner ensures correct layering).
        foreach (var childId in container.ChildNodeIds)
        {
            var child = view.Model.FindNode(childId);
            if (child?.AsContainer() is { } nested)
                DrawContainerRecursive(view, dl, nested, layout, visibleNodeIds, zoom, headerHt, corner);
        }
    }

    private void DrawSingleContainer(
        GraphView view,
        ImDrawListPtr dl,
        IContainerNodeModel container,
        CanvasLayout layout,
        float zoom,
        float headerHt,
        float corner)
    {
        if (!layout.NodeScreenRects.TryGetValue(container.Id, out var rect)) return;

        var pMin = rect.Min;
        var pMax = rect.Min + rect.Size;

        var catColor = view.Host.Theme.GetCategoryHeaderColor(container.Category);

        // Interior background: category color at 8% alpha.
        var fillColor = new Vector4(catColor.X, catColor.Y, catColor.Z, 0.08f);
        dl.AddRectFilled(pMin, pMax, ImGui.GetColorU32(fillColor), corner);

        // Header strip: full category color.
        var headerPMax = new Vector2(pMax.X, pMin.Y + headerHt);
        dl.AddRectFilled(pMin, headerPMax, ImGui.GetColorU32(catColor), corner, ImDrawFlags.RoundCornersTop);

        // Header divider: 1 px at 40% alpha.
        var divColor = new Vector4(catColor.X, catColor.Y, catColor.Z, 0.4f);
        dl.AddLine(
            new Vector2(pMin.X, pMin.Y + headerHt),
            new Vector2(pMax.X, pMin.Y + headerHt),
            ImGui.GetColorU32(divColor), 1f);

        // Outline: 2 px, category color darkened 50%.
        var outlineColor = new Vector4(catColor.X * 0.5f, catColor.Y * 0.5f, catColor.Z * 0.5f, catColor.W);
        dl.AddRect(pMin, pMax, ImGui.GetColorU32(outlineColor), corner, ImDrawFlags.None, OutlinePx);

        // Title and collapse indicator (skip at low zoom to avoid sub-pixel text).
        if (!view.Viewport.IsLowZoom)
        {
            DrawCollapseIndicator(dl, container, pMin, headerHt, zoom);
            DrawTitle(dl, container, pMin, pMax, headerHt, view.Host.Theme, zoom);
        }

        // Selection outline (drawn over the body outline).
        DrawSelectionOutline(view, dl, container, pMin, pMax, corner);
    }

    private static void DrawTitle(
        ImDrawListPtr dl,
        IContainerNodeModel container,
        Vector2 pMin, Vector2 pMax,
        float headerHt, IEditorTheme theme, float zoom)
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
        var titleSize = font.CalcTextSizeA(targetFontSize, float.MaxValue, 0f, container.Title);
        float textX = pMin.X + TitleLeftPad * zoom;
        float textY = pMin.Y + (headerHt - titleSize.Y) * 0.5f;
        float maxTextX = pMax.X - 4f;
        if (textX < maxTextX)
            dl.AddText(font, targetFontSize, new Vector2(textX, textY), textColor, container.Title);

        if (useFont) ImGui.PopFont();
    }

    private static void DrawCollapseIndicator(
        ImDrawListPtr dl,
        IContainerNodeModel container,
        Vector2 pMin, float headerHt, float zoom)
    {
        // Draw a simple ASCII chevron: "v" for expanded, ">" for collapsed.
        string glyph = container.IsCollapsed ? ">" : "v";
        float cx = pMin.X + ChevronLeftPad * zoom;
        float cy = pMin.Y + (headerHt - ImGui.GetTextLineHeight()) * 0.5f;
        dl.AddText(new Vector2(cx, cy), 0xFFFFFFFF, glyph);
    }

    private static void DrawSelectionOutline(
        GraphView view,
        ImDrawListPtr dl,
        IContainerNodeModel container,
        Vector2 pMin, Vector2 pMax, float corner)
    {
        if (!view.Selection.Contains(SelectionEntry.OfNode(container.Id))) return;

        float border = view.Host.Theme.NodeBorderThickness * view.Viewport.Zoom;
        uint selColor = view.Selection.Items.Count == 1
            ? ImGui.GetColorU32(view.Host.Theme.PrimarySelectionAccent)
            : ImGui.GetColorU32(view.Host.Theme.SelectionAccent);
        dl.AddRect(pMin, pMax, selColor, corner, ImDrawFlags.None, border + 1f);
    }
}
