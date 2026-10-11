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

        /// <summary>Draws <paramref name="p"/>'s text at a SCREEN position (outside any camera mode), with its line offset.</summary>
        public static void DrawScreen(in DebugPrimitive p, Vector2 screen, Color color)
            => Raylib.DrawTextEx(Font, p.TextContent.ToString(), new Vector2(screen.X, screen.Y + p.LineOffsetPx), PixelSize(p), 1f, color);
    }
}
