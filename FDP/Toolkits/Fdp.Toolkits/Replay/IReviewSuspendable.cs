namespace Fdp.Toolkit.Replay
{
    /// <summary>
    /// ⭐ <c>CE-3329</c> R8, row 44 — a live recording a During Action Review suspends and resumes: the node's main recording
    /// (<see cref="RecordingModule"/>) and every episode recorder (<see cref="EpisodeRecorderModule"/>). ⭐ One seam, so a review writes
    /// nothing into ANY live history — ⛔ the review used to suspend only the main one.
    /// </summary>
    public interface IReviewSuspendable
    {
        /// <summary>Nothing written from now on; the file stays open, flushed. A <paramref name="label"/> notes the review in the meta.</summary>
        void SuspendForReview(string? label = null, double atSimSeconds = 0);

        /// <summary>Resume the SAME recording once the clock stands at <paramref name="atSimSeconds"/> (a keyframe first).</summary>
        void ResumeAfterReview(double? atSimSeconds = null);
    }
}
