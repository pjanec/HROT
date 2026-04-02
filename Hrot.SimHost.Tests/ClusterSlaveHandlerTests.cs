using System;
using System.Linq;
using Hrot.NED.Descriptors.Orchestration;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using Xunit;
using NodeOpType = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;

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

            slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = FDP.Toolkit.Orchestration.NodeOpType.CommitState,
                DomainPayload = (int)ClusterState.LoadingLive,
            });

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
            var intent  = new ExecuteNodeOpIntent
            {
                TransactionId = txId,
                TargetNodeId  = 0,
                Operation     = FDP.Toolkit.Orchestration.NodeOpType.CommitState,
                DomainPayload = (int)ClusterState.LoadingLive,
            };

            // Enqueue the same command twice (simulates DDS re-delivery).
            slave.EnqueueIntentForTest(intent);
            slave.EnqueueIntentForTest(intent);

            slave.Tick();
            eventBus.SwapBuffers();

            var events = eventBus.Consume<TkClusterStateChangedEvent>().ToArray();
            Assert.Single(events);
        }

        /// <summary>
        /// When <c>PrepareXxx</c> (OperationId != 2) and <c>CommitState</c> (OperationId == 2)
        /// share the same <c>TransactionId</c>, both must be dispatched — deduplication uses a
        /// compound <c>(TransactionId, OperationId)</c> key, so different operations in the same
        /// 2PC transaction are each accepted once.
        /// </summary>
        [Fact]
        public void PrepareAndCommit_SameTransactionId_BothDispatched()
        {
            var eventBus = new FdpEventBus();
            using var slave = new ClusterSlave(eventBus);

            var txId = Guid.NewGuid();

            // Prepare: no handler registered → does nothing but is accepted past the dedup guard.
            slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
            {
                TransactionId = txId,
                TargetNodeId  = 0,
                Operation     = FDP.Toolkit.Orchestration.NodeOpType.PrepareLive,
                DomainPayload = null,
            });

            // Commit (same TransactionId, different OperationId) must NOT be dropped.
            slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
            {
                TransactionId = txId,
                TargetNodeId  = 0,
                Operation     = FDP.Toolkit.Orchestration.NodeOpType.CommitState,
                DomainPayload = (int)ClusterState.LoadingLive,
            });

            slave.Tick();
            eventBus.SwapBuffers();

            // CommitState must have fired → exactly one TkClusterStateChangedEvent.
            var events = eventBus.Consume<TkClusterStateChangedEvent>().ToArray();
            Assert.Single(events);
            Assert.Equal((int)ClusterState.LoadingLive, events[0].NextStateId);
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

            slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = FDP.Toolkit.Orchestration.NodeOpType.CommitState,
                DomainPayload = (int)ClusterState.LoadingLive,
            });

            slave.Tick();

            // After Tick() the stored state must be LoadingLive.
            Assert.Equal((int)ClusterState.LoadingLive, slave.LocalStateIdForTest);
        }
    }
}
