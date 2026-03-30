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
    /// Reference implementation of the live-load DSM handler (CGF1-G0405).
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
    /// <b>Commit path:</b> No-op.  The toolkit <c>DrillSlave</c> publishes
    /// <c>TkDsmStateChangedEvent</c> automatically on <c>CommitState</c>, so the
    /// guard call previously present in the Bagira <c>LiveLoadDsmHandler</c> is
    /// no longer required.
    /// </para>
    /// </summary>
    public sealed class ReferenceLiveLoadHandler : IDsmHandler
    {
        /// <summary>Integer value of <c>NodeOpType.PrepareLive</c>.</summary>
        public const int PrepareLiveOperationId  = 9;
        /// <summary>Integer value of <c>NodeOpType.FinalizeLive</c>.</summary>
        public const int FinalizeLiveOperationId = 10;

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
        /// Root directory where drill recording files are staged; forwarded to
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
                    var drillId = ParseDrillId(cmd.PayloadJson);
                    await _controller.PrepareRecordingAsync(drillId, _storageDirectory)
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
        /// No-op.  The toolkit <c>DrillSlave</c> automatically publishes
        /// <c>TkDsmStateChangedEvent</c> on <c>CommitState</c>, so no guard is
        /// needed here.
        /// </remarks>
        public void Commit(OrchestrationCommand cmd, EntityRepository? repo) { }

        /// <inheritdoc />
        public void Abort(OrchestrationCommand cmd, EntityRepository? repo) { }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static Guid ParseDrillId(string? payloadJson)
        {
            if (!string.IsNullOrWhiteSpace(payloadJson))
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("DrillId", out var prop))
                {
                    var raw = prop.GetString();
                    if (Guid.TryParse(raw, out var g)) return g;
                    throw new InvalidOperationException(
                        $"[ReferenceLiveLoadHandler] 'DrillId' value '{raw}' is not a valid GUID. " +
                        "Refusing to start recording under an unintended drill id.");
                }
            }
            throw new InvalidOperationException(
                "[ReferenceLiveLoadHandler] PayloadJson is missing or does not contain a 'DrillId' " +
                $"property. Payload: '{payloadJson}'. Refusing to start recording under an unknown drill id.");
        }
    }
}
