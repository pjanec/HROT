using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
using Fdp.Kernel.Orchestration;
using FDP.Kernel.Logging;
using ModuleHost.Core.Scheduling;

namespace FDP.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// Reference implementation of the replay-load DSM handler (CGF1-G0405).
    ///
    /// <para>
    /// Handles <c>PrepareReplay (operationId=11)</c>, <c>FinalizeReplay (operationId=12)</c>,
    /// and the Live-from-Replay <c>PrepareLive (operationId=9)</c> branch
    /// (CGF1-S0304 / CGF1-S0305).
    /// </para>
    ///
    /// <para>
    /// <b>PrepareReplay flow:</b>
    /// <list type="number">
    ///   <item>
    ///     <b>PrepareAsync:</b> Calls <see cref="IRecordReplayController.PrepareReplayAsync"/>.
    ///     Extracts <c>MaxNetworkId</c> via <see cref="IRecordReplayController.ActiveMaxNetworkId"/>
    ///     and publishes a <see cref="OrchestrationStatus"/> with
    ///     <c>ResultJson = {"MaxNetworkId": N}</c> so the orchestrator can reset the
    ///     ID allocator above the replay's ID space.
    ///   </item>
    ///   <item>
    ///     <b>Commit:</b> Disables <see cref="SimulationSystemGroup"/>,
    ///     <see cref="NetworkLifecycleSystemGroup"/>, and invokes
    ///     <c>bypassLifecycleToggle(true)</c> to prevent ghost-creation during playback.
    ///   </item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>FinalizeReplay flow:</b>
    /// <list type="number">
    ///   <item>
    ///     <b>PrepareAsync:</b> Calls <see cref="IRecordReplayController.TeardownReplayAsync"/>.
    ///   </item>
    ///   <item>
    ///     <b>Commit:</b> Re-enables both system groups and toggles bypass off.
    ///   </item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Live-from-Replay <c>PrepareLive</c> branch (CGF1-S0305):</b>
    /// Guarded by <see cref="IRecordReplayController.IsReplayActive"/> — only claims
    /// this command when a replay is active, leaving cold <c>PrepareLive</c> commands
    /// to <see cref="ReferenceLiveLoadHandler"/>.
    /// </para>
    ///
    /// <para>
    /// The <paramref name="bypassLifecycleToggle"/> delegate abstracts the
    /// <c>GhostCreationSystem.BypassLifecycle</c> property toggle so that
    /// <c>FDP.Toolkit.Orchestration</c> does not need a dependency on
    /// <c>FDP.Toolkit.Replication</c>.  Pass
    /// <c>bypass => ghostCreationSystem.BypassLifecycle = bypass</c> from the
    /// application wiring layer.
    /// </para>
    /// </summary>
    public sealed class ReferenceReplayLoadHandler : IDsmHandler
    {
        /// <summary>Integer value of <c>NodeOpType.PrepareReplay</c>.</summary>
        public const int PrepareReplayOperationId  = 11;
        /// <summary>Integer value of <c>NodeOpType.FinalizeReplay</c>.</summary>
        public const int FinalizeReplayOperationId = 12;
        /// <summary>Integer value of <c>NodeOpType.PrepareLive</c>.</summary>
        public const int PrepareLiveOperationId    = 9;

        private readonly IRecordReplayController     _controller;
        private readonly SimulationSystemGroup?      _simGroup;
        private readonly NetworkLifecycleSystemGroup? _lifecycleGroup;
        private readonly Action<bool>?               _bypassLifecycleToggle;
        private readonly IOrchestrationTransport?    _transport;
        private readonly int                         _nodeId;
        private readonly string                      _storageDirectory;

        /// <param name="controller">
        /// Record/replay lifecycle controller.
        /// </param>
        /// <param name="simGroup">
        /// <see cref="SimulationSystemGroup"/> whose <c>Enabled</c> flag is cleared during
        /// replay to prevent simulation logic from running on top of recorded data.
        /// </param>
        /// <param name="lifecycleGroup">
        /// <see cref="NetworkLifecycleSystemGroup"/> whose <c>Enabled</c> flag is cleared
        /// during replay so no lifecycle state changes or ghost promotions occur during
        /// playback.
        /// </param>
        /// <param name="bypassLifecycleToggle">
        /// Optional delegate invoked with <c>true</c> when replay begins and <c>false</c>
        /// when it ends; maps to <c>GhostCreationSystem.BypassLifecycle</c>.
        /// Pass <c>bypass =&gt; ghostSystem.BypassLifecycle = bypass</c> from app code.
        /// Pass <c>null</c> when the subsystem has no ghost-creation system (e.g. tests).
        /// </param>
        /// <param name="transport">
        /// Optional transport for publishing <c>NodeOpStatus</c> ACKs.
        /// Pass <c>null</c> in unit tests that do not require DDS.
        /// </param>
        /// <param name="nodeId">Local node identifier included in ACK messages.</param>
        /// <param name="storageDirectory">
        /// Root directory where drill recording files are staged; forwarded to
        /// <see cref="IRecordReplayController.PrepareReplayAsync"/>.
        /// </param>
        public ReferenceReplayLoadHandler(
            IRecordReplayController      controller,
            SimulationSystemGroup?       simGroup,
            NetworkLifecycleSystemGroup? lifecycleGroup,
            Action<bool>?                bypassLifecycleToggle,
            IOrchestrationTransport?     transport,
            int                          nodeId,
            string                       storageDirectory)
        {
            _controller            = controller       ?? throw new ArgumentNullException(nameof(controller));
            _simGroup              = simGroup;
            _lifecycleGroup        = lifecycleGroup;
            _bypassLifecycleToggle = bypassLifecycleToggle;
            _transport             = transport;
            _nodeId                = nodeId;
            _storageDirectory      = storageDirectory ?? throw new ArgumentNullException(nameof(storageDirectory));
        }

        /// <inheritdoc />
        /// <remarks>
        /// Returns <c>true</c> for <c>PrepareReplay</c> and <c>FinalizeReplay</c>
        /// unconditionally.
        /// <para>
        /// Returns <c>true</c> for <c>PrepareLive</c> <b>only</b> when a replay session
        /// is currently active (<see cref="IRecordReplayController.IsReplayActive"/>).
        /// This conditional prevents the Live-from-Replay branch from stealing cold
        /// <c>PrepareLive</c> commands that belong to <see cref="ReferenceLiveLoadHandler"/>
        /// (CGF1-S0305 / BATCH-18 A.1).
        /// </para>
        /// </remarks>
        public bool CanHandle(int operationId) =>
            operationId == PrepareReplayOperationId ||
            operationId == FinalizeReplayOperationId ||
            (operationId == PrepareLiveOperationId && _controller.IsReplayActive);

        /// <inheritdoc />
        public async Task<string?> PrepareAsync(OrchestrationCommand cmd, CancellationToken ct)
        {
            if (cmd.OperationId == PrepareReplayOperationId)
            {
                var drillId = ParseDrillId(cmd.PayloadJson);
                await _controller.PrepareReplayAsync(drillId, _storageDirectory)
                    .ConfigureAwait(false);

                var maxNetworkId = _controller.ActiveMaxNetworkId;
                _transport?.PublishStatus(new OrchestrationStatus(
                    TransactionId:   cmd.TransactionId,
                    NodeId:          _nodeId,
                    StatusCode:      OrchestrationStatusCode.Success,
                    IsParticipating: true,
                    ResultJson:      $"{{\"MaxNetworkId\":{maxNetworkId}}}"));

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] PrepareReplay complete (drillId={0}, MaxNetworkId={1}).",
                    drillId, maxNetworkId);
            }
            else if (cmd.OperationId == FinalizeReplayOperationId)
            {
                await _controller.TeardownReplayAsync().ConfigureAwait(false);

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] TeardownReplay complete.");
            }
            else if (cmd.OperationId == PrepareLiveOperationId)
            {
                // CGF1-S0305: Live-from-Replay branch.
                // 1. Tear down replay — EntityRepository is left at historical state.
                // 2. Start recording under the branched DrillId.
                var branchedDrillId = ParseDrillId(cmd.PayloadJson);
                await _controller.TeardownReplayAsync().ConfigureAwait(false);
                await _controller.PrepareRecordingAsync(branchedDrillId, _storageDirectory)
                    .ConfigureAwait(false);

                _transport?.PublishStatus(new OrchestrationStatus(
                    TransactionId:   cmd.TransactionId,
                    NodeId:          _nodeId,
                    StatusCode:      OrchestrationStatusCode.Success,
                    IsParticipating: true,
                    ResultJson:      string.Empty));

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] Live-from-Replay branch complete (branchedDrillId={0}).",
                    branchedDrillId);
            }

            return null;
        }

        /// <inheritdoc />
        public void Commit(OrchestrationCommand cmd, EntityRepository? repo)
        {
            if (cmd.OperationId == PrepareReplayOperationId)
            {
                SetSystemsEnabled(false);
                _bypassLifecycleToggle?.Invoke(true);

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] Commit(PrepareReplay) — sim+lifecycle disabled, BypassLifecycle=true.");
            }
            else if (cmd.OperationId == FinalizeReplayOperationId)
            {
                SetSystemsEnabled(true);
                _bypassLifecycleToggle?.Invoke(false);

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] Commit(FinalizeReplay) — sim+lifecycle re-enabled, BypassLifecycle=false.");
            }
            else if (cmd.OperationId == PrepareLiveOperationId)
            {
                // CGF1-S0305: Live-from-Replay branch — re-enable simulation so live
                // ticks resume from the historical snapshot state.
                SetSystemsEnabled(true);
                _bypassLifecycleToggle?.Invoke(false);

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] Commit(PrepareLive/branch) — sim+lifecycle re-enabled, BypassLifecycle=false.");
            }
        }

        /// <inheritdoc />
        public void Abort(OrchestrationCommand cmd, EntityRepository? repo) { }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private void SetSystemsEnabled(bool enabled)
        {
            if (_simGroup      != null) _simGroup.Enabled      = enabled;
            if (_lifecycleGroup != null) _lifecycleGroup.Enabled = enabled;
        }

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
                        $"[ReferenceReplayLoadHandler] 'DrillId' value '{raw}' is not a valid GUID. " +
                        "Refusing to open a recording under an unintended drill id.");
                }
            }
            throw new InvalidOperationException(
                "[ReferenceReplayLoadHandler] PayloadJson is missing or does not contain a 'DrillId' " +
                $"property. Payload: '{payloadJson}'. Refusing to open replay under an unknown drill id.");
        }
    }
}
