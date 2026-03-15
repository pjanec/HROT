using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Fdp.Kernel;
using FDP.Toolkit.Replay;
using ModuleHost.Core;

namespace Bagira.SimHost.Modules.Orchestration
{
    /// <summary>
    /// Control-plane factory and orchestrator for ECS recording and replay.
    /// Implements <see cref="IDsmHandler"/> and is registered with
    /// <see cref="DrillSlave"/> so that DSM commands can be dispatched to it.
    /// <para>
    /// This class is a <em>pure factory</em>: it constructs typed
    /// <see cref="RecordingModule"/> / <see cref="StoryRecorderModule"/> /
    /// <see cref="ReplayModule"/> objects with the correct
    /// <see cref="RecordingConfiguration"/> context and routes their lifecycle
    /// through <see cref="ModuleHostKernel"/>.  It never directly owns an
    /// <c>AsyncRecorder</c> or <c>PlaybackController</c>.
    /// </para>
    /// </summary>
    public sealed class EcsRecordReplayController : IDsmHandler
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
        /// Finalizes the active recording.  Uninstalls the <see cref="RecordingModule"/>
        /// which triggers its blocking <see cref="RecordingModule.Dispose"/>:
        /// flushes LZ4 buffers, writes <c>MaxNetworkId</c> and <c>.meta.json</c>.
        /// </summary>
        public async Task FinalizeRecordingAsync()
        {
            if (_activeRecordingModule == null) return;
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
    }
}
