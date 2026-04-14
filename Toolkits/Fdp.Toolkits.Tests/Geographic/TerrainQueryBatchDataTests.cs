using System;
using Fdp.Kernel.Collections;
using Fdp.Modules.Geographic.Components;
using Xunit;

namespace Fdp.Modules.Geographic.Tests
{
    /// <summary>
    /// Sanity tests for <see cref="TerrainQueryBatchData"/> native-array allocation and disposal.
    /// </summary>
    public class TerrainQueryBatchDataTests : IDisposable
    {
        private TerrainQueryBatchData _batch;

        public TerrainQueryBatchDataTests()
        {
            _batch = new TerrainQueryBatchData
            {
                Requests = new NativeArray<TerrainQueryRequest>(TerrainQueryBatchData.DefaultCapacity, Allocator.Persistent),
                Results  = new NativeArray<TerrainQueryResult>(TerrainQueryBatchData.DefaultCapacity,  Allocator.Persistent),
                Count    = 0,
            };
        }

        public void Dispose()
        {
            if (_batch.Requests.IsCreated) _batch.Requests.Dispose();
            if (_batch.Results.IsCreated)  _batch.Results.Dispose();
        }

        [Fact]
        public void Allocate_CreatesBothArraysWithDefaultCapacity()
        {
            Assert.True(_batch.Requests.IsCreated);
            Assert.True(_batch.Results.IsCreated);
            Assert.Equal(TerrainQueryBatchData.DefaultCapacity, _batch.Requests.Length);
            Assert.Equal(TerrainQueryBatchData.DefaultCapacity, _batch.Results.Length);
        }

        [Fact]
        public void Count_StartsAtZero()
        {
            Assert.Equal(0, _batch.Count);
        }

        [Fact]
        public void Dispose_ClearsIsCreated()
        {
            _batch.Requests.Dispose();
            _batch.Results.Dispose();

            Assert.False(_batch.Requests.IsCreated);
            Assert.False(_batch.Results.IsCreated);

            // Prevent double-dispose in Dispose()
            _batch = default;
        }
    }
}
