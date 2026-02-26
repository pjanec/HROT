using System.Runtime.InteropServices;

namespace Fdp.Kernel
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
        /// Convenience flag (TimeScale == 0.0).
        /// </summary>
        public bool IsPaused => TimeScale == 0.0f;
    }
}
