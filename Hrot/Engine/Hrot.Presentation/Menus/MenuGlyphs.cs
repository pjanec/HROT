using System.Numerics;
using NodeEditor.Core.Widgets;

namespace Hrot.UI.Common.Menus;

/// <summary>
/// ⭐ Terrain authoring (docs/UX/terrain-authoring-2-blocks.svg) — a menu row's icon key looked up in the editor's ONE icon table (<see cref="Icons"/>): <c>terrain/reroll</c> is a dice in amber, and so on.
/// Installed as <c>MenuIconRenderer.GlyphResolver</c>; a key the table does not know falls through to the atlas resolver as before.
/// </summary>
public static class MenuGlyphs
{
    public static bool Resolve(string key, out string glyph, out Vector4 colour)
    {
        if (Icons.TryGet(key, out var icon)) { glyph = icon.Glyph; colour = icon.Color; return true; }
        glyph = ""; colour = default;
        return false;
    }
}
