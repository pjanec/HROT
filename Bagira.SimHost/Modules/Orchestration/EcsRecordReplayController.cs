using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration;
using Fdp.Kernel;
using Fdp.Kernel.Orchestration;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replay;
using ModuleHost.Core;

namespace Bagira.SimHost.Modules.Orchestration
{
    /// <summary>
    /// Control-plane factory and orchestrator for ECS recording and replay.
    /// Implements <see cref="IDsmHandler"/> for legacy 2PC dispatch stubs and
    /// <see cref="IRecordReplayController"/> for the canonical recording/replay
    /// lifecycle contract shared across subsystems (CGF1-S0304).
    ///
    /// <para>
    /// This class is a <em>pure factory</em>: it constructs typed
    /// <see cref="RecordingModule"/> / <see cref="StoryRecorderModule"/> /
    /// <see cref="ReplayModule"/> objects with the correct
    /// <see cref="RecordingConfiguration"/> context and routes their lifecycle
    /// through <see cref="ModuleHostKernel"/>.  It never directly owns an
    /// <c>AsyncRecorder</c> or <c>PlaybackController</c>.
    /// </para>
    ///
    /// <para>
    /// The <see cref="IDsmHandler"/> stubs (<see cref="CanHandle"/> always returns
    /// <c>false</c>) remain in place so <see cref="DrillSlave"/> can still hold a
    /// reference; actual DSM handling is delegated to
    /// <see cref="Handlers.LiveLoadDsmHandler"/> and
    /// <see cref="Handlers.ReplayLoadDsmHandler"/> which call the methods on this
    /// class (CGF1-S0304 / S0305).
    /// </para>
    /// </summary>
    public sealed class EcsRecordReplayController : IDsmHandler, IRecordReplayController
    {
        private readonly ModuleHostKernel _kernel;
        private readonly int              _nodeId;
        private readonly EntityRepository _repo;

        private RecordingModule?                        _activeRecordingModule;
        private ReplayModule?                           _activeReplayModule;
        private readonly Dictionary<Guid, StoryRecorderModule> _storyModules = new();

        /// <summary>Module installed by the most recent <see cref="PrepareRecordingAsync"/> call.</summary>
        public RecordingModule? ActiveRecordingModule => _activeRecordingModule;

        /// <summary>Module installed by the most recent <see cref="PrepareReplayAsync"/> call.</summary>
        public ReplayModule? ActiveReplayModule => _activeReplayModule;

        /// <param name="kernel">Kernel that manages module topology.</param>
        /// <param name="nodeId">Local node identifier embedded in recording file names.</param>
        /// <param name="repo">
        /// Live <see cref="EntityRepository"/> — passed to <see cref="ReplayModule"/> for
        /// off-main-thread seeks.
        /// </param>
        public EcsRecordReplayController(ModuleHostKernel kernel, int nodeId, EntityRepository repo)
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _repo   = repo   ?? throw new ArgumentNullException(nameof(repo));
            _nodeId = nodeId;
        }

        // ── Global recording ─────────────────────────────────────────────────────

        /// <summary>
        /// Opens a new recording for the given drill.  Installs a
        /// <see cref="RecordingModule"/> via <see cref="ModuleHostKernel"/>;
        /// after this call the <see cref="RecorderTickSystem"/> is live in the
        /// topological graph and every frame is captured.
        /// </summary>
        public async Task PrepareRecordingAsync(Guid drillId, string storageDirectory)
        {
            var filePath = $"{storageDirectory}/{drillId}/node_{_nodeId}.fdp";
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var config = new RecordingConfiguration
            {
                FilePath     = filePath,
                EntityFilter = null,   // record all entities above MinRecordableId
                DrillId      = drillId,
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
                    "[SimHost] FinalizeRecordingAsync called but no active recording module exists " +
                    "(possible ordering violation: FinalizeLive without a preceding PrepareLive).");
                return;
            }
            _activeRecordingModule.SetMaxNetworkId(maxNetworkId);
            await _kernel.UninstallModuleAsync(_activeRecordingModule);
            _activeRecordingModule = null;
        }

        // ── Story recording ───────────────────────────────────────────────────────

        /// <summary>
        /// Starts a per-story recording filtered to entities with
        /// <see cref="StoryTag.StoryId"/> == <paramref name="storyId"/>.
        /// Multiple stories may be recorded concurrently without sharing I/O.
        /// </summary>
        public async Task StartStoryRecordingAsync(Guid storyId, string storageDirectory)
        {
            var filePath = $"{storageDirectory}/stories/{storyId}_node{_nodeId}.fdp";
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var config = new RecordingConfiguration
            {
                FilePath     = filePath,
                EntityFilter = BuildStoryFilter(storyId),
                DrillId      = storyId,
            };
            var module = new StoryRecorderModule(config);
            _storyModules[storyId] = module;
            await _kernel.InstallModuleAsync(module);
        }

        /// <summary>
        /// Stops and finalizes a per-story recording.  Only the named story's
        /// buffers are flushed; other concurrent story recorders are unaffected.
        /// </summary>
        public async Task StopStoryRecordingAsync(Guid storyId)
        {
            if (_storyModules.Remove(storyId, out var module))
                await _kernel.UninstallModuleAsync(module);
        }

        // ── Replay ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Installs a <see cref="ReplayModule"/> for the given drill recording.
        /// Schema validation runs during installation; throws
        /// <see cref="System.IO.InvalidDataException"/> if the schema has drifted.
        /// </summary>
        public async Task PrepareReplayAsync(Guid drillId, string storageDirectory)
        {
            var filePath = $"{storageDirectory}/{drillId}/node_{_nodeId}.fdp";
            _activeReplayModule = new ReplayModule(filePath, _repo);
            await _kernel.InstallModuleAsync(_activeReplayModule);
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
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds an entity filter predicate that accepts only entities whose
        /// <see cref="StoryTag"/> component matches <paramref name="storyId"/>.
        /// The predicate uses the live <see cref="_repo"/> to read components.
        /// </summary>
        private Predicate<Entity> BuildStoryFilter(Guid storyId) =>
            entity =>
                _repo.HasComponent<StoryTag>(entity) &&
                _repo.GetComponentRO<StoryTag>(entity).StoryId == storyId;

        // ── IRecordReplayController: seek / tick ─────────────────────────────────

        /// <summary>
        /// Off-main-thread wall-clock seek.  Delegates to
        /// <see cref="ReplayModule.SeekToWallClockTicksAsync"/> when a replay module is
        /// active; returns <c>Task.CompletedTask</c> otherwise.
        /// </summary>
        public Task SeekToTimeAsync(long targetWallClockTicks) =>
            _activeReplayModule?.SeekToWallClockTicksAsync(targetWallClockTicks)
                ?? Task.CompletedTask;

        /// <summary>
        /// No-op in this ECS implementation: frame advancement is driven automatically
        /// by <see cref="FDP.Toolkit.Replay.PlaybackTickSystem"/> which is registered
        /// by <see cref="ReplayModule"/> into the kernel scheduler.
        /// </summary>
        public void ProcessPlaybackTick(GlobalTime currentTime) { }

        // ── IDsmHandler (full 2PC dispatch wired in CGF1-S0202) ──────────────────

        /// <inheritdoc />
        /// <remarks>
        /// Returns <c>false</c> for all operations — this class acts as a pure factory
        /// and lifecycle helper; handler dispatch is performed by
        /// <see cref="Handlers.LiveLoadDsmHandler"/> and
        /// <see cref="Handlers.ReplayLoadDsmHandler"/> which call into this class directly
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
