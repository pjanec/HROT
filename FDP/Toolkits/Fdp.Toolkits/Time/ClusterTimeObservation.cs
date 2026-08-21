using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Time.Messages;

namespace Fdp.Toolkit.Time
{
    /// <summary>
    /// <b>The cluster's last time-mode DECISION, as observed on a node's bus (`T7`).</b>
    ///
    /// <para>Two classes folded <see cref="SwitchTimeModeEvent"/> into local fields with the same
    /// four lines — <c>ClusterUiCache.DrainTimeMode</c> and
    /// <c>ClusterTimeTransportAdapter.Update</c>. They serve different roles on different nodes and
    /// are NOT a duplicate surface, but the fold itself was duplicate CODE. This is the one
    /// implementation of it.</para>
    ///
    /// <para><b>It is NOT "is the local clock paused", and the name says so deliberately.</b> The
    /// distinction is measured, not stylistic:</para>
    ///
    /// <list type="bullet">
    /// <item><description><b>On the master it is PROMPT.</b> The Deterministic event is published at
    /// the top of <c>SwitchToDeterministic</c>, and <c>UpdateBarrierPending</c> stops advancing
    /// <c>_totalTime</c> from that same instant — so this flag and the master's frozen sim time turn
    /// over together. <c>GetMode()</c> is the LATE reading here: it answers <c>Continuous</c> for the
    /// whole lookahead window (200 ms by default).</description></item>
    /// <item><description><b>On a slave it RUNS AHEAD of the local clock.</b>
    /// <c>SlaveSyncController</c> defers a future-barrier event and applies it when the barrier
    /// lands; this fold applies it on arrival. For the lookahead window the node's own time is still
    /// advancing while this reads <c>true</c> — correctly, because the cluster timeline it reports is
    /// the one the slave is about to snap to.</description></item>
    /// </list>
    ///
    /// <para>⇒ ask this for <i>"what did the cluster decide"</i>. Ask <c>ISimClock.IsAdvancing</c> or
    /// <see cref="HaltReasonResolver"/> for <i>"is MY time moving, and why not"</i>. Conflating the
    /// two is how this area grew a dozen disagreeing <c>IsPaused</c> flags.</para>
    /// </summary>
    public sealed class ClusterTimeObservation
    {
        /// <summary>
        /// The cluster's last pause decision: <c>true</c> once a Deterministic switch has been seen,
        /// <c>false</c> again on Continuous. See the class remarks — this is a decision, not a
        /// local clock reading.
        /// </summary>
        public bool PauseRequested { get; private set; }

        /// <summary>
        /// Last non-zero time scale carried by a mode event. A mode event with a zero scale is not a
        /// scale change (Continuous events carry <c>FixedDelta = 0</c>, not a scale), so it is
        /// ignored rather than latched as a stop.
        /// </summary>
        public float TimeScale { get; private set; } = 1f;

        /// <summary>
        /// Last non-zero barrier anchor. On a Deterministic event this is the future barrier; on a
        /// Continuous one it is the master's real-clock resume anchor.
        /// </summary>
        public long BarrierWallTicks { get; private set; }

        /// <summary>
        /// Sim time carried by the last RESUME. Deliberately not updated on pause: the paused value
        /// is already displayed from whatever the node was showing, and overwriting it here would
        /// make a display jump backwards by the barrier window on every pause.
        /// </summary>
        public double ResumeSimTime { get; private set; }

        /// <summary>Folds one mode event. Call once per event, in bus order.</summary>
        public void Apply(SwitchTimeModeEvent ev)
        {
            bool deterministic = ev.TargetMode == TimeMode.Deterministic;

            PauseRequested = deterministic;

            if (ev.TimeScale > 0f)
                TimeScale = ev.TimeScale;

            if (ev.BarrierWallTicks > 0)
                BarrierWallTicks = ev.BarrierWallTicks;

            if (!deterministic && ev.SimTimeSnapshot > 0.0)
                ResumeSimTime = ev.SimTimeSnapshot;
        }
    }
}
