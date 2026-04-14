using System;
using Fdp.Kernel;
using Fdp.ModuleHost_Core.Abstractions;

namespace FDP.Toolkit.Replay
{
    /// <summary>
    /// Data-plane module for per-episode recording.  Functions identically to
    /// <see cref="RecordingModule"/> but is constructed with an entity filter that
    /// restricts the <c>AsyncRecorder</c> to entities tagged with a specific episode.
    /// <para>
    /// Multiple <see cref="EpisodeRecorderModule"/> instances may run concurrently
    /// alongside the global <see cref="RecordingModule"/>: each owns a distinct
    /// <c>AsyncRecorder</c>, a distinct LZ4 background worker, and a distinct file
    /// stream — no shared I/O bottleneck, no mutex.
    /// </para>
    /// <para>
    /// Uninstalling a specific <see cref="EpisodeRecorderModule"/> at
    /// <c>StopEpisode</c> flushes its buffers and closes its file handles without
    /// affecting any other running recorder module.
    /// </para>
    /// </summary>
    public sealed class EpisodeRecorderModule : IEcsModule, IDisposable
    {
        private readonly RecordingModule _inner;

        /// <inheritdoc/>
        public string Name => $"EpisodeRecording_{_config.ExerciseId:N}";

        /// <inheritdoc/>
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly RecordingConfiguration _config;

        /// <summary>
        /// Creates a episode recorder module.  The <paramref name="config"/> must
        /// include an <see cref="RecordingConfiguration.EntityFilter"/> predicate that
        /// selects only entities whose <see cref="EpisodeTag.EpisodeId"/> matches the
        /// target episode — typically constructed by the caller before passing in.
        /// </summary>
        public EpisodeRecorderModule(RecordingConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _inner  = new RecordingModule(config);
        }

        /// <inheritdoc/>
        public void RegisterSystems(ISystemRegistry registry) => _inner.RegisterSystems(registry);

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float deltaTime) { /* driven by inner RecordingModule */ }

        /// <inheritdoc cref="RecordingModule.Dispose"/>
        public void Dispose() => _inner.Dispose();
    }
}
