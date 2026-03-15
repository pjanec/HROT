using System;
using Fdp.Kernel;

namespace FDP.Toolkit.Replay
{
    /// <summary>
    /// Initialization contract for <see cref="RecordingModule"/> and
    /// <see cref="StoryRecorderModule"/>.  Passed at construction time so that
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
        /// For story recorders, inject a predicate that checks
        /// <c>StoryTag.StoryId == targetStoryId</c> so only the story's own
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
        /// Drill or Story identifier embedded in the recording header metadata.
        /// </summary>
        public required Guid DrillId { get; init; }
    }
}
