using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Contracts.Symbology;
using Raylib_cs;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// ⭐ <c>CE-8608</c> (docs/designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md §6 S-2/S-6, R-8603; docs/UX/DESIGN_Unit_Symbols.md) — draws a <see cref="SymbolCode"/>
    /// with raylib, for both gizmo renderers: the <c>MilStd2525</c> and <c>Icon</c> primitives whose text parses as a land-unit code, on the 2-D map (world, upright on
    /// the north-up map, S-7) and in a card cell (screen). The size is the primitive's thickness (the frame's height in pixels; 0 ⇒ <see cref="DefaultHeightPx"/>).
    /// <para>⭐ R-8603, measured (<c>SymbolRendererFrameRail</c>): 500 symbols as raw vectors every frame cost ~40 ms of CPU — too much. So each DISTINCT symbol (code ×
    /// pixel height) is rasterised ONCE through the one <see cref="SymbolGeometry"/> into an image (2× supersampled, halved ⇒ antialiased), uploaded as a texture, and
    /// every draw after is one textured quad. A scenario has a few dozen distinct codes; the geometry stays the single source (the ImGui ORBAT icon uses it too).</para>
    /// 0 B per draw once a symbol is cached. ⚠ Needs a GL context (the first draw of a symbol uploads its texture).
    /// </summary>
    public static class SymbolRenderer
    {
        public const float DefaultHeightPx = 20f;
        private const int Supersample = 2;

        private struct Entry { public Texture2D Texture; public float CentreX, CentreY, Width, Height; }

        private static readonly Dictionary<long, Entry> _cache = new();
        private static readonly BoundsSink _bounds = new();
        private static readonly ImageSink _image = new();

        /// <summary>How many distinct symbols are cached (a texture each).</summary>
        public static int CachedCount => _cache.Count;

        /// <summary>The frame height a primitive asks for, in pixels.</summary>
        public static float HeightOf(in DebugPrimitive p) => p.ThicknessU16 > 0 ? p.ThicknessU16 * 0.1f : DefaultHeightPx;

        /// <summary>Draws in the 2-D map's world (inside <c>BeginMode2D</c>): one symbol pixel = 1/<paramref name="zoom"/> world units; upright on a north-up map.</summary>
        public static void DrawWorld(in SymbolCode s, Vector2 world, float heightPx, float zoom, bool worldYUp)
        {
            var e = Get(s, heightPx);
            float k = zoom > 0f ? 1f / zoom : 1f;
            var dest = worldYUp
                ? new Rectangle(world.X - e.CentreX * k, world.Y - (e.Height - e.CentreY) * k, e.Width * k, e.Height * k)
                : new Rectangle(world.X - e.CentreX * k, world.Y - e.CentreY * k, e.Width * k, e.Height * k);
            var src = new Rectangle(0, 0, e.Width, worldYUp ? -e.Height : e.Height);   // the north-up camera mirrors y: un-mirror the rows
            Raylib.DrawTexturePro(e.Texture, src, dest, Vector2.Zero, 0f, Color.White);
        }

        /// <summary>Draws on the screen (outside the camera), the symbol's centre at <paramref name="screen"/>.</summary>
        public static void DrawScreen(in SymbolCode s, Vector2 screen, float heightPx)
        {
            var e = Get(s, heightPx);
            Raylib.DrawTexturePro(e.Texture, new Rectangle(0, 0, e.Width, e.Height),
                new Rectangle(MathF.Round(screen.X - e.CentreX), MathF.Round(screen.Y - e.CentreY), e.Width, e.Height), Vector2.Zero, 0f, Color.White);
        }

        /// <summary>
        /// ⭐ CE-8610 — the symbol's pixel box (width, height) and where its centre sits in it, from the geometry alone (no GL: card LAYOUT runs it, a headless
        /// rail too). The same numbers <see cref="DrawInBox"/> draws with.
        /// </summary>
        public static (float Width, float Height, float CentreX, float CentreY) Measure(in SymbolCode s, float heightPx)
        {
            int h = Math.Clamp((int)MathF.Round(heightPx), 6, 256);
            long key = (uint)s.GetHashCode() | ((long)h << 32);
            if (_boxes.TryGetValue(key, out var b)) return b;
            _bounds.Reset();
            SymbolGeometry.Build(s, Vector2.Zero, h, _bounds);
            const float Pad = 2f;
            float minX = MathF.Floor(_bounds.Min.X - Pad), minY = MathF.Floor(_bounds.Min.Y - Pad);
            b = ((int)MathF.Ceiling(_bounds.Max.X + Pad - minX), (int)MathF.Ceiling(_bounds.Max.Y + Pad - minY), -minX, -minY);
            _boxes[key] = b;
            return b;
        }

        /// <summary>⭐ CE-8610 — draws the symbol with its box's top-left at <paramref name="topLeft"/> (a card's left cell).</summary>
        public static void DrawInBox(in SymbolCode s, Vector2 topLeft, float heightPx)
        {
            var e = Get(s, heightPx);
            Raylib.DrawTexturePro(e.Texture, new Rectangle(0, 0, e.Width, e.Height),
                new Rectangle(MathF.Round(topLeft.X), MathF.Round(topLeft.Y), e.Width, e.Height), Vector2.Zero, 0f, Color.White);
        }

        private static readonly Dictionary<long, (float, float, float, float)> _boxes = new();

        /// <summary>
        /// ⭐ CE-8613 (DESIGN_Stochastic_Unit_UI UI-8, mockup ④) — an icon text <c>meter/&lt;0..5&gt;</c>: a 5-box LEVEL meter, pixel-sized at a world point (the
        /// detection overlay's pair level) — a gizmo cannot place pixel offsets in the world, so the renderer draws the boxes. 0 B.
        /// </summary>
        public static bool TryMeter(in FixedString32 text, out int filled)
        {
            filled = 0;
            ReadOnlySpan<byte> b = System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.AsRef(in text), 1));
            if (b[0] != (byte)'m' || b[1] != (byte)'e' || b[2] != (byte)'t' || b[3] != (byte)'e' || b[4] != (byte)'r' || b[5] != (byte)'/') return false;
            int d = b[6] - '0';
            if (d < 0 || d > 5 || b[7] != 0) return false;
            filled = d;
            return true;
        }

        /// <summary>The meter (68 × 20 px black back, five 10 px boxes 12 px apart — mockup ④) centred at <paramref name="world"/>, inside the 2-D camera.</summary>
        public static void DrawMeterWorld(int filled, Color on, Vector2 world, float zoom)
        {
            float k = zoom > 0f ? 1f / zoom : 1f;
            Raylib.DrawRectangleRec(new Rectangle(world.X - 34f * k, world.Y - 10f * k, 68f * k, 20f * k), new Color((byte)0, (byte)0, (byte)0, (byte)190));
            var off = new Color((byte)51, (byte)51, (byte)51, (byte)255);
            for (int i = 0; i < 5; i++)
                Raylib.DrawRectangleRec(new Rectangle(world.X + (-30f + i * 12f) * k, world.Y - 5f * k, 10f * k, 10f * k), i < filled ? on : off);
        }

        /// <summary>
        /// ⭐ CE-8611 (DESIGN_Stochastic_Unit_UI UI-13) — the cached texture of a symbol for an ImGui image (an ORBAT row): its id (rlImGui's raylib texture id)
        /// and its width over its height. The SAME texture the map draws — one geometry, one cache.
        /// </summary>
        public static (nint TextureId, float Aspect) TextureOf(in SymbolCode s, float heightPx)
        {
            var e = Get(s, heightPx);
            return ((nint)e.Texture.Id, e.Height > 0f ? e.Width / e.Height : 1f);
        }

        private static Entry Get(in SymbolCode s, float heightPx)
        {
            int h = Math.Clamp((int)MathF.Round(heightPx), 6, 256);
            long key = (uint)s.GetHashCode() | ((long)h << 32);
            if (_cache.TryGetValue(key, out var e)) return e;
            e = Build(s, h);
            _cache[key] = e;
            return e;
        }

        private static Entry Build(in SymbolCode s, int h)
        {
            var (bw, bh, cx, cy) = Measure(s, h);
            float minX = -cx, minY = -cy;
            int w = (int)bw, hh = (int)bh;
            var image = Raylib.GenImageColor(w * Supersample, hh * Supersample, new Color(0, 0, 0, 0));
            _image.Begin(image, new Vector2(-minX, -minY), Supersample);
            SymbolGeometry.Build(s, Vector2.Zero, h, _image);
            image = _image.End();
            Raylib.ImageResize(ref image, w, hh);
            var texture = Raylib.LoadTextureFromImage(image);
            Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);
            Raylib.UnloadImage(image);
            return new Entry { Texture = texture, CentreX = -minX, CentreY = -minY, Width = w, Height = hh };
        }

        private static Color C(uint rgba) => new((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);

        /// <summary>The symbol's pixel extent, from the same geometry (so the texture fits exactly what is drawn).</summary>
        private sealed class BoundsSink : ISymbolSink
        {
            public Vector2 Min, Max;
            public void Reset() { Min = new Vector2(float.MaxValue); Max = new Vector2(float.MinValue); }
            private void Add(Vector2 p, float r) { Min = Vector2.Min(Min, p - new Vector2(r)); Max = Vector2.Max(Max, p + new Vector2(r)); }
            public void Polygon(ReadOnlySpan<Vector2> points, uint fill, uint outline, float outlinePx) { foreach (var p in points) Add(p, outlinePx); }
            public void Line(Vector2 a, Vector2 b, uint colour, float px) { Add(a, px); Add(b, px); }
            public void Ellipse(Vector2 c, float rx, float ry, uint fill, uint outline, float outlinePx) { Add(c - new Vector2(rx, ry), outlinePx); Add(c + new Vector2(rx, ry), outlinePx); }
            public void Text(Vector2 c, string text, float sizePx, uint colour) => Add(c, sizePx * 0.5f * Math.Max(1, text.Length));
        }

        /// <summary>Rasterises the geometry into an image on the CPU, scaled by the supersample factor.</summary>
        private sealed class ImageSink : ISymbolSink
        {
            private Image _img; private Vector2 _o; private float _s;
            public void Begin(Image img, Vector2 origin, float scale) { _img = img; _o = origin; _s = scale; }
            public Image End() => _img;
            private Vector2 P(Vector2 v) => (v + _o) * _s;
            private int T(float px) => Math.Max(1, (int)MathF.Round(px * _s));

            public void Polygon(ReadOnlySpan<Vector2> points, uint fill, uint outline, float outlinePx)
            {
                if ((fill & 0xFF) != 0)
                {
                    var f = C(fill); var a = P(points[0]);
                    for (int i = 1; i + 1 < points.Length; i++)
                    {
                        var b = P(points[i]); var c = P(points[i + 1]);
                        Raylib.ImageDrawTriangle(ref _img, a, b, c, f); Raylib.ImageDrawTriangle(ref _img, a, c, b, f);   // either winding
                    }
                }
                if (outlinePx > 0f && (outline & 0xFF) != 0)
                {
                    var o = C(outline); int t = T(outlinePx);
                    for (int i = 0; i < points.Length; i++)
                    {
                        var a = P(points[i]);
                        Raylib.ImageDrawLineEx(ref _img, a, P(points[(i + 1) % points.Length]), t, o);
                        if (t > 2) Raylib.ImageDrawCircleV(ref _img, a, t / 2, o);   // a round joint, no notch at the corners
                    }
                }
            }

            public void Line(Vector2 a, Vector2 b, uint colour, float px) => Raylib.ImageDrawLineEx(ref _img, P(a), P(b), T(px), C(colour));

            public void Ellipse(Vector2 centre, float rx, float ry, uint fill, uint outline, float outlinePx)
            {
                int n = Math.Clamp((int)(MathF.Max(rx, ry) * 1.5f) + 10, 12, 48);
                Span<Vector2> ring = stackalloc Vector2[48];
                for (int i = 0; i < n; i++)
                {
                    float a = i * MathF.Tau / n;
                    ring[i] = centre + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
                }
                Polygon(ring.Slice(0, n), fill, outline, outlinePx);
            }

            public void Text(Vector2 centre, string text, float sizePx, uint colour)
            {
                var font = GizmoTextDraw.Font;
                float px = MathF.Round(sizePx * _s);
                var size = Raylib.MeasureTextEx(font, text, px, _s);
                Span<sbyte> ascii = stackalloc sbyte[16];
                int n = Math.Min(text.Length, 15);
                for (int i = 0; i < n; i++) ascii[i] = (sbyte)text[i];
                ascii[n] = 0;
                unsafe
                {
                    fixed (Image* img = &_img)
                    fixed (sbyte* t = ascii)
                        Raylib.ImageDrawTextEx(img, font, t, P(centre) - size * 0.5f, px, _s, C(colour));
                }
            }
        }
    }
}
