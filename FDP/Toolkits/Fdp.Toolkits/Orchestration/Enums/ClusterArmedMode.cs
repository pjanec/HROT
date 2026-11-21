namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐⭐ <c>CE-3341</c> (AQ108 D12a, R-3068) — the ARMED RUN MODE: what ▶ runs next, ONE cluster-wide fact held by the master and read by every
    /// host through <c>ClusterView.ArmedMode</c>. The toolkit-level (wire) twin of the UI's <c>RunArmed</c> — same three names; the UI maps by name.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>PERMANENT WIRE VALUES (R-42)</b> — travels as an <c>int</c> on <c>ClusterStateTopic.ArmedMode</c> and as a name in the
    /// <c>SetRunSettings</c> payload. ⭐ <see cref="Live"/> is <b>0 on purpose</b>: a sender that never sets the field (an older peer, a test)
    /// reads as the master's default, Live — the same inverse-polarity trick as <c>RecordLiveRunsOff</c> (CE-4090).
    /// </remarks>
    public enum ClusterArmedMode : int
    {
        /// <summary>The exercise with live players (the default; also what an unset wire field means).</summary>
        Live = 0,
        /// <summary>A dry run of the scenario being edited (nothing recorded, ■ restores).</summary>
        Preview = 1,
        /// <summary>After Action Review — a recording played back.</summary>
        AAR = 2,
    }
}
