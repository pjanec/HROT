using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using Xunit;

namespace Hrot.SimHost.Integration.Tests
{
    /// <summary>
    /// CMC-S006 — verifies that ClusterSlave instances publish NodeHeartbeatEvent
    /// to the FdpEventBus after 1 s has elapsed.
    ///
    /// <para>DDS-based heartbeat delivery is temporarily disabled after BATCH-03.
    /// Phase 5 translators (CMC-S012/S013) will restore the DDS path.</para>
    /// </summary>
    [Collection("LogCapture")]
    public sealed class ClusterSlaveHeartbeatTests
    {
        private const int SimHostNodeId = 1;
        private const int CgfNodeId     = 400;

        [Fact]
        public async Task ClusterSlaves_PublishNodeHeartbeatEvents_ToBus()
        {
            var simHostBus = new FdpEventBus();
            var cgfBus     = new FdpEventBus();

            using var simHostSlave = new ClusterSlave(SimHostNodeId, "SimHost", simHostBus);
            using var cgfSlave     = new ClusterSlave(CgfNodeId,     "CGF",     cgfBus);

            // Wait for the heartbeat timer to elapse (> 1 second).
            await Task.Delay(1200);

            simHostSlave.Tick();
            cgfSlave.Tick();
            simHostBus.SwapBuffers();
            cgfBus.SwapBuffers();

            var simHostHeartbeats = new List<NodeHeartbeatEvent>();
            foreach (var e in simHostBus.ConsumeManaged<NodeHeartbeatEvent>())
                simHostHeartbeats.Add(e);

            var cgfHeartbeats = new List<NodeHeartbeatEvent>();
            foreach (var e in cgfBus.ConsumeManaged<NodeHeartbeatEvent>())
                cgfHeartbeats.Add(e);

            Assert.Single(simHostHeartbeats);
            Assert.Equal(SimHostNodeId, simHostHeartbeats[0].NodeId);
            Assert.Equal("SimHost",     simHostHeartbeats[0].SubsystemName);

            Assert.Single(cgfHeartbeats);
            Assert.Equal(CgfNodeId, cgfHeartbeats[0].NodeId);
            Assert.Equal("CGF",     cgfHeartbeats[0].SubsystemName);
        }
    }
}
