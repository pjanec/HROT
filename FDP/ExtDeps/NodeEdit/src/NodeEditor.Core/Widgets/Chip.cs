using System.Numerics;
using ImGuiNET;

namespace NodeEditor.Core.Widgets;

/// <summary>What a <see cref="Chip"/> carries at its right end.</summary>
public enum ChipEnd
{
    /// <summary>Nothing (a plain button-like chip, e.g. the list's <i>Add</i>).</summary>
    None,
    /// <summary>▾ — the chip opens a picker (an entity / place value, a condition blank).</summary>
    Dropdown,
    /// <summary>✕ — the chip can be removed; <see cref="ChipHit"/> says whether the body or the ✕ was hit.</summary>
    Removable,
    /// <summary>No end glyph — the chip is an on / off switch (a flag): on = tinted and framed, off = dim.</summary>
    Toggle,
}

/// <summary>Which part of a <see cref="Chip"/> was clicked.</summary>
public enum ChipHit { None, Body, Remove }

/// <summary>
/// ⭐ K3 (docs/DESIGN_Struct_Editor.md §8) — THE ONE chip (moved here from <c>ReferenceLeafDrawers.DrawChip</c>, U-8): a frame-coloured rounded rectangle — glyph · text · end — sized by its text
/// (capped at <c>maxWidth</c>), drawn in plain ImGui (<c>InvisibleButton</c> + a <c>DrawList</c> rect with the style's <c>FrameRounding</c>, K3). One function, three ends (<see cref="ChipEnd"/>).
/// Pure ImGui, no domain types. ⭐ R-2084: nothing is allocated per frame. The entity-link leaf, the condition editor's blanks, <see cref="ChipRow"/>, <see cref="ListBlock"/>'s <i>Add</i> and the
/// entity picker's field all draw through it.
/// </summary>
public static class Chip
{
    /// <summary>FontAwesome <c>Xmark</c> — the removable chip's end.</summary>
    public const string RemoveGlyph = "";

    private static float ArrowOf(float frameHeight) => frameHeight * 0.55f;

    /// <summary>The width a chip of this label / icon / end takes at the current font and style (the same value <see cref="Draw"/> uses) — for wrapping rows.</summary>
    public static float WidthOf(string label, IconGlyph? icon, float maxWidth, ChipEnd end = ChipEnd.Dropdown)
    {
        float h = ImGui.GetFrameHeight(), pad = ImGui.GetStyle().FramePadding.X;
        float glyphW = icon is { } ig ? ImGui.CalcTextSize(ig.Glyph).X + 5f : 0f;
        float endW = end is ChipEnd.Dropdown or ChipEnd.Removable ? 6f + ArrowOf(h) : 0f;
        return MathF.Min(maxWidth, pad + glyphW + ImGui.CalcTextSize(label).X + endW + pad);
    }

    /// <summary>The common case: a dropdown chip — true when clicked (the caller opens its picker).</summary>
    public static bool Dropdown(string id, string label, IconGlyph? icon, float maxWidth) => Draw(id, label, icon, maxWidth, ChipEnd.Dropdown) == ChipHit.Body;

    /// <summary>Draws the chip at the cursor and answers which part was clicked this frame. <paramref name="on"/> is the toggle's state; <paramref name="tint"/> frames the chip and colours its
    /// text (e.g. <see cref="IconPalette.Error"/> for a thing that is gone).</summary>
    public static ChipHit Draw(string id, string label, IconGlyph? icon, float maxWidth, ChipEnd end = ChipEnd.Dropdown, bool on = false, Vector4? tint = null)
    {
        var style = ImGui.GetStyle();
        float h = ImGui.GetFrameHeight(), pad = style.FramePadding.X, arrow = ArrowOf(h);
        float glyphW = icon is { } ig ? ImGui.CalcTextSize(ig.Glyph).X + 5f : 0f;
        float w = WidthOf(label, icon, maxWidth, end);
        var pos = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
        bool hovered = ImGui.IsItemHovered();
        bool overEnd = hovered && end == ChipEnd.Removable && ImGui.GetMousePos().X >= pos.X + w - pad - arrow - 2f;

        var dl = ImGui.GetWindowDrawList();
        var max = pos + new Vector2(w, h);
        uint fill = end == ChipEnd.Toggle && on
            ? ImGui.GetColorU32(IconPalette.Blue with { W = hovered ? 0.45f : 0.35f })
            : ImGui.GetColorU32(hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg);
        dl.AddRectFilled(pos, max, fill, style.FrameRounding);
        if (tint is { } t) dl.AddRect(pos, max, ImGui.GetColorU32(t), style.FrameRounding);
        else if (end == ChipEnd.Toggle && on) dl.AddRect(pos, max, ImGui.GetColorU32(IconPalette.Blue), style.FrameRounding);

        float ty = pos.Y + (h - ImGui.GetTextLineHeight()) * 0.5f, x = pos.X + pad;
        if (icon is { } g) { dl.AddText(new Vector2(x, ty), ImGui.GetColorU32(g.Color), g.Glyph); x += glyphW; }
        float textRight = pos.X + w - pad - (end is ChipEnd.Dropdown or ChipEnd.Removable ? arrow + 4f : 0f);
        dl.PushClipRect(new Vector2(x, pos.Y), new Vector2(textRight + (end is ChipEnd.Dropdown or ChipEnd.Removable ? 4f : 0f), pos.Y + h), true);
        uint textCol = tint is { } tc ? ImGui.GetColorU32(tc)
            : ImGui.GetColorU32(end == ChipEnd.Toggle && !on ? ImGuiCol.TextDisabled : ImGuiCol.Text);
        dl.AddText(new Vector2(x, ty), textCol, label);
        dl.PopClipRect();

        float ax = pos.X + w - pad - arrow * 0.5f, ay = pos.Y + h * 0.5f, r = arrow * 0.32f;
        if (end == ChipEnd.Dropdown)
            dl.AddTriangleFilled(new Vector2(ax - r, ay - r * 0.6f), new Vector2(ax + r, ay - r * 0.6f), new Vector2(ax, ay + r * 0.6f), ImGui.GetColorU32(ImGuiCol.TextDisabled));
        else if (end == ChipEnd.Removable)
        {
            float gw = ImGui.CalcTextSize(RemoveGlyph).X;
            dl.AddText(new Vector2(ax - gw * 0.5f, ty), ImGui.GetColorU32(overEnd ? ImGuiCol.Text : ImGuiCol.TextDisabled), RemoveGlyph);
        }
        if (!clicked) return ChipHit.None;
        return overEnd ? ChipHit.Remove : ChipHit.Body;
    }
}
