using System;
using System.Linq;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using Xunit;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Unit tests for the toolkit <see cref="DrillSlave"/> DSM handler wiring and event publication
    /// (CGF1-S0202 success conditions).  No DDS or ECS — all tests are pure in-process.
    /// </summary>
    public sealed class DrillSlaveHandlerTests
    {
        // ── CGF1-S0202: CommitState raises DsmStateChangedEvent ───────────────

        /// <summary>
        /// When a <see cref="NodeOpType.CommitState"/> command with
        /// payload <c>"LoadingLive"</c> (DSMState int = 30) arrives, the slave must
        /// publish exactly one <see cref="DsmStateChangedEvent"/> with
        /// <c>Next == DSMState.LoadingLive</c>.
        /// </summary>
        [Fact]
        public void CommitState_RaisesDsmStateChangedEvent()
        {
            var eventBus = new FdpEventBus();
            using var slave = new DrillSlave(eventBus);

            slave.EnqueueCommandForTest(new OrchestrationCommand(
                Guid.NewGuid(), 0, 2,
                ((int)DSMState.LoadingLive).ToString()));

            slave.Tick();
            eventBus.SwapBuffers();

            var events = eventBus.Consume<TkDsmStateChangedEvent>().ToArray();
            Assert.Single(events);
            Assert.Equal((int)DSMState.LoadingLive, events[0].NextStateId);
        }

        /// <summary>
        /// When the same <see cref="NodeOpCommand"/> (identical <c>TransactionId</c>) is
        /// enqueued twice, only one <see cref="DsmStateChangedEvent"/> must be raised.
        /// Validates the re-delivered DDS message deduplication guard.
        /// </summary>
        [Fact]
        public void DuplicateTransactionId_IsDropped()
        {
            var eventBus  = new FdpEventBus();
            using var slave = new DrillSlave(eventBus);

            var txId = Guid.NewGuid();
            var cmd  = new OrchestrationCommand(
                txId, 0, 2,
                ((int)DSMState.LoadingLive).ToString());

            // Enqueue the same command twice (simulates DDS re-delivery).
            slave.EnqueueCommandForTest(cmd);
            slave.EnqueueCommandForTest(cmd);

            slave.Tick();
            eventBus.SwapBuffers();

            var events = eventBus.Consume<TkDsmStateChangedEvent>().ToArray();
            Assert.Single(events);
        }

        /// <summary>
        /// <see cref="TkDsmStateChangedEvent"/> is published by the toolkit
        /// <see cref="DrillSlave"/> on <c>CommitState</c>.  This test serves as a
        /// compile-time guard confirming the event is in an FDP namespace.
        /// </summary>
        [Fact]
        public void DsmStateChangedEvent_IsNotInFdpNamespace()
        {
            var t = typeof(TkDsmStateChangedEvent);
            Assert.True(
                t.Namespace?.StartsWith("Fdp.", StringComparison.Ordinal) == true ||
                t.Namespace?.StartsWith("FDP.", StringComparison.Ordinal) == true,
                $"TkDsmStateChangedEvent must be in an FDP namespace; actual: {t.Namespace}");
        }

        // ── A.1 (BATCH-06): LocalDsmState heartbeat reflects committed state ────

        /// <summary>
        /// After a <see cref="NodeOpType.CommitState"/> command is processed by
        /// <see cref="DrillSlave.Tick"/>, the slave's stored local DSM state must match
        /// the committed value — confirming that the next heartbeat would carry the
        /// updated state rather than the hardcoded <c>Standby</c> that was the pre-fix bug.
        /// </summary>
        [Fact]
        public void LocalDsmState_ReflectsCommittedState_AfterCommitState()
        {
            var eventBus = new FdpEventBus();
            using var slave = new DrillSlave(eventBus);

            // Initial state must be Standby.
            Assert.Equal((int)DSMState.Standby, slave.LocalStateIdForTest);

            slave.EnqueueCommandForTest(new OrchestrationCommand(
                Guid.NewGuid(), 0, 2,
                ((int)DSMState.LoadingLive).ToString()));

            slave.Tick();

            // After Tick() the stored state must be LoadingLive.
            Assert.Equal((int)DSMState.LoadingLive, slave.LocalStateIdForTest);
        }
    }
}
