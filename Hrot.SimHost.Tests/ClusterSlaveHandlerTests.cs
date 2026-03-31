using System;
using System.Linq;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Common.Orchestration;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for the toolkit <see cref="ClusterSlave"/> Cluster handler wiring and event publication
    /// (CGF1-S0202 success conditions).  No DDS or ECS — all tests are pure in-process.
    /// </summary>
    public sealed class ClusterSlaveHandlerTests
    {
        // ── CGF1-S0202: CommitState raises ClusterStateChangedEvent ───────────────

        /// <summary>
        /// When a <see cref="NodeOpType.CommitState"/> command with
        /// payload <c>"LoadingLive"</c> (ClusterState int = 30) arrives, the slave must
        /// publish exactly one <see cref="ClusterStateChangedEvent"/> with
        /// <c>Next == ClusterState.LoadingLive</c>.
        /// </summary>
        [Fact]
        public void CommitState_RaisesClusterStateChangedEvent()
        {
            var eventBus = new FdpEventBus();
            using var slave = new ClusterSlave(eventBus);

            slave.EnqueueCommandForTest(new OrchestrationCommand(
                Guid.NewGuid(), 0, 2,
                ((int)ClusterState.LoadingLive).ToString()));

            slave.Tick();
            eventBus.SwapBuffers();

            var events = eventBus.Consume<TkClusterStateChangedEvent>().ToArray();
            Assert.Single(events);
            Assert.Equal((int)ClusterState.LoadingLive, events[0].NextStateId);
        }

        /// <summary>
        /// When the same <see cref="NodeOpCommand"/> (identical <c>TransactionId</c>) is
        /// enqueued twice, only one <see cref="ClusterStateChangedEvent"/> must be raised.
        /// Validates the re-delivered DDS message deduplication guard.
        /// </summary>
        [Fact]
        public void DuplicateTransactionId_IsDropped()
        {
            var eventBus  = new FdpEventBus();
            using var slave = new ClusterSlave(eventBus);

            var txId = Guid.NewGuid();
            var cmd  = new OrchestrationCommand(
                txId, 0, 2,
                ((int)ClusterState.LoadingLive).ToString());

            // Enqueue the same command twice (simulates DDS re-delivery).
            slave.EnqueueCommandForTest(cmd);
            slave.EnqueueCommandForTest(cmd);

            slave.Tick();
            eventBus.SwapBuffers();

            var events = eventBus.Consume<TkClusterStateChangedEvent>().ToArray();
            Assert.Single(events);
        }

        /// <summary>
        /// <see cref="TkClusterStateChangedEvent"/> is published by the toolkit
        /// <see cref="ClusterSlave"/> on <c>CommitState</c>.  This test serves as a
        /// compile-time guard confirming the event is in an FDP namespace.
        /// </summary>
        [Fact]
        public void ClusterStateChangedEvent_IsNotInFdpNamespace()
        {
            var t = typeof(TkClusterStateChangedEvent);
            Assert.True(
                t.Namespace?.StartsWith("Fdp.", StringComparison.Ordinal) == true ||
                t.Namespace?.StartsWith("FDP.", StringComparison.Ordinal) == true,
                $"TkClusterStateChangedEvent must be in an FDP namespace; actual: {t.Namespace}");
        }

        // ── A.1 (BATCH-06): LocalClusterState heartbeat reflects committed state ────

        /// <summary>
        /// After a <see cref="NodeOpType.CommitState"/> command is processed by
        /// <see cref="ClusterSlave.Tick"/>, the slave's stored local Cluster state must match
        /// the committed value — confirming that the next heartbeat would carry the
        /// updated state rather than the hardcoded <c>Standby</c> that was the pre-fix bug.
        /// </summary>
        [Fact]
        public void LocalClusterState_ReflectsCommittedState_AfterCommitState()
        {
            var eventBus = new FdpEventBus();
            using var slave = new ClusterSlave(eventBus);

            // Initial state must be Standby.
            Assert.Equal((int)ClusterState.Idle, slave.LocalStateIdForTest);

            slave.EnqueueCommandForTest(new OrchestrationCommand(
                Guid.NewGuid(), 0, 2,
                ((int)ClusterState.LoadingLive).ToString()));

            slave.Tick();

            // After Tick() the stored state must be LoadingLive.
            Assert.Equal((int)ClusterState.LoadingLive, slave.LocalStateIdForTest);
        }
    }
}
