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
    /// systems into a <see cref="SystemGroup"/> (BHU-010 success condition).
    /// </summary>
    public class CognitiveRuntimeModuleTests
    {
        [Fact]
        public void CognitiveRuntimeModule_RegistersAllTickSystems()
        {
            // Arrange
            var registry = new DoctrineRegistry();
            var module   = new CognitiveRuntimeModule(registry);

            // Assert — 6 systems: arbitration, CognitiveInterrupt, BTree, HsmHsm128, HsmHsm64, CognitiveCleanup
            // Order: ChannelArbitrationSystem -> CognitiveInterruptSystem -> BTreeTickSystem
            //        -> HsmTickSystem<BrainHsm128> -> HsmTickSystem<BrainHsm64> -> CognitiveCleanupSystem
            Assert.Equal(6, module.SimulationSystems.Count);
            Assert.IsType<ChannelArbitrationSystem>(module.SimulationSystems[0]);
            Assert.IsType<CognitiveInterruptSystem>(module.SimulationSystems[1]);
            Assert.IsType<BTreeTickSystem>(module.SimulationSystems[2]);
            Assert.IsType<HsmTickSystem<BrainHsm128>>(module.SimulationSystems[3]);
            Assert.IsType<HsmTickSystem<BrainHsm64>>(module.SimulationSystems[4]);
            Assert.IsType<CognitiveCleanupSystem>(module.SimulationSystems[5]);

            // BHU-010: CognitiveInterruptSystem must appear before BTree and HSM ticks.
            var systemsList = module.SimulationSystems.ToList();
            int interruptIdx = systemsList.FindIndex(s => s is CognitiveInterruptSystem);
            int btreeIdx     = systemsList.FindIndex(s => s is BTreeTickSystem);
            int hsmIdx128    = systemsList.FindIndex(s => s is HsmTickSystem<BrainHsm128>);
            int cleanupIdx   = systemsList.FindIndex(s => s is CognitiveCleanupSystem);
            Assert.True(interruptIdx < btreeIdx,
                "CognitiveInterruptSystem must be registered before BTreeTickSystem.");
            Assert.True(interruptIdx < hsmIdx128,
                "CognitiveInterruptSystem must be registered before HsmTickSystem.");
            Assert.True(cleanupIdx > hsmIdx128,
                "CognitiveCleanupSystem must be registered after all brain tick systems.");
        }
    }
}
