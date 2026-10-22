using System;

namespace Fdp.Core.FlightRecorder
{
    /// <summary>
    /// ⭐ <c>CE-3299</c> B4 / <c>D18</c> (R-3047) — where a NEW recording branches off an old one: its first <see cref="FrameCount"/>
    /// frames are a byte copy of <see cref="SourcePath"/>'s first frames, then it records on. ⇒ every exercise replays straight through
    /// its branch point on its own, and the old recording is never written again. Used by both branches: a checkpoint restore (the
    /// live recording up to the mark) and a replay going live (the replayed recording up to the frame on screen).
    /// 📄 docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md §1h.
    /// </summary>
    /// <param name="SourcePath">The parent recording (<c>.fdp</c>).</param>
    /// <param name="FrameCount">How many of its frames to copy — the branch point's frame index + 1.</param>
    /// <param name="ParentExerciseId">The parent's exercise (provenance in the new <c>.meta.json</c>).</param>
    /// <param name="BranchWallTicks">The wall time of the branch point (provenance).</param>
    public sealed record RecordingBranch(string SourcePath, int FrameCount, Guid ParentExerciseId, long BranchWallTicks);
}
