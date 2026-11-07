using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Raylib_cs;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// ⭐ CE-1033 S3 (M13) — THE gizmo text drawing, extracted from <see cref="DebugPrimitiveRenderer2D"/> so the 3-D map's label
    /// overlay draws a <see cref="DebugPrimitiveShape.Text"/> exactly as the 2-D map does: the same font, the same pixel size
    /// (<see cref="DebugPrimitive.ThicknessU16"/> carries it; 0 ⇒ 13 px), the same line offset. 📄 docs/DESIGN_Map_3D_Mode.md §3.4.
    /// </summary>
    public static class GizmoTextDraw
    {
        /// <summary>The renderer's default font size (px) when a Text names none.</summary>
        public const float DefaultPx = 13f;

        /// <summary>The pixel size a Text asks for.</summary>
        public static float PixelSize(in DebugPrimitive p) => p.ThicknessU16 > 0 ? p.ThicknessU16 : DefaultPx;

        /// <summary>The font gizmo text is drawn with: the host's embedded TTF if it loaded, else Raylib's built-in one.
        /// ⚠ Drawing with an invalid font texture corrupts the active render batch (missing lines AND no text) — hence the check.</summary>
        public static Font Font
            => DebugPrimitiveRenderer2D.TextFont is { } f && f.Texture.Id != 0 ? f : Raylib.GetFontDefault();

        /// <summary>
        /// ⭐ B-17 P1 — the intern map of the buffer being drawn (set by the drawing layer before it draws). A text longer than 31 characters
        /// (<c>DrawTextLong</c>) carries only its first 31 inline and its full text under <see cref="DebugPrimitive.StringHash"/>; ⛔ before this every
        /// gizmo text drawer used the inline preview, so a long line (the placement hint, a tool hint) was cut at 31 characters on every map.
        /// </summary>
        public static StringInternMap? Strings { get; set; }

        /// <summary>⭐ B-17 P1 — <paramref name="p"/>'s whole text: the interned string when it has one, else the inline text. The ONE reader.</summary>
        public static string TextOf(in DebugPrimitive p)
            => p.Space != CoordinateSpace.EntityLocal && p.StringHash != 0 && Strings?.TryResolve(p.StringHash) is { } full ? full : p.TextContent.ToString();

        /// <summary>Draws <paramref name="p"/>'s text at a SCREEN position (outside any camera mode), with its line offset.</summary>
        public static void DrawScreen(in DebugPrimitive p, Vector2 screen, Color color)
            => Raylib.DrawTextEx(Font, TextOf(in p), new Vector2(screen.X, screen.Y + p.LineOffsetPx), PixelSize(p), 1f, color);
    }
}
