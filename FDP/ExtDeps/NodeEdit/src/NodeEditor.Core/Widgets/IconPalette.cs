using System.Numerics;

namespace NodeEditor.Core.Widgets;

/// <summary>
/// ⭐ Q-C1 — the ONE colour list for icons (docs/UX/DESIGN_Colour_Icons.md §2): the status tokens and one hue per view family. A call site never picks
/// its own colour (R-174); it names a token. Values are the mockup's (docs/UX/ui-conventions-visual.svg), readable on the dark slate and on the
/// neutral glyph-button background.
/// </summary>
public static class IconPalette
{
    private static Vector4 Hex(int rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

    // status tokens
    public static readonly Vector4 Error   = Hex(0xE5534B);
    public static readonly Vector4 Pending = Hex(0xE3B341);
    public static readonly Vector4 Ok      = Hex(0x57AB5A);
    public static readonly Vector4 Info    = Hex(0x539BF5);
    public static readonly Vector4 Muted   = Hex(0x8B93A1);

    // one hue per view family
    public static readonly Vector4 Hierarchy = Hex(0x6CB6FF);
    public static readonly Vector4 Type      = Hex(0xB083F0);
    public static readonly Vector4 Macro     = Hex(0xE3A04B);
    public static readonly Vector4 Layout    = Hex(0x4AC2B3);
    public static readonly Vector4 Window    = Hex(0xADBAC7);
}
