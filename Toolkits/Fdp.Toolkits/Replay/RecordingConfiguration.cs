using System;
using Fdp.Core;

namespace Fdp.Toolkit.Replay
{
    /// <summary>
    /// Initialization contract for <see cref="RecordingModule"/> and
    /// <see cref="EpisodeRecorderModule"/>.  Passed at construction time so that
    /// <c>AsyncRecorder</c> is decoupled from any global state.
    /// </summary>
    public sealed class RecordingConfiguration
    {
        /// <summary>
        /// Absolute path for the <c>.fdp</c> output file.
        /// </summary>
        public required string FilePath { get; init; }

        /// <summary>
        /// Optional entity filter predicate.  When <c>null</c>, all entities
        /// above <c>AsyncRecorder.MinRecordableId</c> are recorded.
        /// <para>
        /// For episode recorders, inject a predicate that checks
        /// <c>EpisodeTag.EpisodeId == targetEpisodeId</c> so only the episode's own
        /// entities enter that recorder's <c>AsyncRecorder</c>.
        /// </para>
        /// <para>
        /// The predicate receives the full <see cref="Entity"/> handle
        /// (index + generation) so callers can validate the generation and
        /// avoid filtering against recycled slots.
        /// </para>
        /// </summary>
        public Predicate<Entity>? EntityFilter { get; init; }

        /// <summary>
        /// Exercise or Episode identifier embedded in the recording header metadata.
        /// </summary>
        public required Guid ExerciseId { get; init; }

        /// <summary>
        /// When <c>true</c>, <see cref="RecorderTickSystem"/> calls
        /// <c>AsyncRecorder.CaptureFrame(blocking: true)</c> on every frame, blocking the
        /// caller until the front-buffer swap completes.  This prevents delta-frame drops
        /// in CPU-bound tight loops (e.g. scenario-level deterministic recording) at the
        /// cost of main-thread stalls.
        /// <para>
        /// Production default is <c>false</c> (non-blocking).  Set to <c>true</c> only for
        /// offline tooling, test scenarios, or any pipeline that cannot tolerate frame drops
        /// — the caller must ensure the main thread can tolerate the stall.
        /// </para>
        /// </summary>
        public bool Blocking { get; init; } = false;
    }
}
