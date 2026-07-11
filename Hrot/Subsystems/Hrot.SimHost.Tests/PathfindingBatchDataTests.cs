using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Navigation;
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
        /// <see cref="PathfindingBatchData.Results"/> array length should equal that capacity.
        /// </summary>
        [Fact]
        public void PathfindingBatchData_Allocation_CapacityMatchesDefault()
        {
            // Arrange + Act
            var data = new PathfindingBatchData
            {
                Results  = new NativeArray<PathResult>(PathfindingBatchData.DefaultCapacity, Allocator.Persistent),
            };

            try
            {
                // Assert
                Assert.Equal(PathfindingBatchData.DefaultCapacity, data.Results.Length);
            }
            finally
            {
                if (data.Results.IsCreated)  data.Results.Dispose();
            }
        }

        /// <summary>
        /// <see cref="PathfindingBatchData.DefaultCapacity"/> must match the production constant.
        /// A (Stale Test TH-3): constant changed from 64 to 256 in PathfindingBatchData.cs.
        /// </summary>
        [Fact]
        public void PathfindingBatchData_DefaultCapacity_Is64()
        {
            Assert.Equal(256, PathfindingBatchData.DefaultCapacity);
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
                Assert.Equal(PathfindingBatchData.DefaultCapacity, batch.Results.Length);
            });
            Assert.Null(ex);
        }
    }
}
