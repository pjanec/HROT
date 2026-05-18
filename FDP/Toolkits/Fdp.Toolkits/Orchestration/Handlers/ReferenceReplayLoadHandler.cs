using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Orchestration;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Scheduling;

namespace Fdp.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// Reference implementation of the replay-load Cluster handler (CGF1-G0405).
    ///
    /// <para>
    /// Handles <c>PrepareReplay (operationId=11)</c>, <c>FinalizeReplay (operationId=12)</c>,
    /// <c>NodeReplaySeek (operationId=13)</c>, and the Live-from-Replay
    /// <c>PrepareLive (operationId=9)</c> branch (CGF1-S0304 / CGF1-S0305).
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
    ///     <b>Commit:</b> Disables <see cref="TogglableInputGroup"/>,
    ///     <see cref="TogglableSimulationGroup"/>, <see cref="TogglablePostSimulationGroup"/>,
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
    ///     <b>Commit:</b> Re-enables all system groups and toggles bypass off.
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
        private readonly TogglableInputGroup?         _inputGroup;
        private readonly TogglableSimulationGroup?    _simGroup;
        private readonly TogglablePostSimulationGroup? _postSimGroup;
        private readonly NetworkLifecycleSystemGroup? _lifecycleGroup;
        private readonly Action<bool>?                _bypassLifecycleToggle;
        private readonly string                       _storageDirectory;
        private readonly Action? _suspendGlobalTimePush;
        private readonly Action? _resumeGlobalTimePush;

        /// <param name="controller">Record/replay lifecycle controller.</param>
        /// <param name="inputGroup">
        /// <see cref="TogglableInputGroup"/> whose <c>Enabled</c> flag is cleared during
        /// replay to prevent live operator commands and network ingress from corrupting
        /// historical ECS state.
        /// </param>
        /// <param name="simGroup">
        /// <see cref="TogglableSimulationGroup"/> whose <c>Enabled</c> flag is cleared during
        /// replay to prevent simulation logic from running on top of recorded data.
        /// </param>
        /// <param name="postSimGroup">
        /// <see cref="TogglablePostSimulationGroup"/> whose <c>Enabled</c> flag is cleared
        /// during replay to prevent physics integration from overwriting restored historical
        /// <c>SimTransform</c> positions.
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
            IRecordReplayController       controller,
            TogglableInputGroup?          inputGroup,
            TogglableSimulationGroup?     simGroup,
            TogglablePostSimulationGroup? postSimGroup,
            NetworkLifecycleSystemGroup?  lifecycleGroup,
            Action<bool>?                 bypassLifecycleToggle,
            string                        storageDirectory,
            Action?                       suspendGlobalTimePush = null,
            Action?                       resumeGlobalTimePush  = null)
        {
            _controller            = controller       ?? throw new ArgumentNullException(nameof(controller));
            _inputGroup            = inputGroup;
            _simGroup              = simGroup;
            _postSimGroup          = postSimGroup;
            _lifecycleGroup        = lifecycleGroup;
            _bypassLifecycleToggle = bypassLifecycleToggle;
            _storageDirectory      = storageDirectory ?? throw new ArgumentNullException(nameof(storageDirectory));
            _suspendGlobalTimePush = suspendGlobalTimePush;
            _resumeGlobalTimePush  = resumeGlobalTimePush;
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
            operation == NodeOpType.NodeReplaySeek ||
            operation == NodeOpType.PrepareState ||
            (operation == NodeOpType.PrepareLive && _controller.IsReplayActive);

        /// <inheritdoc />
        public bool CanHandle(ExecuteNodeOpIntent intent)
        {
            if (intent.Operation == NodeOpType.PrepareReplay ||
                intent.Operation == NodeOpType.FinalizeReplay ||
                intent.Operation == NodeOpType.NodeReplaySeek) return true;

            if (intent.Operation == NodeOpType.PrepareLive && _controller.IsReplayActive) return true;

            return intent.Operation == NodeOpType.PrepareState &&
                   intent.DomainPayload is EditLoadHandlerPayload p &&
                   (p.TargetState == ClusterState.OperatingReplay || p.TargetState == ClusterState.Idle);
        }

        /// <inheritdoc />
        public async Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            if (intent.Operation == NodeOpType.PrepareReplay)
            {
                var exerciseId = ResolveExerciseId(intent.DomainPayload);
                await _controller.PrepareReplayAsync(exerciseId, _storageDirectory)
                    .ConfigureAwait(false);

                var maxNetworkId    = _controller.ActiveMaxNetworkId;
                var durationSeconds = _controller.ActiveReplayDurationSeconds;

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] PrepareReplay complete (exerciseId={0}, MaxNetworkId={1}, Duration={2}s).",
                    exerciseId, maxNetworkId, durationSeconds);

                return new ReplayPrepareResult(maxNetworkId, durationSeconds);
            }
            else if (intent.Operation == NodeOpType.FinalizeReplay)
            {
                await _controller.TeardownReplayAsync().ConfigureAwait(false);

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] TeardownReplay complete.");
            }
            else if (intent.Operation == NodeOpType.NodeReplaySeek)
            {
                var relativeTicks = intent.DomainPayload is ReplaySeekPayload rsp
                    ? rsp.TargetWallTicks
                    : long.MaxValue;

                // Shift the relative slider ticks into absolute UTC indexing time
                long absoluteTargetTicks = _controller.ActiveRecordingStartWallTicks + relativeTicks;

                GlobalTime restoredTime = await _controller.SeekToTimeAsync(absoluteTargetTicks)
                    .ConfigureAwait(false);

                // The restored frame contains the historical TotalTime from the live simulation.
                // We must convert the actual landed absolute wall ticks back into a relative
                // 0-based duration (in seconds) so the Orchestrator's UI slider stays in bounds.
                long actualAbsoluteTicks = restoredTime.TotalWallTicks;
                double relativeLandedSeconds = (actualAbsoluteTicks - _controller.ActiveRecordingStartWallTicks) / (double)TimeSpan.TicksPerSecond;

                // Overwrite the time going back to the Orchestrator
                restoredTime.TotalTime = Math.Max(0.0, relativeLandedSeconds);

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] NodeReplaySeek complete (targetTicks={0}, restoredWallTicks={1}).",
                    absoluteTargetTicks,
                    restoredTime.TotalWallTicks);

                return new ReplaySeekResult(restoredTime);
            }
            else if (intent.Operation == NodeOpType.PrepareLive)
            {
                // CGF1-S0305: Live-from-Replay branch.
                // Capture the historical time BEFORE teardown; after TeardownReplayAsync the
                // replay module is gone and _controller.GetCurrentReplayTime() returns default.
                GlobalTime historicalTime = _controller.GetCurrentReplayTime();

                var branchedExerciseId = ResolveExerciseId(intent.DomainPayload);
                await _controller.TeardownReplayAsync().ConfigureAwait(false);
                await _controller.PrepareRecordingAsync(branchedExerciseId, _storageDirectory)
                    .ConfigureAwait(false);

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] Live-from-Replay branch complete (branchedExerciseId={0}, historicalWallTicks={1}).",
                    branchedExerciseId,
                    historicalTime.TotalWallTicks);

                return new LiveBranchResult(historicalTime);
            }

            return null;
        }

        /// <inheritdoc />
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            if (intent.Operation == NodeOpType.PrepareReplay)
            {
                SetSystemsEnabled(false);
                _suspendGlobalTimePush?.Invoke();
                _bypassLifecycleToggle?.Invoke(true);

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] Commit(PrepareReplay) — sim+lifecycle disabled.");
            }
            else if (intent.Operation == NodeOpType.FinalizeReplay)
            {
                SetSystemsEnabled(true);
                _resumeGlobalTimePush?.Invoke();
                _bypassLifecycleToggle?.Invoke(false);

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] Commit(FinalizeReplay) — sim+lifecycle re-enabled.");
            }
            else if (intent.Operation == NodeOpType.PrepareLive)
            {
                SetSystemsEnabled(true);
                _resumeGlobalTimePush?.Invoke();
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
            if (_inputGroup     != null) _inputGroup.Enabled     = enabled;
            if (_simGroup       != null) _simGroup.Enabled       = enabled;
            if (_postSimGroup   != null) _postSimGroup.Enabled   = enabled;
            if (_lifecycleGroup != null) _lifecycleGroup.Enabled = enabled;
        }

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
