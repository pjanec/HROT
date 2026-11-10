using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    public static partial class TerrainWorldMesh
    {
        /// <summary>⭐ CE-4392 X-7 — the soot round a breach (its colour and the mottled pattern in the 3-D layer).</summary>
        public const string ScorchMaterial = "scorch";

        /// <summary>⭐ CE-4392 X-7 — the lighter, streaked soot further from a breach.</summary>
        public const string ScorchLightMaterial = "scorch-light";

        /// <summary>
        /// ⭐ CE-4392 (DESIGN_Map_3D_Mode §6aj X-7; 🔒 user: "black explosion marks all around the hole so it does not look like something nice") — a ragged ring of soot on BOTH faces
        /// of the wall round breach <paramref name="b"/>: from the opening's edge out to a jittered rim 0.25–0.9 m away (larger for a larger breach), kept on the panel's face, 1.5 cm
        /// off it. Placed by a hash of the breach, so every node and every rebuild draws the same marks. Presentation only.
        /// </summary>
        public static void AddScorch(TerrainWallPanel panel, in WallBreach b, List<Vector3> v, List<int> t, List<TerrainTriangleTag> g)
        {
            float length = Vector2.Distance(panel.A, panel.B);
            if (length < 1e-3f) return;
            var dir = (panel.B - panel.A) / length;
            var n = new Vector2(-dir.Y, dir.X);
            float w = b.To - b.From, h = b.Top - b.Bottom;
            if (w <= 0f || h <= 0f) return;
            float margin = Math.Clamp((0.25f * MathF.Min(w, h)) + 0.2f, 0.25f, 0.9f);
            float cu = (b.From + b.To) * 0.5f, cz = (b.Bottom + b.Top) * 0.5f;
            uint seed = (uint)(b.Panel * 73856093) ^ (uint)(MathF.Round(b.From * 100f) * 19349663) ^ (uint)(MathF.Round(b.Bottom * 100f) * 83492791) ^ 0x2545F491u;
            float R() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed & 0xFFFFFF) / (float)0x1000000; }

            const int K = 32;   // points round the opening
            Span<float> iu = stackalloc float[K], iz = stackalloc float[K], mu = stackalloc float[K], mz = stackalloc float[K], ou = stackalloc float[K], oz = stackalloc float[K];
            float perimeter = 2f * (w + h);
            for (int k = 0; k < K; k++)
            {
                // a point on the opening's rectangle, walked round it; pushed out away from its centre twice: the black ring, then the lighter, spiky one (blast streaks)
                float s = perimeter * k / K, u, z;
                if (s < w) { u = b.From + s; z = b.Bottom; }
                else if (s < w + h) { u = b.To; z = b.Bottom + (s - w); }
                else if (s < (2f * w) + h) { u = b.To - (s - w - h); z = b.Top; }
                else { u = b.From; z = b.Top - (s - (2f * w) - h); }
                iu[k] = u; iz[k] = z;
                var out2 = new Vector2(u - cu, z - cz);
                out2 = out2.LengthSquared() > 1e-9f ? Vector2.Normalize(out2) : Vector2.UnitY;
                float m1 = margin * (0.3f + (0.35f * R()));
                float m2 = m1 + (margin * ((k % 2 == 0 ? 0.9f : 0.3f) + (0.7f * R())));   // every other point a streak
                mu[k] = Math.Clamp(u + (out2.X * m1), 0f, length); mz[k] = Math.Clamp(z + (out2.Y * m1), panel.BaseZ, panel.TopZ);
                ou[k] = Math.Clamp(u + (out2.X * m2), 0f, length); oz[k] = Math.Clamp(z + (out2.Y * m2), panel.BaseZ, panel.TopZ);
            }
            var dark = new TerrainTriangleTag(TerrainTriangleKind.Wall, ScorchMaterial);
            var light = new TerrainTriangleTag(TerrainTriangleKind.Wall, ScorchLightMaterial);
            float off = (panel.Thickness * 0.5f) + 0.015f;
            for (int side = -1; side <= 1; side += 2)
            {
                var face = n * (off * side);
                Vector3 P(float u, float z) => new(panel.A + (dir * u) + face, z);
                void Ring(ReadOnlySpan<float> au, ReadOnlySpan<float> az, ReadOnlySpan<float> bu, ReadOnlySpan<float> bz, float lift, TerrainTriangleTag tag)
                {
                    var l = n * (lift * side);
                    for (int k = 0; k < K; k++)
                    {
                        int j = (k + 1) % K;
                        int q = v.Count;
                        v.Add(P(au[k], az[k]) + new Vector3(l, 0f)); v.Add(P(au[j], az[j]) + new Vector3(l, 0f)); v.Add(P(bu[j], bz[j]) + new Vector3(l, 0f)); v.Add(P(bu[k], bz[k]) + new Vector3(l, 0f));
                        if (side > 0) { t.Add(q); t.Add(q + 1); t.Add(q + 2); t.Add(q); t.Add(q + 2); t.Add(q + 3); }
                        else { t.Add(q); t.Add(q + 2); t.Add(q + 1); t.Add(q); t.Add(q + 3); t.Add(q + 2); }
                        g.Add(tag); g.Add(tag);
                    }
                }
                Ring(mu, mz, ou, oz, 0f, light);        // the lighter streaks, under …
                Ring(iu, iz, mu, mz, 0.004f, dark);     // … the black ring at the opening's edge
            }
        }
    }
}
