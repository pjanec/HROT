using System;
using System.Collections.Generic;
using Fdp.Network.Cyclone.Topics;
using CycloneDDS.Runtime;

namespace Fdp.Network.Cyclone.Services
{
    /// <summary>
    /// Simple ID Allocator Server for testing.
    /// Handles Alloc, Reset, and GetStatus requests.
    /// One server per exercise session.
    /// </summary>
    public class DdsIdAllocatorServer : IDisposable, Fdp.Toolkit.NetworkSpawning.IWorldIdAuthority
    {
        private readonly DdsReader<IdRequest> _requestReader;
        private readonly DdsWriter<IdResponse> _responseWriter;
        private readonly DdsWriter<IdStatus> _statusWriter;

        /// <summary>
        /// ⭐ Guards <see cref="_nextId"/>. <c>ProcessRequests</c> runs on the orchestrator's
        /// <c>HostedIdAllocatorServer</c> polling thread while <see cref="ResetToBase"/> is called from the
        /// <c>ClusterMaster</c>'s thread at a world boundary — ⛔ the counter is read-modify-written on both.
        /// </summary>
        private readonly object _idLock = new();

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
            ulong start;
            ulong count = request.Count;

            lock (_idLock)
            {
                start = _nextId;
                _nextId += count;
            }

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

            lock (_idLock) { _nextId = request.Start; }

            if (isGlobal)
                BroadcastReset();

            PublishStatus();
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>HN-037</c>: the world boundary resets the ONE authority, directly.</b>
        /// 📄 <c>docs/DESIGN_Deterministic_Network_Ids.md</c> §11.
        ///
        /// <para>⭐⭐ <b>Why the master calls this instead of writing a <c>Req_Reset</c>.</b> The orchestrator
        /// hosts the SERVER; it holds no <c>DdsIdAllocator</c> client to write a request with. Handing it one
        /// just to talk to a server in its own process would add a DDS round-trip — and a race — between the
        /// reset and the fan-out that immediately follows it. ⭐ In-process the authority is right here.</para>
        ///
        /// <para>⭐ The broadcast is the SAME <c>Resp_Reset</c> a remote <c>Req_Reset</c> produces, so every
        /// client flushes its pool through the one path that already existed *(<c>mgmt-1</c> §5.7)* — ⛔ no
        /// second protocol.</para>
        ///
        /// <para>⚠⚠ Destructive by design: correct only where the world has just been cleared. The guard is
        /// the CALLER's — see <see cref="Fdp.Toolkit.NetworkSpawning.IWorldIdAuthority"/>.</para>
        /// </summary>
        public void ResetToBase(long firstId)
        {
            lock (_idLock) { _nextId = (ulong)firstId; }
            BroadcastReset();
            PublishStatus();
        }

        /// <summary>Tells every client to forget its reservations and re-fetch.</summary>
        private void BroadcastReset()
        {
            _responseWriter.Write(new IdResponse
            {
                ClientId = "", // Broadcast
                ReqNo = 0,
                Type = EIdResponseType.Resp_Reset,
                Start = 0,
                Count = 0
            });
        }

        private void PublishStatus()
        {
            ulong next;
            lock (_idLock) { next = _nextId; }

            _statusWriter.Write(new IdStatus
            {
                HighestIdAllocated = next - 1
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
