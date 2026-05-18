using System;
using System.Diagnostics;

namespace Fdp.Toolkit.Time
{
    /// <summary>
    /// High-resolution clock that returns 100-nanosecond UTC ticks anchored to the
    /// system's hardware performance counter.
    /// <para>
    /// <c>DateTime.UtcNow.Ticks</c> alone is limited by the OS timer resolution
    /// (typically 10-15 ms on Windows), making it unusable for sub-millisecond NTP
    /// clock synchronization between simulation nodes.
    /// <c>Stopwatch.GetTimestamp()</c> is high-resolution but expressed in an opaque
    /// hardware-frequency unit that differs between machines, so raw tick values
    /// cannot be compared across nodes.
    /// </para>
    /// <para>
    /// This class resolves both problems by capturing a UTC baseline once at
    /// application startup and then adding the elapsed high-resolution
    /// <see cref="TimeSpan"/> (whose ticks are always 100-ns units) to that
    /// baseline.  The result is simultaneously:
    /// <list type="bullet">
    ///   <item>Sub-microsecond accurate (driven by the CPU performance counter).</item>
    ///   <item>Expressed in universal 100-ns units (identical on every machine).</item>
    ///   <item>Anchored to the UTC epoch (consistent with <c>DateTime.UtcNow</c> and
    ///         the replay flight-recorder headers).</item>
    /// </list>
    /// </para>
    /// </summary>
    internal static class HighResUtcClock
    {
        // Captured once at class initialisation (first use).
        private static readonly long s_utcBaseline = DateTime.UtcNow.Ticks;
        private static readonly long s_swBaseline  = Stopwatch.GetTimestamp();

        /// <summary>
        /// Returns the current time as high-resolution 100-nanosecond UTC ticks.
        /// Equivalent to <c>DateTime.UtcNow.Ticks</c> but with sub-millisecond
        /// precision driven by the hardware performance counter.
        /// </summary>
        public static long GetTicks()
        {
            return s_utcBaseline
                + Stopwatch.GetElapsedTime(s_swBaseline, Stopwatch.GetTimestamp()).Ticks;
        }
    }
}
