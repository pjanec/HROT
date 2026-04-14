using System;
using System.Linq;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.ModuleHost.Network.Cyclone.Services;
using Fdp.ModuleHost.Network.Cyclone.Topics;
using Xunit;

namespace Fdp.ModuleHost.Network.Cyclone.Tests.Services
{
    public class DdsIdAllocatorTests : IDisposable
    {
        private DdsParticipant _participant;
        private string _topicPrefix;

        public DdsIdAllocatorTests()
        {
            // Create isolated participant for tests
            _participant = new DdsParticipant(domainId: 0);
            _topicPrefix = Guid.NewGuid().ToString(); // Unique topics per test run
        }

        public void Dispose()
        {
            _participant?.Dispose();
        }

        private (DdsWriter<IdResponse>, DdsReader<IdRequest>) CreateMockServer()
        {
            // Server reads Requests and writes Responses
            // Note: Topic names in DdsIdAllocator are hardcoded "IdAlloc_Request", etc.
            // This makes isolation hard if tests run in parallel or share domain.
            // Ideally DdsIdAllocator would accept topic prefix.
            // But strict requirement says follow spec.
            // We'll rely on ClientId filtering for clients, but Server needs to see all.
            // For tests, we might collide if parallel.
            
            var writer = new DdsWriter<IdResponse>(_participant);
            var reader = new DdsReader<IdRequest>(_participant);
            return (writer, reader);
        }

        [Fact]
        public void AllocateId_WithMockServer_ReturnsSequentialIds()
        {
            var clientId = "TestClient_" + Guid.NewGuid();
            var (serverRespWriter, serverReqReader) = CreateMockServer();
            using var allocator = new DdsIdAllocator(_participant, clientId);

            // 1. Allocator sends request on init
            // Mock Server loop
            IdRequest request = default;
            bool received = false;
            
            for (int i = 0; i < 200; i++) // Poll for request
            {
                using var scope = serverReqReader.Take();
                foreach (var sample in scope)
                {
                    // if (!sample.HasData) continue;
                    var req = sample.Data;
                    request = req;
                    if (request.ClientId == clientId)
                    {
                        received = true;
                        break;
                    }
                }
                if (received) break;
                Thread.Sleep(10);
            }
            Assert.True(received, "Server did not receive ID Request");
            Assert.Equal(EIdRequestType.Req_Alloc, request.Type);

            // 2. Server sends response
            serverRespWriter.Write(new IdResponse
            {
                ClientId = clientId,
                ReqNo = request.ReqNo,
                Type = EIdResponseType.Resp_Alloc,
                Start = 1000,
                Count = 100
            });

            // 3. Allocator should receive and return ID
            // AllocateId blocks inside (spins)
            long id = allocator.AllocateId();
            Assert.Equal(1000, id);
            Assert.Equal(1001, allocator.AllocateId());
        }

        [Fact]
        public void Reset_SendsGlobalRequest()
        {
            var clientId = "ResetClient_" + Guid.NewGuid();
            var (serverRespWriter, serverReqReader) = CreateMockServer();
            using var allocator = new DdsIdAllocator(_participant, clientId);

            // Clear initial request
            Thread.Sleep(50);
            using (serverReqReader.Take()) { }

            // Call Reset
            allocator.Reset(5000);

            // Check for Reset request
            IdRequest request = default;
            bool received = false;
            
            for (int i = 0; i < 20; i++)
            {
                using var scope = serverReqReader.Take();
                foreach (var sample in scope)
                {
                    // if (!sample.HasData) continue;
                    var req = sample.Data;
                    if (req.Type == EIdRequestType.Req_Reset)
                    {
                        request = req;
                        received = true;
                        break;
                    }
                }
                if (received) break;
                Thread.Sleep(10);
            }

            Assert.True(received, "Server did not receive Reset Request");
            Assert.Equal("", request.ClientId); // Global
            Assert.Equal(5000ul, request.Start);
        }

        [Fact]
        public void ResponseReset_ClearsPool_RequestsNew()
        {
            var clientId = "RespResetClient_" + Guid.NewGuid();
            var (serverRespWriter, serverReqReader) = CreateMockServer();
            using var allocator = new DdsIdAllocator(_participant, clientId);

            // 1. Initial Alloc
            Thread.Sleep(100);
            serverRespWriter.Write(new IdResponse { ClientId = clientId, Type = EIdResponseType.Resp_Alloc, Start = 100, Count = 10 });
            
            Assert.Equal(100, allocator.AllocateId()); // Pool has 100..109

            // 2. Clear server requests buffer
             using (serverReqReader.Take()) { }

            // 3. Server sends RESET
            serverRespWriter.Write(new IdResponse { ClientId = clientId, Type = EIdResponseType.Resp_Reset });
            
            // 4. Client should process reset on next call, clear pool, and Request new chunk.
            // AllocateId calls ProcessResponses.
            // But since pool empty, it loops.
            // We need to feed it a new alloc response for it to return.
            // But we first check if it SENT a request.
            
            // We can't easily check "Has Sent Request" while blocked in AllocateId.
            // But we can trigger ProcessResponses logic by calling AllocateId().
            // Wait, AllocateId spins.
            // Loop:
            //   ProcessResponses (processes Reset -> clears pool -> Requests Chunk)
            //   Check Pool (empty)
            //   Loop...
            
            // So we need another thread or check side effect.
            // Mock Server can listen.
            
            // Simulate Async Server
            bool reRequestReceived = false;
            var t = new Thread(() => 
            {
                for (int i = 0; i < 50; i++)
                {
                    using var scope = serverReqReader.Take();
                    foreach (var sample in scope)
                    {
                        // if (!sample.HasData) continue;
                        var req = sample.Data;
                        if (req.ClientId == clientId && req.Type == EIdRequestType.Req_Alloc) // New alloc request
                        {
                            reRequestReceived = true;
                            // Send new IDs to unblock client
                            serverRespWriter.Write(new IdResponse { ClientId = clientId, ReqNo = req.ReqNo, Type = EIdResponseType.Resp_Alloc, Start = 200, Count = 10 });
                            return;
                        }
                    }
                    Thread.Sleep(20);
                }
            });
            t.Start();
            
            long newId = allocator.AllocateId(); // This should block/spin until server replies
            t.Join();
            
            Assert.True(reRequestReceived, "Client did not re-request IDs after Reset");
            Assert.Equal(200, newId); // Got new range
        }
    }
}
