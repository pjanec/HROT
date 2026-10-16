using System;
using System.Numerics;
using Fdp.Presentation.Fonts;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-1140 (docs/DESIGN_Entity_Control.md §13.8) — the FPS HUD's text: the editor's label font (Roboto, the embedded face every
/// panel and card uses), baked once at <see cref="BakeSize"/> and smoothed, drawn at the HUD's size. Raylib's built-in bitmap font is
/// the fallback (no GL context yet, or the bake failed). 🔒 User, 2026-10-16: <i>"The font used for the HUD is ugly and difficult to read;
/// font used for entity labels/card would work better"</i>.
/// </summary>
public static class HudText
{
    /// <summary>The size the face is baked at (px): larger than drawn, so the down-scaled glyphs stay crisp.</summary>
    public const int BakeSize = 40;

    private static Font? s_font;
    private static bool s_tried;

    /// <summary>The HUD face, loaded on first use (the draw thread, a GL context current); null ⇒ use Raylib's default.</summary>
    public static Font? Font()
    {
        if (s_tried) return s_font;
        s_tried = true;
        try
        {
            var bytes = EmbeddedFontResources.GetRobotoRegularTtfBytes();
            var font = Raylib.LoadFontFromMemory(".ttf", bytes, BakeSize, null!, 0);
            if (font.Texture.Id == 0) return null;
            Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);
            s_font = font;
        }
        catch (Exception) { s_font = null; }
        return s_font;
    }

    /// <summary>The width (px) of <paramref name="text"/> at the HUD size.</summary>
    public static float Measure(Font? font, string text)
        => font is { } f ? Raylib.MeasureTextEx(f, text, PossessionStation.HudFontSize, 1f).X : Raylib.MeasureText(text, PossessionStation.HudFontSize);

    public static void Draw(Font? font, string text, int x, int y, Color colour)
    {
        if (font is { } f) Raylib.DrawTextEx(f, text, new Vector2(x, y), PossessionStation.HudFontSize, 1f, colour);
        else Raylib.DrawText(text, x, y, PossessionStation.HudFontSize, colour);
    }
}
