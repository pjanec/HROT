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
/// ⭐ CE-4128 — and the FILM's text (captions, title cards, graphics labels — <see cref="FilmText"/>): the ONE raylib text path, so a burned-in
/// subtitle is the editor's face too. 🔒 User, 2026-10-30: <i>"the font used for subtitles looks terrible, it is the default ugly raylib font.
/// pls use same font as imgui is using for everything else"</i>. Baked larger (a title card is drawn up to ~h/14) and with Latin-1 + dashes and quotes.
/// </summary>
public static class HudText
{
    /// <summary>The size the face is baked at (px): larger than drawn, so the down-scaled glyphs stay crisp.</summary>
    public const int BakeSize = 64;

    /// <summary>The glyphs baked: printable ASCII and Latin-1, plus the dashes, quotes and ellipsis a caption may carry (anything else draws '?').</summary>
    private static int[] Codepoints()
    {
        var cps = new System.Collections.Generic.List<int>();
        for (int c = 32; c < 127; c++) cps.Add(c);
        for (int c = 160; c < 256; c++) cps.Add(c);
        cps.AddRange(new[] { 0x2013, 0x2014, 0x2018, 0x2019, 0x201C, 0x201D, 0x2026, 0x2022 });
        return cps.ToArray();
    }

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
            var cps = Codepoints();
            var font = Raylib.LoadFontFromMemory(".ttf", bytes, BakeSize, cps, cps.Length);
            if (font.Texture.Id == 0) return null;
            Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);
            s_font = font;
        }
        catch (Exception) { s_font = null; }
        return s_font;
    }

    // ── ⭐ S1d-3 — the HUD's colour icons (R-4139: FontAwesome glyphs drawn in the colour of their meaning) ─────────────────────────────────────────
    /// <summary>FontAwesome solid: the eye.</summary>
    public const string Eye = "\uf06e";
    /// <summary>FontAwesome solid: the binoculars.</summary>
    public const string Binoculars = "\uf1e5";
    /// <summary>FontAwesome solid: the crosshairs.</summary>
    public const string Crosshairs = "\uf05b";
    /// <summary>FontAwesome solid: the video camera (⭐ SD-S2d — the drone ground station's VIEW label).</summary>
    public const string Video = "\uf03d";

    private static Font? s_icons;
    private static bool s_iconsTried;

    /// <summary>The HUD's icon face (FontAwesome solid, the few glyphs above), loaded on first use; null ⇒ no icons (the label still reads).</summary>
    public static Font? Icons()
    {
        if (s_iconsTried) return s_icons;
        s_iconsTried = true;
        try
        {
            var bytes = EmbeddedFontResources.GetFontAwesomeSolidTtfBytes();
            int[] cps = { 0xf06e, 0xf1e5, 0xf05b, 0xf03d };
            var font = Raylib.LoadFontFromMemory(".ttf", bytes, BakeSize, cps, cps.Length);
            if (font.Texture.Id == 0) return null;
            Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);
            s_icons = font;
        }
        catch (Exception) { s_icons = null; }
        return s_icons;
    }

    /// <summary>One icon of <see cref="Icons"/> at <paramref name="size"/> px, its top-left at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static void DrawIcon(string glyph, float x, float y, float size, Color colour)
    {
        if (Icons() is { } f) Raylib.DrawTextEx(f, glyph, new Vector2(x, y), size, 0f, colour);
    }

    /// <summary>The width (px) of <paramref name="text"/> at the HUD size.</summary>
    public static float Measure(Font? font, string text) => Measure(font, text, PossessionStation.HudFontSize);

    public static void Draw(Font? font, string text, int x, int y, Color colour) => Draw(font, text, x, y, PossessionStation.HudFontSize, colour);

    /// <summary>⭐ CE-4128 — the width (px) of <paramref name="text"/> at <paramref name="size"/> px (the film's sizes).</summary>
    public static float Measure(Font? font, string text, float size)
        => font is { } f ? Raylib.MeasureTextEx(f, text, size, size / 20f).X : Raylib.MeasureText(text, (int)size);

    /// <summary>⭐ CE-4128 — <paramref name="text"/> at <paramref name="size"/> px, its top-left at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static void Draw(Font? font, string text, float x, float y, float size, Color colour)
    {
        if (font is { } f) Raylib.DrawTextEx(f, text, new Vector2(x, y), size, size / 20f, colour);
        else Raylib.DrawText(text, (int)x, (int)y, (int)size, colour);
    }
}
