using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
using Fdp.Kernel.Orchestration;
using FDP.Kernel.Logging;
using ModuleHost.Core.Scheduling;

namespace FDP.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// Reference implementation of the replay-load Cluster handler (CGF1-G0405).
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
    ///     and publishes a <see cref="NodeOpCompletedEvent"/> with
    ///     <c>ResultPayload = maxNetworkId</c> (typed int) so the orchestrator can reset the
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
    public sealed class ReferenceReplayLoadHandler : IClusterStateHandler
    {
        private readonly IRecordReplayController      _controller;
        private readonly SimulationSystemGroup?       _simGroup;
        private readonly NetworkLifecycleSystemGroup? _lifecycleGroup;
        private readonly Action<bool>?                _bypassLifecycleToggle;
        private readonly string                       _storageDirectory;

        /// <param name="controller">Record/replay lifecycle controller.</param>
        /// <param name="simGroup">
        /// <see cref="SimulationSystemGroup"/> whose <c>Enabled</c> flag is cleared during
        /// replay to prevent simulation logic from running on top of recorded data.
        /// </param>
        /// <param name="lifecycleGroup">
        /// <see cref="NetworkLifecycleSystemGroup"/> whose <c>Enabled</c> flag is cleared
        /// during replay so no lifecycle state changes or ghost promotions occur during playback.
        /// </param>
        /// <param name="bypassLifecycleToggle">
        /// Optional delegate invoked with <c>true</c> when replay begins and <c>false</c> when
        /// it ends; maps to <c>GhostCreationSystem.BypassLifecycle</c>.
        /// Pass <c>null</c> when the subsystem has no ghost-creation system (e.g. tests).
        /// </param>
        /// <param name="storageDirectory">
        /// Root directory where exercise recording files are staged.
        /// </param>
        public ReferenceReplayLoadHandler(
            IRecordReplayController      controller,
            SimulationSystemGroup?       simGroup,
            NetworkLifecycleSystemGroup? lifecycleGroup,
            Action<bool>?                bypassLifecycleToggle,
            string                       storageDirectory)
        {
            _controller            = controller       ?? throw new ArgumentNullException(nameof(controller));
            _simGroup              = simGroup;
            _lifecycleGroup        = lifecycleGroup;
            _bypassLifecycleToggle = bypassLifecycleToggle;
            _storageDirectory      = storageDirectory ?? throw new ArgumentNullException(nameof(storageDirectory));
        }

        /// <inheritdoc />
        /// <remarks>
        /// Returns <c>true</c> for <c>PrepareReplay</c> and <c>FinalizeReplay</c>
        /// unconditionally.  Returns <c>true</c> for <c>PrepareLive</c> <b>only</b> when a
        /// replay session is active (<see cref="IRecordReplayController.IsReplayActive"/>).
        /// </remarks>
        public bool CanHandle(NodeOpType operation) =>
            operation == NodeOpType.PrepareReplay ||
            operation == NodeOpType.FinalizeReplay ||
            (operation == NodeOpType.PrepareLive && _controller.IsReplayActive);

        /// <inheritdoc />
        public async Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            if (intent.Operation == NodeOpType.PrepareReplay)
            {
                var exerciseId = intent.DomainPayload is Guid g ? g : Guid.Empty;
                await _controller.PrepareReplayAsync(exerciseId, _storageDirectory)
                    .ConfigureAwait(false);

                var maxNetworkId = _controller.ActiveMaxNetworkId;

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] PrepareReplay complete (exerciseId={0}, MaxNetworkId={1}).",
                    exerciseId, maxNetworkId);

                return (object?)maxNetworkId;
            }
            else if (intent.Operation == NodeOpType.FinalizeReplay)
            {
                await _controller.TeardownReplayAsync().ConfigureAwait(false);

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] TeardownReplay complete.");
            }
            else if (intent.Operation == NodeOpType.PrepareLive)
            {
                // CGF1-S0305: Live-from-Replay branch.
                var branchedExerciseId = intent.DomainPayload is Guid bg ? bg : Guid.Empty;
                await _controller.TeardownReplayAsync().ConfigureAwait(false);
                await _controller.PrepareRecordingAsync(branchedExerciseId, _storageDirectory)
                    .ConfigureAwait(false);

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] Live-from-Replay branch complete (branchedExerciseId={0}).",
                    branchedExerciseId);
            }

            return null;
        }

        /// <inheritdoc />
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            if (intent.Operation == NodeOpType.PrepareReplay)
            {
                SetSystemsEnabled(false);
                _bypassLifecycleToggle?.Invoke(true);

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] Commit(PrepareReplay) — sim+lifecycle disabled.");
            }
            else if (intent.Operation == NodeOpType.FinalizeReplay)
            {
                SetSystemsEnabled(true);
                _bypassLifecycleToggle?.Invoke(false);

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] Commit(FinalizeReplay) — sim+lifecycle re-enabled.");
            }
            else if (intent.Operation == NodeOpType.PrepareLive)
            {
                SetSystemsEnabled(true);
                _bypassLifecycleToggle?.Invoke(false);

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] Commit(PrepareLive/branch) — sim+lifecycle re-enabled.");
            }
        }

        /// <inheritdoc />
        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) { }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private void SetSystemsEnabled(bool enabled)
        {
            if (_simGroup       != null) _simGroup.Enabled       = enabled;
            if (_lifecycleGroup != null) _lifecycleGroup.Enabled = enabled;
        }
    }
}
