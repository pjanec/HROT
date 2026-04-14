using System.Diagnostics;

namespace Fdp.Toolkit.Time.Controllers
{
    /// <summary>
    /// Configuration for time controllers.
    /// </summary>
    public class TimeConfig
    {
        public static TimeConfig Default => new();
        
        /// <summary>
        /// PLL gain for slave synchronization (0.0 - 1.0).
        /// Higher = faster convergence, lower = smoother.
        /// </summary>
        public double PLLGain { get; set; } = 0.1;
        
        /// <summary>
        /// Maximum frequency deviation for PLL (±5% default).
        /// Prevents physics instability from aggressive corrections.
        /// </summary>
        public double MaxSlew { get; set; } = 0.05;
        
        /// <summary>
        /// Error threshold triggering hard snap (milliseconds).
        /// </summary>
        public double SnapThresholdMs { get; set; } = 500.0;
        
        /// <summary>
        /// Number of samples for jitter filtering.
        /// </summary>
        public int JitterWindowSize { get; set; } = 5;
        
        /// <summary>
        /// Estimated average network latency (ticks).
        /// Used to compensate for transmission delay.
        /// </summary>
        public long AverageLatencyTicks { get; set; } = Stopwatch.Frequency * 2 / 1000; // 2ms default
        
        /// <summary>
        /// Fixed delta time for deterministic lockstep (seconds).
        /// Typically 16.67ms (60Hz) or 33.33ms (30Hz).
        /// </summary>
        public float FixedDeltaSeconds { get; set; } = 1.0f / 60.0f;  // 60 FPS
        
        /// <summary>
        /// Timeout for waiting on ACKs in lockstep mode (milliseconds).
        /// If exceeded, log warning (but still wait).
        /// </summary>
        public double LockstepTimeoutMs { get; set; } = 1000.0;  // 1 second
        
        /// <summary>
        /// Wall-clock lookahead (Stopwatch ticks) for the Future Barrier protocol.
        /// <para>
        /// <see cref="DistributedTimeCoordinator"/> adds this to the master's current
        /// <see cref="Fdp.Core.GlobalTime.TotalWallTicks"/> when computing
        /// <see cref="Fdp.Toolkit.Time.Messages.SwitchTimeModeEvent.BarrierWallTicks"/>.
        /// All PLL-synchronized slaves will reach that wall-tick value within
        /// approximately one ECS frame of the master, so the default
        /// of ≈ 200 ms (expressed as Stopwatch ticks) is sufficient for DDS delivery
        /// across a LAN even under moderate load.
        /// </para>
        /// Set to a smaller value (e.g. 10–50 ms) in unit tests that use wall-clock sleeps.
        /// </summary>
        public long LookaheadWallTicks { get; set; } = (long)(0.2 * Stopwatch.Frequency);

        /// <summary>
        /// Maximum acceptable Round-Trip Time for a <see cref="Messages.TimeSyncResponse"/>.
        /// Responses whose RTT exceeds this value are discarded (spike rejection).
        /// Default: 200 ms expressed as Stopwatch ticks.
        /// </summary>
        public long MaxRttTicks { get; set; } = (long)(0.2 * Stopwatch.Frequency);

        /// <summary>
        /// How often (in Stopwatch ticks) the slave re-sends a <see cref="Messages.TimeSyncRequest"/>
        /// to correct hardware clock skew across long simulation sessions.
        /// Default: 1 second.
        /// </summary>
        public long SyncRefreshIntervalTicks { get; set; } = Stopwatch.Frequency;

        /// <summary>
        /// Weight applied to incremental sync offset updates (range 0.0–1.0).
        /// 1.0 = hard-snap every response; 0.1 (default) = gentle steering.
        /// </summary>
        public double SyncCorrectionWeight { get; set; } = 0.1;
    }
}
