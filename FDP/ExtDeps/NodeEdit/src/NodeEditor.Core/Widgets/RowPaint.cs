using System.Numerics;
using ImGuiNET;

namespace NodeEditor.Core.Widgets;

/// <summary>The one painter of a <see cref="RowIcon"/> and a row's label at the item rect (the leaf technique of NodeEditor.UI TreeLayout, made shared). Zero allocation.</summary>
internal static class RowPaint
{
    /// <summary>Paints icon then label starting at <paramref name="x"/>, vertically centred in the item rect <paramref name="min"/>..<paramref name="max"/>. Returns the x the label started at.</summary>
    internal static float IconAndLabel(ImDrawListPtr dl, Vector2 min, Vector2 max, float x, in RowIcon icon, string label, uint textColor)
    {
        float size = ImGui.GetFontSize();
        float lineH = ImGui.GetTextLineHeight();
        float cy = (min.Y + max.Y) * 0.5f;
        if (!icon.IsNone)
        {
            var p = new Vector2(x, cy - size * 0.5f);
            if (icon.IsImage)
                dl.AddImage(icon.Texture, p, new Vector2(p.X + size, p.Y + size), icon.Uv0, icon.Uv1);
            else
            {
                var gw = ImGui.CalcTextSize(icon.Glyph!).X;
                dl.AddText(new Vector2(x + (size - gw) * 0.5f, cy - lineH * 0.5f), ImGui.GetColorU32(icon.Color), icon.Glyph!);
            }
            x += size + ImGui.GetStyle().ItemInnerSpacing.X;
        }
        dl.AddText(new Vector2(x, cy - lineH * 0.5f), textColor, label);
        return x;
    }
}
