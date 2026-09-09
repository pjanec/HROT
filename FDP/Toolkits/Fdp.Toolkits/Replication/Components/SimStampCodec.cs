using System;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// Encodes and decodes the <b>simulation-time stamp</b> carried on position samples
    /// (<c>WorldPos.Time</c> on the NED stack, <c>BdcWorldPos.Time</c> on BDC).
    ///
    /// <para>
    /// <b>Why this type exists.</b> Both stacks carry the stamp in a pre-existing
    /// <see cref="DateTime"/> field whose own declaration documents it as
    /// <c>"Sync timestamp (exercise FILETIME). 0 = unspecified"</c>. Both are IDL-backed
    /// (<c>hrot-sim-desc</c> and <c>bdc-entity-msgs</c>), so a type change would touch every node —
    /// the design deliberately reuses the field rather than widening the wire. That leaves one
    /// encoding rule and one sentinel rule to get right, in four call sites across two stacks, which
    /// is exactly the shape that drifts. So it lives here, once.
    /// See <c>docs/DESIGN_Dead_Reckoning.md</c> §"Encoding".
    /// </para>
    ///
    /// <para>
    /// <b>The stamp is SIMULATION time, never wall time.</b> Dead reckoning ages a sample as
    /// <c>simNow - stamp</c>, and that subtraction is only meaningful if both terms come from the
    /// cluster-synced simulation clock. A wall-clock stamp re-introduces exactly the node-to-node
    /// skew the sync exists to remove, and it keeps running while the cluster is paused. This
    /// matches the codebase's other cross-node sample age — EQS <c>BecomesStale</c> ages over
    /// <c>view.Time</c> for the same reason.
    /// </para>
    ///
    /// <para>
    /// <b>The one-tick bias, and why it is not cuteness.</b> Simulation time legitimately starts at
    /// <c>0.0</c>, but <c>0</c> ticks is the field's documented "unspecified" sentinel. Encoding a
    /// t=0 sample as 0 ticks would make a correctly stamped sample indistinguishable from an
    /// unstamped one, and would fire the loud unstamped diagnostic on the first frame of every run.
    /// Biasing by a single tick (100 ns) keeps <c>0</c> unambiguously meaning "nobody stamped this"
    /// at a cost far below the resolution anything here cares about.
    /// </para>
    /// </summary>
    public static class SimStampCodec
    {
        /// <summary>
        /// Reserves tick 0 as the wire's "unspecified" sentinel so that simulation time 0.0 —
        /// a legitimate value — does not encode to it. See the type remarks.
        /// </summary>
        private const long UnspecifiedSentinelBiasTicks = 1;

        /// <summary>
        /// Encodes simulation seconds for the wire. Negative input is clamped to zero: simulation
        /// time does not run backwards, and a negative stamp would decode to an age larger than the
        /// run itself.
        /// </summary>
        public static DateTime Encode(double simTimeSeconds)
        {
            if (simTimeSeconds < 0.0) simTimeSeconds = 0.0;

            long ticks = (long)(simTimeSeconds * TimeSpan.TicksPerSecond) + UnspecifiedSentinelBiasTicks;
            return new DateTime(ticks, DateTimeKind.Unspecified);
        }

        /// <summary>
        /// Decodes a wire stamp back to simulation seconds.
        /// </summary>
        /// <returns>
        /// <c>false</c> when the sample carries the "unspecified" sentinel — i.e. it was published
        /// by something that does not stamp. Callers must treat that as a defect and say so loudly:
        /// there is deliberately no silent degraded path, because a silently unstamped sample
        /// extrapolates from an age of "now minus zero" and throws the entity across the map.
        /// </returns>
        public static bool TryDecode(DateTime wireStamp, out double simTimeSeconds)
        {
            long ticks = wireStamp.Ticks;
            if (ticks < UnspecifiedSentinelBiasTicks)
            {
                simTimeSeconds = 0.0;
                return false;
            }

            simTimeSeconds = (ticks - UnspecifiedSentinelBiasTicks) / (double)TimeSpan.TicksPerSecond;
            return true;
        }
    }
}
