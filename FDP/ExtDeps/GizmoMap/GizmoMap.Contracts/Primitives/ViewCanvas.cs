using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>A <see cref="CoordinateSpace.ViewCanvas"/> text's horizontal alignment: where its <c>TextX</c> sits on the glyphs.</summary>
    public enum ViewCanvasAlign : short { Left = 0, Centre = 1, Right = 2 }

    /// <summary>
    /// ⭐⭐ Simulated devices S1d-2 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.6 R2-1, "S1d-2 AS BUILT") — THE view HUD canvas: the one
    /// place its size, its fit into a view and its text conventions live, for the producer (a HUD projector on the BRAIN, which never knows the
    /// station's window) and the drawer (the station's screen pass) alike. ⭐ Why not <see cref="CoordinateSpace.Screen"/>: screen pixels from
    /// the top-left cannot place a HUD whose window size the producer does not know — a corner bracket, a centred reticle and a bottom-centre
    /// readout need the view's size. The canvas is a fixed 16:9 reference fitted UNIFORMLY (the reticle stays square) and centred; a HUD that
    /// darkens outside its window draws past the canvas edge to cover the bands. 0 B.
    /// </summary>
    public static class ViewCanvas
    {
        /// <summary>The canvas in canvas pixels (16:9).</summary>
        public const float Width = 1280f, Height = 720f;

        /// <summary>Where the canvas lands in a view of <paramref name="viewSize"/> pixels: its top-left and its uniform scale (canvas px → view px).</summary>
        public static void Fit(Vector2 viewSize, out Vector2 origin, out float scale)
        {
            scale = viewSize.X <= 0f || viewSize.Y <= 0f ? 1f : MathF.Min(viewSize.X / Width, viewSize.Y / Height);
            origin = new Vector2((viewSize.X - Width * scale) * 0.5f, (viewSize.Y - Height * scale) * 0.5f);
        }

        /// <summary>A canvas point in view pixels.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ToView(Vector2 canvas, Vector2 origin, float scale) => origin + canvas * scale;

        /// <summary>The alignment of a canvas text (its <see cref="DebugPrimitive.LineOffsetPx"/>; the ONE reader).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ViewCanvasAlign AlignOf(in DebugPrimitive p) => (ViewCanvasAlign)p.LineOffsetPx;

        /// <summary>A canvas text: <paramref name="x"/> per <paramref name="align"/>, <paramref name="baseline"/> its baseline, <paramref name="px"/> its
        /// size in canvas pixels. The ONE writer of the alignment.</summary>
        public static DebugPrimitive Text(float x, float baseline, FixedString32 text, Rgba32 color, float px, byte layer,
                                          ViewCanvasAlign align = ViewCanvasAlign.Left, GizmoTextStyle style = GizmoTextStyle.Halo)
        {
            var p = DebugPrimitive.MakeText(x, baseline, text, color, CoordinateSpace.ViewCanvas, layer, px, 0f, style);
            p.LineOffsetPx = (short)align;
            return p;
        }

        /// <summary>A canvas line, <paramref name="width"/> canvas pixels wide.</summary>
        public static DebugPrimitive Line(Vector2 a, Vector2 b, Rgba32 color, float width, byte layer)
        {
            var p = DebugPrimitive.MakeLine(new Vector3(a, 0f), new Vector3(b, 0f), color, width, SizeMode.ScreenPixels, PipelineTarget.All, layer);
            p.Space = CoordinateSpace.ViewCanvas;
            return p;
        }

        /// <summary>A canvas rectangle from its top-left <paramref name="at"/> and <paramref name="size"/>: filled with <paramref name="fill"/>, outlined
        /// <paramref name="outlineWidth"/> wide in <paramref name="outline"/> (alpha 0 ⇒ no outline).</summary>
        public static DebugPrimitive Rect(Vector2 at, Vector2 size, Rgba32 fill, Rgba32 outline, float outlineWidth, byte layer)
        {
            var p = DebugPrimitive.MakeBox2D(at + size * 0.5f, size * 0.5f, outline, 0f, outlineWidth, SizeMode.ScreenPixels, PipelineTarget.All, layer, fill);
            p.Space = CoordinateSpace.ViewCanvas;
            return p;
        }

        /// <summary>A canvas disc of <paramref name="radius"/>: filled with <paramref name="fill"/>, ringed <paramref name="ringWidth"/> wide in
        /// <paramref name="ring"/> (alpha 0 ⇒ no ring).</summary>
        public static DebugPrimitive Disc(Vector2 centre, float radius, Rgba32 fill, Rgba32 ring, float ringWidth, byte layer)
        {
            var p = DebugPrimitive.MakeSphere(new Vector3(centre, 0f), radius, ring, ringWidth, SizeMode.ScreenPixels, PipelineTarget.All, layer, fill);
            p.Space = CoordinateSpace.ViewCanvas;
            return p;
        }

        /// <summary>A filled canvas triangle.</summary>
        public static DebugPrimitive Triangle(Vector2 a, Vector2 b, Vector2 c, Rgba32 color, byte layer)
        {
            var p = DebugPrimitive.MakeFilledTriangle(a, b, c, color, PipelineTarget.All, layer);
            p.Space = CoordinateSpace.ViewCanvas;
            p.SizeMode = SizeMode.ScreenPixels;
            return p;
        }
    }
}
