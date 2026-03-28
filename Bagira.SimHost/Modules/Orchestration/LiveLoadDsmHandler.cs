using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration;
using Fdp.Kernel;

namespace Bagira.SimHost.Modules.Orchestration
{
    /// <summary>
    /// Stub DSM handler for live-session load operations.
    ///
    /// <para>Handles <see cref="NodeOpType.PrepareLive"/> and
    /// <see cref="NodeOpType.FinalizeLive"/> commands.  In Phase 2.0 the prepare step
    /// returns immediately (no work to do until Stage 3.4 wires the ECS record/replay
    /// controller).  The commit step publishes <see cref="DsmStateChangedEvent"/> as a
    /// safeguard in case the slave-level <c>CommitState</c> event was not already raised
    /// for this transaction.</para>
    ///
    /// <para>Full implementation is deferred to <c>CGF1-S0304</c> (dynamic recording
    /// modules); this stub satisfies the CGF1-S0202 success conditions.</para>
    /// </summary>
    public sealed class LiveLoadDsmHandler : IDsmHandler
    {
        private readonly DrillSlave _slave;
        private readonly FdpEventBus _eventBus;

        /// <param name="slave">
        /// Owning slave; used to call
        /// <see cref="DrillSlave.PublishDsmStateChanged"/> as a guard.
        /// </param>
        /// <param name="eventBus">Event bus for <see cref="DsmStateChangedEvent"/> publication.</param>
        public LiveLoadDsmHandler(DrillSlave slave, FdpEventBus eventBus)
        {
            _slave    = slave;
            _eventBus = eventBus;
        }

        /// <inheritdoc />
        public bool CanHandle(NodeOpType op) =>
            op == NodeOpType.PrepareLive || op == NodeOpType.FinalizeLive;

        /// <summary>
        /// Phase 2.0 stub — returns <c>null</c> (success) immediately.
        /// Full implementation (scenario load, ECS warm-up) is deferred to Stage 3.4.
        /// </summary>
        public Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct) =>
            Task.FromResult<string?>(null);

        /// <summary>
        /// Commits the live-load command.  Publishes <see cref="DsmStateChangedEvent"/>
        /// via the event bus as a safeguard if the slave-level <c>CommitState</c> handling
        /// has not already done so for this transaction.
        /// <para>
        /// <b>Note:</b> In Phase 2.0 the live-load transition target is hardcoded to
        /// <see cref="DSMState.LoadingLive"/>.  The authoritative post-commit state will be
        /// derived from the command payload once 2PC plumbing is complete in
        /// <c>CGF1-S0202+</c>.
        /// </para>
        /// </summary>
        public void Commit(NodeOpCommand cmd, EntityRepository? repo)
        {
            // Guard: publish DsmStateChangedEvent if not already raised by the slave for
            // this transaction.  The slave's CommitState path is the primary publisher;
            // this call is defensive for handler-only commit flows.
            _slave.PublishDsmStateChanged(DSMState.Standby, DSMState.LoadingLive);
        }

        /// <inheritdoc />
        public void Abort(NodeOpCommand cmd, EntityRepository? repo)
        {
            // No resources to release in the Phase 2.0 stub.
        }
    }
}
