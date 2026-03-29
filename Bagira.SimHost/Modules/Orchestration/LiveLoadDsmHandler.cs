using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration;
using Fdp.Kernel;
using Fdp.Kernel.Orchestration;

namespace Bagira.SimHost.Modules.Orchestration
{
    /// <summary>
    /// DSM handler for live-session load and finalize operations (CGF1-S0304).
    ///
    /// <para>Handles <see cref="NodeOpType.PrepareLive"/> and
    /// <see cref="NodeOpType.FinalizeLive"/> commands.</para>
    ///
    /// <para>
    /// <b>PrepareLive flow:</b>
    /// Calls <see cref="EcsRecordReplayController.PrepareRecordingAsync"/> so a new
    /// recording session starts on the next kernel frame.  When no controller is
    /// provided (test / legacy path) the call is a no-op.
    /// </para>
    ///
    /// <para>
    /// <b>FinalizeLive flow:</b>
    /// Awaits any pending checkpoint drain
    /// (<see cref="CheckpointIOWorker.DrainAsync"/>), then calls
    /// <see cref="EcsRecordReplayController.FinalizeRecordingAsync"/> to flush the
    /// LZ4 buffer and write the <c>.meta.json</c> manifest (CGF1-S0303 + CGF1-S0304).
    /// </para>
    ///
    /// <para>
    /// The commit step publishes <see cref="DsmStateChangedEvent"/> as a safeguard
    /// in case the slave-level <c>CommitState</c> event was not already raised for
    /// this transaction.
    /// </para>
    /// </summary>
    public sealed class LiveLoadDsmHandler : IDsmHandler
    {
        private readonly DrillSlave                  _slave;
        private readonly FdpEventBus                 _eventBus;
        private readonly CheckpointIOWorker?         _checkpointWorker;
        private readonly EcsRecordReplayController?  _controller;
        private readonly string                      _storageDirectory;

        /// <param name="slave">
        /// Owning slave; used to call
        /// <see cref="DrillSlave.PublishDsmStateChanged"/> as a guard.
        /// </param>
        /// <param name="eventBus">Event bus for <see cref="DsmStateChangedEvent"/> publication.</param>
        /// <param name="checkpointWorker">
        /// Optional <see cref="CheckpointIOWorker"/>; when provided, <see cref="PrepareAsync"/>
        /// calls <see cref="CheckpointIOWorker.DrainAsync"/> before returning for
        /// <see cref="NodeOpType.FinalizeLive"/> to ensure all in-flight checkpoint writes
        /// complete before the live session is torn down (CGF1-S0303).
        /// </param>
        /// <param name="controller">
        /// Optional <see cref="EcsRecordReplayController"/>; when provided,
        /// <see cref="PrepareAsync"/> calls <see cref="EcsRecordReplayController.PrepareRecordingAsync"/>
        /// for <see cref="NodeOpType.PrepareLive"/> and
        /// <see cref="EcsRecordReplayController.FinalizeRecordingAsync"/> for
        /// <see cref="NodeOpType.FinalizeLive"/> (CGF1-S0304).
        /// </param>
        /// <param name="storageDirectory">
        /// Root directory where drill recording files are staged; forwarded to
        /// <see cref="EcsRecordReplayController.PrepareRecordingAsync"/>.
        /// Defaults to <c>C:\FDP_Temp</c>.
        /// </param>
        public LiveLoadDsmHandler(
            DrillSlave                  slave,
            FdpEventBus                 eventBus,
            CheckpointIOWorker?         checkpointWorker = null,
            EcsRecordReplayController?  controller       = null,
            string                      storageDirectory = @"C:\FDP_Temp")
        {
            _slave            = slave     ?? throw new ArgumentNullException(nameof(slave));
            _eventBus         = eventBus  ?? throw new ArgumentNullException(nameof(eventBus));
            _checkpointWorker = checkpointWorker;
            _controller       = controller;
            _storageDirectory = storageDirectory;
        }

        /// <inheritdoc />
        public bool CanHandle(NodeOpType op) =>
            op == NodeOpType.PrepareLive || op == NodeOpType.FinalizeLive;

        /// <summary>
        /// For <see cref="NodeOpType.PrepareLive"/>: calls
        /// <see cref="EcsRecordReplayController.PrepareRecordingAsync"/> when a controller
        /// is injected, starting a new flight-recorder session.
        /// <para>
        /// For <see cref="NodeOpType.FinalizeLive"/>: awaits any pending checkpoint drain
        /// (<see cref="CheckpointIOWorker.DrainAsync"/>), then calls
        /// <see cref="EcsRecordReplayController.FinalizeRecordingAsync"/> to flush and
        /// write the <c>.meta.json</c> manifest (CGF1-S0303 + CGF1-S0304).
        /// </para>
        /// </summary>
        public async Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct)
        {
            if (cmd.Operation == NodeOpType.PrepareLive)
            {
                if (_controller != null)
                {
                    var drillId = ParseDrillId(cmd.PayloadJson);
                    await _controller.PrepareRecordingAsync(drillId, _storageDirectory)
                        .ConfigureAwait(false);
                }
            }
            else if (cmd.Operation == NodeOpType.FinalizeLive)
            {
                if (_checkpointWorker != null)
                    await _checkpointWorker.DrainAsync().ConfigureAwait(false);

                if (_controller != null)
                    await _controller.FinalizeRecordingAsync().ConfigureAwait(false);
            }

            return null;
        }

        /// <summary>
        /// Commits the live-load command.  Publishes <see cref="DsmStateChangedEvent"/>
        /// via the event bus as a safeguard if the slave-level <c>CommitState</c> handling
        /// has not already done so for this transaction.
        /// </summary>
        public void Commit(NodeOpCommand cmd, EntityRepository? repo)
        {
            // Guard: publish DsmStateChangedEvent if not already raised by the slave.
            _slave.PublishDsmStateChanged(DSMState.Standby, DSMState.LoadingLive);
        }

        /// <inheritdoc />
        public void Abort(NodeOpCommand cmd, EntityRepository? repo)
        {
            // No resources to release.
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static Guid ParseDrillId(string? payloadJson)
        {
            if (!string.IsNullOrWhiteSpace(payloadJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(payloadJson);
                    if (doc.RootElement.TryGetProperty("DrillId", out var prop))
                    {
                        var raw = prop.GetString();
                        if (Guid.TryParse(raw, out var g)) return g;
                    }
                }
                catch { /* malformed JSON — fall through to new Guid */ }
            }
            return Guid.NewGuid();
        }
    }
}

