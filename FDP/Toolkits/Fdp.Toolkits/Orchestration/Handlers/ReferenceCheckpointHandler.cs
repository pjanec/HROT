using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Orchestration;
using Fdp.Core.Logging;

namespace Fdp.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// Reference implementation of the checkpoint Cluster handler (CGF1-G0405).
    /// Handles <c>TakeSnapshot</c> — 3-step binary checkpointing protocol (CGF1-S0303).
    /// </summary>
    public sealed class ReferenceCheckpointHandler : ITickableClusterStateHandler
    {
        private readonly CheckpointIOWorker          _worker;
        private readonly EntityRepository?           _liveRepo;
        private readonly EventAccumulator            _eventAccumulator;

        private readonly Dictionary<Guid, ExecuteNodeOpIntent> _pendingPrepares = new();

        /// <param name="worker">Background I/O worker that owns the LZ4+disk pipeline.</param>
        /// <param name="liveRepo">Live <see cref="EntityRepository"/> to snapshot.</param>
        /// <param name="eventAccumulator">Accumulator that flushes event history into each checkpoint snapshot.</param>
        public ReferenceCheckpointHandler(
            CheckpointIOWorker        worker,
            EntityRepository?         liveRepo,
            EventAccumulator          eventAccumulator)
        {
            _worker           = worker           ?? throw new ArgumentNullException(nameof(worker));
            _liveRepo         = liveRepo;
            _eventAccumulator = eventAccumulator ?? throw new ArgumentNullException(nameof(eventAccumulator));
        }

        /// <inheritdoc />
        public bool CanHandle(NodeOpType operation) => operation == NodeOpType.TakeSnapshot;

        /// <summary>Step 1: Records intent as in-progress.</summary>
        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            if (intent.Operation != NodeOpType.TakeSnapshot)
                return Task.FromResult<object?>(null);

            _pendingPrepares[intent.TransactionId] = intent;

            FdpLog<ReferenceCheckpointHandler>.Info(
                "[ReferenceCheckpointHandler] InProgress for TakeSnapshot {0}.",
                intent.TransactionId);

            return Task.FromResult<object?>(null);
        }

        /// <summary>Step 2: Synchronous RAM clone + enqueue to background worker.</summary>
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            if (intent.Operation != NodeOpType.TakeSnapshot) return;
            _pendingPrepares.Remove(intent.TransactionId);

            var source = repo ?? _liveRepo;
            if (source == null)
            {
                FdpLog<ReferenceCheckpointHandler>.Error(
                    "[ReferenceCheckpointHandler] Commit: no EntityRepository available — " +
                    "snapshot for request {0} cannot be taken.", intent.TransactionId);
                return;
            }

            var snap = new EntityRepository();
            snap.SyncFrom(source);
            _eventAccumulator.FlushToReplica(snap.Bus, source.GlobalVersion - 1);
            _worker.Enqueue(snap, intent.TransactionId);

            FdpLog<ReferenceCheckpointHandler>.Info(
                "[ReferenceCheckpointHandler] Commit: snapshot enqueued to I/O worker for request {0}.",
                intent.TransactionId);
        }

        /// <inheritdoc />
        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            _pendingPrepares.Remove(intent.TransactionId);
        }

        /// <summary>
        /// Step 3 (poll): Drains completed results and logs deferred results.
        /// Called each frame from <c>ClusterSlave.Tick()</c>.
        /// </summary>
        public void DrainDeferredAcks()
        {
            foreach (var (requestId, success) in _worker.TakeCompletedResults())
            {
                FdpLog<ReferenceCheckpointHandler>.Info(
                    "[ReferenceCheckpointHandler] Deferred result — request {0} → {1}.",
                    requestId, success ? "Success" : "Timeout");
            }
        }
    }
}
