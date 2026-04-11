using System.Diagnostics;
using FDP.Toolkit.Time.Controllers;
using Xunit;

namespace FDP.Toolkit.Time.Tests
{
    /// <summary>
    /// Unit tests for TC3-P1-T02: TimeConfig NTP sync property defaults.
    /// </summary>
    public class TimeConfigTests
    {
        /// <summary>TC3-P1-T02-SC1 — MaxRttTicks defaults to (long)(0.2 * Stopwatch.Frequency).</summary>
        [Fact]
        public void TimeConfig_Default_MaxRttTicks_IsApproximately200ms()
        {
            var config = TimeConfig.Default;
            long expected = (long)(0.2 * Stopwatch.Frequency);

            Assert.Equal(expected, config.MaxRttTicks);
        }

        /// <summary>TC3-P1-T02-SC2 — SyncRefreshIntervalTicks defaults to Stopwatch.Frequency (1 second).</summary>
        [Fact]
        public void TimeConfig_Default_SyncRefreshIntervalTicks_Is1Second()
        {
            var config = TimeConfig.Default;

            Assert.Equal(Stopwatch.Frequency, config.SyncRefreshIntervalTicks);
        }

        /// <summary>TC3-P1-T02-SC3 — SyncCorrectionWeight defaults to 0.1.</summary>
        [Fact]
        public void TimeConfig_Default_SyncCorrectionWeight_IsPointOne()
        {
            var config = TimeConfig.Default;

            Assert.Equal(0.1, config.SyncCorrectionWeight);
        }
    }
}
