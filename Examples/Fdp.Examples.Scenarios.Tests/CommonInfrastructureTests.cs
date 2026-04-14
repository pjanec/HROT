using Fdp.Examples.Common.Helpers;
using Fdp.Core.Collections;
using Fdp.Modules.Geographic.Components;
using Xunit;

namespace Fdp.Examples.Scenarios.Tests
{
    /// <summary>
    /// DEM1-I002: Tests for Fdp.Examples.Common infrastructure helpers.
    /// </summary>
    public class CommonInfrastructureTests
    {
        // ── MockTerrainProvider ───────────────────────────────────────────────

        private static TerrainQueryResult QuerySingle(MockTerrainProvider provider, float x)
        {
            using var requests = new NativeArray<TerrainQueryRequest>(1, Allocator.Temp);
            using var results  = new NativeArray<TerrainQueryResult>(1, Allocator.Temp);
            requests[0] = new TerrainQueryRequest { QueryX = x };
            provider.QueryBatch(requests, 1, results);
            return results[0];
        }

        [Fact]
        public void MockTerrainProvider_FlatZone_ReturnsZeroAltitude()
        {
            var provider = new MockTerrainProvider();
            var result = QuerySingle(provider, 10.0f);
            Assert.True(result.HasHit);
            Assert.Equal(0.0f, result.HitZ);
        }

        [Fact]
        public void MockTerrainProvider_Ramp_ReturnsCorrectAltitude()
        {
            var provider = new MockTerrainProvider();
            var result = QuerySingle(provider, 30.0f);
            Assert.True(result.HasHit);
            // Expected: (30 - 20) * 0.2 = 2.0
            Assert.Equal(2.0f, result.HitZ, precision: 2);
        }

        [Fact]
        public void MockTerrainProvider_Spike_ReturnsOneHundred()
        {
            var provider = new MockTerrainProvider();
            var result = QuerySingle(provider, 40.0f);
            Assert.True(result.HasHit);
            Assert.Equal(100.0f, result.HitZ);
        }

        // ── DemoRoadGraphFactory ──────────────────────────────────────────────

        [Fact]
        public void DemoRoadGraphFactory_CreatesNonNullBlob()
        {
            var blob = DemoRoadGraphFactory.CreateCityIntersection();
            try
            {
                Assert.True(blob.Nodes.IsCreated, "Nodes array should be created");
                Assert.True(blob.Nodes.Length >= 4, $"Expected at least 4 nodes, got {blob.Nodes.Length}");
                Assert.True(blob.Segments.IsCreated, "Segments array should be created");
                Assert.True(blob.Segments.Length > 0, "Segments array should be non-empty");
            }
            finally
            {
                blob.Dispose();
            }
        }
    }
}
