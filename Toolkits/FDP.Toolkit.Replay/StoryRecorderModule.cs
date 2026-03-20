using System;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Replay
{
    /// <summary>
    /// Data-plane module for per-story recording.  Functions identically to
    /// <see cref="RecordingModule"/> but is constructed with an entity filter that
    /// restricts the <c>AsyncRecorder</c> to entities tagged with a specific story.
    /// <para>
    /// Multiple <see cref="StoryRecorderModule"/> instances may run concurrently
    /// alongside the global <see cref="RecordingModule"/>: each owns a distinct
    /// <c>AsyncRecorder</c>, a distinct LZ4 background worker, and a distinct file
    /// stream — no shared I/O bottleneck, no mutex.
    /// </para>
    /// <para>
    /// Uninstalling a specific <see cref="StoryRecorderModule"/> at
    /// <c>StopStory</c> flushes its buffers and closes its file handles without
    /// affecting any other running recorder module.
    /// </para>
    /// </summary>
    public sealed class StoryRecorderModule : IEcsModule, IDisposable
    {
        private readonly RecordingModule _inner;

        /// <inheritdoc/>
        public string Name => $"StoryRecording_{_config.DrillId:N}";

        /// <inheritdoc/>
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly RecordingConfiguration _config;

        /// <summary>
        /// Creates a story recorder module.  The <paramref name="config"/> must
        /// include an <see cref="RecordingConfiguration.EntityFilter"/> predicate that
        /// selects only entities whose <see cref="StoryTag.StoryId"/> matches the
        /// target story — typically constructed by the caller before passing in.
        /// </summary>
        public StoryRecorderModule(RecordingConfiguration config)
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
