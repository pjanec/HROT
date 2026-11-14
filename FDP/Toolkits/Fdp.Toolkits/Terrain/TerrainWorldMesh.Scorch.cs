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

        /// <summary>⭐ CE-4392 X-8 — a material burnt by a blast: <c>&lt;material&gt;-scorched</c> (dark) or <c>-singed</c> (less so); the 3-D layer darkens the material's own colour
        /// and keeps its pattern.</summary>
        public const string ScorchedSuffix = "-scorched", SingedSuffix = "-singed";

        /// <summary>⭐ CE-4392 X-8 — the deepest a broken brick reaches into a breach, as a share of the opening's smaller side (the hole stays open).</summary>
        public const float ToothDepthShare = 0.18f;

        /// <summary>
        /// ⭐ CE-4392 (DESIGN_Map_3D_Mode §6aj X-8; 🔒 user: "render the hole with some teeth, like missing bricks or bricks extending into the hole") — the breach's rim BROKEN: along each
        /// edge of the opening, brick-sized stubs (0.25 m long, two courses high) in the wall's own material reach into the hole by a jittered depth, staggered course by course, with
        /// about a third of the slots left empty (bricks gone). Inside the opening's rectangle and never past <see cref="ToothDepthShare"/> of its smaller side, so the middle stays open;
        /// presentation only (sight, fire and the navmesh read the breach itself). Placed by a hash of the breach.
        /// </summary>
        public static void AddBrokenRim(TerrainWallPanel panel, in WallBreach breach, List<Vector3> v, List<int> t, List<TerrainTriangleTag> g)
        {
            var b = breach;   // a copy the local function may read
            float length = Vector2.Distance(panel.A, panel.B);
            float w = b.To - b.From, h = b.Top - b.Bottom;
            if (length < 1e-3f || w < 0.3f || h < 0.3f) return;
            var dir = (panel.B - panel.A) / length;
            var n = new Vector2(-dir.Y, dir.X);
            float half = panel.Thickness * 0.5f;
            float maxDepth = MathF.Min(w, h) * ToothDepthShare;
            const float BrickLong = 0.25f, BrickTall = 0.17f;
            uint seed = (uint)(b.Panel * 2654435761u) ^ (uint)(MathF.Round(b.To * 100f) * 40503) ^ (uint)(MathF.Round(b.Top * 100f) * 9973) ^ 0x68E31DA4u;
            float R() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed & 0xFFFFFF) / (float)0x1000000; }
            var material = panel.Material;

            void Stub(float u0, float u1, float z0, float z1)
            {
                u0 = Math.Clamp(u0, b.From, b.To); u1 = Math.Clamp(u1, b.From, b.To);
                z0 = Math.Clamp(z0, b.Bottom, b.Top); z1 = Math.Clamp(z1, b.Bottom, b.Top);
                if (u1 - u0 < 0.02f || z1 - z0 < 0.02f) return;
                // a broken brick is a little thinner than the wall, and set in or out of it a touch
                float t0 = -half * (0.75f + (0.25f * R())), t1 = half * (0.75f + (0.25f * R()));
                var a = panel.A + (dir * u0); var c = panel.A + (dir * u1);
                var fp = new[] { a + (n * t0), c + (n * t0), c + (n * t1), a + (n * t1) };
                var min = Vector2.Min(Vector2.Min(fp[0], fp[1]), Vector2.Min(fp[2], fp[3]));
                var max = Vector2.Max(Vector2.Max(fp[0], fp[1]), Vector2.Max(fp[2], fp[3]));
                int first = g.Count;
                AddPrism(new TerrainPrism { Kind = TerrainPrismKind.Wall, Footprint = fp, Triangles = new[] { 0, 1, 2, 0, 2, 3 }, BaseZ = z0, TopZ = z1, Min = min, Max = max, Material = material, Panel = b.Panel }, v, t, g);
                // ⭐ the blast scorched these bricks most (user: "shouldn't the bricks inside the hole be also scorched?") — the wall's own material, burnt; every face a wall face
                string burnt = (material?.Name ?? TerrainMaterialLibrary.DefaultMaterial) + (R() < 0.7f ? ScorchedSuffix : SingedSuffix);
                for (int k = first; k < g.Count; k++) g[k] = new TerrainTriangleTag(TerrainTriangleKind.Wall, burnt);
            }

            // the top and bottom edges: stubs hang down / stand up, a course at a time; the bottom only when the breach does not reach the floor
            bool hasBottom = b.Bottom > panel.BaseZ + 0.05f, hasTop = b.Top < panel.TopZ - 0.05f;
            for (int row = 0; row < 2; row++)
            {
                float offset = row * BrickLong * 0.5f;
                for (float u = b.From - offset; u < b.To; u += BrickLong)
                {
                    float depth = maxDepth * (row == 0 ? 0.35f + (0.65f * R()) : 0.2f + (0.4f * R()));
                    float zc = row * BrickTall;
                    if (hasTop && R() > 0.33f) Stub(u, u + (BrickLong * 0.95f), b.Top - MathF.Min(depth + zc, maxDepth), b.Top - zc);
                    if (hasBottom && R() > 0.33f) Stub(u, u + (BrickLong * 0.95f), b.Bottom + zc, b.Bottom + MathF.Min(depth + zc, maxDepth));
                }
            }
            // the sides: each course a brick reaching in by a jittered depth (or gone)
            for (float z = b.Bottom; z < b.Top; z += BrickTall)
            {
                float zt = MathF.Min(z + (BrickTall * 0.95f), b.Top);
                if (R() > 0.33f) Stub(b.From, b.From + (maxDepth * (0.2f + (0.8f * R()))), z, zt);
                if (R() > 0.33f) Stub(b.To - (maxDepth * (0.2f + (0.8f * R()))), b.To, z, zt);
            }
        }
    
        /// <summary>
        /// ⭐ <c>CE-4591</c> (P-4; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §9.1 V-11) — a CRATER's mark, SIZED by its record and KEPT while the record exists: a dark
        /// disc over the bowl and a ragged lighter ring of blast streaks out past the rim, draped 2 cm over the cratered ground (the baked world's grid). Placed by a hash of the
        /// crater, so every node and every rebuild draws the same. Presentation only.
        /// </summary>
        public static void AddCraterScorch(TerrainWorld world, in Crater c, List<Vector3> v, List<int> t, List<TerrainTriangleTag> g)
        {
            if (c.Radius <= 0f) return;
            const int K = 24;
            uint seed = (uint)(MathF.Round(c.Centre.X * 100f) * 73856093) ^ (uint)(MathF.Round(c.Centre.Y * 100f) * 19349663) ^ 0x9E3779B9u;
            float R() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed & 0xFFFFFF) / (float)0x1000000; }
            const float Lift = 0.02f;
            Vector3 At(Vector2 p) => new(p, world.GroundHeightAt(p.X, p.Y) + Lift);
            var dark = new TerrainTriangleTag(TerrainTriangleKind.Ground, ScorchMaterial);
            var light = new TerrainTriangleTag(TerrainTriangleKind.Ground, ScorchLightMaterial);
            int centre = v.Count;
            v.Add(At(c.Centre));
            int ring0 = v.Count;
            for (int k = 0; k < K; k++)
            {
                float a = 2f * MathF.PI * k / K;
                v.Add(At(c.Centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * (c.Radius * (0.95f + 0.15f * R()))));
            }
            for (int k = 0; k < K; k++)
            {
                t.Add(centre); t.Add(ring0 + k); t.Add(ring0 + (k + 1) % K);
                g?.Add(dark);
            }
            int ring1 = v.Count;
            for (int k = 0; k < K; k++)
            {
                float a = 2f * MathF.PI * k / K;
                float reach = c.OuterRadius * ((k % 2 == 0 ? 1.15f : 0.95f) + 0.35f * R());   // every other point a streak
                v.Add(At(c.Centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * reach));
            }
            for (int k = 0; k < K; k++)
            {
                int a0 = ring0 + k, a1 = ring0 + (k + 1) % K, b0 = ring1 + k, b1 = ring1 + (k + 1) % K;
                t.Add(a0); t.Add(b0); t.Add(b1); g?.Add(light);
                t.Add(a0); t.Add(b1); t.Add(a1); g?.Add(light);
            }
        }
    }
}
