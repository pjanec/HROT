using Fdp.Kernel;
using FDP.Toolkit.Physics;
using FDP.Toolkit.Physics.Components;
using Xunit;

namespace Hrot.SimHost.Tests
{
    public class PhysicsToolkitModuleTests
    {
        [Fact]
        public void PhysicsModule_Initialize_CreatesBatchDataSingleton()
        {
            using var world = new EntityRepository();

            var module = new PhysicsToolkitModule();
            module.Initialize(world);

            Assert.True(world.HasSingleton<RaycastBatchData>());

            if (world.HasSingleton<RaycastBatchData>())
            {
                ref var batch = ref world.GetSingleton<RaycastBatchData>();
                if (batch.Requests.IsCreated) batch.Requests.Dispose();
                if (batch.Hits.IsCreated) batch.Hits.Dispose();
            }
        }
    }
}
