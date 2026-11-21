using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>⭐ CE-1146 CL-2 (docs/DESIGN_Map_3D_Mode.md §6aa.CL) — how a cloud layer's texture looks: crisp heaps, a soft sheet, or streaks.</summary>
    public enum CloudLook : byte { Puffy = 0, Sheet = 1, Streaks = 2 }

    /// <summary>
    /// ⭐ CE-1146 (§6aa.CL) — one cloud layer as the 3-D view draws it, in renderer terms (this assembly knows no weather): the height of its
    /// base (m, the scene's Z) and its thickness, the share of the sky it covers, its look, the size of one texture tile (m), how far the
    /// wind has carried it (m) and its colour.
    /// </summary>
    public readonly record struct SceneCloudLayer(float HeightZ, float ThicknessM, float Coverage, CloudLook Look, float TileM, Vector2 Drift, Color Tint);

    /// <summary>
    /// ⭐ CE-1146 CL-1…CL-5 (docs/DESIGN_Map_3D_Mode.md §6aa.CL; 🔒 user, 2026-10-22: <i>"yes exactly that"</i>) — the cloud decks, drawn as
    /// BACKGROUND by <see cref="MapCamera3D.BeginMode"/>: one flat translucent grid per layer around the eye, textured by tileable fbm noise
    /// whose covered share is the layer's coverage, fading out toward its rim, moved toward the eye into the far clip (a uniform scale about
    /// the eye changes nothing on screen), with no depth — so the world is always drawn over them. The noise, the thresholds and the
    /// geometry are pure (the rails run them); only <see cref="Draw"/> and <see cref="TextureFor"/> need a GL context.
    /// </summary>
    public static class CloudDeckRenderer
    {
        /// <summary>The texture's side (texels) and the grid's cells per side.</summary>
        public const int TextureSize = 128, Grid = 24;
        /// <summary>CL-4 — the deck's radius: this many times its height above or below the eye, within these bounds (m).</summary>
        public const float RadiusPerHeight = 10f, MinRadius = 6000f, MaxRadius = 30000f;
        /// <summary>CL-4 — the deck is scaled toward the eye until its farthest point lies within this share of the far clip.</summary>
        public const float FarClipShare = 0.45f;
        /// <summary>CL-3 — closer than this to a deck's base or top (m) the eye is IN it: none is drawn (the fog is the cloud).</summary>
        public const float InsideMargin = 5f;

        // ── the noise (pure) ─────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Tileable fbm value noise at (<paramref name="u"/>, <paramref name="v"/>) in [0, 1)² — five octaves, period one tile;
        /// <see cref="CloudLook.Streaks"/> varies six times faster along v than along u (streaks along u). About 0…1.</summary>
        public static float Noise(CloudLook look, float u, float v)
        {
            u -= MathF.Floor(u); v -= MathF.Floor(v);
            float sum = 0f, amp = 0.5f, norm = 0f;
            int cellsU = 4, cellsV = look == CloudLook.Streaks ? 24 : 4;
            for (int o = 0; o < 5; o++)
            {
                sum += amp * Lattice(u * cellsU, v * cellsV, cellsU, cellsV, o);
                norm += amp;
                amp *= 0.5f; cellsU *= 2; cellsV *= 2;
            }
            return sum / norm;
        }

        private static float Lattice(float x, float y, int periodX, int periodY, int octave)
        {
            int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Hash(x0, y0, periodX, periodY, octave), b = Hash(x0 + 1, y0, periodX, periodY, octave);
            float c = Hash(x0, y0 + 1, periodX, periodY, octave), d = Hash(x0 + 1, y0 + 1, periodX, periodY, octave);
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
        }

        private static float Hash(int x, int y, int periodX, int periodY, int octave)
        {
            x = ((x % periodX) + periodX) % periodX;
            y = ((y % periodY) + periodY) % periodY;
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)octave * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }

        private static readonly Dictionary<CloudLook, float[]> _fields = new();

        /// <summary>The noise on the texture's texels (cached per look; read-only).</summary>
        public static float[] Field(CloudLook look)
        {
            lock (_fields)
            {
                if (_fields.TryGetValue(look, out var f)) return f;
                f = new float[TextureSize * TextureSize];
                for (int y = 0; y < TextureSize; y++)
                    for (int x = 0; x < TextureSize; x++)
                        f[y * TextureSize + x] = Noise(look, (x + 0.5f) / TextureSize, (y + 0.5f) / TextureSize);
                _fields[look] = f;
                return f;
            }
        }

        /// <summary>CL-1 — the noise value above which <paramref name="coverage"/> of the texels lie (the field's own quantile).</summary>
        public static float Threshold(CloudLook look, float coverage)
        {
            coverage = Math.Clamp(coverage, 0f, 1f);
            if (coverage >= 1f) return float.NegativeInfinity;
            if (coverage <= 0f) return float.PositiveInfinity;
            var sorted = (float[])Field(look).Clone();
            Array.Sort(sorted);
            return sorted[(int)((1f - coverage) * (sorted.Length - 1))];
        }

        /// <summary>CL-2 — how soft the edge of a cloud is (in noise units) and how opaque its middle.</summary>
        public static float Softness(CloudLook look) => look switch { CloudLook.Sheet => 0.12f, CloudLook.Streaks => 0.06f, _ => 0.035f };
        public static float Density(CloudLook look) => look switch { CloudLook.Streaks => 0.55f, CloudLook.Sheet => 0.9f, _ => 0.95f };

        /// <summary>CL-1 — a texel's opacity 0…1 for noise <paramref name="n"/>: a soft step at the threshold, times the look's density; a
        /// full cover is opaque everywhere (an overcast).</summary>
        public static float Alpha(CloudLook look, float threshold, float n)
        {
            if (float.IsNegativeInfinity(threshold)) return Density(look);
            if (float.IsPositiveInfinity(threshold)) return 0f;
            float s = Softness(look);
            float t = Math.Clamp((n - threshold + s) / (2f * s), 0f, 1f);
            return t * t * (3f - 2f * t) * Density(look);
        }

        /// <summary>The texture's opacities for <paramref name="look"/> at <paramref name="coverage"/>, row by row (the rails read it).</summary>
        public static float[] Opacity(CloudLook look, float coverage)
        {
            var field = Field(look);
            float th = Threshold(look, coverage);
            var a = new float[field.Length];
            for (int i = 0; i < field.Length; i++) a[i] = Alpha(look, th, field[i]);
            return a;
        }

        // ── the geometry (pure) ──────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>CL-3 — the plane drawn for <paramref name="layer"/> seen from <paramref name="eyeZ"/>: its base when it is above the eye,
        /// its top when below; false inside it.</summary>
        public static bool PlaneOf(in SceneCloudLayer layer, float eyeZ, out float planeZ)
        {
            float top = layer.HeightZ + MathF.Max(0f, layer.ThicknessM);
            planeZ = eyeZ < layer.HeightZ ? layer.HeightZ : top;
            return eyeZ < layer.HeightZ - InsideMargin || eyeZ > top + InsideMargin;
        }

        /// <summary>CL-4 — the deck's radius around the eye for a plane <paramref name="height"/> above (or below) it.</summary>
        public static float RadiusFor(float height) => Math.Clamp(MathF.Abs(height) * RadiusPerHeight, MinRadius, MaxRadius);

        /// <summary>CL-4 — the factor every vertex is moved toward the eye by, so the farthest lies within <see cref="FarClipShare"/> of the
        /// far clip (1 when it already does).</summary>
        public static float ScaleFor(float radius, float height, float farClip)
            => MathF.Min(1f, farClip * FarClipShare / MathF.Max(1f, MathF.Sqrt(radius * radius + height * height)));

        /// <summary>CL-4 — the opacity at <paramref name="r"/> m from the point under the eye: full over the inner half, none at the rim.</summary>
        public static float Fade(float r, float radius)
        {
            float t = Math.Clamp((r - 0.5f * radius) / (0.5f * radius), 0f, 1f);
            return 1f - t * t * (3f - 2f * t);
        }

        // ── the drawing (GL) ─────────────────────────────────────────────────────────────────────────────────────────────────

        private static readonly Dictionary<(CloudLook Look, int Bucket), Texture2D> _textures = new();

        /// <summary>The coverage's step in the texture cache (sixteenths).</summary>
        public static int BucketOf(float coverage) => (int)MathF.Round(Math.Clamp(coverage, 0f, 1f) * 16f);

        /// <summary>The cached texture for <paramref name="look"/> at <paramref name="coverage"/> — white, its alpha the opacity; tiled,
        /// mipmapped. Needs a GL context.</summary>
        public static Texture2D TextureFor(CloudLook look, float coverage)
        {
            int bucket = BucketOf(coverage);
            if (_textures.TryGetValue((look, bucket), out var tex)) return tex;
            var opacity = Opacity(look, bucket / 16f);
            var image = Raylib.GenImageColor(TextureSize, TextureSize, new Color(255, 255, 255, 0));
            for (int y = 0; y < TextureSize; y++)
                for (int x = 0; x < TextureSize; x++)
                    Raylib.ImageDrawPixel(ref image, x, y, new Color(255, 255, 255, (int)(255f * opacity[y * TextureSize + x])));
            tex = Raylib.LoadTextureFromImage(image);
            Raylib.UnloadImage(image);
            Raylib.GenTextureMipmaps(ref tex);
            Raylib.SetTextureFilter(tex, TextureFilter.Trilinear);
            Raylib.SetTextureWrap(tex, TextureWrap.Repeat);
            _textures[(look, bucket)] = tex;
            return tex;
        }

        /// <summary>Layers drawn by the last frame (rails).</summary>
        public static int LayersDrawn { get; private set; }

        /// <summary>
        /// CL-3…CL-5 — draws <paramref name="layers"/> for <paramref name="camera"/>, farthest first, inside <c>BeginMode3D</c> and before the
        /// world: no depth test, no depth write, both faces; the state is restored after.
        /// </summary>
        public static void Draw(MapCamera3D camera, IReadOnlyList<SceneCloudLayer> layers)
        {
            LayersDrawn = 0;
            if (layers.Count == 0) return;
            var eye = camera.Eye;
            float far = camera.FarClip;
            Span<int> order = stackalloc int[Math.Min(layers.Count, 8)];
            int n = 0;
            for (int i = 0; i < layers.Count && n < order.Length; i++) order[n++] = i;
            for (int i = 1; i < n; i++)   // farthest plane first
                for (int j = i; j > 0 && Distance(layers[order[j]], eye.Z) > Distance(layers[order[j - 1]], eye.Z); j--)
                    (order[j], order[j - 1]) = (order[j - 1], order[j]);

            Rlgl.DrawRenderBatchActive();
            Rlgl.DisableDepthTest();
            Rlgl.DisableDepthMask();
            Rlgl.DisableBackfaceCulling();
            for (int k = 0; k < n; k++)
            {
                var layer = layers[order[k]];
                if (layer.Coverage <= 0f || !PlaneOf(layer, eye.Z, out float planeZ)) continue;
                float h = planeZ - eye.Z;
                float radius = RadiusFor(h), scale = ScaleFor(radius, h, far), tile = MathF.Max(100f, layer.TileM);
                var tex = TextureFor(layer.Look, layer.Coverage);
                if (tex.Id == 0) continue;
                float u0 = Frac((eye.X + layer.Drift.X) / tile), v0 = Frac((eye.Y + layer.Drift.Y) / tile);
                float step = 2f * radius / Grid;
                for (int gy = 0; gy < Grid; gy++)
                {
                    Rlgl.SetTexture(tex.Id);   // ⚠ per row: flushing the batch resets it to the default (white) texture
                    Rlgl.Begin(DrawMode.Quads);
                    for (int gx = 0; gx < Grid; gx++)
                    {
                        float x0 = -radius + gx * step, y0 = -radius + gy * step;
                        Vertex(eye, x0, y0, h, scale, radius, tile, u0, v0, layer.Tint);
                        Vertex(eye, x0, y0 + step, h, scale, radius, tile, u0, v0, layer.Tint);
                        Vertex(eye, x0 + step, y0 + step, h, scale, radius, tile, u0, v0, layer.Tint);
                        Vertex(eye, x0 + step, y0, h, scale, radius, tile, u0, v0, layer.Tint);
                    }
                    Rlgl.End();
                    Rlgl.DrawRenderBatchActive();
                }
                Rlgl.SetTexture(0);
                LayersDrawn++;
            }
            Rlgl.EnableBackfaceCulling();
            Rlgl.EnableDepthMask();
            Rlgl.EnableDepthTest();
        }

        private static float Distance(in SceneCloudLayer layer, float eyeZ)
            => PlaneOf(layer, eyeZ, out float z) ? MathF.Abs(z - eyeZ) : 0f;

        private static void Vertex(Vector3 eye, float dx, float dy, float h, float scale, float radius, float tile, float u0, float v0, Color tint)
        {
            float a = Fade(MathF.Sqrt(dx * dx + dy * dy), radius) * tint.A / 255f;
            Rlgl.Color4ub(tint.R, tint.G, tint.B, (byte)(255f * a));
            Rlgl.TexCoord2f(u0 + dx / tile, v0 + dy / tile);
            var p = HrotToRaylib.Position(eye + new Vector3(dx, dy, h) * scale);
            Rlgl.Vertex3f(p.X, p.Y, p.Z);
        }

        private static float Frac(float x) => x - MathF.Floor(x);
    }
}
