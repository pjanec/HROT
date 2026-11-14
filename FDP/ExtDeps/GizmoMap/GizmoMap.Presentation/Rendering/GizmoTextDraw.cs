using System;
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

        /// <summary>⭐ B-17 P7 ⑥ — the box colours (sheet <c>map-gizmo-look-2.svg</c>): a backdrop is black at ~60 %, a button is the ImGui button blue with its border.</summary>
        public static readonly Color BackdropFill = new Color((byte)0, (byte)0, (byte)0, (byte)155);
        public static readonly Color ButtonFill = new Color((byte)0x23, (byte)0x41, (byte)0x5F, (byte)255);
        public static readonly Color ButtonBorder = new Color((byte)0x33, (byte)0x69, (byte)0xAD, (byte)255);

        /// <summary>Box padding in screen px: 6 at the sides, 3 above and below.</summary>
        public const float PadX = 6f, PadY = 3f;

        /// <summary>⭐ B-17 P7 ⑥ — the screen size of the box a <paramref name="style"/> text of <paramref name="textWidthPx"/> × <paramref name="px"/> gets (Plain ⇒ the bare text).</summary>
        public static Vector2 BoxSize(GizmoTextStyle style, float textWidthPx, float px)
            => style == GizmoTextStyle.Plain ? new Vector2(textWidthPx, px) : new Vector2(textWidthPx + 2f * PadX, px + 2f * PadY);

        /// <summary>
        /// ⭐ B-17 P7 ⑥ — THE one text drawer: the box (when <paramref name="style"/> asks for one) and then the text, at <paramref name="at"/> (the text's own
        /// top-left). <paramref name="scale"/> is 1 on screen and <c>1 / camera zoom</c> inside a camera mode, where <paramref name="px"/> and
        /// <paramref name="spacing"/> arrive already scaled — so the padding, the border and the corner radius scale with it and the box stays a constant
        /// number of screen pixels. Allocates nothing.
        /// </summary>
        public static void DrawStyled(Font font, string str, Vector2 at, float px, float spacing, float scale, GizmoTextStyle style, Color color)
        {
            if (style != GizmoTextStyle.Plain)
            {
                Vector2 m = Raylib.MeasureTextEx(font, str, px, spacing);
                float padX = PadX * scale, padY = PadY * scale;
                var box = new Rectangle(at.X - padX, at.Y - padY, m.X + 2f * padX, m.Y + 2f * padY);
                float roundness = 2f * (3f * scale) / MathF.Min(box.Width, box.Height);   // a 3 px corner radius
                if (style == GizmoTextStyle.Button)
                {
                    Raylib.DrawRectangleRounded(box, roundness, 4, ButtonBorder);
                    float b = 1f * scale;
                    var inner = new Rectangle(box.X + b, box.Y + b, box.Width - 2f * b, box.Height - 2f * b);
                    Raylib.DrawRectangleRounded(inner, 2f * (2f * scale) / MathF.Min(inner.Width, inner.Height), 4, ButtonFill);
                }
                else
                    Raylib.DrawRectangleRounded(box, roundness, 4, BackdropFill);
            }
            Raylib.DrawTextEx(font, str, at, px, spacing, color);
        }

        /// <summary>Draws <paramref name="p"/>'s text at a SCREEN position (outside any camera mode), with its line offset — and its box (<see cref="DebugPrimitive.TextStyle"/>).</summary>
        public static void DrawScreen(in DebugPrimitive p, Vector2 screen, Color color)
            => DrawStyled(Font, TextOf(in p), new Vector2(screen.X, screen.Y + p.LineOffsetPx), PixelSize(p), 1f, 1f, p.TextStyle, color);
    }
}
