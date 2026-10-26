using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Common.Orchestration;
using Fdp.Core;
using Fdp.Core.Orchestration;
using Fdp.Core.FlightRecorder;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost;

namespace Hrot.SimHost.Modules.Orchestration
{
    /// <summary>
    /// Control-plane factory and orchestrator for ECS recording and replay.
    /// Implements <see cref="IClusterOpHandler"/> for legacy 2PC dispatch stubs and
    /// <see cref="IRecordReplayController"/> for the canonical recording/replay
    /// lifecycle contract shared across subsystems (CGF1-S0304).
    ///
    /// <para>
    /// This class is a <em>pure factory</em>: it constructs typed
    /// <see cref="RecordingModule"/> / <see cref="EpisodeRecorderModule"/> /
    /// <see cref="ReplayModule"/> objects with the correct
    /// <see cref="RecordingConfiguration"/> context and routes their lifecycle
    /// through <see cref="ModuleHostKernel"/>.  It never directly owns an
    /// <c>AsyncRecorder</c> or <c>PlaybackController</c>.
    /// </para>
    ///
    /// <para>
    /// The <see cref="IClusterOpHandler"/> stubs (<see cref="CanHandle"/> always returns
    /// <c>false</c>) remain in place so <see cref="ClusterSlave"/> can still hold a
    /// reference; actual cluster state handling is delegated to
    /// <see cref="Handlers.LiveLoadClusterStateHandler"/> and
    /// <see cref="Handlers.ReplayLoadClusterOpHandler"/> which call the methods on this
    /// class (CGF1-S0304 / S0305).
    /// </para>
    /// </summary>
    /// <remarks>
    /// ⭐⭐ <c>P5a</c> (Q91 §4-G, <c>R-4003</c>/<c>R-4011</c>) — lives in <c>Hrot.Network.Orchestration</c> so EVERY ECS host can
    /// compose it: IG references this assembly and not <c>Hrot.SimHost</c>. ⭐ It is the ONE record/replay controller —
    /// C10 deleted the CGF skeleton and the listen-only one (ExCon, the only non-ECS host, holds none).
    /// ⚠ The namespace is still <c>Hrot.SimHost.Modules.Orchestration</c> so no caller changed in the move; renaming it is a
    /// Roslyn job for later. Build one through <see cref="ForNode"/>.
    /// </remarks>
    public sealed class EcsRecordReplayController : IClusterOpHandler, IRecordReplayController
    {
        /// <summary>
        /// ⭐⭐ <c>P5a</c> — THE one composition of a node's recording controller, used by every ECS host (ruling 9: it was
        /// written out by hand on CGF and SimHost, and IG would have been the third copy). A seek is a world
        /// replacement: the lifecycle module's in-flight bookkeeping is cleared (resuming to live is NOT armed — a seek
        /// stays inside the replay), then the transport's own post-seek hook runs; and the lifecycle module is gated
        /// while a replay is active (DESIGN.md §2.1m step 1).
        /// </summary>
        /// <param name="transportAfterSeek">The replication module's post-seek hook (<c>INedReplicationModule.AfterSeekCallback</c>), or null.</param>
        /// <param name="lifecycle">The node's <c>EntityLifecycleModule</c>, or null when it has none.</param>
        public static EcsRecordReplayController ForNode(
            ModuleHostKernel kernel, int nodeId, EntityRepository world,
            Action? transportAfterSeek, Fdp.Toolkit.Lifecycle.EntityLifecycleModule? lifecycle)
        {
            Action? afterSeek = transportAfterSeek;
            if (lifecycle != null)
            {
                var chained = afterSeek;
                afterSeek = () => { lifecycle.OnWorldReplaced(resumingToLive: false); chained?.Invoke(); };
            }

            var controller = new EcsRecordReplayController(kernel, nodeId, world, afterSeek);
            if (lifecycle != null)
                lifecycle.IsReplayActive = () => controller.IsReplayActive;
            return controller;
        }

        private readonly ModuleHostKernel _kernel;
        private readonly int              _nodeId;
        private readonly EntityRepository _repo;
        private readonly Action?          _afterSeek;

        private RecordingModule?                        _activeRecordingModule;
        private ReplayModule?                           _activeReplayModule;
        private readonly Dictionary<Guid, EpisodeRecorderModule> _episodeModules = new();

        /// <summary>Module installed by the most recent <see cref="PrepareRecordingAsync"/> call.</summary>
        public RecordingModule? ActiveRecordingModule => _activeRecordingModule;

        /// <summary>Module installed by the most recent <see cref="PrepareReplayAsync"/> call.</summary>
        public ReplayModule? ActiveReplayModule => _activeReplayModule;

        /// <inheritdoc />
        public bool IsReplayActive => _activeReplayModule != null;

        /// <inheritdoc />
        public long ActiveMaxNetworkId => _activeReplayModule?.MaxNetworkId ?? 0;

        /// <inheritdoc />
        public long ActiveRecordingStartWallTicks => _activeReplayModule?.RecordingStartWallTicks ?? 0;

        /// <inheritdoc />
        public long ActiveRecordingEndWallTicks => _activeReplayModule?.RecordingEndWallTicks ?? 0;

        /// <summary>⭐ R-3052…R-3055 — the recording this node replays now (its own, of the exercise the cluster replays), or
        /// <see langword="null"/> outside a replay: what the debug API's journal history and recording search scan, read-only.</summary>
        public string? ActiveReplayFilePath { get; private set; }

        /// <inheritdoc />
        public long ReplayAnchorWallTicks
        {
            get => _activeReplayModule?.AnchorWallTicks ?? 0;
            set { if (_activeReplayModule != null) _activeReplayModule.AnchorWallTicks = value; }
        }

        /// <inheritdoc />
        public bool IsRestoringReplay => _activeReplayModule?.IsRestoring ?? false;

        /// <inheritdoc />
        public float ActiveReplayDurationSeconds
        {
            get
            {
                if (_activeReplayModule == null) return 0f;
                var duration = _activeReplayModule.Duration;
                if (duration > TimeSpan.Zero)
                    return (float)duration.TotalSeconds;
                return _activeReplayModule.TotalFrames / 60f;
            }
        }

        /// <inheritdoc />
        /// <remarks>During a replay the kernel does not push the clock singleton (<c>SuspendGlobalTimePush</c>), so the world's
        /// <c>GlobalTime</c> is the one the playback restored — the frame's recorded clock.</remarks>
        public double? CurrentReplaySimSeconds
            => _activeReplayModule != null && _repo.HasSingletonUnmanaged<GlobalTime>() ? _repo.GetSingletonUnmanaged<GlobalTime>().TotalTime : null;

        /// <inheritdoc />
        public void HoldEgress(string reason) => Fdp.Toolkit.Replication.Services.EgressHold.Hold(_repo, reason);

        /// <inheritdoc />
        public void ReleaseEgress() => Fdp.Toolkit.Replication.Services.EgressHold.Release(_repo);

        /// <param name="kernel">Kernel that manages module topology.</param>
        /// <param name="nodeId">Local node identifier embedded in recording file names.</param>
        /// <param name="repo">
        /// Live <see cref="EntityRepository"/> — passed to <see cref="ReplayModule"/> for
        /// off-main-thread seeks.
        /// </param>
        public EcsRecordReplayController(ModuleHostKernel kernel, int nodeId, EntityRepository repo, Action? afterSeek = null)
        {
            _kernel    = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _repo      = repo   ?? throw new ArgumentNullException(nameof(repo));
            _nodeId    = nodeId;

            // Unify NetworkEntityMap resync for all subsystems (Editor, SimHost, CGF, etc.)
            // Wrap the downstream afterSeek callback with NetworkEntityMap rebuild logic.
            _afterSeek = () =>
            {
                // 1. Rebuild NetworkEntityMap from the current ECS state.
                //    ⭐ ROUTED 2026-09-11 to NetworkEntityMap.RebuildFromWorld. The body used to live
                //      inline here, behind a private lambda, while its own comment above claimed to
                //      "unify NetworkEntityMap resync for all subsystems" — ⛔ which it could not, because
                //      a module with its own seek path (the ReplayBrowser) had no way to reach it and so
                //      went without a map entirely. One implementation, reachable by all (ruling 9).
                if (_repo.HasSingletonManaged<NetworkEntityMap>())
                    _repo.GetSingletonManaged<NetworkEntityMap>()!.RebuildFromWorld(_repo);

                // 2. Chain downstream transport-layer cleanup (e.g., NED replication invalidation)
                afterSeek?.Invoke();
            };
        }

        // ── Global recording ─────────────────────────────────────────────────────

        /// <summary>
        /// Opens a new recording for the given exercise.  Installs a
        /// <see cref="RecordingModule"/> via <see cref="ModuleHostKernel"/>;
        /// after this call the <see cref="RecorderTickSystem"/> is live in the
        /// topological graph and every frame is captured.
        /// </summary>
        public Task PrepareRecordingAsync(Guid exerciseId, string storageDirectory)
            => PrepareRecordingAsync(exerciseId, storageDirectory, branch: null);

        /// <summary>⭐ CE-3299 D17 (R-3045) — the mode the session's FIRST recording was opened in; a branch of it (a checkpoint restore) keeps it.</summary>
        public double SessionCheckpointingSeconds { get; private set; }

        /// <summary>
        /// ⭐ <c>CE-3299</c> B4 / <c>D18</c> (R-3047) — a recording that branches: <paramref name="branch"/>'s frames up to the branch
        /// point are copied in first, then every frame is captured after them. ⛔ Never over an existing recording (a loud failure).
        /// </summary>
        public async Task PrepareRecordingAsync(Guid exerciseId, string storageDirectory, RecordingBranch? branch, double checkpointingSeconds = 0)
        {
            if (branch == null) SessionCheckpointingSeconds = Math.Max(0, checkpointingSeconds);   // D17 — a branch inherits its parent's mode
            var filePath = GetRecordingFilePath(exerciseId, storageDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var config = new RecordingConfiguration
            {
                FilePath     = filePath,
                EntityFilter = null,   // record all entities above MinRecordableId
                ExerciseId      = exerciseId,
                NodeId = _nodeId,
                Branch = branch,
                CheckpointingSeconds = branch == null ? SessionCheckpointingSeconds : 0,   // D17 — a branch reads its parent's (StartBranch)
                SlotRunInFlight = () => _kernel.SlotRunInFlight,   // ⭐ CE-3316 — a checkpoint waits for a step with no slot run in flight
            };
            _activeRecordingModule = new RecordingModule(config);
            await _kernel.InstallModuleAsync(_activeRecordingModule);
        }

        /// <summary>
        /// Finalizes the active recording.  Sets <paramref name="maxNetworkId"/> on the
        /// recorder (so it is persisted to <c>.meta.json</c>), then uninstalls the
        /// <see cref="RecordingModule"/> which triggers its blocking
        /// <see cref="RecordingModule.Dispose"/>: flushes LZ4 buffers and writes the manifest.
        /// <para>
        /// When <see cref="_activeRecordingModule"/> is <c>null</c> a <c>Warn</c> log is
        /// emitted and the call returns immediately.  This covers both the benign
        /// "no recording was started" case and the ordering-violation case where
        /// <c>FinalizeLive</c> arrives without a matching <c>PrepareLive</c>.
        /// </para>
        /// </summary>
        /// <param name="maxNetworkId">
        /// Highest network entity ID used during the recording session.  Pass <c>0</c> (or omit)
        /// when no network map is available (e.g. offline / test scenarios).
        /// </param>
        public async Task FinalizeRecordingAsync(long maxNetworkId = 0)
        {
            if (_activeRecordingModule == null)
            {
                FdpLog<EcsRecordReplayController>.Warn(
                    "[Node-{0}] FinalizeRecordingAsync called but no active recording module exists " +
                    "(possible ordering violation: FinalizeLive without a preceding PrepareLive).", _nodeId);
                return;
            }
            _activeRecordingModule.SetMaxNetworkId(maxNetworkId);
            await _kernel.UninstallModuleAsync(_activeRecordingModule);
            _activeRecordingModule = null;
        }

        // ── Episode recording ───────────────────────────────────────────────────────

        /// <summary>
        /// Starts a per-episode recording filtered to entities with
        /// <see cref="EpisodeTag.EpisodeId"/> == <paramref name="episodeId"/>.
        /// Multiple episodes may be recorded concurrently without sharing I/O.
        /// </summary>
        public async Task StartEpisodeRecordingAsync(Guid episodeId, string storageDirectory)
        {
            var filePath = GetEpisodeRecordingFilePath(episodeId, storageDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var config = new RecordingConfiguration
            {
                FilePath     = filePath,
                EntityFilter = BuildEpisodeFilter(episodeId),
                ExerciseId      = episodeId,
                NodeId = 0,
            };
            var module = new EpisodeRecorderModule(config);
            _episodeModules[episodeId] = module;
            await _kernel.InstallModuleAsync(module);
        }

        /// <summary>
        /// Stops and finalizes a per-episode recording.  Only the named episode's
        /// buffers are flushed; other concurrent episode recorders are unaffected.
        /// </summary>
        public async Task StopEpisodeRecordingAsync(Guid episodeId)
        {
            if (_episodeModules.Remove(episodeId, out var module))
                await _kernel.UninstallModuleAsync(module);
        }

        // ── Replay ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Installs a <see cref="ReplayModule"/> for the given exercise recording.
        /// Schema validation runs during installation; throws
        /// <see cref="System.IO.InvalidDataException"/> if the schema has drifted.
        /// </summary>
        public async Task PrepareReplayAsync(Guid exerciseId, string storageDirectory)
        {
            var filePath = GetRecordingFilePath(exerciseId, storageDirectory);
            ActiveReplayFilePath = filePath;
            _activeReplayModule = new ReplayModule(filePath, _repo, _kernel.GetTimeController(), _afterSeek);
            await _kernel.InstallModuleAsync(_activeReplayModule);

            // ⭐⭐ R-4012 (Q91 §4-P P6) — while this node restores its own recording it takes in no world state (the
            //   recording holds everything). Wired HERE, by the controller every ECS host builds, so no host can miss it;
            //   idempotent per replay. ⛔ Outside the span the predicate is false and ingress builds the flooded world.
            int gated = 0;
            foreach (var system in _kernel.SystemScheduler.GetAllSystems())
            {
                if (system is not Fdp.Interfaces.IReplayRestoreGated g) continue;
                g.IsReplayRestoring = () => IsRestoringReplay;
                gated++;
            }
            FdpLog<EcsRecordReplayController>.Info(
                "[Node-{0}] Replay prepared ({1}); {2} ingress system(s) gated while restoring (R-4012).",
                _nodeId, _activeReplayModule.HasRecording ? "own recording" : "NO recording — listener", gated);
        }

        /// <summary>
        /// Tears down the active replay.  Uninstalls the <see cref="ReplayModule"/>
        /// and closes file handles.  <c>EntityRepository</c> is left intact at the
        /// historical state (ready for a Live-from-Replay branch).
        /// </summary>
        public async Task TeardownReplayAsync()
        {
            if (_activeReplayModule == null) return;
            await _kernel.UninstallModuleAsync(_activeReplayModule);
            _activeReplayModule = null;
            ActiveReplayFilePath = null;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>The file <see cref="PrepareRecordingAsync"/> writes for this exercise — under THIS node's id. ⭐ A caller that reports
        /// the path asks here, ⛔ never rebuilds it with a node id of its own (the editor's debug API did, with 0, after the editor moved
        /// to node 1 — CE-3228).</summary>
        public string GetRecordingFilePath(Guid exerciseId, string storageDirectory) =>
            Fdp.Toolkit.Orchestration.OrchestrationConstants.GetExerciseRecordingFilePath(
                storageDirectory, exerciseId, _nodeId);

        private string GetEpisodeRecordingFilePath(Guid episodeId, string storageDirectory) =>
            Fdp.Toolkit.Orchestration.OrchestrationConstants.GetEpisodeRecordingFilePath(
                storageDirectory, episodeId, _nodeId);

        /// <summary>
        /// Builds an entity filter predicate that accepts only entities whose
        /// <see cref="EpisodeTag"/> component matches <paramref name="episodeId"/>.
        /// The predicate uses the live <see cref="_repo"/> to read components.
        /// </summary>
        private Predicate<Entity> BuildEpisodeFilter(Guid episodeId) =>
            entity =>
                _repo.HasComponent<EpisodeTag>(entity) &&
                _repo.GetComponentRO<EpisodeTag>(entity).EpisodeId == episodeId;

        // ── IRecordReplayController: seek / tick ─────────────────────────────────

        /// <summary>
        /// Off-main-thread wall-clock seek.  Delegates to
        /// <see cref="ReplayModule.SeekToWallClockTicksAsync"/> when a replay module is
        /// active; returns <c>default(GlobalTime)</c> with a warning otherwise.
        /// </summary>
        public async Task<GlobalTime> SeekToTimeAsync(long targetWallClockTicks)
        {
            if (_activeReplayModule == null)
            {
                FdpLog<EcsRecordReplayController>.Warn(
                    "[EcsRecordReplayController] SeekToTimeAsync called with no active replay module.");
                return default;
            }
            bool restored = await _activeReplayModule.SeekToWallClockTicksAsync(targetWallClockTicks)
                .ConfigureAwait(false);
            // ⭐ R-4012 — outside this node's recording: nothing restored, no landed time.
            if (!restored) return default;
            // Return the seek target time expressed as the indexing time that
            // ClusterMaster will pass to SnapAndPause.  TotalTime is the inverse
            // conversion (ticks -> seconds) so the master's MasterSyncController
            // stores TotalTime = targetWallClockTicks / Freq.  The SwitchTimeModeEvent
            // then carries SimTimeSnapshot = TotalTime, which ApplyTimeSnap propagates
            // to all slave time controllers.
            return new GlobalTime
            {
                TotalWallTicks = targetWallClockTicks,
                TotalTime      = targetWallClockTicks / (double)Stopwatch.Frequency,
            };
        }

        /// <summary>
        /// No-op in this ECS implementation: frame advancement is driven automatically
        /// by <see cref="Fdp.Toolkit.Replay.PlaybackTickSystem"/> which is registered
        /// by <see cref="ReplayModule"/> into the kernel scheduler.
        /// </summary>
        public void ProcessPlaybackTick(GlobalTime currentTime) { }

        /// <inheritdoc />
        public RecordingBranch? CurrentReplayBranch =>
            _activeReplayModule is { HasRecording: true, CurrentFrame: >= 0 } replay
                ? new RecordingBranch(replay.FilePath, replay.CurrentFrame + 1, replay.RecordedExerciseId, replay.CurrentFrameWallTicks)
                : null;

        /// <inheritdoc />
        public GlobalTime GetCurrentReplayTime() =>
            _activeReplayModule != null ? _kernel.GetTimeController().GetCurrentState() : default;

        // ── IClusterOpHandler (full 2PC dispatch wired in CGF1-S0202) ──────────────────

        /// <inheritdoc />
        /// <remarks>
        /// Returns <c>false</c> for all operations — this class acts as a pure factory
        /// and lifecycle helper; handler dispatch is performed by
        /// <see cref="Handlers.LiveLoadClusterStateHandler"/> and
        /// <see cref="Handlers.ReplayLoadClusterOpHandler"/> which call into this class directly
        /// (CGF1-S0304 / S0305).
        /// </remarks>
        public bool CanHandle(NodeOpType op) => false;

        /// <inheritdoc />
        public Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct)
            => Task.FromResult<string?>(null);

        /// <inheritdoc />
        public void Commit(NodeOpCommand cmd, EntityRepository? repo) { }

        /// <inheritdoc />
        public void Abort(NodeOpCommand cmd, EntityRepository? repo) { }
    }
}
