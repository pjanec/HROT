using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.CGF;
using Bagira.Map.Common;
using Bagira.Orchestrator;
using Bagira.SimHost;
using Bagira.SimHost.Configuration;
using CycloneDDS.Runtime;
using Xunit;

namespace Bagira.SimHost.Integration.Tests
{
    /// <summary>
    /// CGF1-S0104 — verifies that both SimHost and CGF DrillSlave instances
    /// publish heartbeats that the DrillMaster picks up within 2 s.
    /// </summary>
    [Collection("LogCapture")]
    public sealed class DrillSlaveHeartbeatTests
    {
        /// <summary>
        /// Dedicated DDS domain so this test does not contend with domain-0 tests.
        /// Domain 16 is reserved for DrillSlave integration tests.
        /// </summary>
        private const int TestDomain = 16;

        private const int SimHostNodeId = 1;
        private const int CgfNodeId = 400;

        [Fact]
        public async Task OrchestratorReceivesHeartbeatsFromBothNodes()
        {
            using var cancel = new CancellationTokenSource();

            // ── Orchestrator (DrillMaster) ────────────────────────────────────
            using var orchParticipant = BagiraEnvironment.CreateParticipant(TestDomain);
            using var drill = new DrillMaster(orchParticipant);

            // Pump the orchestrator on a background task
            var orchestratorPump = Task.Run(() =>
            {
                while (!cancel.IsCancellationRequested)
                {
                    drill.Tick();
                    Thread.Sleep(4);
                }
            }, cancel.Token);

            // Short delay so DDS discovery completes before slaves start publishing
            await Task.Delay(300);

            // ── SimHost DrillSlave ────────────────────────────────────────────
            var simHostCfg = new NodeConfiguration
            {
                DdsDomainId = TestDomain,
            };
            var simHostApp = new SimHostApp(TestDomain, NodeRole.AllInOne, simHostCfg);
            try
            {
                simHostApp.InitializeHeadless(TestDomain, SimHostNodeId);

                // ── CGF DrillSlave ─────────────────────────────────────────────
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
                var roster = drill.NodeRoster.ActiveNodes;

                Assert.True(roster.ContainsKey(SimHostNodeId),
                    $"DrillMaster NodeRoster does not contain SimHost nodeId={SimHostNodeId}. " +
                    $"Active nodes: [{string.Join(", ", roster.Keys)}]");

                Assert.True(roster.ContainsKey(CgfNodeId),
                    $"DrillMaster NodeRoster does not contain CGF nodeId={CgfNodeId}. " +
                    $"Active nodes: [{string.Join(", ", roster.Keys)}]");

                Assert.Equal(DSMState.Standby, roster[SimHostNodeId].LocalDsmState);
                Assert.Equal(DSMState.Standby, roster[CgfNodeId].LocalDsmState);
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
