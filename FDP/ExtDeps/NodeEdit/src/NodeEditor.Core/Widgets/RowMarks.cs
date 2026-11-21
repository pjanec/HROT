using System.Numerics;
using ImGuiNET;

namespace NodeEditor.Core.Widgets;

/// <summary>
/// ⭐ §8 (docs/DESIGN_Struct_Editor.md) — the marks a form row can carry, drawn ONCE for every form: the amber <b>changed bar</b> at the left edge of the control and its <b>↺</b> reset, the <b>error line</b> under a row
/// (⚠ glyph + <see cref="IconPalette.Error"/> text — <c>FormRow</c> draws its invalid-input reason through it), and the <b>set box</b> of an optional field. Pure ImGui, zero allocation.
/// </summary>
/// <remarks>Usage in a <see cref="FormGrid"/> row: <c>RowMarks.ChangedBar(changed); ImGui.InputFloat("##v", ref v); if (changed &amp;&amp; RowMarks.ResetButton("##r")) Reset(); if (bad) RowMarks.ErrorLine(reason);</c></remarks>
public static class RowMarks
{
    /// <summary>The bar's width and the gap after it — ALWAYS reserved (changed or not), so a row's controls stay aligned whether or not it is marked.</summary>
    public const float BarWidth = 3f, BarGap = 3f;

    /// <summary>The slot the changed bar occupies at the left of a control: the cursor moves past it (also when <paramref name="changed"/> is false — nothing shifts); when changed an amber bar of the control's height is drawn in it.</summary>
    public static void ChangedBar(bool changed)
    {
        var pos = ImGui.GetCursorScreenPos();
        if (changed)
            ImGui.GetWindowDrawList().AddRectFilled(pos, pos + new Vector2(BarWidth, ImGui.GetFrameHeight()), ImGui.GetColorU32(IconPalette.Amber));
        ImGui.SetCursorScreenPos(new Vector2(pos.X + BarWidth + BarGap, pos.Y));
    }

    /// <summary>A ↺ glyph button (amber) on the control's line — true when clicked (the caller resets the value). <paramref name="id"/> is the caller's constant id.</summary>
    public static bool ResetButton(string id, string tooltip = "Reset to default")
    {
        ImGui.SameLine(0f, 4f);
        bool clicked = GlyphWidgets.GlyphButton(id, new IconGlyph(KitGlyph.Reset, IconPalette.Amber), tooltip);
        KitProbe.Set(KitProbe.ResetMark, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        return clicked;
    }

    /// <summary>The error line under a row: a ⚠ and the reason in <see cref="IconPalette.Error"/>. One painter for every form.</summary>
    public static void ErrorLine(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, IconPalette.Error);
        ImGui.TextUnformatted(KitGlyph.Warning);
        ImGui.SameLine(0f, 5f);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    /// <summary>⭐ CE-3396 — the same line for a WARNING (a ⚠ and the reason in <see cref="IconPalette.Warning"/>): what does not block the action.</summary>
    public static void WarningLine(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, IconPalette.Warning);
        ImGui.TextUnformatted(KitGlyph.Warning);
        ImGui.SameLine(0f, 5f);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    /// <summary>The <i>set</i> box of an optional field (unchecked = not set, the control after it is disabled by the caller). True on the frame it was toggled.</summary>
    public static bool SetBox(string id, ref bool isSet)
    {
        bool toggled = ImGui.Checkbox(id, ref isSet);
        KitProbe.Set(KitProbe.SetBox, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        return toggled;
    }
}
