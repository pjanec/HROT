using Xunit;
using ModuleHost.Core;
using Fdp.Kernel;
using ModuleHost.Core.Network;

namespace ModuleHost.Core.Tests.Extraction
{
    public class MigrationSmokeTests
    {
        [ComponentId(226)]
        private struct TestPosition { }

        [Fact]
        public void KernelCreation_BeforeMigration_Succeeds()
        {
            var world = new EntityRepository();
            var accumulator = new EventAccumulator();
            using var kernel = new ModuleHostKernel(world, accumulator);
            Assert.NotNull(kernel);
        }

        [Fact]
        public void ComponentRegistration_BeforeMigration_Succeeds()
        {
            var world = new EntityRepository();
            // This test will fail after we remove Position from Core
            // That's expected - we'll update it then
            world.RegisterComponent<TestPosition>();
            Assert.True(true);
        }
    }
}
