using System.Threading;
using Bagira.Orchestrator;
using Bagira.SimHost;
using CycloneDDS.Runtime;
using FDP.Toolkit.Time.Messages;
using Xunit;

namespace Bagira.SimHost.Tests
{
    [Collection("SimHostDds")]
    public class SimHostTimeSyncTests : IDisposable
    {
        private const uint TestDomain = 210u;
        private readonly DdsParticipant _allocatorParticipant = new DdsParticipant(TestDomain);
        private readonly DrillMaster    _drillMaster;

        public SimHostTimeSyncTests()
        {
            _drillMaster = new DrillMaster(_allocatorParticipant);
        }

        public void Dispose()
        {
            _drillMaster.Dispose();
            _allocatorParticipant.Dispose();
        }

        [Fact]
        public void SimHost_BroadcastsTimePulse_PerTick()
        {
            const uint domainId = TestDomain;

            using var readerParticipant = new DdsParticipant(domainId);
            using var reader = new DdsReader<TimePulseDescriptor>(readerParticipant, "TimePulse");

            var app = new SimHostApp();
            app.InitializeHeadless(domainIdOverride: (int)domainId);

            Thread.Sleep(100);
            app.Tick(0.1f);
            Thread.Sleep(100);

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
