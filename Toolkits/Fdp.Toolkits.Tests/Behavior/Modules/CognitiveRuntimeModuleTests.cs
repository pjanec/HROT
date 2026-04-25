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
            var registry = new DoctrineRegistry();
            var module   = new CognitiveRuntimeModule(registry);

            // Assert — 5 systems: arbitration, HsmDamageBridge, BTree, HsmHsm128, HsmHsm64
            // Order: ChannelArbitrationSystem → HsmDamageBridgeSystem → BTreeTickSystem
            //        → HsmTickSystem<BrainHsm128> → HsmTickSystem<BrainHsm64>
            Assert.Equal(5, module.SimulationSystems.Count);
            Assert.IsType<ChannelArbitrationSystem>(module.SimulationSystems[0]);
            Assert.IsType<HsmDamageBridgeSystem>(module.SimulationSystems[1]);
            Assert.IsType<BTreeTickSystem>(module.SimulationSystems[2]);
            Assert.IsType<HsmTickSystem<BrainHsm128>>(module.SimulationSystems[3]);
            Assert.IsType<HsmTickSystem<BrainHsm64>>(module.SimulationSystems[4]);

            // PACK-M001: HsmDamageBridgeSystem must appear before BTreeTickSystem.
            var systemsList = module.SimulationSystems.ToList();
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
