using System.Threading;
using Bagira.SimHost;
using CycloneDDS.Runtime;
using FDP.Toolkit.Time.Messages;
using Xunit;

namespace Bagira.SimHost.Tests
{
    public class SimHostTimeSyncTests
    {
        [Fact]
        public void SimHost_BroadcastsTimePulse_PerTick()
        {
            const uint domainId = 210u;

            using var readerParticipant = new DdsParticipant(domainId);
            using var reader = new DdsReader<TimePulseDescriptor>(readerParticipant, "TimePulse");

            var app = new SimHostApp();
            app.InitializeHeadless(domainIdOverride: (int)domainId);

            Thread.Sleep(200);
            app.Tick(0.1f);
            Thread.Sleep(200);

            using var loan = reader.Take();
            bool found = false;
            foreach (var sample in loan)
            {
                if (!sample.IsValid)
                    continue;

                found = true;
                break;
            }

            Assert.True(found, "Expected a TimePulseDescriptor sample after one tick.");
        }
    }
}
