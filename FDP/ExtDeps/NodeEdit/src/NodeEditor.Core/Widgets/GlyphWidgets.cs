using System.Numerics;
using ImGuiNET;

namespace NodeEditor.Core.Widgets;

/// <summary>
/// ⭐ U-2 (docs/UX/DESIGN_Colour_Icons.md §2) — a coloured glyph as a button or a switch option, on a NEUTRAL background: unselected ≈ <c>#1C1C22</c>, selected ≈ <c>#34343E</c> with a light frame
/// (📐 rendered: on ImGui's default blue button a blue or violet glyph nearly vanishes). In a switch every option keeps its OWN colour; the selected one is full strength and framed, the others
/// draw at ~60 % — ⛔ never the selected one coloured and the rest grey (Q-I2). Zero allocation.
/// <para>⭐ Deviation from the design (Q-C1, R-4145): these live in <c>NodeEditor.Core</c>, not <c>Fdp.Presentation.IconWidgets</c>, so the node-editor pickers can use them.</para>
/// </summary>
public static class GlyphWidgets
{
    public static readonly Vector4 Neutral         = Rgb(0x1C1C22);
    public static readonly Vector4 NeutralHovered  = Rgb(0x2A2A32);
    public static readonly Vector4 NeutralSelected = Rgb(0x34343E);
    public static readonly Vector4 SelectedFrame   = new(0.78f, 0.78f, 0.84f, 0.85f);
    /// <summary>The strength of an unselected option of a switch.</summary>
    public const float UnselectedAlpha = 0.6f;

    private static Vector4 Rgb(int rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

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

        var bg = selected ? NeutralSelected : hovered ? NeutralHovered : Neutral;
        var dl = ImGui.GetWindowDrawList();
        var max = pos + size;
        dl.AddRectFilled(pos, max, ImGui.GetColorU32(bg), style.FrameRounding);
        if (selected) dl.AddRect(pos, max, ImGui.GetColorU32(SelectedFrame), style.FrameRounding);

        var c = icon.Color;
        if (isToggle && !selected) c.W *= UnselectedAlpha;
        dl.AddText(new Vector2(pos.X + (size.X - glyphW) * 0.5f, pos.Y + (size.Y - ImGui.GetTextLineHeight()) * 0.5f), ImGui.GetColorU32(c), icon.Glyph);

        if (hovered && tooltip is not null) ImGui.SetTooltip(tooltip);
        return clicked;
    }
}
