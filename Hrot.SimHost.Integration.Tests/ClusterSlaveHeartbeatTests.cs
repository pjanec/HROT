using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.CGF;
using Hrot.Map.Common;
using Hrot.Orchestrator;
using Hrot.SimHost;
using Hrot.SimHost.Configuration;
using CycloneDDS.Runtime;
using Xunit;

namespace Hrot.SimHost.Integration.Tests
{
    /// <summary>
    /// CGF1-S0104 — verifies that both SimHost and CGF ClusterSlave instances
    /// publish heartbeats that the ClusterMaster picks up within 2 s.
    /// </summary>
    [Collection("LogCapture")]
    public sealed class ClusterSlaveHeartbeatTests
    {
        /// <summary>
        /// Dedicated DDS domain so this test does not contend with domain-0 tests.
        /// Domain 16 is reserved for ClusterSlave integration tests.
        /// </summary>
        private const int TestDomain = 16;

        private const int SimHostNodeId = 1;
        private const int CgfNodeId = 400;

        [Fact]
        public async Task OrchestratorReceivesHeartbeatsFromBothNodes()
        {
            using var cancel = new CancellationTokenSource();

            // ── Orchestrator (ClusterMaster) ────────────────────────────────────
            using var orchParticipant = HrotEnvironment.CreateParticipant(TestDomain);
            using var exercise = new ClusterMaster(orchParticipant);

            // Pump the orchestrator on a background task
            var orchestratorPump = Task.Run(() =>
            {
                while (!cancel.IsCancellationRequested)
                {
                    exercise.Tick();
                    Thread.Sleep(4);
                }
            }, cancel.Token);

            // Short delay so DDS discovery completes before slaves start publishing
            await Task.Delay(300);

            // ── SimHost ClusterSlave ────────────────────────────────────────────
            var simHostCfg = new NodeConfiguration
            {
                DdsDomainId = TestDomain,
            };
            var simHostApp = new SimHostApp(TestDomain, NodeRole.AllInOne, simHostCfg);
            try
            {
                simHostApp.InitializeHeadless(TestDomain, SimHostNodeId);

                // ── CGF ClusterSlave ─────────────────────────────────────────────
                using var cgfApp = new CgfApplication(TestDomain, CgfNodeId);

                // ── Tick loop ─────────────────────────────────────────────────
                var deadline = DateTime.UtcNow.AddSeconds(2);
                while (DateTime.UtcNow < deadline)
                {
                    simHostApp.Tick(1f / 60f);
                    cgfApp.Tick();
                    Thread.Sleep(16); // ~60 Hz
                }

                // ── Assert ────────────────────────────────────────────────────
                var roster = exercise.NodeRoster.ActiveNodes;

                Assert.True(roster.ContainsKey(SimHostNodeId),
                    $"ClusterMaster NodeRoster does not contain SimHost nodeId={SimHostNodeId}. " +
                    $"Active nodes: [{string.Join(", ", roster.Keys)}]");

                Assert.True(roster.ContainsKey(CgfNodeId),
                    $"ClusterMaster NodeRoster does not contain CGF nodeId={CgfNodeId}. " +
                    $"Active nodes: [{string.Join(", ", roster.Keys)}]");

                Assert.Equal(ClusterState.Idle, roster[SimHostNodeId].LocalClusterState);
                Assert.Equal(ClusterState.Idle, roster[CgfNodeId].LocalClusterState);
            }
            finally
            {
                cancel.Cancel();
                await orchestratorPump.ContinueWith(_ => { });
                simHostApp.Shutdown();
            }
        }
    }
}
