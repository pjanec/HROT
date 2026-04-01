using System;
using System.Text.Json;
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
    /// <b>Commit path:</b> Publishes a <c>NodeOpStatus(Success)</c> ACK via the optional
    /// <see cref="IOrchestrationTransport"/> so that
    /// <c>ClusterMaster.ConsumeNodeOpStatuses</c> can populate
    /// <c>DistributedTransaction.NodeResponses</c> for the 2PC History UI (CGF1-S0501).
    /// When no transport is provided (e.g. local-only nodes) the commit is a no-op.
    /// </para>
    /// </summary>
    public sealed class ReferenceLiveLoadHandler : IClusterStateHandler
    {
        /// <summary>Integer value of <c>NodeOpType.PrepareLive</c>.</summary>
        public const int PrepareLiveOperationId  = 9;
        /// <summary>Integer value of <c>NodeOpType.FinalizeLive</c>.</summary>
        public const int FinalizeLiveOperationId = 10;

        private readonly CheckpointIOWorker?      _checkpointWorker;
        private readonly IRecordReplayController? _controller;
        private readonly string                   _storageDirectory;
        private readonly IOrchestrationTransport? _transport;
        private readonly int                      _nodeId;

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
        /// <param name="transport">
        /// Optional transport for publishing <c>NodeOpStatus</c> ACKs back to the
        /// orchestrator on <see cref="Commit"/>.  Pass <see langword="null"/> for nodes
        /// that do not need to ACK (no-op).
        /// </param>
        /// <param name="nodeId">
        /// Node ID stamped into the <c>NodeOpStatus</c> ACK.  Ignored when
        /// <paramref name="transport"/> is <see langword="null"/>.
        /// </param>
        public ReferenceLiveLoadHandler(
            CheckpointIOWorker?       checkpointWorker = null,
            IRecordReplayController?  controller       = null,
            string                    storageDirectory = @"C:\FDP_Temp",
            IOrchestrationTransport?  transport        = null,
            int                       nodeId           = 0)
        {
            _checkpointWorker = checkpointWorker;
            _controller       = controller;
            _storageDirectory = storageDirectory ?? @"C:\FDP_Temp";
            _transport        = transport;
            _nodeId           = nodeId;
        }

        /// <inheritdoc />
        public bool CanHandle(int operationId) =>
            operationId == PrepareLiveOperationId ||
            operationId == FinalizeLiveOperationId;

        /// <inheritdoc />
        public async Task<string?> PrepareAsync(OrchestrationCommand cmd, CancellationToken ct)
        {
            if (cmd.OperationId == PrepareLiveOperationId)
            {
                if (_controller != null)
                {
                    var exerciseId = ParseExerciseId(cmd.PayloadJson);
                    await _controller.PrepareRecordingAsync(exerciseId, _storageDirectory)
                        .ConfigureAwait(false);
                }
            }
            else if (cmd.OperationId == FinalizeLiveOperationId)
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
        /// Publishes a <c>NodeOpStatus(Success)</c> ACK so that
        /// <c>ClusterMaster.ConsumeNodeOpStatuses</c> populates
        /// <c>DistributedTransaction.NodeResponses</c> for the 2PC History UI.
        /// </remarks>
        public void Commit(OrchestrationCommand cmd, EntityRepository? repo)
        {
            _transport?.PublishStatus(new OrchestrationStatus(
                TransactionId:   cmd.TransactionId,
                NodeId:          _nodeId,
                StatusCode:      OrchestrationStatusCode.Success,
                IsParticipating: true,
                ResultJson:      string.Empty));
        }

        /// <inheritdoc />
        public void Abort(OrchestrationCommand cmd, EntityRepository? repo) { }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static Guid ParseExerciseId(string? payloadJson)
        {
            if (!string.IsNullOrWhiteSpace(payloadJson))
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("ExerciseId", out var prop))
                {
                    var raw = prop.GetString();
                    if (Guid.TryParse(raw, out var g)) return g;
                    throw new InvalidOperationException(
                        $"[ReferenceLiveLoadHandler] 'ExerciseId' value '{raw}' is not a valid GUID. " +
                        "Refusing to start recording under an unintended exercise id.");
                }
            }
            throw new InvalidOperationException(
                "[ReferenceLiveLoadHandler] PayloadJson is missing or does not contain a 'ExerciseId' " +
                $"property. Payload: '{payloadJson}'. Refusing to start recording under an unknown exercise id.");
        }
    }
}
