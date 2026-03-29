using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Systems;
using ModuleHost.Core.Scheduling;

namespace Bagira.SimHost.Modules.Orchestration.Handlers
{
    /// <summary>
    /// DSM handler for <see cref="NodeOpType.PrepareReplay"/>,
    /// <see cref="NodeOpType.FinalizeReplay"/>, and the Live-from-Replay
    /// <see cref="NodeOpType.PrepareLive"/> branch (CGF1-S0304 / CGF1-S0305).
    ///
    /// <para>
    /// <b>PrepareReplay flow:</b>
    /// <list type="number">
    ///   <item>
    ///     <b>PrepareAsync:</b> Calls
    ///     <see cref="EcsRecordReplayController.PrepareReplayAsync"/> which
    ///     installs a <see cref="FDP.Toolkit.Replay.ReplayModule"/> via the kernel.
    ///     Extracts <c>MaxNetworkId</c> from the opened recording and publishes a
    ///     <see cref="NodeOpStatus"/> with <c>ResultJson = {"MaxNetworkId": N}</c>
    ///     directly so the orchestrator can reset the ID allocator above the replay's
    ///     ID space.
    ///   </item>
    ///   <item>
    ///     <b>Commit:</b> Disables <see cref="SimulationSystemGroup"/>,
    ///     <see cref="NetworkLifecycleSystemGroup"/>, and sets
    ///     <see cref="GhostCreationSystem.BypassLifecycle"/> to <c>true</c>.
    ///   </item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>FinalizeReplay flow:</b>
    /// <list type="number">
    ///   <item>
    ///     <b>PrepareAsync:</b> Calls
    ///     <see cref="EcsRecordReplayController.TeardownReplayAsync"/> which
    ///     uninstalls the <see cref="FDP.Toolkit.Replay.ReplayModule"/> and closes
    ///     file handles.  <see cref="EntityRepository"/> is left intact at the
    ///     historical state (ready for Live-from-Replay — CGF1-S0305).
    ///   </item>
    ///   <item>
    ///     <b>Commit:</b> Re-enables <see cref="SimulationSystemGroup"/>,
    ///     <see cref="NetworkLifecycleSystemGroup"/>; resets
    ///     <see cref="GhostCreationSystem.BypassLifecycle"/> to <c>false</c>.
    ///   </item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class ReplayLoadDsmHandler : IDsmHandler
    {
        private readonly EcsRecordReplayController   _controller;
        private readonly SimulationSystemGroup       _simGroup;
        private readonly NetworkLifecycleSystemGroup _lifecycleGroup;
        private readonly GhostCreationSystem         _ghostCreationSystem;
        private readonly DdsWriter<NodeOpStatus>?    _statusWriter;
        private readonly int                         _nodeId;
        private readonly string                      _storageDirectory;

        /// <param name="controller">
        /// Control-plane factory that owns the active <see cref="FDP.Toolkit.Replay.ReplayModule"/>.
        /// </param>
        /// <param name="simGroup">
        /// <see cref="SimulationSystemGroup"/> whose <c>Enabled</c> flag is cleared during replay
        /// to prevent simulation logic from running on top of recorded data.
        /// </param>
        /// <param name="lifecycleGroup">
        /// <see cref="NetworkLifecycleSystemGroup"/> whose <c>Enabled</c> flag is cleared during
        /// replay so no lifecycle state changes or ghost promotions occur during playback.
        /// </param>
        /// <param name="ghostCreationSystem">
        /// Ghost-creation system; <see cref="GhostCreationSystem.BypassLifecycle"/> is set to
        /// <c>true</c> during replay so incoming DDS lifecycle events do not spawn new ghosts.
        /// </param>
        /// <param name="statusWriter">
        /// Optional DDS writer for publishing <see cref="NodeOpStatus"/> ACKs with
        /// <c>MaxNetworkId</c> in <c>ResultJson</c>.
        /// </param>
        /// <param name="nodeId">Local node identifier included in <c>NodeOpStatus.NodeId</c>.</param>
        /// <param name="storageDirectory">
        /// Root directory where drill recording files are staged.  Passed through to
        /// <see cref="EcsRecordReplayController.PrepareReplayAsync"/> as the storage root.
        /// </param>
        public ReplayLoadDsmHandler(
            EcsRecordReplayController   controller,
            SimulationSystemGroup       simGroup,
            NetworkLifecycleSystemGroup lifecycleGroup,
            GhostCreationSystem         ghostCreationSystem,
            DdsWriter<NodeOpStatus>?    statusWriter,
            int                         nodeId,
            string                      storageDirectory)
        {
            _controller          = controller          ?? throw new ArgumentNullException(nameof(controller));
            _simGroup            = simGroup            ?? throw new ArgumentNullException(nameof(simGroup));
            _lifecycleGroup      = lifecycleGroup      ?? throw new ArgumentNullException(nameof(lifecycleGroup));
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
            _statusWriter        = statusWriter;
            _nodeId              = nodeId;
            _storageDirectory    = storageDirectory    ?? throw new ArgumentNullException(nameof(storageDirectory));
        }

        /// <inheritdoc />
        /// <remarks>
        /// Returns <c>true</c> for <see cref="NodeOpType.PrepareReplay"/> and
        /// <see cref="NodeOpType.FinalizeReplay"/> unconditionally.
        /// <para>
        /// Returns <c>true</c> for <see cref="NodeOpType.PrepareLive"/> <b>only</b> when
        /// a replay session is currently active
        /// (<see cref="EcsRecordReplayController.ActiveReplayModule"/> is not <c>null</c>).
        /// This conditional prevents the Live-from-Replay branch from stealing cold
        /// <see cref="NodeOpType.PrepareLive"/> commands that belong to
        /// <see cref="LiveLoadDsmHandler"/> (CGF1-S0305 / BATCH-18 A.1).
        /// </para>
        /// </remarks>
        public bool CanHandle(NodeOpType op) =>
            op == NodeOpType.PrepareReplay || op == NodeOpType.FinalizeReplay
                || (op == NodeOpType.PrepareLive && _controller.ActiveReplayModule != null);

        /// <summary>
        /// For <see cref="NodeOpType.PrepareReplay"/>: opens the recording file via
        /// <see cref="EcsRecordReplayController.PrepareReplayAsync"/>, then publishes
        /// <see cref="NodeOpStatus"/> with <c>ResultJson = {"MaxNetworkId": N}</c> directly
        /// (fire-and-forget ACK pattern — <c>DrillSlave</c> discards the return value).
        /// <para>
        /// For <see cref="NodeOpType.FinalizeReplay"/>: calls
        /// <see cref="EcsRecordReplayController.TeardownReplayAsync"/> and returns.
        /// </para>
        /// <para>
        /// For the Live-from-Replay <see cref="NodeOpType.PrepareLive"/> branch
        /// (CGF1-S0305): tears down the active replay without mutating the
        /// <see cref="EntityRepository"/> (historical state preserved in-place),
        /// then installs a new <see cref="FDP.Toolkit.Replay.RecordingModule"/> for
        /// the branched drill.  Publishes a <see cref="NodeOpStatus"/>(<c>Success</c>)
        /// ACK so the orchestrator can restore time scale.
        /// </para>
        /// </summary>
        public async Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct)
        {
            if (cmd.Operation == NodeOpType.PrepareReplay)
            {
                var drillId = ParseDrillId(cmd.PayloadJson);
                await _controller.PrepareReplayAsync(drillId, _storageDirectory)
                    .ConfigureAwait(false);

                var maxNetworkId = _controller.ActiveReplayModule?.MaxNetworkId ?? 0;
                _statusWriter?.Write(new NodeOpStatus
                {
                    TransactionId   = cmd.TransactionId,
                    NodeId          = _nodeId,
                    Status          = OpStatus.Success,
                    IsParticipating = true,
                    ErrorCode       = 0,
                    ResultJson      = $"{{\"MaxNetworkId\":{maxNetworkId}}}",
                });

                FdpLog<ReplayLoadDsmHandler>.Info(
                    "[SimHost] ReplayLoadDsmHandler: PrepareReplay complete (drillId={0}, MaxNetworkId={1}).",
                    drillId, maxNetworkId);
            }
            else if (cmd.Operation == NodeOpType.FinalizeReplay)
            {
                await _controller.TeardownReplayAsync().ConfigureAwait(false);

                FdpLog<ReplayLoadDsmHandler>.Info(
                    "[SimHost] ReplayLoadDsmHandler: TeardownReplay complete.");
            }
            else if (cmd.Operation == NodeOpType.PrepareLive)
            {
                // CGF1-S0305: Live-from-Replay branch.
                // 1. Tear down replay — EntityRepository is left at historical state (no memcpy).
                // 2. Immediately start recording under the new branched DrillId.
                var branchedDrillId = ParseDrillId(cmd.PayloadJson);
                await _controller.TeardownReplayAsync().ConfigureAwait(false);
                await _controller.PrepareRecordingAsync(branchedDrillId, _storageDirectory)
                    .ConfigureAwait(false);

                _statusWriter?.Write(new NodeOpStatus
                {
                    TransactionId   = cmd.TransactionId,
                    NodeId          = _nodeId,
                    Status          = OpStatus.Success,
                    IsParticipating = true,
                    ErrorCode       = 0,
                    ResultJson      = string.Empty,
                });

                FdpLog<ReplayLoadDsmHandler>.Info(
                    "[SimHost] ReplayLoadDsmHandler: Live-from-Replay branch complete (branchedDrillId={0}).",
                    branchedDrillId);
            }

            return null;
        }

        /// <summary>
        /// For <see cref="NodeOpType.PrepareReplay"/>: disables
        /// <see cref="SimulationSystemGroup"/> and <see cref="NetworkLifecycleSystemGroup"/>;
        /// sets <see cref="GhostCreationSystem.BypassLifecycle"/> to <c>true</c>.
        /// <para>
        /// For <see cref="NodeOpType.FinalizeReplay"/>: re-enables both system groups and
        /// resets <see cref="GhostCreationSystem.BypassLifecycle"/> to <c>false</c>.
        /// </para>
        /// <para>
        /// For the Live-from-Replay <see cref="NodeOpType.PrepareLive"/> branch
        /// (CGF1-S0305): re-enables both system groups and clears
        /// <see cref="GhostCreationSystem.BypassLifecycle"/> so the live simulation
        /// resumes from the historical snapshot state.
        /// </para>
        /// </summary>
        public void Commit(NodeOpCommand cmd, EntityRepository? repo)
        {
            if (cmd.Operation == NodeOpType.PrepareReplay)
            {
                _simGroup.Enabled                    = false;
                _lifecycleGroup.Enabled              = false;
                _ghostCreationSystem.BypassLifecycle = true;

                FdpLog<ReplayLoadDsmHandler>.Info(
                    "[SimHost] ReplayLoadDsmHandler: Commit(PrepareReplay) — sim+lifecycle disabled, BypassLifecycle=true.");
            }
            else if (cmd.Operation == NodeOpType.FinalizeReplay)
            {
                _simGroup.Enabled                    = true;
                _lifecycleGroup.Enabled              = true;
                _ghostCreationSystem.BypassLifecycle = false;

                FdpLog<ReplayLoadDsmHandler>.Info(
                    "[SimHost] ReplayLoadDsmHandler: Commit(FinalizeReplay) — sim+lifecycle re-enabled, BypassLifecycle=false.");
            }
            else if (cmd.Operation == NodeOpType.PrepareLive)
            {
                // CGF1-S0305: Live-from-Replay branch — re-enable simulation so live
                // ticks resume from the historical snapshot state.
                _simGroup.Enabled                    = true;
                _lifecycleGroup.Enabled              = true;
                _ghostCreationSystem.BypassLifecycle = false;

                FdpLog<ReplayLoadDsmHandler>.Info(
                    "[SimHost] ReplayLoadDsmHandler: Commit(PrepareLive/branch) — sim+lifecycle re-enabled, BypassLifecycle=false.");
            }
        }

        /// <inheritdoc />
        public void Abort(NodeOpCommand cmd, EntityRepository? repo)
        {
            // No pre-allocated resources to release;  async work in PrepareAsync
            // is already driven to completion before Abort can be called.
        }

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
                        $"[SimHost] ReplayLoadDsmHandler: 'DrillId' value '{raw}' is not a valid GUID. " +
                        "Refusing to open a recording under an unintended drill id.");
                }
            }
            throw new InvalidOperationException(
                "[SimHost] ReplayLoadDsmHandler: PayloadJson is missing or does not contain a 'DrillId' " +
                $"property. Payload: '{payloadJson}'. Refusing to open replay under an unknown drill id.");
        }
    }
}
