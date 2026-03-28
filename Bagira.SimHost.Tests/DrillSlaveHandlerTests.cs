using System;
using System.Linq;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration;
using Bagira.SimHost.Modules.Orchestration;
using Fdp.Kernel;
using Xunit;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="DrillSlave"/> DSM handler wiring and event publication
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
        public void CommitState_RaisesEsmStateChangedEvent()
        {
            var eventBus = new FdpEventBus();
            using var slave = new DrillSlave(eventBus);
            slave.RegisterHandler(new LiveLoadDsmHandler(slave, eventBus));

            slave.EnqueueCommandForTest(new NodeOpCommand
            {
                TransactionId = Guid.NewGuid(),
                Operation     = NodeOpType.CommitState,
                PayloadJson   = ((int)DSMState.LoadingLive).ToString(),
            });

            slave.Tick();
            eventBus.SwapBuffers();

            var events = eventBus.Consume<DsmStateChangedEvent>().ToArray();
            Assert.Single(events);
            Assert.Equal(DSMState.LoadingLive, events[0].Next);
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
            var cmd  = new NodeOpCommand
            {
                TransactionId = txId,
                Operation     = NodeOpType.CommitState,
                PayloadJson   = ((int)DSMState.LoadingLive).ToString(),
            };

            // Enqueue the same command twice (simulates DDS re-delivery).
            slave.EnqueueCommandForTest(cmd);
            slave.EnqueueCommandForTest(cmd);

            slave.Tick();
            eventBus.SwapBuffers();

            var events = eventBus.Consume<DsmStateChangedEvent>().ToArray();
            Assert.Single(events);
        }

        /// <summary>
        /// <see cref="DsmStateChangedEvent"/> is defined in <c>Bagira.Common</c>, not in
        /// any <c>FDP/</c> project.  This test serves as a compile-time guard: if the type
        /// were moved to an FDP project the import would break.
        /// </summary>
        [Fact]
        public void DsmStateChangedEvent_IsNotInFdpNamespace()
        {
            var t = typeof(DsmStateChangedEvent);
            Assert.False(
                t.Namespace?.StartsWith("Fdp.", StringComparison.Ordinal) == true ||
                t.Namespace?.StartsWith("FDP.", StringComparison.Ordinal) == true,
                $"DsmStateChangedEvent must not be in an FDP namespace; actual: {t.Namespace}");
        }
    }
}
