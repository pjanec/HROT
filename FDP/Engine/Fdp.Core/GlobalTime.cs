using System.Runtime.InteropServices;

namespace Fdp.Core
{
    /// <summary>
    /// Singleton descriptor for simulation time state.
    /// Pushed into ECS world every frame.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.GlobalTime)]
    public struct GlobalTime
    {
        /// <summary>
        /// Total elapsed simulation time (seconds).
        /// Affected by TimeScale and pausing.
        /// </summary>
        public double TotalTime;

        /// <summary>
        /// Time elapsed since last frame (seconds).
        /// Used for physics integration (pos += vel * DeltaTime).
        /// </summary>
        public float DeltaTime;

        /// <summary>
        /// Speed multiplier.
        /// 0.0 = Paused, 1.0 = Realtime, 2.0 = 2x speed.
        /// </summary>
        public float TimeScale;

        /// <summary>
        /// Current frame number (increments every frame regardless of pause).
        /// Replaces legacy FrameCount.
        /// </summary>
        public long FrameNumber;

        /// <summary>
        /// Wall clock time when simulation started (UTC ticks).
        /// </summary>
        public long StartWallTicks;

        /// <summary>
        /// The unscaled, real-world time elapsed (in seconds).
        /// Useful for UI or inputs that shouldn't slow down with slo-mo.
        /// </summary>
        public float UnscaledDeltaTime;

        /// <summary>
        /// Total real-world time elapsed (in seconds).
        /// </summary>
        public double UnscaledTotalTime;

        /// <summary>
        /// Stable, frame-locked wall-clock ticks for the current simulation frame (UTC ticks).
        /// This is the single source of truth for wall-clock time within a frame.
        /// Populated once at the start of each frame by the time controller (Master: Stopwatch
        /// accumulator; Slave: PLL virtual clock). Every system in the frame — including the
        /// flight recorder — reads this field instead of calling DateTime.UtcNow directly,
        /// guaranteeing a constant timestamp across all PostSimulation systems.
        /// </summary>
        public long TotalWallTicks;

        /// <summary>
        /// True while simulation time is moving forward on this frame.
        ///
        /// <para>THE predicate for "is the simulation running", and the reason it is
        /// <c>DeltaTime</c> and not <c>TimeScale</c>: a pause is issued as
        /// <c>PauseTimeIntent</c> → <c>SwitchToDeterministic</c> → <c>MasterMode.Stepping</c>, and
        /// <c>MasterSyncController.UpdateStepping</c> rebuilds the clock with <c>TimeScale</c>
        /// UNCHANGED. Nothing on any pause path ever sets <c>TimeScale</c> to zero, so
        /// <see cref="IsPaused"/> is false while the simulation is paused. Writing
        /// <c>IsAdvancing =&gt; !IsPaused</c> would ship that same dead flag under a better name.</para>
        ///
        /// <para>Read it from the singleton the kernel pushed THIS frame — the instance a time
        /// controller hands back from <c>GetCurrentState()</c> carries a zero delta forever and so
        /// reports "halted" always.</para>
        /// </summary>
        public bool IsAdvancing => DeltaTime > 0.0f;

        /// <summary>The complement of <see cref="IsAdvancing"/>: time is not moving this frame.</summary>
        public bool IsHalted => !IsAdvancing;

        /// <summary>
        /// Convenience flag (TimeScale == 0.0).
        /// </summary>
        [System.Obsolete(
            "GlobalTime.IsPaused is TimeScale == 0, and no pause path ever sets TimeScale to zero, " +
            "so it is FALSE while the simulation is paused. It has never had a production reader. " +
            "Use IsAdvancing / IsHalted instead — they read DeltaTime, which a pause does change. " +
            "Kept because TimeScale == 0 is still a legitimate question to ask about the SCALE.")]
        public bool IsPaused => TimeScale == 0.0f;
    }
}
