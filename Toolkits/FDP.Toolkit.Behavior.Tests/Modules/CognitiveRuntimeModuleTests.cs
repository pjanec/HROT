using Fdp.Kernel;
using FDP.Toolkit.Behavior.Modules;
using FDP.Toolkit.Behavior.Systems;
using FDP.Toolkit.Behavior.Components;
using Xunit;

namespace FDP.Toolkit.Behavior.Tests.Modules
{
    /// <summary>
    /// Verifies that <see cref="CognitiveRuntimeModule"/> registers exactly the expected
    /// systems into a <see cref="SystemGroup"/> (MOD1-P2T2 success condition).
    /// </summary>
    public class CognitiveRuntimeModuleTests
    {
        [Fact]
        public void CognitiveRuntimeModule_RegistersAllTickSystems()
        {
            // Arrange
            using var world = TestWorldFactory.Create();
            var registry = new DoctrineRegistry();
            var module   = new CognitiveRuntimeModule(registry);

            var group = new SystemGroup();
            group.Create(world);

            // Act
            module.RegisterSystems(group);

            // Assert — 4 systems expected: arbitration, BTree, HsmHsm128, HsmHsm64
            var systems = group.GetSystems();
            Assert.Equal(4, systems.Count);
            Assert.Contains(systems, s => s is ChannelArbitrationSystem);
            Assert.Contains(systems, s => s is BTreeTickSystem);
            Assert.Contains(systems, s => s is HsmTickSystem<BrainHsm128>);
            Assert.Contains(systems, s => s is HsmTickSystem<BrainHsm64>);
        }
    }
}
