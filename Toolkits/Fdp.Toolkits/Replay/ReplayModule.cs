using System;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Time;

namespace Fdp.Toolkit.Replay
{
    /// <summary>
    /// Data-plane module that strictly owns one <see cref="PlaybackController"/> and the
    /// <see cref="PlaybackTickSystem"/> that drives continuous frame-by-frame playback.
    /// <para>
    /// Lifecycle: <see cref="RegisterSystems"/> opens the <c>.fdp</c> file and validates
    /// the schema manifest via <see cref="SchemaValidator"/>.  An
    /// <see cref="System.IO.InvalidDataException"/> is thrown in the constructor if the
    /// file is malformed or the schema has drifted.
    /// </para>
    /// <para>
    /// Heavy, orchestrated seeks (SysOp-coordinated <c>ReplaySeek</c>) are performed via
    /// <see cref="SeekToFrameAsync"/> which wraps <see cref="PlaybackController.SeekToFrame"/>
    /// in a background <see cref="Task"/> so callers can fan-out multiple seeks and
    /// await them with <c>Task.WhenAll</c>.
    /// </para>
    /// </summary>
    public sealed class ReplayModule : IEcsModule, IDisposable
    {
        private readonly string _filePath;
        private readonly EntityRepository _repo;
        private readonly ITimeController _timeController;
        private readonly Action? _afterSeek;
        private PlaybackController? _playback;
        private PlaybackTickSystem? _tickSystem;

        /// <inheritdoc/>
        public string Name => "Replay";

        /// <inheritdoc/>
        /// <remarks>
        /// Synchronous policy so <see cref="PlaybackTickSystem"/> runs on the main
        /// thread and writes directly into the live <see cref="EntityRepository"/>.
        /// </remarks>
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        /// <summary>
        /// Creates a replay module.  The <c>.fdp</c> file is not opened until
        /// <see cref="RegisterSystems"/> is called (lazy open inside the kernel's
        /// install barrier).
        /// </summary>
        /// <param name="filePath">Absolute path to the <c>.fdp</c> recording file.</param>
        /// <param name="repo">
        /// Live <see cref="EntityRepository"/> that playback data will be blasted into.
        /// Used by <see cref="SeekToFrameAsync"/> which runs off the main thread.
        /// </param>
        /// <param name="timeController">
        /// Active time controller from the kernel.  <see cref="PlaybackTickSystem"/> reads
        /// <c>TotalTime</c> from this on every tick and converts it to wall-clock ticks via
        /// <c>TimeSpan.TicksPerSecond</c> to drive the pull-model cursor.
        /// The time controller is seeded by the cluster master (via <c>SnapAndPause</c>)
        /// after each seek, re-anchoring the indexing cursor to the new position.
        /// </param>
        public ReplayModule(string filePath, EntityRepository repo, ITimeController timeController, Action? afterSeek = null)
        {
            _filePath        = filePath        ?? throw new ArgumentNullException(nameof(filePath));
            _repo            = repo            ?? throw new ArgumentNullException(nameof(repo));
            _timeController  = timeController  ?? throw new ArgumentNullException(nameof(timeController));
            _afterSeek       = afterSeek;
        }

        /// <inheritdoc/>
        /// <exception cref="System.IO.InvalidDataException">
        /// Thrown if the file format is invalid or the component schema has drifted since
        /// recording.  Thrown synchronously so the kernel's install barrier surfaces the
        /// error before any tick system is registered.
        /// </exception>
        public void RegisterSystems(ISystemRegistry registry)
        {
            // PlaybackController ctor validates magic bytes and schema via SchemaValidator.
            _playback   = new PlaybackController(_filePath);
            _tickSystem = new PlaybackTickSystem(_playback, _timeController, _afterSeek);
            registry.RegisterSystem(_tickSystem);
        }

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float deltaTime) { /* driven by PlaybackTickSystem */ }


        /// <summary>
        /// Main-thread synchronous seek.  Delegates to
        /// <see cref="PlaybackController.SeekToFrame"/> synchronously to guarantee ECS
        /// memory safety. The async interface contract is satisfied by returning
        /// a completed task.
        /// Must not be called before <see cref="RegisterSystems"/>.
        /// </summary>
        /// <param name="targetFrameIndex">Zero-based frame index within the recording.</param>
        public Task SeekToFrameAsync(int targetFrameIndex)
        {
            if (_playback == null)
                throw new InvalidOperationException(
                    "ReplayModule.RegisterSystems() must be called before SeekToFrameAsync.");

            // Execute strictly on the calling (main) thread to prevent ECS memory corruption.
            // The ECS repository is single-threaded for structural changes; seeking must not
            // race with UI rendering or other main-thread operations.
            _playback.SeekToFrame(_repo, targetFrameIndex);
            Fdp.Toolkit.Replication.Utilities.SmartEgressUtil.ForceMarkAllDirty(_repo);
            _afterSeek?.Invoke();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Main-thread synchronous wall-clock seek.  Delegates to
        /// <see cref="PlaybackController.SeekToWallClockTicks"/> synchronously to guarantee
        /// ECS memory safety. The async interface contract is satisfied by returning
        /// a completed task.
        /// Must not be called before <see cref="RegisterSystems"/>.
        /// </summary>
        /// <param name="targetWallTicks">
        /// UTC wall-clock ticks (<see cref="System.DateTime.UtcNow"/>.Ticks scale).
        /// The controller floor-seeks to the frame whose wall timestamp is ≤ this value.
        /// </param>
        public Task SeekToWallClockTicksAsync(long targetWallTicks)
        {
            if (_playback == null)
                throw new InvalidOperationException(
                    "ReplayModule.RegisterSystems() must be called before SeekToWallClockTicksAsync.");

            // Execute strictly on the calling (main) thread to prevent ECS memory corruption.
            // The ECS repository is single-threaded for structural changes; seeking must not
            // race with UI rendering or other main-thread operations.
            _playback.SeekToWallClockTicks(_repo, targetWallTicks);
            Fdp.Toolkit.Replication.Utilities.SmartEgressUtil.ForceMarkAllDirty(_repo);
            _afterSeek?.Invoke();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Number of frames in the recording.
        /// Returns 0 if <see cref="RegisterSystems"/> has not been called yet.
        /// </summary>
        public int TotalFrames => _playback?.TotalFrames ?? 0;

        /// <summary>
        /// Wall-clock duration of the recording, as stored in
        /// <see cref="Fdp.Core.FlightRecorder.Metadata.RecordingMetadata.Duration"/>.
        /// Returns <see cref="TimeSpan.Zero"/> if <see cref="RegisterSystems"/> has not been
        /// called yet or if the recording pre-dates duration support.
        /// </summary>
        public TimeSpan Duration => _playback?.Metadata.Duration ?? TimeSpan.Zero;

        /// <summary>
        /// The maximum network entity ID present in the recording, as reported by
        /// the recording session's <c>AsyncRecorder</c>.
        /// Returns <c>0</c> if the recording pre-dates <c>MaxNetworkId</c> support,
        /// if <see cref="RegisterSystems"/> has not been called, or if the
        /// <c>.meta.json</c> file was absent at open time.
        /// Used by <c>ReplayLoadClusterStateHandler</c> to populate <c>NodeOpStatus.ResultJson</c>
        /// so the orchestrator can reset the ID allocator above the replay's ID space
        /// (CGF1-S0304).
        /// </summary>
        public long MaxNetworkId => _playback?.Metadata.MaxNetworkId ?? 0;

        /// <summary>
        /// Starting wall-clock ticks of the recording timeline.
        /// Uses frame-0 wall ticks when frames are indexed; falls back to the
        /// file header timestamp for empty recordings.
        /// Returns <c>0</c> if <see cref="RegisterSystems"/> has not been called yet.
        /// </summary>
        public long RecordingStartWallTicks =>
            _playback == null ? 0 :
            _playback.TotalFrames > 0
                ? _playback.GetFrameMetadata(0).WallClockTicks
                : _playback.RecordingTimestamp;

        /// <summary>ACID-safe dispose: closes the <c>PlaybackController</c> file handles.</summary>
        public void Dispose()
        {
            _playback?.Dispose();
            _playback  = null;
            _tickSystem = null;
        }
    }
}
