using System.Numerics;
using ImGuiNET;

namespace NodeEditor.Core.Widgets;

/// <summary>
/// ⭐ U-2 (docs/UX/DESIGN_Colour_Icons.md §2, §I1) — a coloured glyph as a button or a switch option. ⭐⭐ CE-4333: drawn through <see cref="IconButtonChrome"/>, the ONE chrome of every icon button — transparent at rest, the accent
/// fill for the selected option of a switch, a white inset frame on hover. In a switch every option keeps its OWN colour; the selected one is full strength and filled, the others draw at ~60 % — ⛔ never the selected one coloured and the
/// rest grey (Q-I2). Zero allocation.
/// <para>⭐ Deviation from the design (Q-C1, R-4145): these live in <c>NodeEditor.Core</c>, not <c>Fdp.Presentation.IconWidgets</c>, so the node-editor pickers can use them.</para>
/// </summary>
public static class GlyphWidgets
{
    /// <summary>The strength of an unselected option of a switch.</summary>
    public const float UnselectedAlpha = 0.6f;

    /// <summary>A plain glyph button (full strength). <paramref name="id"/> is the caller's constant id.</summary>
    public static bool GlyphButton(string id, in IconGlyph icon, string? tooltip = null)
        => Draw(id, icon, selected: false, isToggle: false, tooltip);

    /// <summary>One option of a switch / a toggle: <paramref name="selected"/> = framed and full strength, otherwise the glyph keeps its colour at ~60 %. Returns true when clicked.</summary>
    public static bool GlyphToggle(string id, in IconGlyph icon, bool selected, string? tooltip = null)
        => Draw(id, icon, selected, isToggle: true, tooltip);

    /// <summary>The width a glyph button / toggle of this icon takes (for a caller that right-aligns a row of them).</summary>
    public static float WidthOf(in IconGlyph icon) => MathF.Max(ImGui.GetFrameHeight(), ImGui.CalcTextSize(icon.Glyph).X + ImGui.GetStyle().FramePadding.X * 2f);

    private static bool Draw(string id, in IconGlyph icon, bool selected, bool isToggle, string? tooltip)
    {
        var style = ImGui.GetStyle();
        float glyphW = ImGui.CalcTextSize(icon.Glyph).X;
        float h = ImGui.GetFrameHeight();
        var size = new Vector2(WidthOf(icon), h);
        var pos = ImGui.GetCursorScreenPos();

        bool clicked = ImGui.InvisibleButton(id, size);
        bool hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        IconButtonChrome.DrawToggleFill(dl, pos, size, selected);   // ⭐ CE-4333 — behind the glyph; nothing at rest

        var c = icon.Color;
        if (isToggle && !selected) c.W *= UnselectedAlpha;
        dl.AddText(new Vector2(pos.X + (size.X - glyphW) * 0.5f, pos.Y + (size.Y - ImGui.GetTextLineHeight()) * 0.5f), ImGui.GetColorU32(c), icon.Glyph);

        IconButtonChrome.DrawHoverFrame(dl, pos, size, hovered);   // ⭐ on top of the glyph
        if (hovered && tooltip is not null) ImGui.SetTooltip(tooltip);
        return clicked;
    }
}
