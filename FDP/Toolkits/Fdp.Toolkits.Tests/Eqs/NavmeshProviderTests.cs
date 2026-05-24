using System;
using System.Numerics;
using Fdp.Toolkit.Spatial.Eqs;
using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>
    /// Unit tests for <see cref="StubNavmeshProvider"/> (TASK-EQS-016).
    /// </summary>
    public class NavmeshProviderTests
    {
        // T-NP1: StubNavmeshProvider.IsReachable always returns true.
        [Fact]
        public void StubNavmeshProvider_IsReachable_AlwaysTrue()
        {
            var nav = new StubNavmeshProvider();
            Assert.True(nav.IsReachable(Vector2.Zero, new Vector2(10, 10)));
        }

        // T-NP2: StubNavmeshProvider.TryGetPathDistance returns Euclidean distance.
        [Fact]
        public void StubNavmeshProvider_TryGetPathDistance_ReturnsEuclidean()
        {
            var nav = new StubNavmeshProvider();
            bool ok = nav.TryGetPathDistance(Vector2.Zero, new Vector2(3, 4), out float dist);
            Assert.True(ok);
            Assert.True(Math.Abs(dist - 5f) < 0.001f); // 3-4-5 triangle
        }
    }
}
