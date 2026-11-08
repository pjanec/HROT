using Font = Raylib_cs.Font;
using RlApi = Raylib_cs.Raylib;

namespace Fdp.Presentation.Fonts;

/// <summary>
/// ⭐ B-17 P7 ⑥b — THE one loader of the font gizmo text is drawn with, called by every host (<c>RaylibPresentationShell</c>, <c>PlayerApplication</c>) so
/// they cannot differ: Roboto at 32 px (crisp when scaled down to 9–13 px), bilinear, with an EXPLICIT codepoint list — ASCII plus the typographic glyphs a
/// hint wants (<c>· ° — – × …</c>). ⛔ Raylib's default set is ASCII only, so a "°" drew as "?" and every hint spelled "deg". The arrows and the tick are
/// NOT in Roboto (the symbol fallback carries them — <c>EditorFontCoverageTests</c>), so they are not listed: a glyph the font lacks draws as a box.
/// It lives here, not in <c>GizmoMap.Presentation</c>, because the TTF resource is Fdp.Presentation's and GizmoMap must not reference it.
/// </summary>
public static class GizmoFont
{
    /// <summary>Every codepoint baked: 32–126 and the Roboto glyphs above (checked against the TTF's cmap).</summary>
    public static readonly int[] Codepoints = Build();

    private static int[] Build()
    {
        int[] extra = { 0x00B7, 0x00B0, 0x2014, 0x2013, 0x00D7, 0x2026 };
        var list = new int[(126 - 32 + 1) + extra.Length];
        int n = 0;
        for (int c = 32; c <= 126; c++) list[n++] = c;
        for (int i = 0; i < extra.Length; i++) list[n++] = extra[i];
        return list;
    }

    /// <summary>Loads the font (needs the GL context), registers it as <see cref="GizmoMap.Presentation.DebugPrimitiveRenderer2D.TextFont"/> and returns it.</summary>
    public static Font Load()
    {
        byte[] ttf = EmbeddedFontResources.GetRobotoRegularTtfBytes();
        Font font = RlApi.LoadFontFromMemory(".ttf", ttf, 32, Codepoints, Codepoints.Length);
        RlApi.SetTextureFilter(font.Texture, Raylib_cs.TextureFilter.Bilinear);
        GizmoMap.Presentation.DebugPrimitiveRenderer2D.TextFont = font;
        return font;
    }
}
