using System.IO;
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
    }
}
