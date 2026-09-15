using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
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

            // P1: SimHost declares a MULTI-role mask; the heartbeat must preserve it end to end.
            const NodeRole simHostRoles = NodeRole.MuscleGround | NodeRole.Perception;
            using var simHostSlave = new ClusterSlave(SimHostNodeId, "SimHost", simHostBus, simHostRoles);
            using var cgfSlave     = new ClusterSlave(CgfNodeId,     "CGF",     cgfBus, NodeRole.Brain);

            // Wait for the heartbeat timer to elapse (> 1 second).
            await Task.Delay(1200);

            simHostSlave.Tick();
            cgfSlave.Tick();
            simHostBus.SwapBuffers();
            cgfBus.SwapBuffers();

            var simHostHeartbeats = new List<NodeHeartbeatEvent>();
            foreach (var e in simHostBus.ReadManaged<NodeHeartbeatEvent>())
                simHostHeartbeats.Add(e);

            var cgfHeartbeats = new List<NodeHeartbeatEvent>();
            foreach (var e in cgfBus.ReadManaged<NodeHeartbeatEvent>())
                cgfHeartbeats.Add(e);

            Assert.Single(simHostHeartbeats);
            Assert.Equal(SimHostNodeId, simHostHeartbeats[0].NodeId);
            Assert.Equal("SimHost",     simHostHeartbeats[0].SubsystemName);
            // P1: the declared multi-role mask survives, not collapsed to one role.
            Assert.Equal(simHostRoles, simHostHeartbeats[0].Roles);
            Assert.True(simHostHeartbeats[0].Roles.HasFlag(NodeRole.MuscleGround));
            Assert.True(simHostHeartbeats[0].Roles.HasFlag(NodeRole.Perception));

            Assert.Single(cgfHeartbeats);
            Assert.Equal(CgfNodeId, cgfHeartbeats[0].NodeId);
            Assert.Equal("CGF",     cgfHeartbeats[0].SubsystemName);
            Assert.Equal(NodeRole.Brain, cgfHeartbeats[0].Roles);
        }
    }
}
