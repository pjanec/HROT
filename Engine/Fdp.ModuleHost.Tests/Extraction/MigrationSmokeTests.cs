using Xunit;
using Fdp.ModuleHost_Core;
using Fdp.Kernel;
using Fdp.ModuleHost_Core.Network;

namespace Fdp.ModuleHost_Core.Tests.Extraction
{
    public class MigrationSmokeTests
    {
        [ComponentId(90)]
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
