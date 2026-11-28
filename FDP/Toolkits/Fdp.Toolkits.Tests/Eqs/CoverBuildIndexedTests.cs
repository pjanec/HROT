using System;
using System.IO;
using System.Numerics;
using System.Linq;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkits.Tests.Eqs
{
    /// <summary>
    /// ⭐ <c>CE-4366</c> (L-4) — the cover build asks "inside a solid?" and "something stands here?" through the world's spatial index
    /// (<see cref="TerrainWorld.InsideFootprint(float, float)"/> and its height-banded form) instead of scanning every prism per point —
    /// 52 s → 0.4 s on the scale terrain. A world WITHOUT an index answers point queries over every prism (the old scan), so the cover
    /// built with the Bepu index must be the cover built without it, point for point, on the shipped terrains.
    /// </summary>
    public class CoverBuildIndexedTests
    {
        private static TerrainWorld Load(string name, bool indexed)
        {
            var folder = Path.Combine(ContentLayout.TerrainAssets(ContentLayout.TryFindSourceTree()!), name);
            var world = TerrainWorldParser.Parse(File.ReadAllText(Directory.GetFiles(folder, "*.world.geojson").Single()), name,
                TerrainAssets.ForFolder(folder));
            if (indexed) Fdp.Toolkit.Spatial.Bepu.BepuTerrainIndex.AttachTo(world);
            return world;
        }

        [Theory]
        [InlineData("demo-city")]
        [InlineData("test-town")]
        [InlineData("bt-range")]
        [InlineData("cqb-village")]
        public void CE4366_TheIndexedCoverBuild_IsTheScanningOne(string terrain)
        {
            var plain = TerrainCoverProvider.Build(Load(terrain, indexed: false)).Points;
            var fast = TerrainCoverProvider.Build(Load(terrain, indexed: true)).Points;
            Assert.True(plain.Count > 0);
            Assert.Equal(plain.Count, fast.Count);
            for (int i = 0; i < plain.Count; i++)
                Assert.Equal(plain[i], fast[i]);
        }

        /// <summary>
        /// ⭐ <c>CE-5044</c> — the vehicle cover search (<see cref="VehicleFootprint.Clear"/>, handoff ui → terrain "large terrain router and cover") asks
        /// "is this hull sample inside a solid?" through the spatial index too — it scanned every prism per sample (333 k on Stechovice).
        /// On seeded random hulls near the prisms, with and without a height band, the indexed answer is the literal scan's answer.
        /// 🔴 Red-proof: the indexed query with the band dropped (<c>InsideFootprint(x, y)</c> for every height) ⇒ a hull under a deck reads blocked ⇒ red.
        /// </summary>
        [Theory]
        [InlineData("demo-city")]
        [InlineData("test-town")]
        [InlineData("cqb-village")]
        public void TheVehicleFootprint_ThroughTheIndex_IsTheScanOfEveryPrism(string terrain)
        {
            var world = Load(terrain, indexed: true);
            Assert.True(world.Prisms.Count > 0);
            var rng = new Random(4242);
            int blocked = 0, clear = 0;
            for (int n = 0; n < 4000; n++)
            {
                var prism = world.Prisms[rng.Next(world.Prisms.Count)];
                var at = new Vector3(
                    prism.Min.X - 4f + (float)rng.NextDouble() * (prism.Max.X - prism.Min.X + 8f),
                    prism.Min.Y - 4f + (float)rng.NextDouble() * (prism.Max.Y - prism.Min.Y + 8f),
                    prism.BaseZ - 3f + (float)rng.NextDouble() * (prism.TopZ - prism.BaseZ + 6f));
                float angle = (float)(rng.NextDouble() * Math.PI * 2);
                var toward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                float length = 2f + (float)rng.NextDouble() * 6f, width = 1.5f + (float)rng.NextDouble() * 2f;
                float height = n % 4 == 0 ? 0f : 1f + (float)rng.NextDouble() * 3f;   // a quarter with no band
                bool fast = VehicleFootprint.Clear(world, at, toward, length, width, height);
                bool scan = ClearByScan(world, at, toward, length, width, height);
                Assert.True(scan == fast, $"sample {n}: scan {scan}, indexed {fast} at {at} L{length} W{width} H{height}");
                if (fast) clear++; else blocked++;
            }
            Assert.True(blocked > 100 && clear > 100, $"the samples must test both answers: blocked {blocked}, clear {clear}");
        }

        // The oracle: VehicleFootprint.Clear's sampling as it is, with Solid as it WAS — every prism scanned.
        private static bool ClearByScan(TerrainWorld world, Vector3 at, Vector2 towardCover, float length, float width, float height)
        {
            var f = towardCover.LengthSquared() > 1e-6f ? Vector2.Normalize(towardCover) : Vector2.UnitX;
            var t = new Vector2(-f.Y, f.X);
            int nl = Math.Max(1, (int)MathF.Ceiling(length / VehicleFootprint.SampleStep)), nw = Math.Max(1, (int)MathF.Ceiling(width / VehicleFootprint.SampleStep));
            var c = new Vector2(at.X, at.Y);
            for (int i = 0; i <= nl; i++)
                for (int j = 0; j <= nw; j++)
                {
                    var p = c + t * (length * (i / (float)nl - 0.5f)) + f * (width * (j / (float)nw - 0.5f));
                    foreach (var prism in world.Prisms)
                    {
                        if (p.X < prism.Min.X || p.Y < prism.Min.Y || p.X > prism.Max.X || p.Y > prism.Max.Y) continue;
                        if (height > 0f && (prism.TopZ <= at.Z || prism.BaseZ >= at.Z + height)) continue;
                        if (PolygonMath.Contains(prism.Footprint, p)) return false;
                    }
                }
            return true;
        }
    }
}
