using System.Numerics;
using ImGuiNET;

namespace NodeEditor.Core.Widgets;

/// <summary>The three background colours of a header that keeps its own meaning (Q-C5) — e.g. the raw editor's authority green.</summary>
public readonly record struct HeaderColors(Vector4 Normal, Vector4 Hovered, Vector4 Active);

/// <summary>
/// ⭐ C1 (docs/UX/DESIGN_UI_Conventions.md §4) — THE collapsing header: <b>slate</b> (<c>#2B2F38</c>, hover <c>#353A45</c>, active <c>#3F4552</c>), then chevron · coloured icon · label.
/// The colours are pushed around this header's own <c>CollapsingHeader</c> only — ⛔ never a global <c>ImGuiCol.Header</c> change (blue means SELECTED). A header whose colour means
/// something passes <see cref="HeaderColors"/> (Q-C5). Zero allocation per frame.
/// </summary>
public static class SectionHeader
{
    public static readonly Vector4 Slate        = Rgb(0x2B2F38);
    public static readonly Vector4 SlateHovered = Rgb(0x353A45);
    public static readonly Vector4 SlateActive  = Rgb(0x3F4552);
    /// <summary>The default colours (slate).</summary>
    public static readonly HeaderColors Default = new(Slate, SlateHovered, SlateActive);

    private static Vector4 Rgb(int rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

    /// <summary>Draws the header; returns whether it is open (draw the body when true).</summary>
    /// <param name="id">The caller-owned id (constant per header).</param>
    /// <param name="label">The caller's text.</param>
    /// <param name="icon">Image or glyph (Colour Icons map) painted right of the chevron.</param>
    /// <param name="flags">ImGui tree-node flags, e.g. DefaultOpen.</param>
    /// <param name="colors">Override colours for a header whose colour means something (Q-C5); null = slate.</param>
    public static bool Draw(string id, string label, in RowIcon icon, ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.None, HeaderColors? colors = null)
    {
        var c = colors ?? Default;
        ImGui.PushStyleColor(ImGuiCol.Header, c.Normal);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, c.Hovered);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, c.Active);
        ImGui.PushID(id);
        bool open = ImGui.CollapsingHeader("##hdr", flags);
        ImGui.PopID();
        ImGui.PopStyleColor(3);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        RowPaint.IconAndLabel(ImGui.GetWindowDrawList(), min, max, min.X + ImGui.GetTreeNodeToLabelSpacing(), icon, label, ImGui.GetColorU32(ImGuiCol.Text));
        return open;
    }
}
