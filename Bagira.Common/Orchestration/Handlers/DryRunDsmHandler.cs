using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Fdp.Kernel;
using FDP.Kernel.Logging;

namespace Bagira.Common.Orchestration.Handlers
{
    /// <summary>
    /// DSM handler that implements the dry-run snapshot / rewind protocol (CGF1-S0309).
    ///
    /// <para>
    /// <b>LoadingDryRun</b>: captures a RAM snapshot of the live
    /// <see cref="EntityRepository"/> so that the cluster can preview a scenario
    /// branch without modifying the authoritative state.
    /// </para>
    ///
    /// <para>
    /// <b>UnloadingDryRun</b>: restores the live repository from the snapshot,
    /// effectively rewinding all changes made during the dry-run session.
    /// </para>
    ///
    /// <para>
    /// All other <see cref="NodeOpType.PrepareState"/> targets are no-ops.
    /// This handler never performs disk I/O and does not implement
    /// <see cref="ITickableDsmHandler"/>.
    /// </para>
    ///
    /// <para>
    /// Subsystems that carry no <see cref="EntityRepository"/> (IOS, CGF skeleton, IG)
    /// should pass <c>liveRepo: null</c>; <see cref="Commit"/> will log a warning and
    /// skip the copy safely.
    /// </para>
    /// </summary>
    public sealed class DryRunDsmHandler : IDsmHandler
    {
        private readonly EntityRepository? _liveRepo;
        private EntityRepository? _snap;

        /// <param name="liveRepo">
        /// The subsystem's authoritative entity repository.
        /// Pass <see langword="null"/> for subsystems that carry no ECS state
        /// (IOS, IG, CGF skeleton) — the handler then behaves as a no-op for
        /// both LoadingDryRun and UnloadingDryRun while remaining registered
        /// so the DrillSlave can ACK the two-phase commit correctly.
        /// </param>
        public DryRunDsmHandler(EntityRepository? liveRepo)
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
        /// Acts on <see cref="DSMState.LoadingDryRun"/> and
        /// <see cref="DSMState.UnloadingDryRun"/>; all other targets are no-ops.
        /// </remarks>
        public void Commit(NodeOpCommand cmd, EntityRepository? repo)
        {
            var target = ParseTargetState(cmd.PayloadJson);

            switch (target)
            {
                case DSMState.LoadingDryRun:
                    LoadingDryRunCommit();
                    break;

                case DSMState.UnloadingDryRun:
                    UnloadingDryRunCommit();
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

        // ── Private helpers ───────────────────────────────────────────────────

        private void LoadingDryRunCommit()
        {
            if (_liveRepo == null)
            {
                FdpLog<DryRunDsmHandler>.Warn(
                    "[DryRun] LoadingDryRun: liveRepo is null — snapshot skipped. " +
                    "This subsystem carries no ECS state.");
                _snap = null;
                return;
            }

            var snap = new EntityRepository();
            snap.SyncFrom(_liveRepo);
            _snap = snap;

            FdpLog<DryRunDsmHandler>.Info(
                "[DryRun] LoadingDryRun: snapshot captured.");
        }

        private void UnloadingDryRunCommit()
        {
            if (_snap == null)
            {
                FdpLog<DryRunDsmHandler>.Warn(
                    "[DryRun] UnloadingDryRun: no snapshot present — rewind skipped. " +
                    "Was LoadingDryRun committed successfully?");
                return;
            }

            if (_liveRepo == null)
            {
                FdpLog<DryRunDsmHandler>.Warn(
                    "[DryRun] UnloadingDryRun: liveRepo is null — snapshot discarded without rewind.");
                _snap.Dispose();
                _snap = null;
                return;
            }

            _liveRepo.SyncFrom(_snap);
            _snap.Dispose();
            _snap = null;

            FdpLog<DryRunDsmHandler>.Info(
                "[DryRun] UnloadingDryRun: live repo rewound to snapshot.");
        }

        private static DSMState ParseTargetState(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return DSMState.Standby;
            if (int.TryParse(payloadJson, out var n)) return (DSMState)n;
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("TargetState", out var prop))
                    return (DSMState)prop.GetInt32();
            }
            catch { /* malformed payload — treat as Standby */ }
            return DSMState.Standby;
        }
    }
}
