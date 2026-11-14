using System.Collections.Generic;
using Fdp.Core.Diagnostics;
using Xunit;

namespace Fdp.Core.Tests.Diagnostics
{
    /// <summary>
    /// ⭐ CE-4566 (X-11) — the write instrument (~13 ms per tick) has its OWN switch: <c>HROT_FRAME_STATS=1</c>, which every frame-time measurement
    /// sets, leaves it off; <c>HROT_WRITE_STATS=1</c> turns it on. 🔴 Red-proof: the old <c>|| HROT_FRAME_STATS == "1"</c> back ⇒ red.
    /// </summary>
    public sealed class TableWriteStatsSwitchTests
    {
        private static bool On(params (string Name, string Value)[] vars)
        {
            var env = new Dictionary<string, string>();
            foreach (var (n, v) in vars) env[n] = v;
            return TableWriteStats.SwitchedOn(name => env.TryGetValue(name, out var v) ? v : null);
        }

        [Fact]
        public void X11_FrameStats_LeavesTheWriteInstrumentOff_ItsOwnSwitchTurnsItOn()
        {
            Assert.False(On(("HROT_FRAME_STATS", "1")));
            Assert.True(On(("HROT_WRITE_STATS", "1")));
            Assert.True(On(("HROT_WRITE_STATS", "1"), ("HROT_FRAME_STATS", "1")));
            Assert.False(On());
        }
    }
}
