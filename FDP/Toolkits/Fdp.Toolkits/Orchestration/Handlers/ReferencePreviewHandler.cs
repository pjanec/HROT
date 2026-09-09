using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration.Preview;

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

        // ⭐⭐⭐ HN-017 — the SAME bracket the editor's handler uses. 📄 DESIGN_Deterministic_Network_Ids.md §4c.
        // ⭐⭐ This is the handler on the 2PC path — registered on FIVE production ClusterSlaves (IG, CGF ×2,
        //    SimHost, ExCon) — so the master's PrepareState broadcast makes the restore CLUSTER-WIDE with no
        //    new protocol: every node puts back its OWN reservation. 🔒 The user's requirement, `2026-08-23`.
        // ⛔ It shares PreviewStateBracket rather than reimplementing it: HN-016 records that two preview
        //    handlers exist and have already diverged once (includeTransient) — one more divergence is one
        //    too many.
        private readonly PreviewStateBracket? _bracket;

        /// <param name="liveRepo">
        /// The subsystem's authoritative entity repository.
        /// Pass <see langword="null"/> for subsystems that carry no ECS state
        /// (ExCon, IG, CGF skeleton) — the handler then behaves as a no-op for
        /// both LoadingPreview and UnloadingPreview while remaining registered
        /// so the ClusterSlave can ACK the two-phase commit correctly.
        /// </param>
        /// <param name="rewindables">
        /// ⭐⭐ The non-ECS state this NODE must also put back — its id allocator and entity map.
        /// 📄 <c>DESIGN_Deterministic_Network_Ids.md</c> §2b/§4c.
        /// <para>⛔⛔ <b>Pass BOTH or neither</b> — restoring the allocator alone guarantees a duplicate-id
        /// throw from <c>NetworkEntityMap.Register</c> on the second preview.</para>
        /// <para>⚠ Null/empty for the nodes that pass <c>liveRepo: null</c> — they carry no ECS state and no
        /// allocator to rewind.</para>
        /// </param>
        public ReferencePreviewHandler(
            EntityRepository? liveRepo,
            System.Collections.Generic.IEnumerable<IPreviewRewindable>? rewindables = null)
        {
            _liveRepo = liveRepo;
            _bracket  = rewindables is null ? null : new PreviewStateBracket(rewindables);
        }

        /// <summary>⭐ Exposed for rails — a rail must reach the CONSTRUCTED object.</summary>
        public PreviewStateBracket? TestHook_Bracket => _bracket;

        /// <inheritdoc />
        public bool CanHandle(NodeOpType operation) => operation == NodeOpType.PrepareState;

        /// <inheritdoc />
        public bool CanHandle(ExecuteNodeOpIntent intent)
        {
            if (intent.Operation != NodeOpType.PrepareState) return false;
            return intent.DomainPayload is EditLoadHandlerPayload p &&
                   (p.TargetState == ClusterState.LoadingPreview || p.TargetState == ClusterState.UnloadingPreview);
        }

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
            // ⭐ Nothing was rewound ⇒ nothing is restored. Drop the capture rather than applying it.
            _bracket?.Discard();
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

            _bracket?.Capture();

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

            // ⭐ AFTER the repo rewind — the map describes entities the rewind has just restored.
            _bracket?.Restore();

            FdpLog<ReferencePreviewHandler>.Info(
                "[ReferencePreviewHandler] UnloadingPreview: live repo rewound to snapshot.");
        }
    }
}
