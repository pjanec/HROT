using Fdp.Core;
using Fdp.Toolkit.Behavior.Modules;
using Fdp.Toolkit.Behavior.Systems;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests.Modules
{
    /// <summary>
    /// Verifies that <see cref="MissionControlModule"/> registers exactly the expected
    /// systems into a <see cref="SystemGroup"/> (MOD1-P2T1 success condition).
    /// </summary>
    public class MissionControlModuleTests
    {
        [Fact]
        public void MissionControlModule_RegistersSystems()
        {
            // Arrange
            using var world = TestWorldFactory.Create();
            var registry = new DoctrineRegistry();
            var module   = new MissionControlModule(registry);

            var group = new SystemGroup();
            group.Create(world);

            // Act
            module.RegisterSystems(group);

            // Assert
            var systems = group.GetSystems();
            Assert.Equal(2, systems.Count);
            Assert.Contains(systems, s => s is DoctrineIngressSystem);
            Assert.Contains(systems, s => s is MissionDirectorSystem);
        }
    }
}
