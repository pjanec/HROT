using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Perception.LineOfSight;
using Xunit;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// ⭐ CE-3032 — <see cref="ColliderIndex"/> must never MISS a collider the old all-colliders loop would have
    /// tested and found crossing the sight line. The strategies keep their exact geometric test, so "never misses"
    /// is exactly "same answer as before".
    /// </summary>
    public class ColliderIndexTests
    {
        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)]
        public void Query_NeverMissesACrossingCollider_RandomWorlds_CE3032(int seed)
        {
            var rng = new Random(seed);
            int n = 400;
            var centres = new Vector2[n];
            var radii = new float[n];
            for (int i = 0; i < n; i++)
            {
                centres[i] = new Vector2((float)rng.NextDouble() * 700f - 100f, (float)rng.NextDouble() * 700f - 100f);
                radii[i] = (float)rng.NextDouble() * 6f;
            }
            var index = new ColliderIndex();
            index.Build(n, i => centres[i], i => radii[i]);

            var near = new List<int>();
            int checkedHits = 0;
            for (int q = 0; q < 2000; q++)
            {
                var a = new Vector2((float)rng.NextDouble() * 700f - 100f, (float)rng.NextDouble() * 700f - 100f);
                // Include axis-aligned, diagonal-through-corners and zero-length segments.
                Vector2 b = (q % 7) switch
                {
                    0 => new Vector2(a.X + (float)rng.NextDouble() * 300f, a.Y),
                    1 => new Vector2(a.X, a.Y - (float)rng.NextDouble() * 300f),
                    2 => a + new Vector2(ColliderIndex.CellSize * 3f, ColliderIndex.CellSize * 3f),
                    3 => a,
                    _ => new Vector2((float)rng.NextDouble() * 700f - 100f, (float)rng.NextDouble() * 700f - 100f),
                };
                index.Query(a, b, near);
                var found = new HashSet<int>(near);
                Assert.Equal(near.Count, found.Count);            // each collider at most once
                for (int i = 0; i < n; i++)
                {
                    bool crosses = LosGeometry.SegmentCircle(a, b, centres[i], radii[i], out _, out _)
                                   || LosGeometry.LegacyCrossing(a, b, centres[i], radii[i]);
                    if (!crosses) continue;
                    checkedHits++;
                    Assert.True(found.Contains(i), $"seed {seed} query {q}: collider {i} crosses {a}->{b} but was not returned");
                }
            }
            Assert.True(checkedHits > 100, "the random worlds must actually exercise crossings");
        }

        [Fact]
        public void Query_EmptyIndex_ReturnsNothing_AndRebuildForgetsTheOldSet_CE3032()
        {
            var index = new ColliderIndex();
            var near = new List<int>();
            index.Build(0, _ => default, _ => 0f);
            index.Query(Vector2.Zero, new Vector2(100f, 0f), near);
            Assert.Empty(near);

            index.Build(1, _ => new Vector2(50f, 0f), _ => 1f);
            index.Query(Vector2.Zero, new Vector2(100f, 0f), near);
            Assert.Equal(new[] { 0 }, near);

            index.Build(1, _ => new Vector2(50f, 300f), _ => 1f);
            index.Query(Vector2.Zero, new Vector2(100f, 0f), near);
            Assert.Empty(near);
        }
    }
}
