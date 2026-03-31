using Fdp.Kernel;
using Fdp.Kernel.Collections;
using FDP.Toolkit.Navigation;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="PathfindingBatchData"/> singleton (MOD1-P6T3).
    /// </summary>
    public class PathfindingBatchDataTests
    {
        /// <summary>
        /// After allocating with <see cref="PathfindingBatchData.DefaultCapacity"/>, the
        /// <see cref="PathfindingBatchData.Requests"/> array length should equal that capacity.
        /// </summary>
        [Fact]
        public void PathfindingBatchData_Allocation_CapacityMatchesDefault()
        {
            // Arrange + Act
            var data = new PathfindingBatchData
            {
                Requests = new NativeArray<PathRequest>(PathfindingBatchData.DefaultCapacity, Allocator.Persistent),
                Results  = new NativeArray<PathResult>(PathfindingBatchData.DefaultCapacity, Allocator.Persistent),
            };

            try
            {
                // Assert
                Assert.Equal(PathfindingBatchData.DefaultCapacity, data.Requests.Length);
                Assert.Equal(PathfindingBatchData.DefaultCapacity, data.Results.Length);
                Assert.Equal(0, data.Count);
            }
            finally
            {
                if (data.Requests.IsCreated) data.Requests.Dispose();
                if (data.Results.IsCreated)  data.Results.Dispose();
            }
        }

        /// <summary>
        /// <see cref="PathfindingBatchData.DefaultCapacity"/> must be 64 as specified in the design.
        /// </summary>
        [Fact]
        public void PathfindingBatchData_DefaultCapacity_Is64()
        {
            Assert.Equal(64, PathfindingBatchData.DefaultCapacity);
        }

        /// <summary>
        /// The singleton can be registered and retrieved via the ECS world without exception.
        /// </summary>
        [Fact]
        public void PathfindingBatchData_Singleton_CanBeRetrievedFromWorld()
        {
            using var world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(world);

            // Act + Assert — retrieval must not throw
            var ex = Record.Exception(() =>
            {
                ref var batch = ref world.GetSingleton<PathfindingBatchData>();
                Assert.Equal(PathfindingBatchData.DefaultCapacity, batch.Requests.Length);
            });
            Assert.Null(ex);
        }
    }
}
