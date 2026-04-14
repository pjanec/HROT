using System;
using System.Threading;
using Hrot.SimHost;
using CycloneDDS.Runtime;
using Fdp.ModuleHost.Network.Cyclone.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    [Collection("SimHostDds")]
    public class SimHostTimeSyncTests : IDisposable
    {
        private const uint TestDomain = 210u;
        private readonly DdsParticipant           _allocatorParticipant = new DdsParticipant(TestDomain);
        private readonly DdsIdAllocatorServer     _idAllocatorServer;
        private readonly Thread                   _idServerThread;
        private readonly CancellationTokenSource  _idServerCts;

        public SimHostTimeSyncTests()
        {
            _idAllocatorServer = new DdsIdAllocatorServer(_allocatorParticipant);
            _idServerCts       = new CancellationTokenSource();
            _idServerThread    = new Thread(() =>
            {
                while (!_idServerCts.IsCancellationRequested)
                {
                    _idAllocatorServer.ProcessRequests();
                    Thread.Sleep(1);
                }
            }) { IsBackground = true, Name = "Test-IdAllocServer-" + TestDomain };
            _idServerThread.Start();
        }

        public void Dispose()
        {
            _idServerCts.Cancel();
            _idServerThread.Join(TimeSpan.FromSeconds(2));
            _idServerCts.Dispose();
            _idAllocatorServer.Dispose();
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
