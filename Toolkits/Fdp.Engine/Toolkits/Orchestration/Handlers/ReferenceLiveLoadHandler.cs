using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
using Fdp.Kernel.Orchestration;
using FDP.Kernel.Logging;

namespace FDP.Toolkit.Orchestration.Handlers
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
    /// <b>FinalizeLive flow:</b> Awaits any pending checkpoint drain
    /// (<see cref="CheckpointIOWorker.DrainAsync"/>), then calls
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
        private readonly CheckpointIOWorker?      _checkpointWorker;
        private readonly IRecordReplayController? _controller;
        private readonly string                   _storageDirectory;

        /// <param name="checkpointWorker">
        /// Optional <see cref="CheckpointIOWorker"/>; when provided,
        /// <see cref="PrepareAsync"/> calls <see cref="CheckpointIOWorker.DrainAsync"/>
        /// before returning for <c>FinalizeLive</c> to ensure all in-flight checkpoint
        /// writes complete before the live session is torn down (CGF1-S0303).
        /// </param>
        /// <param name="controller">
        /// Optional record/replay controller; when provided, <see cref="PrepareAsync"/>
        /// calls <see cref="IRecordReplayController.PrepareRecordingAsync"/> for
        /// <c>PrepareLive</c> and <see cref="IRecordReplayController.FinalizeRecordingAsync"/>
        /// for <c>FinalizeLive</c> (CGF1-S0304).
        /// </param>
        /// <param name="storageDirectory">
        /// Root directory where exercise recording files are staged; forwarded to
        /// <see cref="IRecordReplayController.PrepareRecordingAsync"/>.
        /// Defaults to <c>C:\FDP_Temp</c>.
        /// </param>
        public ReferenceLiveLoadHandler(
            CheckpointIOWorker?       checkpointWorker = null,
            IRecordReplayController?  controller       = null,
            string                    storageDirectory = @"C:\FDP_Temp")
        {
            _checkpointWorker = checkpointWorker;
            _controller       = controller;
            _storageDirectory = storageDirectory ?? @"C:\FDP_Temp";
        }

        /// <inheritdoc />
        public bool CanHandle(NodeOpType operation) =>
            operation == NodeOpType.PrepareLive ||
            operation == NodeOpType.FinalizeLive;

        /// <inheritdoc />
        public async Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            if (intent.Operation == NodeOpType.PrepareLive)
            {
                if (_controller != null)
                {
                    var exerciseId = ResolveExerciseId(intent.DomainPayload);
                    await _controller.PrepareRecordingAsync(exerciseId, _storageDirectory)
                        .ConfigureAwait(false);
                }
            }
            else if (intent.Operation == NodeOpType.FinalizeLive)
            {
                if (_checkpointWorker != null)
                    await _checkpointWorker.DrainAsync().ConfigureAwait(false);

                if (_controller != null)
                    await _controller.FinalizeRecordingAsync().ConfigureAwait(false);
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
        /// <see cref="EditLoadHandlerPayload"/> whose <c>ExerciseId</c> string may be a
        /// parseable GUID or a human-readable key (DDS / bus path).  Human-readable keys
        /// are converted to a deterministic v3-style GUID via MD5 so that the same key
        /// always maps to the same recording file regardless of the delivery path.
        /// </summary>
        private static Guid ResolveExerciseId(object? domainPayload) =>
            domainPayload switch
            {
                Guid g => g,
                EditLoadHandlerPayload p when p.ExerciseId != null =>
                    Guid.TryParse(p.ExerciseId, out var parsed)
                        ? parsed
                        : GuidFromString(p.ExerciseId),
                _ => Guid.Empty,
            };

        private static Guid GuidFromString(string s)
        {
            var hashBytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(s));
            return new Guid(hashBytes);
        }
    }
}
