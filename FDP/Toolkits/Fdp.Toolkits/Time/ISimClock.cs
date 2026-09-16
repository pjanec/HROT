namespace Fdp.Toolkit.Time
{
    /// <summary>
    /// The ONE named read surface for simulation time (`T1`).
    ///
    /// <para>Everything that wants to know what the clock is doing asks this, rather than reaching
    /// for one of the several dozen unrelated <c>IsPaused</c> flags scattered across debug sessions,
    /// breakpoint managers and UI caches. Those exist because each of them needed to answer "why is
    /// it stopped" and a bare bool could not; collapsing them is `T5`, one site at a time.</para>
    ///
    /// <para><b>This is a READ of the one source, not a new source.</b> It derives every answer from
    /// the <c>GlobalTime</c> singleton the kernel pushed this frame and latches nothing — adding a
    /// thirteenth cached notion of "paused" is the thing the design explicitly forbids.</para>
    ///
    /// <para>`HaltReason` — <i>why</i> it is stopped rather than <i>that</i> it is stopped — is
    /// deliberately absent: it needs the replay-preparation state exposed first (`AS-10`) and is
    /// scheduled as `T6`. A stubbed reason that always answered "Running" would be worse than none.</para>
    /// </summary>
    public interface ISimClock
    {
        /// <summary>
        /// True while simulation time is moving forward this frame (<c>DeltaTime &gt; 0</c>).
        /// <b>Not</b> the negation of <c>GlobalTime.IsPaused</c>, which is false while paused.
        /// </summary>
        bool IsAdvancing { get; }

        /// <summary>The complement of <see cref="IsAdvancing"/>.</summary>
        bool IsHalted { get; }

        /// <summary>Total elapsed simulation time in seconds.</summary>
        double TotalTime { get; }

        /// <summary>Speed multiplier. 1.0 = realtime. Independent of whether time is advancing.</summary>
        float TimeScale { get; }

        /// <summary>Current frame number. Increments even while halted.</summary>
        long FrameNumber { get; }
    }
}
