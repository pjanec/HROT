using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
using FDP.Kernel.Logging;

namespace FDP.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// Reference implementation of the dry-run snapshot / rewind handler (CGF1-G0405).
    ///
    /// <para>
    /// Handles <c>PrepareState (operationId=1)</c> payloads that target
    /// <c>LoadingDryRun (state=20)</c> or <c>UnloadingDryRun (state=22)</c>.
    /// All other <c>PrepareState</c> targets are passed through as no-ops.
    /// </para>
    ///
    /// <para>
    /// <b>LoadingDryRun:</b> captures a RAM snapshot of the live
    /// <see cref="EntityRepository"/> so that the cluster can preview a scenario
    /// branch without modifying the authoritative state.
    /// </para>
    ///
    /// <para>
    /// <b>UnloadingDryRun:</b> restores the live repository from the snapshot,
    /// effectively rewinding all changes made during the dry-run session.
    /// </para>
    ///
    /// <para>
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
    public sealed class ReferenceDryRunHandler : IDsmHandler
    {
        /// <summary>Integer value of <c>NodeOpType.PrepareState</c>.</summary>
        public const int PrepareStateOperationId = 1;

        /// <summary>Integer value of <c>DSMState.LoadingDryRun</c>.</summary>
        private const int LoadingDryRunState   = 20;
        /// <summary>Integer value of <c>DSMState.UnloadingDryRun</c>.</summary>
        private const int UnloadingDryRunState = 22;

        private readonly EntityRepository? _liveRepo;
        private EntityRepository? _snap;

        /// <param name="liveRepo">
        /// The subsystem's authoritative entity repository.
        /// Pass <see langword="null"/> for subsystems that carry no ECS state
        /// (IOS, IG, CGF skeleton) — the handler then behaves as a no-op for
        /// both LoadingDryRun and UnloadingDryRun while remaining registered
        /// so the DrillSlave can ACK the two-phase commit correctly.
        /// </param>
        public ReferenceDryRunHandler(EntityRepository? liveRepo)
        {
            _liveRepo = liveRepo;
        }

        /// <inheritdoc />
        public bool CanHandle(int operationId) => operationId == PrepareStateOperationId;

        /// <inheritdoc />
        public Task<string?> PrepareAsync(OrchestrationCommand cmd, CancellationToken ct)
            => Task.FromResult<string?>(null);

        /// <inheritdoc />
        /// <remarks>
        /// Acts on <c>LoadingDryRun</c> and <c>UnloadingDryRun</c>; all other
        /// targets are no-ops.
        /// </remarks>
        public void Commit(OrchestrationCommand cmd, EntityRepository? repo)
        {
            var target = ParseTargetState(cmd.PayloadJson);

            switch (target)
            {
                case LoadingDryRunState:
                    LoadingDryRunCommit();
                    break;

                case UnloadingDryRunState:
                    UnloadingDryRunCommit();
                    break;
            }
        }

        /// <inheritdoc />
        public void Abort(OrchestrationCommand cmd, EntityRepository? repo)
        {
            _snap?.Dispose();
            _snap = null;
        }

        // ── Internal test accessor ─────────────────────────────────────────────

        /// <summary>
        /// Returns the current in-memory snapshot repository, or <see langword="null"/>
        /// when no dry-run is active.  Exposed for unit tests only.
        /// </summary>
        internal EntityRepository? TestHook_Snap => _snap;

        // ── Private helpers ────────────────────────────────────────────────────

        private void LoadingDryRunCommit()
        {
            if (_liveRepo == null)
            {
                FdpLog<ReferenceDryRunHandler>.Warn(
                    "[ReferenceDryRunHandler] LoadingDryRun: liveRepo is null — snapshot skipped. " +
                    "This subsystem carries no ECS state.");
                _snap = null;
                return;
            }

            var snap = new EntityRepository();
            snap.SyncFrom(_liveRepo);
            _snap = snap;

            FdpLog<ReferenceDryRunHandler>.Info(
                "[ReferenceDryRunHandler] LoadingDryRun: snapshot captured.");
        }

        private void UnloadingDryRunCommit()
        {
            if (_snap == null)
            {
                FdpLog<ReferenceDryRunHandler>.Warn(
                    "[ReferenceDryRunHandler] UnloadingDryRun: no snapshot present — rewind skipped. " +
                    "Was LoadingDryRun committed successfully?");
                return;
            }

            if (_liveRepo == null)
            {
                FdpLog<ReferenceDryRunHandler>.Warn(
                    "[ReferenceDryRunHandler] UnloadingDryRun: liveRepo is null — snapshot discarded without rewind.");
                _snap.Dispose();
                _snap = null;
                return;
            }

            _liveRepo.SyncFrom(_snap);
            _snap.Dispose();
            _snap = null;

            FdpLog<ReferenceDryRunHandler>.Info(
                "[ReferenceDryRunHandler] UnloadingDryRun: live repo rewound to snapshot.");
        }

        private static int ParseTargetState(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return 0;
            if (int.TryParse(payloadJson, out var n)) return n;
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("TargetState", out var prop))
                    return prop.GetInt32();
            }
            catch { /* malformed payload — treat as Standby */ }
            return 0;
        }
    }
}
