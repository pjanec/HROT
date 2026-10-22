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
        /// Node identifier embedded in the recording metadata.
        /// Identifies which distributed node produced this recording.
        /// </summary>
        public required int NodeId { get; init; }

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

        /// <summary>
        /// ⭐ <c>CE-3299</c> B4 / <c>D18</c> — when set, this recording is a BRANCH: its first frames are copied from the parent up
        /// to the branch point, then it records on (null: a recording that starts empty).
        /// </summary>
        public Fdp.Core.FlightRecorder.RecordingBranch? Branch { get; init; }

        /// <summary>
        /// ⭐ CE-3299 D17 (R-3045) — 0: FULL (a keyframe every 60 frames and a delta every other frame). N &gt; 0: CHECKPOINTING — a keyframe every N
        /// seconds of SIM time (and at every named checkpoint), nothing else: restore points, no smooth replay. A branch records in its
        /// parent's mode, whatever this says.
        /// </summary>
        public double CheckpointingSeconds { get; init; }
    }
}
