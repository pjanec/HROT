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
            // ⭐ CE-2074 (2026-10-04): RoeSystem before the ingress — an order's ROE applies in the same frame as its task.
            Assert.Equal(2, module.InputSystems.Count);
            Assert.Single(module.SimulationSystems);
            Assert.IsType<RoeSystem>(module.InputSystems[0]);
            Assert.IsType<BehaviorIngressSystem>(module.InputSystems[1]);
            Assert.IsType<MissionDirectorSystem>(module.SimulationSystems[0]);
        }
    }
}
