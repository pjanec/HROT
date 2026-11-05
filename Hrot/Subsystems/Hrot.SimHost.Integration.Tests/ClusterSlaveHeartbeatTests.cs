using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication;
using Xunit;

namespace Hrot.SimHost.Integration.Tests
{
    /// <summary>
    /// CMC-S006 / CE-285 / CE-286 — verifies that ClusterSlave instances publish a telemetry-only
    /// <see cref="NodeHeartbeatEvent"/> (1 Hz) AND a static <see cref="NodeCapabilitiesEvent"/> once at join.
    ///
    /// <para>⭐ CE-286 (C-roles) moved the role mask OFF the per-tick heartbeat onto the durable capability
    /// token set: a node advertises its role as <c>fdp.role.*</c> tokens (the bit-backed subset) and the
    /// orchestrator/cache DERIVE the mask via <see cref="NodeRoleTokens"/>. This rail proves the multi-role
    /// mask survives that projection end to end, and that a NED node also advertises
    /// <c>fdp.reliable-init</c> (AQ-70 §Q70-B/C).</para>
    /// </summary>
    [Collection("LogCapture")]
    public sealed class ClusterSlaveHeartbeatTests
    {
        private const int SimHostNodeId = 1;
        private const int CgfNodeId     = 400;

        [Fact]
        public async Task ClusterSlaves_PublishHeartbeat_AndCapabilityTokens_ToBus()
        {
            var simHostBus = new FdpEventBus();
            var cgfBus     = new FdpEventBus();

            // SimHost declares a MULTI-role mask; its fdp.role.* tokens must preserve it end to end.
            const NodeRole simHostRoles = NodeRole.MuscleGround | NodeRole.Perception;
            var reliable = new[] { CapabilityTokens.ReliableInit };
            using var simHostSlave = new ClusterSlave(SimHostNodeId, "SimHost", simHostBus, simHostRoles, reliable);
            using var cgfSlave     = new ClusterSlave(CgfNodeId,     "CGF",     cgfBus, NodeRole.Brain, reliable);

            // Wait for the heartbeat timer to elapse (> 1 second). Capabilities publish on the first tick regardless.
            await Task.Delay(1200);

            simHostSlave.Tick();
            cgfSlave.Tick();
            simHostBus.SwapBuffers();
            cgfBus.SwapBuffers();

            var simHostHeartbeats = simHostBus.ReadManaged<NodeHeartbeatEvent>().ToList();
            var cgfHeartbeats     = cgfBus.ReadManaged<NodeHeartbeatEvent>().ToList();
            var simHostCaps       = simHostBus.ReadManaged<NodeCapabilitiesEvent>().ToList();
            var cgfCaps           = cgfBus.ReadManaged<NodeCapabilitiesEvent>().ToList();

            // Heartbeat: telemetry-only, still emitted at 1 Hz (no role field any more).
            Assert.Single(simHostHeartbeats);
            Assert.Equal(SimHostNodeId, simHostHeartbeats[0].NodeId);
            Assert.Equal("SimHost",     simHostHeartbeats[0].SubsystemName);
            Assert.Single(cgfHeartbeats);
            Assert.Equal(CgfNodeId, cgfHeartbeats[0].NodeId);
            Assert.Equal("CGF",     cgfHeartbeats[0].SubsystemName);

            // Capabilities: published ONCE at join, carrying fdp.role.* tokens + fdp.reliable-init.
            Assert.Single(simHostCaps);
            Assert.Equal(SimHostNodeId, simHostCaps[0].NodeId);
            Assert.Contains(NodeRoleTokens.MuscleGround, simHostCaps[0].Capabilities);
            Assert.Contains(NodeRoleTokens.Perception,   simHostCaps[0].Capabilities);
            Assert.Contains(CapabilityTokens.ReliableInit, simHostCaps[0].Capabilities);
            // The multi-role mask survives the token round-trip, not collapsed to one role.
            Assert.Equal(simHostRoles, NodeRoleTokens.MaskFromTokens(simHostCaps[0].Capabilities));

            Assert.Single(cgfCaps);
            Assert.Equal(CgfNodeId, cgfCaps[0].NodeId);
            Assert.Contains(NodeRoleTokens.Brain, cgfCaps[0].Capabilities);
            Assert.Contains(CapabilityTokens.ReliableInit, cgfCaps[0].Capabilities);
            Assert.Equal(NodeRole.Brain, NodeRoleTokens.MaskFromTokens(cgfCaps[0].Capabilities));
        }

        /// <summary>
        /// ⭐⭐⭐ CE-2417 — while a load step WORKS (here: a worker's step, as the chain opens it), the frame's beat means "the load advances": with no
        /// progress in the last second no beat is sent (a hung loader ⇒ the master's 5 s rule ejects the node); progress ⇒ the beat goes; the step
        /// done ⇒ beats as before. 🔒 User: "if it gets stuck, no more hearbeat shall be sent". 🔴 Red-proof: the gate in PublishHeartbeat removed ⇒
        /// the stalled tick beats.
        /// </summary>
        [Fact]
        public async Task CE2417_WhileALoadStepWorks_TheFrameBeatsOnlyIfTheLoadAdvances()
        {
            var bus = new FdpEventBus();
            using var slave = new ClusterSlave(CgfNodeId, "CGF", bus, NodeRole.Brain);
            int Beats() { bus.SwapBuffers(); return bus.ReadManaged<NodeHeartbeatEvent>().Count(); }

            slave.Tick();
            Assert.Equal(1, Beats());                         // the first beat

            var working = slave.Progress.Active("Terrain");   // a worker's load step starts …
            await Task.Delay(1200);
            slave.Tick();
            Assert.Equal(0, Beats());                         // … and has not advanced for a second: ⛔ no beat

            slave.Progress.Advance();                         // it advances
            slave.Tick();
            Assert.Equal(1, Beats());                         // ⇒ the beat goes

            working.Dispose();                                // the step is done (the node now waits — not hung)
            await Task.Delay(1200);
            slave.Tick();
            Assert.Equal(1, Beats());                         // ⇒ beats as before
        }

        [Fact]
        public void ClusterSlave_PublishesCapabilitiesOnlyOnce()
        {
            var bus = new FdpEventBus();
            using var slave = new ClusterSlave(SimHostNodeId, "SimHost", bus, NodeRole.MuscleGround,
                new[] { CapabilityTokens.ReliableInit });

            // Three ticks; capabilities are static and must be published exactly once (durable, at join).
            for (int i = 0; i < 3; i++) { slave.Tick(); }
            bus.SwapBuffers();

            Assert.Single(bus.ReadManaged<NodeCapabilitiesEvent>());
        }
    }
}
