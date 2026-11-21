using System;
using System.Threading.Tasks;
using CycloneDDS.Runtime;
using Fdp.Network.Cyclone.Services;
using Fdp.Network.Cyclone.Topics;
using Xunit;

namespace Fdp.Network.Cyclone.Tests.Integration
{
    public class IdAllocatorServerTests : IDisposable
    {
        private readonly DdsParticipant _participant;
        private readonly DdsIdAllocatorServer _server;

        public IdAllocatorServerTests()
        {
            _participant = new DdsParticipant(domainId: 99); // Test domain
            _server = new DdsIdAllocatorServer(_participant);
        }

        [Fact]
        public void Server_And_Client_Roundtrip()
        {
            // Create client
            using var client = new DdsIdAllocator(_participant, "TestClient");
            
            // Server processes requests in background
            // We run a loop for a short time to service the allocations
            bool keepRunning = true;
            var serverTask = Task.Run(async () =>
            {
                while (keepRunning)
                {
                    _server.ProcessRequests();
                    await Task.Delay(10);
                }
            });
            
            try
            {
                // Client allocates IDs
                // AllocateId blocks until it gets IDs
                long id1 = client.AllocateId();
                long id2 = client.AllocateId();
                
                Assert.InRange(id1, 1, 1000);
                Assert.InRange(id2, 1, 1000);
                Assert.NotEqual(id1, id2);
                Assert.Equal(id1 + 1, id2); // Should be sequential
            }
            finally
            {
                keepRunning = false;
                // Wait for task to finish if needed, or just let it die with dispose
            }
        }

        /// <summary>
        /// ⭐ CE-4013 D3 — two nodes of ONE subsystem (two traffic nodes, two SimHosts) must not share id chunks. With distinct client ids
        /// (HrotNodeBuilder.AllocatorClientId puts the node id in) their ids are disjoint; the second test is the defect it fixes.
        /// </summary>
        [Fact]
        public void TwoClients_WithDistinctIds_NeverHandOutTheSameId()
        {
            using var a = new DdsIdAllocator(_participant, "TrafficAllocator-801");
            using var b = new DdsIdAllocator(_participant, "TrafficAllocator-802");
            var (idsA, idsB) = AllocateBoth(a, b, 150);
            Assert.Empty(System.Linq.Enumerable.Intersect(idsA, idsB));
        }

        /// <summary>⛔ The hazard, pinned: two clients sharing ONE id each keep every chunk sent to it, so ids repeat across them.</summary>
        [Fact]
        public void TwoClients_SharingOneId_HandOutTheSameIds()
        {
            using var a = new DdsIdAllocator(_participant, "TrafficAllocator");
            using var b = new DdsIdAllocator(_participant, "TrafficAllocator");
            var (idsA, idsB) = AllocateBoth(a, b, 150);
            Assert.NotEmpty(System.Linq.Enumerable.Intersect(idsA, idsB));
        }

        private (System.Collections.Generic.List<long>, System.Collections.Generic.List<long>) AllocateBoth(DdsIdAllocator a, DdsIdAllocator b, int each)
        {
            bool keepRunning = true;
            var serverTask = Task.Run(async () =>
            {
                while (keepRunning) { _server.ProcessRequests(); await Task.Delay(5); }
            });
            try
            {
                var idsA = new System.Collections.Generic.List<long>();
                var idsB = new System.Collections.Generic.List<long>();
                for (int i = 0; i < each; i++) { idsA.Add(a.AllocateId()); idsB.Add(b.AllocateId()); }
                return (idsA, idsB);
            }
            finally
            {
                keepRunning = false;
                serverTask.Wait(1000);
            }
        }

        [Fact]
        public void Server_Reset_ClearsAllClients()
        {
            using var client1 = new DdsIdAllocator(_participant, "Client1");
            using var client2 = new DdsIdAllocator(_participant, "Client2");
            
            bool keepRunning = true;
            var serverTask = Task.Run(async () =>
            {
                while (keepRunning)
                {
                    _server.ProcessRequests();
                    await Task.Delay(10);
                }
            });

            try
            {
                // Allocate some IDs
                var id1 = client1.AllocateId();
                var id2 = client2.AllocateId();
                
                // Server sends global reset
                // We use client1 to trigger reset for simplicity, or we can inject a reset request manually.
                // DdsIdAllocator has a Reset method according to interface/impl.
                client1.Reset(5000); // This sends global reset request with start=5000
                
                // Give time for server to process and client to update
                System.Threading.Thread.Sleep(200);
                
                // Next allocations should start from 5000
                long newId = client1.AllocateId();
                Assert.InRange(newId, 5000, 5100);
            }
            finally
            {
                keepRunning = false;
            }
        }

        public void Dispose()
        {
            _server?.Dispose();
            _participant?.Dispose();
        }
    }
}
