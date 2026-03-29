using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using ModuleHost.Core;
using ModuleHost.Core.Scheduling;
using Bagira.SimHost;
using Bagira.SimHost.Modules.Orchestration.Handlers;
using Xunit;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Focused bootstrap tests confirming that <see cref="NodeBootstrapper.BuildOrchestration"/>
    /// registers <see cref="ReplayLoadDsmHandler"/> when the replay-control objects are supplied —
    /// matching the production <see cref="SimHostApp.OnLoad"/> wiring (CGF1-S0304 Part A.1).
    /// </summary>
    public sealed class NodeBootstrapperReplayTests : System.IDisposable
    {
        // Domain 16 is reserved for NodeBootstrapper replay-registration tests.
        private const int TestDomain = 16;

        private readonly EntityRepository    _world;
        private readonly EventAccumulator    _evtAcc;
        private readonly ModuleHostKernel    _kernel;
        private readonly DdsParticipant      _participant;

        public NodeBootstrapperReplayTests()
        {
            _world       = new EntityRepository();
            _evtAcc      = new EventAccumulator();
            _kernel      = new ModuleHostKernel(_world, _evtAcc);
            _kernel.InitializeForTest();
            _participant = new DdsParticipant(TestDomain);
        }

        public void Dispose()
        {
            _participant.Dispose();
            _kernel.Dispose();
            _world.Dispose();
        }

        // ── A.1: Production replay registration ──────────────────────────────

        /// <summary>
        /// Verifies that <see cref="NodeBootstrapper.BuildOrchestration"/> registers
        /// <see cref="ReplayLoadDsmHandler"/> when <paramref name="simGroup"/>,
        /// <paramref name="lifecycleGroup"/>, and <paramref name="ghostCreationSystem"/>
        /// are all non-null — the condition required by the bootstrapper.
        ///
        /// <para>This is the focused test demanded by the BATCH-16 review: the gap was
        /// that <see cref="SimHostApp.OnLoad"/> called <c>BuildOrchestration</c> before
        /// the three objects existed, so they were null and the handler was never registered.
        /// The production fix (two-phase bootstrap) constructs these objects first and passes
        /// them here, which this test validates without a full DDS stack.</para>
        /// </summary>
        [Fact]
        public void BuildOrchestration_WithReplayParams_RegistersReplayLoadDsmHandler()
        {
            var entityMap     = new NetworkEntityMap();
            var ghostSys      = new GhostCreationSystem(entityMap);
            var simGroup      = new SimulationSystemGroup();
            var lifecycleGrp  = new NetworkLifecycleSystemGroup(ghostSys);

            var bootstrapper = new NodeBootstrapper();
            using var slave = bootstrapper.BuildOrchestration(
                NodeRole.AllInOne,
                _kernel,
                _world,
                nodeId:             1,
                participant:        _participant,
                simGroup:           simGroup,
                lifecycleGroup:     lifecycleGrp,
                ghostCreationSystem: ghostSys);

            Assert.True(
                slave.IsHandlerRegistered<ReplayLoadDsmHandler>(),
                "ReplayLoadDsmHandler must be registered by BuildOrchestration when replay params are provided.");
        }

        /// <summary>
        /// When replay params (<paramref name="simGroup"/>) are absent,
        /// <see cref="ReplayLoadDsmHandler"/> must <em>not</em> be registered.
        /// Uses <see cref="NodeRole.ImageGenerator"/> which does not require DDS and does
        /// not create an <see cref="EcsRecordReplayController"/> — the guard condition
        /// <c>controller != null</c> in <see cref="NodeBootstrapper.BuildOrchestration"/>
        /// ensures no partial-wired handler is constructed.
        /// </summary>
        [Fact]
        public void BuildOrchestration_WithoutReplayParams_DoesNotRegisterReplayHandler()
        {
            var bootstrapper = new NodeBootstrapper();
            var slave = bootstrapper.BuildOrchestration(
                NodeRole.ImageGenerator,
                _kernel,
                _world,
                nodeId: 1);

            Assert.False(
                slave.IsHandlerRegistered<ReplayLoadDsmHandler>(),
                "ReplayLoadDsmHandler must NOT be registered when replay params are absent.");
        }
    }
}
