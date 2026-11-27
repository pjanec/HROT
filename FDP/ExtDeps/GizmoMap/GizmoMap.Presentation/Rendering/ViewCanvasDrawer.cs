using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Raylib_cs;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1d-2 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.6 R2-1, "S1d-2 AS BUILT") — THE one screen-space drawer of the
    /// map's post-scene pass, 2-D and 3-D alike (<c>DebugGizmoLayer.DrawOverlay</c>, after the entity cards): a <see cref="CoordinateSpace.ViewCanvas"/>
    /// primitive through the canvas's fit (<see cref="ViewCanvas.Fit"/>), a <see cref="CoordinateSpace.Screen"/> one in plain view pixels. Lines, boxes
    /// (fill + outline), discs (fill + ring), filled triangles and text (aligned, its baseline at <c>TextY</c> for the canvas); anything else is
    /// counted in <see cref="Skipped"/>, never drawn as world. Called outside any camera mode. Allocation: only the text's string (as every gizmo text).
    /// </summary>
    public sealed class ViewCanvasDrawer
    {
        /// <summary>Where a Roboto glyph box's baseline sits, as a fraction of the size (raylib sizes a glyph box ascent + descent: 1900 / 2400).</summary>
        public const float BaselineFraction = 0.79f;

        /// <summary>Primitives the last <see cref="Draw"/> drew.</summary>
        public int Drawn { get; private set; }

        /// <summary>Primitives the last <see cref="Draw"/> could not draw (a shape it has no case for).</summary>
        public int Skipped { get; private set; }

        /// <summary>Draws <paramref name="primitives"/> into a view of <paramref name="viewSize"/> pixels (outside any camera mode).</summary>
        public void Draw(IReadOnlyList<DebugPrimitive> primitives, Vector2 viewSize)
        {
            Drawn = Skipped = 0;
            if (primitives.Count == 0) return;
            ViewCanvas.Fit(viewSize, out var origin, out float scale);
            for (int i = 0; i < primitives.Count; i++)
            {
                var p = primitives[i];
                bool canvas = p.Space == CoordinateSpace.ViewCanvas;
                if (!canvas && p.Space != CoordinateSpace.Screen) { Skipped++; continue; }
                var o = canvas ? origin : Vector2.Zero;
                float k = canvas ? scale : 1f;
                if (DrawOne(in p, o, k, canvas)) Drawn++; else Skipped++;
            }
        }

        private static Color C(Rgba32 c) => new Color(c.R, c.G, c.B, c.A);

        private static bool DrawOne(in DebugPrimitive p, Vector2 o, float k, bool canvas)
        {
            switch (p.Shape)
            {
                case DebugPrimitiveShape.Line:
                {
                    float w = MathF.Max(1f, (p.ThicknessU16 > 0 ? p.ThicknessU16 / 10f : 1f) * k);
                    Raylib.DrawLineEx(o + new Vector2(p.LineStart.X, p.LineStart.Y) * k, o + new Vector2(p.LineEnd.X, p.LineEnd.Y) * k, w, C(p.Color));
                    return true;
                }
                case DebugPrimitiveShape.Box2D:
                {
                    var c = o + new Vector2(p.BoxCenterX, p.BoxCenterY) * k;
                    var e = new Vector2(p.BoxExtentX, p.BoxExtentY) * k;
                    var r = new Rectangle(c.X - e.X, c.Y - e.Y, e.X * 2f, e.Y * 2f);
                    if (p.FillColor.A > 0) Raylib.DrawRectangleRec(r, C(p.FillColor));
                    float w = p.ThicknessU16 / 10f * k;
                    if (p.Color.A > 0 && w > 0f) Raylib.DrawRectangleLinesEx(r, MathF.Max(1f, w), C(p.Color));
                    return true;
                }
                case DebugPrimitiveShape.Sphere:
                {
                    var c = o + new Vector2(p.SphereCenter.X, p.SphereCenter.Y) * k;
                    float rad = p.SphereRadius * k;
                    if (p.FillColor.A > 0) Raylib.DrawCircleV(c, rad, C(p.FillColor));
                    float w = p.ThicknessU16 / 10f * k;
                    if (p.Color.A > 0 && w > 0f) Raylib.DrawRing(c, MathF.Max(0f, rad - MathF.Max(1f, w)), rad, 0f, 360f, 32, C(p.Color));
                    return true;
                }
                case DebugPrimitiveShape.FilledTriangle:
                {
                    var a = o + p.TriA * k; var b = o + p.TriB * k; var t = o + p.TriC * k;
                    // screen y is down: raylib keeps a counter-clockwise ON-SCREEN triangle (a negative cross here) — draw the kept winding
                    var (ka, kb, kc) = DebugPrimitiveRenderer2D.KeptWinding(a, b, t, mirrored: false);
                    Raylib.DrawTriangle(ka, kb, kc, C(p.Color));
                    return true;
                }
                case DebugPrimitiveShape.Text:
                {
                    var font = GizmoTextDraw.Font;
                    string str = GizmoTextDraw.TextOf(in p);
                    float px = GizmoTextDraw.PixelSize(in p) * k;
                    var at = o + new Vector2(p.TextX, p.TextY) * k;
                    if (canvas)
                    {
                        var align = ViewCanvas.AlignOf(in p);
                        if (align != ViewCanvasAlign.Left)
                        {
                            float w = Raylib.MeasureTextEx(font, str, px, 1f).X;
                            at.X -= align == ViewCanvasAlign.Centre ? w * 0.5f : w;
                        }
                        at.Y -= px * BaselineFraction;
                    }
                    else at.Y += p.LineOffsetPx;
                    GizmoTextDraw.DrawStyled(font, str, at, px, 1f, 1f, p.TextStyle, C(p.Color));
                    return true;
                }
                default:
                    return false;
            }
        }
    }
}
