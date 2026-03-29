using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Kernel.Orchestration;
using FDP.Kernel.Logging;

namespace Bagira.SimHost.Modules.Orchestration.Handlers
{
    /// <summary>
    /// DSM handler for <see cref="NodeOpType.TakeSnapshot"/> — implements Steps 1–2 of
    /// the 3-step binary checkpointing protocol (CGF1-S0303).
    ///
    /// <para>
    /// <b>Protocol (from this handler's perspective):</b>
    /// <list type="number">
    ///   <item>
    ///     <b>PrepareAsync (Step 1 — immediate InProgress ACK):</b> Publishes
    ///     <c>NodeOpStatus(InProgress)</c> to DDS immediately so the orchestrator
    ///     knows the command was received.
    ///   </item>
    ///   <item>
    ///     <b>Commit (Step 2 — synchronous RAM clone, ~2 ms):</b> Creates a fresh
    ///     <see cref="EntityRepository"/> snap, calls <c>snap.SyncFrom(liveRepo)</c>
    ///     (unmanaged NativeChunkTable memcpy), and enqueues the snapshot to
    ///     <see cref="CheckpointIOWorker"/>. Ownership of the snapshot is transferred
    ///     to the worker, which disposes it after writing.
    ///   </item>
    ///   <item>
    ///     <b>DrainDeferredAcks (Step 3 — deferred Success/Failure ACK):</b>
    ///     Called each frame by <c>DrillSlave.Tick()</c> via <see cref="ITickableDsmHandler"/>.
    ///     Polls <see cref="CheckpointIOWorker.TakeCompletedResults"/> and publishes
    ///     <c>NodeOpStatus(Success)</c> or <c>NodeOpStatus(Failure)</c> for each
    ///     completed checkpoint write.
    ///   </item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Multiple concurrent <c>TakeSnapshot</c> requests are supported: each checkpoint
    /// gets its own <see cref="EntityRepository"/> snapshot created in <see cref="Commit"/>,
    /// and the worker processes them sequentially (one LZ4 write at a time).
    /// </para>
    /// </summary>
    public sealed class CheckpointDsmHandler : IDsmHandler, ITickableDsmHandler
    {
        private readonly CheckpointIOWorker          _worker;
        private readonly EntityRepository?           _liveRepo;
        private readonly DdsWriter<NodeOpStatus>?    _statusWriter;
        private readonly int                         _nodeId;

        // Pending PrepareAsync transaction IDs: maps TransactionId → cmd (needed for Commit lookup).
        private readonly Dictionary<Guid, NodeOpCommand> _pendingPrepares = new();

        /// <param name="worker">Background I/O worker that owns the LZ4+disk pipeline.</param>
        /// <param name="liveRepo">
        /// Live <see cref="EntityRepository"/> to snapshot; used when <c>Commit</c>
        /// is called with <c>repo: null</c> (production path via DrillSlave dispatch).
        /// </param>
        /// <param name="statusWriter">DDS writer for publishing <c>NodeOpStatus</c> ACKs.</param>
        /// <param name="nodeId">Node identifier included in <c>NodeOpStatus.NodeId</c>.</param>
        public CheckpointDsmHandler(
            CheckpointIOWorker       worker,
            EntityRepository?        liveRepo,
            DdsWriter<NodeOpStatus>? statusWriter,
            int                      nodeId)
        {
            _worker       = worker       ?? throw new ArgumentNullException(nameof(worker));
            _liveRepo     = liveRepo;
            _statusWriter = statusWriter;
            _nodeId       = nodeId;
        }

        /// <inheritdoc />
        /// <remarks>Returns <c>true</c> for <see cref="NodeOpType.TakeSnapshot"/>.</remarks>
        public bool CanHandle(NodeOpType op) => op == NodeOpType.TakeSnapshot;

        /// <summary>
        /// Step 1: Publishes <c>NodeOpStatus(InProgress)</c> immediately.
        /// The actual checkpoint write is deferred to the background worker thread.
        /// </summary>
        public Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct)
        {
            if (cmd.Operation != NodeOpType.TakeSnapshot)
                return Task.FromResult<string?>(null);

            // Cache the command so Commit can look it up by TransactionId.
            _pendingPrepares[cmd.TransactionId] = cmd;

            // Step 1: Immediate InProgress ACK.
            _statusWriter?.Write(new NodeOpStatus
            {
                TransactionId  = cmd.TransactionId,
                NodeId         = _nodeId,
                Status         = OpStatus.InProgress,
                IsParticipating = true,
                ErrorCode      = 0,
                ResultJson     = string.Empty,
            });
            FdpLog<CheckpointDsmHandler>.Info(
                "[SimHost] CheckpointDsmHandler: InProgress ACK published for TakeSnapshot {0}.",
                cmd.TransactionId);

            return Task.FromResult<string?>(null);
        }

        /// <summary>
        /// Step 2: Synchronous RAM clone + enqueue to background worker.
        /// Creates a fresh snapshot <see cref="EntityRepository"/>, calls
        /// <c>snap.SyncFrom(liveRepo)</c> (~2 ms), and enqueues to
        /// <see cref="CheckpointIOWorker"/>. Ownership of the snapshot is transferred;
        /// the worker disposes it after the file is written.
        /// </summary>
        public void Commit(NodeOpCommand cmd, EntityRepository? repo)
        {
            if (cmd.Operation != NodeOpType.TakeSnapshot) return;
            _pendingPrepares.Remove(cmd.TransactionId);

            var source = repo ?? _liveRepo;
            if (source == null)
            {
                FdpLog<CheckpointDsmHandler>.Error(
                    "[SimHost] CheckpointDsmHandler.Commit: no EntityRepository available — " +
                    "snapshot for request {0} cannot be taken.", cmd.TransactionId);
                // Publish failure immediately (no worker enqueue needed).
                _statusWriter?.Write(new NodeOpStatus
                {
                    TransactionId  = cmd.TransactionId,
                    NodeId         = _nodeId,
                    Status         = OpStatus.Failure,
                    IsParticipating = true,
                    ErrorCode      = 1,
                    ResultJson     = string.Empty,
                });
                return;
            }

            // Step 2: synchronous memcpy into isolated snapshot (unmanaged NativeChunkTable).
            var snap = new EntityRepository();
            snap.SyncFrom(source);

            // Enqueue to background worker (Step 3). Worker owns + disposes snap.
            _worker.Enqueue(snap, cmd.TransactionId);

            FdpLog<CheckpointDsmHandler>.Info(
                "[SimHost] CheckpointDsmHandler.Commit: snapshot enqueued to I/O worker for request {0}.",
                cmd.TransactionId);
        }

        /// <inheritdoc />
        public void Abort(NodeOpCommand cmd, EntityRepository? repo)
        {
            _pendingPrepares.Remove(cmd.TransactionId);
        }

        /// <summary>
        /// Step 3 (poll): Drains <see cref="CheckpointIOWorker.TakeCompletedResults"/>
        /// and publishes deferred <c>NodeOpStatus(Success/Failure)</c> ACKs to DDS.
        /// Called each frame from <c>DrillSlave.Tick()</c>.
        /// </summary>
        public void DrainDeferredAcks()
        {
            foreach (var (requestId, success) in _worker.TakeCompletedResults())
            {
                var status = success ? OpStatus.Success : OpStatus.Failure;
                _statusWriter?.Write(new NodeOpStatus
                {
                    TransactionId  = requestId,
                    NodeId         = _nodeId,
                    Status         = status,
                    IsParticipating = true,
                    ErrorCode      = success ? 0 : 3,
                    ResultJson     = string.Empty,
                });
                FdpLog<CheckpointDsmHandler>.Info(
                    "[SimHost] CheckpointDsmHandler: deferred ACK published — request {0} → {1}.",
                    requestId, status);
            }
        }
    }
}
