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
    public sealed class ReferenceReplayLoadHandler : IClusterStateHandler, IClusterStateUpdateObserver
    {
        private readonly IRecordReplayController      _controller;
        private readonly EntityRepository?            _world;   // ⭐ CE-3337

        /// <summary>⭐ R-3055 — the recording this node replays now (its controller's), or <see langword="null"/>. Every ECS host
        /// registers this handler through the shared composition (<c>EcsHostClusterHandlers.Compose</c>), so the debug surface reads
        /// the file from the node's own <c>ClusterSlave</c> — the same way on every node.</summary>
        public string? ReplayFilePath => _controller.ActiveReplayFilePath;

        /// <summary>
        /// ⭐ RB-2 (<c>docs/DESIGN_Replay_Browser_Debug_Http.md</c>) — the replay's zero (the master's anchor, else this node's first
        /// frame) and the replay clock now, in wall ticks; <see langword="null"/> outside a replay. What the debug API resolves a
        /// frame-based seek against.
        /// </summary>
        public (long ZeroWallTicks, long NowWallTicks)? ReplayPosition
            => _controller.IsReplayActive
                ? (_controller.ReplayAnchorWallTicks > 0 ? _controller.ReplayAnchorWallTicks : _controller.ActiveRecordingStartWallTicks,
                   _controller.GetCurrentReplayTime().TotalWallTicks)
                : null;

        /// <summary>⭐ CE-3329 P0 (row 6) — the recorded sim time of the frame on screen; <see langword="null"/> outside a replay.</summary>
        public double? ReplaySimSeconds => _controller.IsReplayActive ? _controller.CurrentReplaySimSeconds : null;
        private readonly TogglableInputGroup?         _inputGroup;
        private readonly TogglableSimulationGroup?    _simGroup;
        private readonly TogglablePostSimulationGroup? _postSimGroup;
        private readonly NetworkLifecycleSystemGroup? _lifecycleGroup;
        private readonly Action<bool>?                _bypassLifecycleToggle;
        private readonly string                       _storageDirectory;
        private readonly Action? _suspendGlobalTimePush;
        private readonly Action? _resumeGlobalTimePush;
        /// <summary>⭐ §2.1m step 3 — see the ctor parameter's remarks.</summary>
        private readonly Action<bool>? _worldReplaced;

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
        /// <param name="worldReplaced">
        /// ⭐⭐⭐ §2.1m step 3 — the WORLD-REPLACEMENT hook. Invoked with <c>false</c> on entering a replay
        /// and <c>true</c> on leaving it for a live world. ⛔ The SEEK boundary is NOT routed through here:
        /// it is composed into the controller's <c>afterSeek</c> chain, beside the
        /// <c>NetworkEntityMap.RebuildFromWorld</c> that already lives there for the same reason.
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
            Action?                       resumeGlobalTimePush  = null,
            // ⭐⭐⭐ Step 3 of docs/designs/replay-and-modules/DESIGN.md §2.1m — THE WORLD-REPLACEMENT HOOK.
            //   Invoked with resumingToLive:false when ENTERING a replay and true when leaving it for a
            //   live world. The implementation clears the ELM's in-flight queues and, on a resume, arms the
            //   re-derive. ⚠ Unset = the previous behaviour exactly.
            Action<bool>?                 worldReplaced         = null,
            // ⭐ CE-3337 — this node's world: a branch's commit runs the after-restore step on it. ⛔ The commit's own `repo` argument is
            //   NULL from ClusterSlave (every call site passes repo: null), so the world must be held here; ⚠ every production caller passes it.
            EntityRepository?             world                 = null)
        {
            _world                 = world;
            _controller            = controller       ?? throw new ArgumentNullException(nameof(controller));
            _inputGroup            = inputGroup;
            _simGroup              = simGroup;
            _postSimGroup          = postSimGroup;
            _lifecycleGroup        = lifecycleGroup;
            _bypassLifecycleToggle = bypassLifecycleToggle;
            _worldReplaced         = worldReplaced;
            _storageDirectory      = storageDirectory ?? throw new ArgumentNullException(nameof(storageDirectory));
            _suspendGlobalTimePush = suspendGlobalTimePush;
            _resumeGlobalTimePush  = resumeGlobalTimePush;
        }

        /// <summary>
        /// ⭐ CE-4079 ④ — how long a node waits for its retraction disposes to be acknowledged before it answers (the sweep that writes them
        /// and the acknowledgements share it). Under the 3 s a hold lapses after (<c>EgressHold</c>): a node that answers at the end of this
        /// budget still holds its egress until the master's commit.
        /// </summary>
        public static readonly TimeSpan RetractionAckBudget = TimeSpan.FromSeconds(2);

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
                // ⭐ R-4012 (Q91 §4-P P1) — this node's span, so the master can compute the ONE replay zero.
                long first = _controller.ActiveRecordingStartWallTicks;
                long last  = _controller.ActiveRecordingEndWallTicks;

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] PrepareReplay complete (exerciseId={0}, MaxNetworkId={1}, Duration={2}s, span {3}).",
                    exerciseId, maxNetworkId, durationSeconds, $"{first}..{last}");

                // ⭐⭐ CE-4079 ④ (DESIGN_Replay_Time_Axis_And_Review §12c W3) — prepared only once this node's retraction disposes were
                //   ACKNOWLEDGED (bounded); a timeout is carried in the result, never fatal.
                string? ackTimeout = await _controller.AwaitRetractionAcksAsync(RetractionAckBudget).ConfigureAwait(false);

                // ⭐ CE-3329 P1 (R3) — this node's wall ↔ sim map: the master intersects them into the cluster's sim axis
                return new ReplayPrepareResult(maxNetworkId, durationSeconds, first, last, _controller.ActiveReplayTimeMap,
                    _controller.ActiveReplayCheckpoints,   // ⭐ CE-3329 §9f — the marks every host's timeline draws
                    _controller.ActiveReplayGeoOrigin, _controller.ActiveReplayTerrainName,   // ⭐ AQ107 C — the origin the run used
                    ackTimeout);
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

                // ⭐⭐ R-4012 (Q91 §4-P P1) — ONE timeline: the master's anchor (the earliest first frame across the cluster).
                //   ⛔ Was: every node added its OWN start, so a node that began recording late (a late Player) restored a
                //   moment shifted by its join delay. A payload without an anchor keeps the node's own start, as before.
                if (intent.DomainPayload is ReplaySeekPayload { AnchorWallTicks: > 0 } anchored)
                    _controller.ReplayAnchorWallTicks = anchored.AnchorWallTicks;

                // Shift the relative slider ticks into absolute UTC indexing time — SATURATING.
                // 🔴 A plain add overflowed for "seek to the end" (long.MaxValue — this handler's own default, and what
                //    scripts send): start + MaxValue wrapped NEGATIVE, the playback clamped that to frame 0, and a
                //    "seek to the end" restored the FIRST frame (measured: ClusterOpE2eScriptTests.RecordAndReplaySeek,
                //    target -8584107341263421530, an empty world). CE-497.
                long startTicks          = _controller.ReplayAnchorWallTicks;
                long absoluteTargetTicks = relativeTicks > 0 && startTicks > long.MaxValue - relativeTicks
                    ? long.MaxValue
                    : startTicks + relativeTicks;

                // ⭐⭐ CE-3329 PW — §12c W2: nothing is published from the swap until the master's commit (the broadcast it sends
                //   when every node answered — OnClusterStateUpdate); the cleanup's sweep retracts what the seek removed meanwhile.
                //   Held here, on the frame thread, before the seek goes off it.
                _controller.HoldEgress("replay seek");
                GlobalTime restoredTime;
                try
                {
                    restoredTime = await _controller.SeekToTimeAsync(absoluteTargetTicks).ConfigureAwait(false);
                }
                finally
                {
                    _awaitingCommit = true;   // ⭐ released by the master's next broadcast (it follows this node's answer)
                }

                // ⭐⭐ CE-4079 ④ (DESIGN_Replay_Time_Axis_And_Review §12c W3) — seek-done only once this node's retraction disposes (the
                //   cleanup's sweep of what the swap removed) were ACKNOWLEDGED by the readers, so W2's barrier holds: nobody publishes the
                //   same id while this node's retraction of it is still in flight. Bounded; a timeout rides in the result, never fatal.
                string? ackTimeout = await _controller.AwaitRetractionAcksAsync(RetractionAckBudget).ConfigureAwait(false);

                // ⭐⭐ R-4012 — outside this node's recording: it restored nothing and listens. It reports no landed time, so
                //   the master takes the time from a node that did restore (ReplaySeekAggregator skips an empty result).
                if (restoredTime.TotalWallTicks == 0)
                {
                    FdpLog<ReferenceReplayLoadHandler>.Info(
                        "[ReferenceReplayLoadHandler] NodeReplaySeek: target {0} is outside this node's recording — listening (R-4012).",
                        absoluteTargetTicks);
                    return new ReplaySeekResult(default, ackTimeout);
                }

                // The restored frame contains the historical TotalTime from the live simulation.
                // We must convert the actual landed absolute wall ticks back into a relative
                // 0-based duration (in seconds) so the Orchestrator's UI slider stays in bounds.
                long actualAbsoluteTicks = restoredTime.TotalWallTicks;
                double relativeLandedSeconds = (actualAbsoluteTicks - startTicks) / (double)TimeSpan.TicksPerSecond;

                // Overwrite the time going back to the Orchestrator
                restoredTime.TotalTime = Math.Max(0.0, relativeLandedSeconds);

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] NodeReplaySeek complete (targetTicks={0}, restoredWallTicks={1}).",
                    absoluteTargetTicks,
                    restoredTime.TotalWallTicks);

                return new ReplaySeekResult(restoredTime, ackTimeout);
            }
            else if (intent.Operation == NodeOpType.PrepareLive)
            {
                // CGF1-S0305: Live-from-Replay branch.
                // Capture the historical time BEFORE teardown; after TeardownReplayAsync the
                // replay module is gone and _controller.GetCurrentReplayTime() returns default.
                GlobalTime historicalTime = _controller.GetCurrentReplayTime();
                // ⭐⭐ CE-3337 — the live run counts its STEPS on from the frame on screen: the recorded GlobalTime's FrameNumber (the step
                //   counter perception stamps sightings with, PerceptionClock). ⛔ The replay clock's own frame count went on instead, so every
                //   TargetMemory.LastSeenTick after a branch was on a shifted count (H3: the units' memory differed from the straight run's).
                //   Only a node that restored this frame from its own recording knows it (a listener's world clock is stale).
                bool fromRecording = _controller.IsRestoringReplay && _world is { } w && w.HasSingletonUnmanaged<GlobalTime>();
                if (fromRecording) historicalTime.FrameNumber = _world!.GetSingletonUnmanaged<GlobalTime>().FrameNumber;
                // ⭐ D18 (R-3047) — the branch keeps its history: this node's recording up to the frame on screen is copied into
                //   the new exercise first (null on a listener — its branch starts empty). Read BEFORE the teardown closes it.
                var history = _controller.CurrentReplayBranch;

                // ⭐ D18 ① — the MASTER mints the branch's id (ClusterMaster.ExecuteTransitionTrajectory); ⛔ an empty id would send
                //   every branch to one folder.
                var branchedExerciseId = ResolveExerciseId(intent.DomainPayload);
                if (branchedExerciseId == Guid.Empty)
                    throw new InvalidOperationException("a branch from a replay needs the exercise id the master minted for it");
                await _controller.TeardownReplayAsync().ConfigureAwait(false);
                // ⭐ CE-4090 (AQ108 D14, §5a) — the branch follows the "Record live runs" setting like any live run: with it off the
                //   replay is torn down and the live world goes on UNRECORDED (the history copy is not started either).
                bool recordingOff = intent.DomainPayload is EditLoadHandlerPayload { RecordingOff: true };
                if (!recordingOff)
                    await _controller.PrepareRecordingAsync(branchedExerciseId, _storageDirectory, history)
                        .ConfigureAwait(false);

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] Live-from-Replay branch complete (branchedExerciseId={0}, historicalWallTicks={1}, history {2}).",
                    branchedExerciseId,
                    historicalTime.TotalWallTicks,
                    history == null ? "none (listener)" : $"{history.FrameCount} frame(s) of {history.ParentExerciseId}");

                // ⭐ CE-4097 (AQ108 D5) — the branch's parent and point ride the answer: the SAME RecordingBranch the sidecar is written from.
                return new LiveBranchResult(historicalTime, fromRecording, history?.ParentExerciseId ?? Guid.Empty, history?.BranchWallTicks ?? 0);
            }

            return null;
        }

        /// <inheritdoc />
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            if (intent.Operation == NodeOpType.PrepareReplay)
            {
                EnterReplayGates();

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] Commit(PrepareReplay) — sim+lifecycle disabled.");
            }
            else if (intent.Operation == NodeOpType.FinalizeReplay)
            {
                LeaveReplayGates();
                // ⭐⭐ Resuming to a LIVE world: clear, then ARM the re-derive so the next tick re-opens
                //   every protocol the restored world still implies (else restored Constructing/TearDown
                //   entities are zombies nothing drives).
                _worldReplaced?.Invoke(true);

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] Commit(FinalizeReplay) — sim+lifecycle re-enabled.");
            }
            else if (intent.Operation == NodeOpType.PrepareLive)
            {
                LeaveReplayGates();   // ⭐ the ONE gate set (W5's release included) — ⛔ was its three lines copied
                // ⭐⭐ Branch-to-live: same as FinalizeReplay — clear and arm. This is the path that left
                //   restored Constructing entities permanently invisible to every default query.
                _worldReplaced?.Invoke(true);
                // ⭐⭐ CE-3337 — a branch is a restore IN PLACE (D18: "a checkpoint restore is a branch too"): the ONE after-restore step —
                //   the entity map rebuilt, the restore COUNTED, every descriptor republished. ⛔ Without the count, every per-process
                //   counter a restore resumes from the world stayed where the original live run had left it: the muscle's route handles
                //   went on from 0x40000026 where the straight run at the branch frame stood at 0x40000004 (the coordinator's H3), and
                //   the sound sequence numbering likewise (CE-3315).
                if ((repo ?? _world) is { } branched) Fdp.Toolkit.Replay.AfterRestore.Run(branched);

                FdpLog<ReferenceReplayLoadHandler>.Info(
                    "[ReferenceReplayLoadHandler] Commit(PrepareLive/branch) — sim+lifecycle re-enabled.");
            }
        }

        /// <inheritdoc />
        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) { }

        // ⭐ CE-3329 W2 — a seek's egress hold waits for the master's commit broadcast.
        private volatile bool _awaitingCommit;

        /// <inheritdoc />
        /// <remarks>⭐ CE-3329 W2 — the master's broadcast after a seek round releases this node's egress hold (a full republish follows:
        /// the seek marked every descriptor dirty). A gone master releases it too.</remarks>
        public void OnClusterStateUpdate(ClusterState state, bool masterGone)
        {
            if (!_awaitingCommit) return;
            _awaitingCommit = false;
            _controller.ReleaseEgress();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ The replay's gates ON: the four toggle groups off, the kernel's clock push suspended (the playback owns <c>GlobalTime</c>),
        /// lifecycle bypassed, the ELM's bookkeeping discarded (the log is about to own the world — ⛔ no re-derive armed).
        /// ⭐ CE-3329 P3 — the ONE set of gates: a review enters them through here too (F9: gates before the replay module's first frame).
        /// </summary>
        public void EnterReplayGates()
        {
            SetSystemsEnabled(false);
            _suspendGlobalTimePush?.Invoke();
            _bypassLifecycleToggle?.Invoke(true);
            _worldReplaced?.Invoke(false);
        }

        /// <summary>⭐ The replay's gates OFF (the caller re-derives the world it resumes to — a replay's finalize, a branch, a review's
        /// checkpoint return).</summary>
        public void LeaveReplayGates()
        {
            SetSystemsEnabled(true);
            _resumeGlobalTimePush?.Invoke();
            _bypassLifecycleToggle?.Invoke(false);
            _controller.ReleaseReplayExit();   // ⭐ CE-3329 W5 (§12u) — the readers' replay-era state is discarded before ingress resumes
        }

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
