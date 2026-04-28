using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;

namespace Fdp.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// Reference implementation of the dry-run snapshot / rewind handler (CGF1-G0405).
    ///
    /// <para>
    /// Handles <c>PrepareState (operationId=1)</c> payloads that target
    /// <c>ClusterState.LoadingPreview</c> or <c>ClusterState.UnloadingPreview</c>.
    /// All other <c>PrepareState</c> targets are passed through as no-ops.
    /// </para>
    ///
    /// <para>
    /// <b>LoadingPreview:</b> captures a RAM snapshot of the live
    /// <see cref="EntityRepository"/> so that the cluster can preview a scenario
    /// branch without modifying the authoritative state.
    /// </para>
    ///
    /// <para>
    /// <b>UnloadingPreview:</b> restores the live repository from the snapshot,
    /// effectively rewinding all changes made during the dry-run session.
    /// </para>
    ///
    /// <para>
    /// This handler never performs disk I/O and does not implement
    /// <see cref="ITickableClusterStateHandler"/>.
    /// </para>
    ///
    /// <para>
    /// Subsystems that carry no <see cref="EntityRepository"/> (ExCon, CGF skeleton, IG)
    /// should pass <c>liveRepo: null</c>; <see cref="Commit"/> will log a warning and
    /// skip the copy safely.
    /// </para>
    /// </summary>
    public sealed class ReferencePreviewHandler : IClusterStateHandler
    {
        private readonly EntityRepository? _liveRepo;
        private EntityRepository? _snap;

        /// <param name="liveRepo">
        /// The subsystem's authoritative entity repository.
        /// Pass <see langword="null"/> for subsystems that carry no ECS state
        /// (ExCon, IG, CGF skeleton) — the handler then behaves as a no-op for
        /// both LoadingPreview and UnloadingPreview while remaining registered
        /// so the ClusterSlave can ACK the two-phase commit correctly.
        /// </param>
        public ReferencePreviewHandler(EntityRepository? liveRepo)
        {
            _liveRepo = liveRepo;
        }

        /// <inheritdoc />
        public bool CanHandle(NodeOpType operation) => operation == NodeOpType.PrepareState;

        /// <inheritdoc />
        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
            => Task.FromResult<object?>(null);

        /// <inheritdoc />
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            var target = intent.DomainPayload switch
            {
                EditLoadHandlerPayload elp => elp.TargetState,
                _                          => (ClusterState)0,
            };

            switch (target)
            {
                case ClusterState.LoadingPreview:
                    LoadingPreviewCommit();
                    break;

                case ClusterState.UnloadingPreview:
                    UnloadingPreviewCommit();
                    break;
            }
        }

        /// <inheritdoc />
        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            _snap?.Dispose();
            _snap = null;
        }

        // ── Internal test accessor ─────────────────────────────────────────────

        /// <summary>
        /// Returns the current in-memory snapshot repository, or <see langword="null"/>
        /// when no dry-run is active.  Exposed for unit tests only.
        /// </summary>
        public EntityRepository? TestHook_Snap => _snap;

        // ── Private helpers ────────────────────────────────────────────────────

        private void LoadingPreviewCommit()
        {
            if (_liveRepo == null)
            {
                FdpLog<ReferencePreviewHandler>.Warn(
                    "[ReferencePreviewHandler] LoadingPreview: liveRepo is null — snapshot skipped. " +
                    "This subsystem carries no ECS state.");
                _snap = null;
                return;
            }

            var snap = new EntityRepository();
            snap.SyncFrom(_liveRepo);
            _snap = snap;

            FdpLog<ReferencePreviewHandler>.Info(
                "[ReferencePreviewHandler] LoadingPreview: snapshot captured.");
        }

        private void UnloadingPreviewCommit()
        {
            if (_snap == null)
            {
                FdpLog<ReferencePreviewHandler>.Warn(
                    "[ReferencePreviewHandler] UnloadingPreview: no snapshot present — rewind skipped. " +
                    "Was LoadingPreview committed successfully?");
                return;
            }

            if (_liveRepo == null)
            {
                FdpLog<ReferencePreviewHandler>.Warn(
                    "[ReferencePreviewHandler] UnloadingPreview: liveRepo is null — snapshot discarded without rewind.");
                _snap.Dispose();
                _snap = null;
                return;
            }

            _liveRepo.SyncFrom(_snap);
            _snap.Dispose();
            _snap = null;

            FdpLog<ReferencePreviewHandler>.Info(
                "[ReferencePreviewHandler] UnloadingPreview: live repo rewound to snapshot.");
        }
    }
}
