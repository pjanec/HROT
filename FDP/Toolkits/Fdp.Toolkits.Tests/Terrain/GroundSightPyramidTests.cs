using System;
using System.Numerics;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ <c>CE-4367</c> (L-5, docs/DESIGN_World_Query_Seam.md §7) — the ground sight trace skips the triangle tests of every cell whose block
    /// (the max-height level) lies below the line; its answer must be the PLAIN march's — the crest form walks every cell and tests every
    /// triangle, so it is the reference. Random lines over rough 1 m ground: eye to eye (most just graze the hills), eye to air, ridge to
    /// ridge, steep and flat, short and 3 km.
    /// </summary>
    public class GroundSightPyramidTests
    {
        internal static TerrainHeightGrid Rough(int n, float cell, int seed)
        {
            var rng = new Random(seed);
            float p1 = (float)rng.NextDouble() * 6f, p2 = (float)rng.NextDouble() * 6f;
            var z = new float[n * n];
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    z[r * n + c] = 500f + 120f * MathF.Sin(c * 0.004f + p1) * MathF.Cos(r * 0.0035f + p2)
                                 + 25f * MathF.Sin(c * 0.021f + r * 0.017f) + 4f * MathF.Sin(c * 0.13f) * MathF.Cos(r * 0.11f)
                                 + 0.4f * (float)rng.NextDouble();
            return new TerrainHeightGrid(Vector2.Zero, cell, n, n, z);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void CE4367_TheSkippingTrace_AnswersAsThePlainMarch(int seed)
        {
            var g = Rough(1537, 1f, seed);   // 1.5 km at 1 m (6 × 6 tiles, 96 × 96 blocks)
            var rng = new Random(seed * 7919);
            int blocked = 0, clear = 0;
            for (int i = 0; i < 4000; i++)
            {
                float L = i % 4 == 0 ? 1500f : 20f + (float)rng.NextDouble() * 900f;
                float x0 = (float)rng.NextDouble() * 1536f, y0 = (float)rng.NextDouble() * 1536f, ang = (float)(rng.NextDouble() * Math.PI * 2);
                float x1 = Math.Clamp(x0 + MathF.Cos(ang) * L, 0f, 1536f), y1 = Math.Clamp(y0 + MathF.Sin(ang) * L, 0f, 1536f);
                float h0 = (i % 5) switch { 0 => 1.7f, 1 => 0.3f, 2 => 30f, 3 => 1.7f, _ => 400f * (float)rng.NextDouble() };
                float h1 = (i % 3) switch { 0 => 1.7f, 1 => 150f, _ => 0.5f + 10f * (float)rng.NextDouble() };
                var a = new Vector3(x0, y0, g.Sample(x0, y0) + h0);
                var b = new Vector3(x1, y1, g.Sample(x1, y1) + h1);
                bool fast = g.Crosses(a, b);
                bool plain = g.Crosses(a, b, out _, out _);
                Assert.True(fast == plain, $"line {i}: {a} → {b}: skipping says {fast}, the plain march {plain}");
                if (plain) blocked++; else clear++;
            }
            Assert.True(blocked > 400 && clear > 400, $"both answers must be exercised (blocked {blocked}, clear {clear})");
        }
    }
}
