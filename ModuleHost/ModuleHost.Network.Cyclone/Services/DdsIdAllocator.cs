using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using ModuleHost.Core.Network.Interfaces;
using ModuleHost.Network.Cyclone.Topics;

namespace ModuleHost.Network.Cyclone.Services
{
    public class DdsIdAllocator : INetworkIdAllocator
    {
        private readonly DdsWriter<IdRequest> _requestWriter;
        private readonly DdsReader<IdResponse> _responseReader;
        private readonly DdsReader<IdStatus> _statusReader;
        private readonly string _clientId;
        private long _requestCounter = 0;
        private readonly Queue<long> _availableIds = new();

        private const int CHUNK_SIZE = 100;
        private const int LOW_WATER_MARK = 10;
        private const int MAX_POLL_ATTEMPTS = 600;

        public DdsIdAllocator(DdsParticipant participant, string clientId)
        {
            _clientId = clientId;
            
            // Create request writer
            _requestWriter = new DdsWriter<IdRequest>(participant);
            
            // Create response reader (filter by our ClientId)
            _responseReader = new DdsReader<IdResponse>(participant);
            
            // Note: Filter optimization requires specific QoS or compile-time support, 
            // verifying logic first. Can also just filter in ProcessResponses which is 
            // what we do here effectively combined with Keyed reading if we used Keyed reader.
            // But strict matching of ClientId string key is efficient.
            // For now, we subscribe to all, and filter in ProcessResponses or use content filter.
            // Using content filter:
            // _responseReader.SetFilter(r => r.ClientId == _clientId);
            // This requires expression tree support in binding. Assuming standard reader for now.
            
            // Create status reader
            _statusReader = new DdsReader<IdStatus>(participant);
            
            // Initial request
            RequestChunk(CHUNK_SIZE);
        }

        public long AllocateId()
        {
            // Poll for responses in case we haven't processed them yet
            ProcessResponses();
            
            // Check if we need more IDs
            if (_availableIds.Count < LOW_WATER_MARK)
            {
                RequestChunk(CHUNK_SIZE);
            }
            
            // If empty, we must block/spin until we get IDs.
            // In a real game engine, we might not want to block, but `AllocateId` 
            // is often synchronous. We'll spin for a bit.
            int attempts = 0;
            while (_availableIds.Count == 0 && attempts < MAX_POLL_ATTEMPTS)
            {
                System.Threading.Thread.Sleep(5); // Short wait
                ProcessResponses();
                
                // Retry request every 20 attempts (approx 100ms) in case of packet loss or "write before match"
                if (attempts % 20 == 19)
                {
                     RequestChunk(CHUNK_SIZE);
                }
                
                attempts++;
            }
            
            if (_availableIds.Count == 0)
            {
                throw new InvalidOperationException("ID pool exhausted and no response from server.");
            }
            
            return _availableIds.Dequeue();
        }

        private void RequestChunk(int count)
        {
            _requestWriter.Write(new IdRequest
            {
                ClientId = _clientId,
                ReqNo = _requestCounter++,
                Type = EIdRequestType.Req_Alloc,
                Start = 0, // Unused for Alloc
                Count = (ulong)count
            });
        }

        private void ProcessResponses()
        {
            // Zero-copy read
            using var scope = _responseReader.Take();
            
            foreach (var sample in scope)
            {
                if (!sample.IsValid) continue;
                var response = sample.Data;

                // Only process responses for us or broadcast?
                // IdResponse uses ClientId as key.
                if (response.ClientId != _clientId && !string.IsNullOrEmpty(response.ClientId))
                    continue;

                if (response.Type == EIdResponseType.Resp_Alloc)
                {
                    // Add chunk to local pool
                    for (ulong i = 0; i < response.Count; i++)
                    {
                        _availableIds.Enqueue((long)(response.Start + i));
                    }
                }
                else if (response.Type == EIdResponseType.Resp_Reset)
                {
                    // Server wants us to forget reservations
                    _availableIds.Clear();
                    // Don't auto-request here? Or do?
                    // Usually reset implies "start over", so maybe request new chunk immediately
                    RequestChunk(CHUNK_SIZE);
                }
            }
        }

        public void Reset(long startId)
        {
            // Send reset request to server
            _requestWriter.Write(new IdRequest
            {
                ClientId = "", // Global request (empty ClientId)
                ReqNo = _requestCounter++,
                Type = EIdRequestType.Req_Reset,
                Start = (ulong)startId,
                Count = 0
            });
            
            // Clear local pool (server will send Reset response which triggers refill)
            // But we do it proactively too.
            _availableIds.Clear();
            
            // Note: We don't RequestChunk here, we wait for the Global Response
            // which should trigger a Reset type response, clearing our pool (redundant)
            // and maybe we should request then? 
            // Actually, if we send Reset, Server will broadcast Reset to everyone.
        }
        
        public void Dispose()
        {
            _requestWriter?.Dispose();
            _responseReader?.Dispose();
            _statusReader?.Dispose();
        }
    }
}
