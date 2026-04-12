using System;
using System.Collections.Generic;
using ModuleHost.Network.Cyclone.Topics;
using CycloneDDS.Runtime;

namespace ModuleHost.Network.Cyclone.Services
{
    /// <summary>
    /// Simple ID Allocator Server for testing.
    /// Handles Alloc, Reset, and GetStatus requests.
    /// One server per exercise session.
    /// </summary>
    public class DdsIdAllocatorServer : IDisposable
    {
        private readonly DdsReader<IdRequest> _requestReader;
        private readonly DdsWriter<IdResponse> _responseWriter;
        private readonly DdsWriter<IdStatus> _statusWriter;
        
        private ulong _nextId = 1;
        private readonly Dictionary<string, long> _clientRequestCounters = new();

        public DdsIdAllocatorServer(DdsParticipant participant)
        {
            _requestReader = new DdsReader<IdRequest>(participant);
            _responseWriter = new DdsWriter<IdResponse>(participant);
            _statusWriter = new DdsWriter<IdStatus>(participant);
            
            PublishStatus(); // Initial status
        }

        public void ProcessRequests()
        {
            using var scope = _requestReader.Take();
            
            foreach (var request in scope)
            {
                if (request.IsValid)
                    HandleRequest(request.Data);
            }
        }

        private void HandleRequest(IdRequest request)
        {
            switch (request.Type)
            {
                case EIdRequestType.Req_Alloc:
                    HandleAlloc(request);
                    break;
                
                case EIdRequestType.Req_Reset:
                    HandleReset(request);
                    break;
                
                case EIdRequestType.Req_GetStatus:
                    PublishStatus();
                    break;
            }
        }

        private void HandleAlloc(IdRequest request)
        {
            ulong start = _nextId;
            ulong count = request.Count;
            
            _nextId += count;
            
            _responseWriter.Write(new IdResponse
            {
                ClientId = request.ClientId,
                ReqNo = request.ReqNo,
                Type = EIdResponseType.Resp_Alloc,
                Start = start,
                Count = count
            });
            
            PublishStatus();
        }

        private void HandleReset(IdRequest request)
        {
            // Global reset (empty ClientId) or specific client
            bool isGlobal = string.IsNullOrEmpty(request.ClientId);
            
            _nextId = request.Start;
            
            if (isGlobal)
            {
                // Tell all clients to reset
                _responseWriter.Write(new IdResponse
                {
                    ClientId = "", // Broadcast
                    ReqNo = 0,
                    Type = EIdResponseType.Resp_Reset,
                    Start = 0,
                    Count = 0
                });
            }
            
            PublishStatus();
        }

        private void PublishStatus()
        {
            _statusWriter.Write(new IdStatus
            {
                HighestIdAllocated = _nextId - 1
            });
        }

        public void Dispose()
        {
            _requestReader?.Dispose();
            _responseWriter?.Dispose();
            _statusWriter?.Dispose();
        }
    }
}
