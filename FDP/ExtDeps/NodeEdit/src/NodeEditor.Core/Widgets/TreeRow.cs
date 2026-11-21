using System.Numerics;
using ImGuiNET;

namespace NodeEditor.Core.Widgets;

/// <summary>
/// ⭐ C1 (docs/UX/DESIGN_UI_Conventions.md §2) — THE tree row: <b>chevron · icon · label</b>. The chevron is ImGui's own tree arrow; the row is
/// <c>TreeNodeEx("##row", OpenOnArrow | SpanAvailWidth | NoTreePushOnOpen [| Leaf])</c> with an empty label, and the icon and the text are painted by the draw list
/// right of the arrow slot. A leaf keeps the chevron's slot (so the labels of one depth share one x); the depth is the indent. A flat drawer (the ORBAT's visible-node
/// list) and a recursive one call the same row: <c>open</c> is the caller's state and the row never pushes the tree stack.
/// <para>⭐ R-2084: nothing is concatenated or allocated per row — the id is the caller's own string (<c>PushID</c> + a constant <c>"##row"</c>).</para>
/// </summary>
public static class TreeRow
{
    /// <summary>Draws one row. Returns true when the row (not its arrow) was clicked — the caller selects. A row with children toggles <paramref name="open"/> from the arrow.</summary>
    /// <param name="id">The caller-owned unique id within the current ID scope (a node id string, a path) — never built per frame.</param>
    /// <param name="label">The caller's text (not concatenated here).</param>
    /// <param name="icon">Image or glyph, or <see cref="RowIcon.None"/> (then the label follows the arrow directly).</param>
    /// <param name="depth">Indent levels (× the style's IndentSpacing).</param>
    /// <param name="hasChildren">false = a leaf: it keeps the chevron's slot but draws no arrow.</param>
    /// <param name="open">The caller's expanded state; written from the arrow of a row with children.</param>
    /// <param name="selected">Draws the selection highlight (blue = selected).</param>
    public static bool Draw(string id, string label, in RowIcon icon, int depth, bool hasChildren, ref bool open, bool selected)
    {
        float indent = depth * ImGui.GetStyle().IndentSpacing;
        if (indent > 0f) ImGui.Indent(indent);

        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        if (!hasChildren) flags |= ImGuiTreeNodeFlags.Leaf;
        if (selected) flags |= ImGuiTreeNodeFlags.Selected;

        ImGui.PushID(id);
        if (hasChildren) ImGui.SetNextItemOpen(open, ImGuiCond.Always);
        bool isOpen = ImGui.TreeNodeEx("##row", flags);
        ImGui.PopID();
        if (hasChildren) open = isOpen;

        bool clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left) && !ImGui.IsItemToggledOpen();

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        RowPaint.IconAndLabel(ImGui.GetWindowDrawList(), min, max, min.X + ImGui.GetTreeNodeToLabelSpacing(), icon, label, ImGui.GetColorU32(ImGuiCol.Text));

        if (indent > 0f) ImGui.Unindent(indent);
        return clicked;
    }
}
