using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
using Fdp.Kernel.Orchestration;
using FDP.Kernel.Logging;

namespace FDP.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// Reference implementation of the checkpoint DSM handler (CGF1-G0405).
    ///
    /// <para>
    /// Handles <c>TakeSnapshot (operationId=4)</c> — implements Steps 1–2 of the
    /// 3-step binary checkpointing protocol (CGF1-S0303).
    /// </para>
    ///
    /// <para>
    /// <b>PrepareAsync (Step 1 — immediate InProgress ACK):</b> Publishes
    /// <c>NodeOpStatus(InProgress)</c> via <see cref="IOrchestrationTransport"/>
    /// immediately so the orchestrator knows the command was received.
    /// </para>
    ///
    /// <para>
    /// <b>Commit (Step 2 — synchronous RAM clone, ~2 ms):</b> Creates a fresh
    /// <see cref="EntityRepository"/> snap, calls <c>snap.SyncFrom(liveRepo)</c>
    /// (unmanaged NativeChunkTable memcpy), and enqueues the snapshot to
    /// <see cref="CheckpointIOWorker"/>. Ownership of the snapshot is transferred
    /// to the worker, which disposes it after writing.
    /// </para>
    ///
    /// <para>
    /// <b>DrainDeferredAcks (Step 3 — deferred Success/Failure ACK):</b>
    /// Called each frame by <c>DrillSlave.Tick()</c> via <see cref="ITickableDsmHandler"/>.
    /// Polls <see cref="CheckpointIOWorker.TakeCompletedResults"/> and publishes
    /// <c>NodeOpStatus(Success)</c> or <c>NodeOpStatus(Failure)</c> for each
    /// completed checkpoint write.
    /// </para>
    ///
    /// <para>
    /// Multiple concurrent <c>TakeSnapshot</c> requests are supported: each checkpoint
    /// gets its own snapshot entity repository created in <see cref="Commit"/>, and the
    /// worker processes them sequentially.
    /// </para>
    /// </summary>
    public sealed class ReferenceCheckpointHandler : ITickableDsmHandler
    {
        /// <summary>Integer value of <c>NodeOpType.TakeSnapshot</c>.</summary>
        public const int TakeSnapshotOperationId = 4;

        private readonly CheckpointIOWorker          _worker;
        private readonly EntityRepository?           _liveRepo;
        private readonly IOrchestrationTransport?    _transport;
        private readonly int                         _nodeId;

        private readonly Dictionary<Guid, OrchestrationCommand> _pendingPrepares = new();

        /// <param name="worker">Background I/O worker that owns the LZ4+disk pipeline.</param>
        /// <param name="liveRepo">
        /// Live <see cref="EntityRepository"/> to snapshot; used when <see cref="Commit"/>
        /// is called with <c>repo: null</c> (production path via DrillSlave dispatch).
        /// </param>
        /// <param name="transport">
        /// Optional transport for publishing <c>NodeOpStatus</c> ACKs.
        /// Pass <c>null</c> in unit tests that do not require DDS.
        /// </param>
        /// <param name="nodeId">Node identifier included in status messages.</param>
        public ReferenceCheckpointHandler(
            CheckpointIOWorker        worker,
            EntityRepository?         liveRepo,
            IOrchestrationTransport?  transport,
            int                       nodeId)
        {
            _worker    = worker ?? throw new ArgumentNullException(nameof(worker));
            _liveRepo  = liveRepo;
            _transport = transport;
            _nodeId    = nodeId;
        }

        /// <inheritdoc />
        public bool CanHandle(int operationId) => operationId == TakeSnapshotOperationId;

        /// <summary>
        /// Step 1: Publishes <c>NodeOpStatus(InProgress)</c> immediately.
        /// </summary>
        public Task<string?> PrepareAsync(OrchestrationCommand cmd, CancellationToken ct)
        {
            if (cmd.OperationId != TakeSnapshotOperationId)
                return Task.FromResult<string?>(null);

            _pendingPrepares[cmd.TransactionId] = cmd;

            _transport?.PublishStatus(new OrchestrationStatus(
                TransactionId:   cmd.TransactionId,
                NodeId:          _nodeId,
                StatusCode:      OrchestrationStatusCode.InProgress,
                IsParticipating: true,
                ResultJson:      string.Empty));

            FdpLog<ReferenceCheckpointHandler>.Info(
                "[ReferenceCheckpointHandler] InProgress ACK published for TakeSnapshot {0}.",
                cmd.TransactionId);

            return Task.FromResult<string?>(null);
        }

        /// <summary>
        /// Step 2: Synchronous RAM clone + enqueue to background worker.
        /// </summary>
        public void Commit(OrchestrationCommand cmd, EntityRepository? repo)
        {
            if (cmd.OperationId != TakeSnapshotOperationId) return;
            _pendingPrepares.Remove(cmd.TransactionId);

            var source = repo ?? _liveRepo;
            if (source == null)
            {
                FdpLog<ReferenceCheckpointHandler>.Error(
                    "[ReferenceCheckpointHandler] Commit: no EntityRepository available — " +
                    "snapshot for request {0} cannot be taken.", cmd.TransactionId);
                _transport?.PublishStatus(new OrchestrationStatus(
                    TransactionId:   cmd.TransactionId,
                    NodeId:          _nodeId,
                    StatusCode:      OrchestrationStatusCode.Timeout,
                    IsParticipating: true,
                    ResultJson:      string.Empty));
                return;
            }

            var snap = new EntityRepository();
            snap.SyncFrom(source);
            _worker.Enqueue(snap, cmd.TransactionId);

            FdpLog<ReferenceCheckpointHandler>.Info(
                "[ReferenceCheckpointHandler] Commit: snapshot enqueued to I/O worker for request {0}.",
                cmd.TransactionId);
        }

        /// <inheritdoc />
        public void Abort(OrchestrationCommand cmd, EntityRepository? repo)
        {
            _pendingPrepares.Remove(cmd.TransactionId);
        }

        /// <summary>
        /// Step 3 (poll): Drains <see cref="CheckpointIOWorker.TakeCompletedResults"/>
        /// and publishes deferred <c>NodeOpStatus(Success/Failure)</c> ACKs.
        /// Called each frame from <c>DrillSlave.Tick()</c>.
        /// </summary>
        public void DrainDeferredAcks()
        {
            foreach (var (requestId, success) in _worker.TakeCompletedResults())
            {
                var statusCode = success
                    ? OrchestrationStatusCode.Success
                    : OrchestrationStatusCode.Timeout;

                _transport?.PublishStatus(new OrchestrationStatus(
                    TransactionId:   requestId,
                    NodeId:          _nodeId,
                    StatusCode:      statusCode,
                    IsParticipating: true,
                    ResultJson:      string.Empty));

                FdpLog<ReferenceCheckpointHandler>.Info(
                    "[ReferenceCheckpointHandler] Deferred ACK published — request {0} → {1}.",
                    requestId, statusCode);
            }
        }
    }
}
