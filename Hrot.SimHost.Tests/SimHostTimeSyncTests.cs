using System.Threading;
using Hrot.Orchestrator;
using Hrot.SimHost;
using CycloneDDS.Runtime;
using Xunit;

namespace Hrot.SimHost.Tests
{
    [Collection("SimHostDds")]
    public class SimHostTimeSyncTests : IDisposable
    {
        private const uint TestDomain = 210u;
        private readonly DdsParticipant _allocatorParticipant = new DdsParticipant(TestDomain);
        private readonly ClusterMaster    _clusterMaster;

        public SimHostTimeSyncTests()
        {
            _clusterMaster = new ClusterMaster(_allocatorParticipant);
        }

        public void Dispose()
        {
            _clusterMaster.Dispose();
            _allocatorParticipant.Dispose();
        }

        [Fact]
        public void SimHost_Tick_DoesNotThrow()
        {
            var app = new SimHostApp();
            app.InitializeHeadless(domainIdOverride: (int)TestDomain);

            Thread.Sleep(50);
            // Should not throw
            app.Tick(0.1f);

            app.Shutdown();
        }
    }
}
