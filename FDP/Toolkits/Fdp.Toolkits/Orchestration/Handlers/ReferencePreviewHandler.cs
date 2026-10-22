using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration.Preview;
using Fdp.Toolkit.Replay;

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
    /// <remarks>
    /// ⭐⭐ <c>D14</c> (<c>R-3046</c>) — <b>THE ONE dry-run handler.</b> The editor's twin
    /// (<c>PreviewClusterOpHandler</c>, driven directly by the editor's Play/Stop and registered on no slave) is gone:
    /// the editor's controller calls <see cref="TriggerLoadingPreview"/> / <see cref="TriggerUnloadingPreview"/> on the
    /// SAME instance its cluster slave holds (<c>EcsHostClusterHandlers</c>), so a dry run started from the toolbar
    /// and one started by the cluster master take one path. 📄 docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md §1b U1.
    /// </remarks>
    public sealed class ReferencePreviewHandler : IClusterStateHandler
    {
        private readonly EntityRepository? _liveRepo;

        // ⭐⭐ CE-3299 B2 — the dry run's rewind point is a RAM KEYFRAME (the flight recorder's own format, R-3040), not a SyncFrom copy.
        //   📐 Measured (restore design §1e): after CE-3305 the keyframe rewind puts back the capture point EXACTLY (0 of ≈ 9 000 values on a
        //   live uc-urban-battle), where SyncFrom left a managed component it shares BY REFERENCE mutated (AirPath).
        private WorldKeyframe? _keyframe;

        // ⚠ The one difference the two handlers had (HN-016): the editor's dry run carries Transient components too — now the keyframe's
        //   type set (CaptureOptions.EditorDryRun vs NodeDryRun; both leave the singletons alone, as SyncFrom did).
        private readonly CaptureOptions _capture;

        // ⭐ B2 / D4 — the step window restarts after the rewind (a window must not span two worlds). Null: the host has no accumulator.
        private readonly EventAccumulator? _accumulator;

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
        /// <param name="includeTransient">Carry Transient components too (the editor's dry run did, HN-016).</param>
        /// <param name="accumulator">⭐ B2 — the host's step-event accumulator, restarted after the rewind (D4).</param>
        public ReferencePreviewHandler(
            EntityRepository? liveRepo,
            System.Collections.Generic.IEnumerable<IPreviewRewindable>? rewindables = null,
            bool includeTransient = false,
            EventAccumulator? accumulator = null)
        {
            _liveRepo    = liveRepo;
            _bracket     = rewindables is null ? null : new PreviewStateBracket(rewindables);
            _capture     = includeTransient ? CaptureOptions.EditorDryRun : CaptureOptions.NodeDryRun;
            _accumulator = accumulator;
        }

        /// <summary>⭐ <c>D14</c> — the local entry point (the editor's Play): the same capture the master's
        /// <c>LoadingPreview</c> commits.</summary>
        public void TriggerLoadingPreview() => LoadingPreviewCommit();

        /// <summary>⭐ <c>D14</c> — the local entry point (the editor's Stop): the same rewind the master's
        /// <c>UnloadingPreview</c> commits.</summary>
        public void TriggerUnloadingPreview() => UnloadingPreviewCommit();

        /// <summary>True between a capture and its rewind.</summary>
        public bool HasSnapshot => _keyframe != null;

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
            _keyframe = null;
            // ⭐ Nothing was rewound ⇒ nothing is restored. Drop the capture rather than applying it.
            _bracket?.Discard();
        }

        // ── Internal test accessor ─────────────────────────────────────────────

        /// <summary>
        /// The dry run's rewind point (a RAM keyframe), or <see langword="null"/> when no dry run is active. Exposed for unit tests only.
        /// </summary>
        public WorldKeyframe? TestHook_Keyframe => _keyframe;

        // ── Private helpers ────────────────────────────────────────────────────

        private void LoadingPreviewCommit()
        {
            if (_liveRepo == null)
            {
                FdpLog<ReferencePreviewHandler>.Warn(
                    "[ReferencePreviewHandler] LoadingPreview: liveRepo is null — snapshot skipped. " +
                    "This subsystem carries no ECS state.");
                _keyframe = null;
                return;
            }

            // ⭐ D14 — two entry points reach this one handler (the master's LoadingPreview, the editor's Play). A second
            //   capture while one is held would move the rewind point forward; the first one stands.
            if (_keyframe != null)
            {
                FdpLog<ReferencePreviewHandler>.Warn(
                    "[ReferencePreviewHandler] LoadingPreview: a dry run is already captured — kept, not re-captured.");
                return;
            }

            _keyframe = WorldKeyframes.Capture(_liveRepo, _capture);

            _bracket?.Capture();

            FdpLog<ReferencePreviewHandler>.Info(
                "[ReferencePreviewHandler] LoadingPreview: snapshot captured.");
        }

        private void UnloadingPreviewCommit()
        {
            if (_keyframe == null)
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
                _keyframe = null;
                return;
            }

            // ⭐ B2 — the keyframe is applied in place (D8: versions only grow; D2: no events re-injected).
            WorldKeyframes.Apply(_liveRepo, _keyframe, RestoreOptions.DryRunExit);
            _keyframe = null;

            // ⭐ AFTER the repo rewind — the map describes entities the rewind has just restored.
            _bracket?.Restore();

            // ⭐ B2 / D4 — the one step after ANY restore: the entity map re-derived from the restored world, WorldEpoch.Restored (the
            //   per-id caches forget the dry run's entities; nothing is deleted on the wire), the step window restarted.
            AfterRestore.Run(_liveRepo, _accumulator);

            FdpLog<ReferencePreviewHandler>.Info(
                "[ReferencePreviewHandler] UnloadingPreview: live repo rewound to snapshot.");
        }
    }
}
