using System;
using Fdp.Toolkit.Orchestration;
using Hrot.Orchestrator;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="OrchestrationLogicPack"/> (PACK2-P001).
    /// </summary>
    public class OrchestrationLogicPackTests
    {
        // Use the test-only constructor overload: ClusterSlave(FdpEventBus? eventBus, int nodeId, string subsystemName)
        // The first positional null cannot match int (production ctor's first param), so it
        // unambiguously resolves to the test-only overload.
        private static ClusterSlave MakeTestSlave()
            => new ClusterSlave(null, 1, "Test");

        /// <summary>
        /// Creating the pack with a bare <see cref="ClusterSlave"/> (no DDS participant,
        /// test-only constructor) succeeds and exposes the correct Name.
        /// </summary>
        [Fact]
        public void OrchestrationLogicPack_Name_IsOrchestrationLogicPack()
        {
            var pack = new OrchestrationLogicPack(MakeTestSlave());

            Assert.Equal("OrchestrationLogicPack", pack.Name);
        }

        /// <summary>
        /// <see cref="OrchestrationLogicPack.Tick"/> delegates to
        /// <see cref="ClusterSlave.Tick"/> without throwing.
        /// </summary>
        [Fact]
        public void OrchestrationLogicPack_Tick_DelegatesToClusterSlave()
        {
            var pack = new OrchestrationLogicPack(MakeTestSlave());

            // ClusterSlave.Tick() with no DDS participant and no event bus should
            // execute without exception.
            var ex = Record.Exception(() => pack.Tick(null!, 0.016f));

            Assert.Null(ex);
        }

        /// <summary>
        /// <see cref="OrchestrationLogicPack.RegisterSystems"/> is a no-op and
        /// does not throw.
        /// </summary>
        [Fact]
        public void OrchestrationLogicPack_RegisterSystems_IsNoOp()
        {
            var pack = new OrchestrationLogicPack(MakeTestSlave());

            // No-op; must not throw.
            var ex = Record.Exception(() => pack.RegisterSystems(null!));

            Assert.Null(ex);
        }

        /// <summary>
        /// Null <see cref="ClusterSlave"/> throws <see cref="ArgumentNullException"/>.
        /// </summary>
        [Fact]
        public void OrchestrationLogicPack_NullSlave_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new OrchestrationLogicPack(null!));
        }
    }
}
