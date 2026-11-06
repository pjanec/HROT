using System.Numerics;
using System.Text;
using ImGuiNET;

namespace NodeEditor.Core.Widgets;

/// <summary>
/// ⭐ U-2 + CE-4286 (docs/UX/DESIGN_Colour_Icons.md §3.1) — THE tab: a normal ImGui tab item with NO close box (lifted from <c>DetailsWindow.BeginTab</c>: ImGui.NET's managed overload with flags takes a
/// <c>p_open</c>, which adds a close box, so the native call is used) and an optional coloured icon at its left. A tab's label is ONE text colour, so the icon is PAINTED: the label reserves the icon's width
/// (spaces before the text) and after <c>BeginTabItem</c> the glyph is drawn in its colour at the item rect through the window draw list. Scrolling, <c>SetSelected</c> and the keyboard stay ImGui's.
/// <para>The id is <c>"###"+id</c>, so it does NOT change when the label text does (⚠ the design said <c>##id</c>, which hashes the text too). The label is encoded into a stack buffer: nothing allocated per tab.</para>
/// <para>Content tab bars use the NEUTRAL palette (<see cref="BeginBar"/>): tab ≈ #24242C, selected ≈ #3A3A48 with a blue top line — ImGui's default blue tabs swallow blue icons. Docked-window tabs are untouched.</para>
/// </summary>
public static class IconTab
{
    public static readonly Vector4 Tab         = Rgb(0x24242C);
    public static readonly Vector4 TabHovered  = Rgb(0x30303C);
    public static readonly Vector4 TabSelected = Rgb(0x3A3A48);
    public static readonly Vector4 Overline    = IconPalette.Blue;
    private const int PushedColors = 7;

    private static Vector4 Rgb(int rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

    /// <summary>Begins a content tab bar with the neutral palette. Always pair with <see cref="EndBar"/> when it returns true.</summary>
    public static bool BeginBar(string id, ImGuiTabBarFlags flags = ImGuiTabBarFlags.FittingPolicyScroll)
    {
        ImGui.PushStyleColor(ImGuiCol.Tab, Tab);
        ImGui.PushStyleColor(ImGuiCol.TabHovered, TabHovered);
        ImGui.PushStyleColor(ImGuiCol.TabSelected, TabSelected);
        ImGui.PushStyleColor(ImGuiCol.TabSelectedOverline, Overline);
        ImGui.PushStyleColor(ImGuiCol.TabDimmed, Tab);
        ImGui.PushStyleColor(ImGuiCol.TabDimmedSelected, TabSelected);
        ImGui.PushStyleColor(ImGuiCol.TabDimmedSelectedOverline, Overline with { W = 0.6f });
        bool open = ImGui.BeginTabBar(id, flags);
        if (!open) ImGui.PopStyleColor(PushedColors);
        return open;
    }

    public static void EndBar()
    {
        ImGui.EndTabBar();
        ImGui.PopStyleColor(PushedColors);
    }

    /// <summary>Begins a tab. Returns true when it is the selected one — draw its body, then <c>ImGui.EndTabItem()</c>.
    /// <paramref name="icon"/> may be null (a plain tab). <paramref name="id"/> is the caller's constant id; <paramref name="flags"/> e.g. <c>SetSelected</c>.</summary>
    public static unsafe bool Begin(string id, string label, IconGlyph? icon, ImGuiTabItemFlags flags = ImGuiTabItemFlags.None)
    {
        float reserve = 0f; int spaces = 0;
        if (icon is { } ic)
        {
            reserve = ImGui.CalcTextSize(ic.Glyph).X + ImGui.GetStyle().ItemInnerSpacing.X;
            float spaceW = ImGui.CalcTextSize(" ").X;
            spaces = spaceW > 0f ? (int)MathF.Ceiling(reserve / spaceW) : 0;
        }
        int count = spaces + Encoding.UTF8.GetByteCount(label) + 3 + Encoding.UTF8.GetByteCount(id);
        Span<byte> buf = count < 512 ? stackalloc byte[512] : new byte[count + 1];
        int n = 0;
        for (int i = 0; i < spaces; i++) buf[n++] = (byte)' ';
        n += Encoding.UTF8.GetBytes(label, buf.Slice(n));
        buf[n++] = (byte)'#'; buf[n++] = (byte)'#'; buf[n++] = (byte)'#';
        n += Encoding.UTF8.GetBytes(id, buf.Slice(n));
        buf[n] = 0;

        bool selected;
        fixed (byte* p = buf) selected = ImGuiNative.igBeginTabItem(p, null, flags) != 0;

        if (icon is { } g)
        {
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var style = ImGui.GetStyle();
            ImGui.GetWindowDrawList().AddText(new Vector2(min.X + style.FramePadding.X, (min.Y + max.Y) * 0.5f - ImGui.GetTextLineHeight() * 0.5f), ImGui.GetColorU32(g.Color), g.Glyph);
        }
        return selected;
    }
}
