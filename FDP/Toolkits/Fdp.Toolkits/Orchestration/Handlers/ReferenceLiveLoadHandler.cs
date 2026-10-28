using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Orchestration;
using Fdp.Core.Logging;

namespace Fdp.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// Reference implementation of the live-load Cluster handler (CGF1-G0405).
    ///
    /// <para>Handles <c>PrepareLive (operationId=9)</c> and
    /// <c>FinalizeLive (operationId=10)</c> commands.</para>
    ///
    /// <para>
    /// <b>PrepareLive flow:</b> Calls
    /// <see cref="IRecordReplayController.PrepareRecordingAsync"/> so a new recording
    /// session starts on the next kernel frame.  When no controller is provided the
    /// call is a no-op.
    /// </para>
    ///
    /// <para>
    /// <b>FinalizeLive flow:</b> calls
    /// <see cref="IRecordReplayController.FinalizeRecordingAsync"/> to flush the LZ4
    /// buffer and write the <c>.meta.json</c> manifest (CGF1-S0303 + CGF1-S0304).
    /// </para>
    ///
    /// <para>
    /// <b>Commit path:</b> Status is now published by <c>ClusterSlave.DispatchIntent</c> via the
    /// event bus so that <c>ClusterMaster.ConsumeNodeOpStatuses</c> can populate
    /// <c>DistributedTransaction.NodeResponses</c> for the 2PC History UI (CGF1-S0501).
    /// </para>
    /// </summary>
    public sealed class ReferenceLiveLoadHandler : IClusterStateHandler
    {
        private readonly IRecordReplayController? _controller;
        private readonly string                   _storageDirectory;
        private Guid _pendingExerciseId;

        /// <param name="controller">
        /// Optional record/replay controller; when provided, <see cref="PrepareAsync"/>
        /// calls <see cref="IRecordReplayController.PrepareRecordingAsync"/> for
        /// <c>PrepareLive</c> and <see cref="IRecordReplayController.FinalizeRecordingAsync"/>
        /// for <c>FinalizeLive</c> (CGF1-S0304).
        /// </param>
        /// <param name="storageDirectory">
        /// Root directory where exercise recording files are staged; forwarded to
        /// <see cref="IRecordReplayController.PrepareRecordingAsync"/>.
        /// Defaults to <see cref="OrchestrationConstants.ResolveStagingRoot"/>.
        /// </param>
        /// <remarks>⭐ CE-3299 B3 — the <c>CheckpointIOWorker</c> it drained before <c>FinalizeLive</c> is retired: a checkpoint is now
        /// a keyframe of the recording this handler finalizes, so the recorder's own flush covers it.</remarks>
        public ReferenceLiveLoadHandler(
            IRecordReplayController?  controller       = null,
            string?                   storageDirectory = null)
        {
            _controller       = controller;
            _storageDirectory = storageDirectory ?? OrchestrationConstants.ResolveStagingRoot();
        }

        /// <inheritdoc />
        public bool CanHandle(NodeOpType operation) =>
            operation == NodeOpType.PrepareLive ||
            operation == NodeOpType.PrepareState ||
            operation == NodeOpType.FinalizeLive;

        /// <inheritdoc />
        public bool CanHandle(ExecuteNodeOpIntent intent)
        {
            if (intent.Operation == NodeOpType.PrepareLive || intent.Operation == NodeOpType.FinalizeLive) return true;
            return intent.Operation == NodeOpType.PrepareState &&
                   intent.DomainPayload is EditLoadHandlerPayload p &&
                   p.TargetState == ClusterState.OperatingLive;
        }

        /// <inheritdoc />
        public async Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            if (intent.Operation == NodeOpType.PrepareLive)
            {
                _pendingExerciseId = ResolveExerciseId(intent.DomainPayload);
            }
            else if (intent.Operation == NodeOpType.PrepareState)
            {
                if (intent.DomainPayload is EditLoadHandlerPayload payload && payload.TargetState == ClusterState.OperatingLive)
                {
                    // ⭐ CE-4090 (AQ108 D14) — "Record live runs" off: the same gate LoadPhaseChain applies on the hosts that have one
                    if (_controller != null && _pendingExerciseId != Guid.Empty && !payload.RecordingOff)
                        await _controller.PrepareRecordingAsync(_pendingExerciseId, _storageDirectory)
                            .ConfigureAwait(false);
                }
            }
            else if (intent.Operation == NodeOpType.FinalizeLive)
            {
                if (_controller != null)
                    await _controller.FinalizeRecordingAsync().ConfigureAwait(false);

                _pendingExerciseId = Guid.Empty;
            }

            return null;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Status is now published by <c>ClusterSlave.DispatchIntent</c> via the event bus.
        /// </remarks>
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
        }

        /// <inheritdoc />
        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) { }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves an exercise <see cref="Guid"/> from the intent's domain payload.
        /// Accepts either a boxed <see cref="Guid"/> (in-process / AllInOne path) or an
        /// <see cref="EditLoadHandlerPayload"/> (DDS / bus path).
        /// </summary>
        private static Guid ResolveExerciseId(object? domainPayload) =>
            domainPayload switch
            {
                Guid g => g,
                EditLoadHandlerPayload p => p.ExerciseId,
                _ => Guid.Empty,
            };
    }
}
