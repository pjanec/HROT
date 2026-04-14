using System;
using System.Threading.Tasks;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;
using Fdp.ModuleHost.Abstractions;

namespace FDP.Toolkit.Replay
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
        public ReplayModule(string filePath, EntityRepository repo)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _repo     = repo     ?? throw new ArgumentNullException(nameof(repo));
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
            _tickSystem = new PlaybackTickSystem(_playback);
            registry.RegisterSystem(_tickSystem);
        }

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float deltaTime) { /* driven by PlaybackTickSystem */ }

        /// <summary>
        /// Sets the number of extra frames to advance in the next tick beyond the default
        /// of 1.  Use for fast-forward (TimeScale &gt; 1).  Resets automatically after
        /// the next <see cref="PlaybackTickSystem.Execute"/> call.
        /// </summary>
        public void SetExtraFramesThisTick(int extraFrames)
        {
            if (_tickSystem != null)
                _tickSystem.ExtraFramesThisTick = extraFrames;
        }

        /// <summary>
        /// Off-main-thread heavy seek.  Delegates to
        /// <see cref="PlaybackController.SeekToFrame"/> inside a background
        /// <see cref="Task"/> so the caller can fan-out via <c>Task.WhenAll</c>.
        /// Must not be called before <see cref="RegisterSystems"/>.
        /// </summary>
        /// <param name="targetFrameIndex">Zero-based frame index within the recording.</param>
        public Task SeekToFrameAsync(int targetFrameIndex)
        {
            if (_playback == null)
                throw new InvalidOperationException(
                    "ReplayModule.RegisterSystems() must be called before SeekToFrameAsync.");
            return Task.Run(() => _playback.SeekToFrame(_repo, targetFrameIndex));
        }

        /// <summary>
        /// Off-main-thread wall-clock seek.  Delegates to
        /// <see cref="PlaybackController.SeekToWallClockTicks"/> so the caller can
        /// await completion before branching to live (CGF1-S0305).
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
            return Task.Run(() => _playback.SeekToWallClockTicks(_repo, targetWallTicks));
        }

        /// <summary>
        /// Number of frames in the recording.
        /// Returns 0 if <see cref="RegisterSystems"/> has not been called yet.
        /// </summary>
        public int TotalFrames => _playback?.TotalFrames ?? 0;

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

        /// <summary>ACID-safe dispose: closes the <c>PlaybackController</c> file handles.</summary>
        public void Dispose()
        {
            _playback?.Dispose();
            _playback  = null;
            _tickSystem = null;
        }
    }
}
