using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hrot.NED.Descriptors.Orchestration;
using Fdp.Kernel;
using FDP.Kernel.Logging;

namespace Hrot.Common.Orchestration.Handlers
{
    /// <summary>
    /// Cluster handler that implements the dry-run snapshot / rewind protocol (CGF1-S0309).
    ///
    /// <para>
    /// <b>LoadingPreview</b>: captures a RAM snapshot of the live
    /// <see cref="EntityRepository"/> so that the cluster can preview a scenario
    /// branch without modifying the authoritative state.
    /// </para>
    ///
    /// <para>
    /// <b>UnloadingPreview</b>: restores the live repository from the snapshot,
    /// effectively rewinding all changes made during the dry-run session.
    /// </para>
    ///
    /// <para>
    /// All other <see cref="NodeOpType.PrepareState"/> targets are no-ops.
    /// This handler never performs disk I/O and does not implement
    /// <see cref="ITickableClusterOpHandler"/>.
    /// </para>
    ///
    /// <para>
    /// Subsystems that carry no <see cref="EntityRepository"/> (ExCon, CGF skeleton, IG)
    /// should pass <c>liveRepo: null</c>; <see cref="Commit"/> will log a warning and
    /// skip the copy safely.
    /// </para>
    /// </summary>
    public sealed class PreviewClusterOpHandler : IClusterOpHandler
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
        public PreviewClusterOpHandler(EntityRepository? liveRepo)
        {
            _liveRepo = liveRepo;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Returns <see langword="true"/> for <see cref="NodeOpType.PrepareState"/>.
        /// </remarks>
        public bool CanHandle(NodeOpType op) => op == NodeOpType.PrepareState;

        /// <inheritdoc />
        /// <remarks>
        /// No async preparation is required for dry-run snapshots; all work is
        /// synchronous and happens in <see cref="Commit"/>.
        /// </remarks>
        public Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct)
            => Task.FromResult<string?>(null);

        /// <inheritdoc />
        /// <remarks>
        /// Acts on <see cref="ClusterState.LoadingPreview"/> and
        /// <see cref="ClusterState.UnloadingPreview"/>; all other targets are no-ops.
        /// </remarks>
        public void Commit(NodeOpCommand cmd, EntityRepository? repo)
        {
            var target = ParseTargetState(cmd.PayloadJson);

            switch (target)
            {
                case ClusterState.LoadingPreview:
                    LoadingPreviewCommit();
                    break;

                case ClusterState.UnloadingPreview:
                    UnloadingPreviewCommit();
                    break;

                default:
                    // No-op for all other PrepareState targets (LoadingEdit, LoadingLive, …).
                    break;
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// Discards the in-progress snapshot without touching the live repository.
        /// </remarks>
        public void Abort(NodeOpCommand cmd, EntityRepository? repo)
        {
            _snap?.Dispose();
            _snap = null;
        }

        // ── Internal test accessor ────────────────────────────────────────────

        /// <summary>
        /// Returns the current in-memory snapshot repository, or <see langword="null"/>
        /// when no dry-run is active.  Exposed for unit tests only.
        /// </summary>
        internal EntityRepository? TestHook_Snap => _snap;

        // ── Public convenience entry-points (used by EditorPreviewAdapter) ────

        /// <summary>
        /// Directly triggers the LoadingPreview snapshot without going through the
        /// 2-phase commit protocol.  Use this from offline editor adapters.
        /// </summary>
        public void TriggerLoadingPreview() => LoadingPreviewCommit();

        /// <summary>
        /// Directly triggers the UnloadingPreview rewind without going through the
        /// 2-phase commit protocol.  Use this from offline editor adapters.
        /// </summary>
        public void TriggerUnloadingPreview() => UnloadingPreviewCommit();

        // ── Private helpers ───────────────────────────────────────────────────

        private void LoadingPreviewCommit()
        {
            if (_liveRepo == null)
            {
                FdpLog<PreviewClusterOpHandler>.Warn(
                    "[Preview] LoadingPreview: liveRepo is null — snapshot skipped. " +
                    "This subsystem carries no ECS state.");
                _snap = null;
                return;
            }

            var snap = new EntityRepository();
            snap.SyncFrom(_liveRepo);
            _snap = snap;

            FdpLog<PreviewClusterOpHandler>.Info(
                "[Preview] LoadingPreview: snapshot captured.");
        }

        private void UnloadingPreviewCommit()
        {
            if (_snap == null)
            {
                FdpLog<PreviewClusterOpHandler>.Warn(
                    "[Preview] UnloadingPreview: no snapshot present — rewind skipped. " +
                    "Was LoadingPreview committed successfully?");
                return;
            }

            if (_liveRepo == null)
            {
                FdpLog<PreviewClusterOpHandler>.Warn(
                    "[Preview] UnloadingPreview: liveRepo is null — snapshot discarded without rewind.");
                _snap.Dispose();
                _snap = null;
                return;
            }

            _liveRepo.SyncFrom(_snap);
            _snap.Dispose();
            _snap = null;

            FdpLog<PreviewClusterOpHandler>.Info(
                "[Preview] UnloadingPreview: live repo rewound to snapshot.");
        }

        private static ClusterState ParseTargetState(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return ClusterState.Idle;
            if (int.TryParse(payloadJson, out var n)) return (ClusterState)n;
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("TargetState", out var prop))
                    return (ClusterState)prop.GetInt32();
            }
            catch { /* malformed payload — treat as Standby */ }
            return ClusterState.Idle;
        }
    }
}
