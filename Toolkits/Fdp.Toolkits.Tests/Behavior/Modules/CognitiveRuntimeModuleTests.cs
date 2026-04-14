using Fdp.Core;
using Fdp.Toolkit.Behavior.Modules;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Behavior.Components;
using System.Linq;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests.Modules
{
    /// <summary>
    /// Verifies that <see cref="CognitiveRuntimeModule"/> registers exactly the expected
    /// systems into a <see cref="SystemGroup"/> (MOD1-P2T2 / PACK-M001 success condition).
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

            // Assert — 5 systems: arbitration, HsmDamageBridge, BTree, HsmHsm128, HsmHsm64
            // Order: ChannelArbitrationSystem → HsmDamageBridgeSystem → BTreeTickSystem
            //        → HsmTickSystem<BrainHsm128> → HsmTickSystem<BrainHsm64>
            var systems = group.GetSystems();
            Assert.Equal(5, systems.Count);
            Assert.Contains(systems, s => s is ChannelArbitrationSystem);
            Assert.Contains(systems, s => s is HsmDamageBridgeSystem);
            Assert.Contains(systems, s => s is BTreeTickSystem);
            Assert.Contains(systems, s => s is HsmTickSystem<BrainHsm128>);
            Assert.Contains(systems, s => s is HsmTickSystem<BrainHsm64>);

            // PACK-M001: HsmDamageBridgeSystem must appear before BTreeTickSystem.
            var systemsList = systems.ToList();
            int bridgeIdx  = systemsList.FindIndex(s => s is HsmDamageBridgeSystem);
            int btreeIdx   = systemsList.FindIndex(s => s is BTreeTickSystem);
            int hsmIdx128  = systemsList.FindIndex(s => s is HsmTickSystem<BrainHsm128>);
            Assert.True(bridgeIdx < btreeIdx,
                "HsmDamageBridgeSystem must be registered before BTreeTickSystem.");
            Assert.True(bridgeIdx < hsmIdx128,
                "HsmDamageBridgeSystem must be registered before HsmTickSystem.");
        }
    }
}
