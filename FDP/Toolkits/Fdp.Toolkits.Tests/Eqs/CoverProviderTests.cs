using System.Runtime.InteropServices;
using System.Numerics;
using Fdp.Toolkit.Spatial.Eqs;
using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>
    /// Unit tests for <see cref="CoverPoint"/>, <see cref="ICoverProvider"/>,
    /// and <see cref="ManualCoverProvider"/> (TASK-EQS-012).
    /// </summary>
    public class CoverProviderTests
    {
        // T-CP1: CoverPoint struct is exactly 24 bytes.
        [Fact]
        public void CoverPoint_IsExactly24Bytes()
        {
            Assert.Equal(24, Marshal.SizeOf<CoverPoint>());
        }

        // T-CP2: ManualCoverProvider radius filter returns only points within range.
        [Fact]
        public void ManualCoverProvider_RadiusFilter_ReturnsOnlyPointsWithinRadius()
        {
            // 3 cover points at distances 5, 15, 20 from origin.
            var provider = new ManualCoverProvider(new[]
            {
                new CoverPoint { PositionX = 5f,  PositionY = 0f, Quality = 1f },
                new CoverPoint { PositionX = 15f, PositionY = 0f, Quality = 1f },
                new CoverPoint { PositionX = 20f, PositionY = 0f, Quality = 1f },
            });

            var results = new CoverPoint[8];
            int count = provider.GetCoverPointsInRadius(Vector2.Zero, radius: 12f, results.AsSpan());

            // Only the point at distance 5 is within radius 12.
            Assert.Equal(1, count);
            Assert.True(System.Math.Abs(results[0].PositionX - 5f) < 0.001f);
        }
    }
}
