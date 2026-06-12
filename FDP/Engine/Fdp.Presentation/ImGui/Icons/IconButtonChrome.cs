using System.Numerics;
using ImGuiNET;

namespace Fdp.Presentation.Icons;

/// <summary>
/// Single shared chrome for all toolbar icon buttons — vector and bitmap renderers
/// route through the same toggle fill and hover frame so every icon looks identical.
/// </summary>
/// <remarks>
/// Draw order at every call site:
/// <c>DrawToggleFill → (glyph image/shape) → DrawHoverFrame</c> (frame on top).
/// </remarks>
public static class IconButtonChrome
{
    /// <summary>
    /// Draws the blue/accent toggle fill BEHIND the glyph. Safe no-op when <paramref name="toggled"/> is <c>false</c>.
    /// Uses <see cref="ImGuiCol.HeaderActive"/> at alpha 0.85 with a 2-pixel corner rounding.
    /// </summary>
    public static void DrawToggleFill(ImDrawListPtr dl, Vector2 pos, Vector2 size, bool toggled)
    {
        if (!toggled) return;
        var c = ImGui.GetStyle().Colors[(int)ImGuiCol.HeaderActive]; c.W = 0.85f;
        dl.AddRectFilled(pos, pos + size, ImGui.GetColorU32(c), 2f);
    }

    /// <summary>
    /// Draws the white inset hover frame ON TOP of the glyph. Safe no-op when <paramref name="hovered"/> is <c>false</c>.
    /// The frame is inset by 1 px so all four edges (including the top) are visible
    /// even when the icon sits flush against a window boundary.
    /// </summary>
    public static void DrawHoverFrame(ImDrawListPtr dl, Vector2 pos, Vector2 size, bool hovered)
    {
        if (!hovered) return;
        var a = pos + new Vector2(1f, 1f);
        var b = pos + size - new Vector2(1f, 1f);   // inset so the top/left edges are visible
        dl.AddRect(a, b, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.9f)), 2f, ImDrawFlags.None, 1.5f);
    }
}
