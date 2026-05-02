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
            var registry = new BehaviorRegistry();
            var module   = new MissionControlModule(registry);

            // Assert
            Assert.Single(module.InputSystems);
            Assert.Single(module.SimulationSystems);
            Assert.IsType<BehaviorIngressSystem>(module.InputSystems[0]);
            Assert.IsType<MissionDirectorSystem>(module.SimulationSystems[0]);
        }
    }
}
