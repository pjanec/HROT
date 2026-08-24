using System;
using System.Collections.Generic;
using System.Threading;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Interop;
using Fdp.Network.Cyclone.Topics;
using Fdp.Toolkit.NetworkSpawning;

namespace Fdp.Network.Cyclone.Services
{
    /// <summary>
    /// DDS-backed network-ID allocator client.
    ///
    /// <para>After construction the client defers its first allocation request until the
    /// corresponding <see cref="DdsIdAllocatorServer"/> reader is matched on the DDS bus
    /// (<see cref="DdsWriter{T}.PublicationMatched"/> event).  Callers of
    /// <see cref="AllocateId"/> are blocked (up to <see cref="DiscoveryTimeout"/>) while
    /// the client waits for the server to be discovered.  This prevents the
    /// "write-before-match" problem that caused the very first request to be silently
    /// dropped when the server was not yet present at construction time.</para>
    /// </summary>
    public class DdsIdAllocator : INetworkIdAllocator, IRestorableIdAllocator
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

        // ── Server-discovery tracking ────────────────────────────────────────
        /// <summary>Timeout waiting for the server's reader to be matched.</summary>
        public static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(3);

        /// <summary><c>0</c> = not yet discovered; <c>1</c> = discovered (and initial request sent).</summary>
        private int _serverDiscoveredFlag = 0;

        /// <summary>Signalled when the server is first discovered (or already matched).</summary>
        private readonly ManualResetEventSlim _serverReadyEvent = new(false);

        /// <summary>
        /// Whether the allocator server's reader is matched to this client's request writer
        /// (or discovery already completed). Used for optional local-server fallback timing.
        /// </summary>
        public bool HasPublicationMatch =>
            System.Threading.Volatile.Read(ref _serverDiscoveredFlag) != 0
            || _requestWriter.CurrentStatus.CurrentCount > 0;

        public DdsIdAllocator(DdsParticipant participant, string clientId)
        {
            _clientId = clientId;

            // Create request writer
            _requestWriter = new DdsWriter<IdRequest>(participant);

            // Subscribe to publication-matched event BEFORE checking current status
            // so we never miss a late match notification.
            _requestWriter.PublicationMatched += OnPublicationMatched;

            // Handle the case where the server's reader was already present when the
            // writer was created (e.g. server and client share the same participant, or
            // the server was started first on a different participant and DDS discovery
            // completed before we subscribed to the event).
            if (_requestWriter.CurrentStatus.CurrentCount > 0)
                HandleServerDiscovered();

            // Create response reader
            _responseReader = new DdsReader<IdResponse>(participant);

            // Create status reader
            _statusReader = new DdsReader<IdStatus>(participant);

            // NOTE: we do NOT call RequestChunk here.  The first request is sent
            // inside HandleServerDiscovered() once the server's reader is matched.
        }

        // ── Server-discovery helpers ─────────────────────────────────────────

        private void OnPublicationMatched(object? sender, DdsApi.DdsPublicationMatchedStatus status)
        {
            if (status.CurrentCount > 0)
                HandleServerDiscovered();
        }

        /// <summary>
        /// Called (at most once) when the server's reader is matched.
        /// Sends the first allocation request and unblocks any waiting <see cref="AllocateId"/> callers.
        /// Thread-safe and idempotent.
        /// </summary>
        private void HandleServerDiscovered()
        {
            // Interlocked swap ensures we run exactly once even if multiple callbacks race.
            if (Interlocked.CompareExchange(ref _serverDiscoveredFlag, 1, 0) == 0)
            {
                _serverReadyEvent.Set();
                RequestChunk(CHUNK_SIZE);
            }
        }

        // ── INetworkIdAllocator ──────────────────────────────────────────────

        public long AllocateId()
        {
            // ── Step 1: wait for the server to be discovered ─────────────────
            // Block until the server's DDS reader is matched to our writer, or
            // the discovery timeout expires.  All requestors are queued here during
            // startup — the first one to return unblocks after receiving IDs.
            if (_serverDiscoveredFlag == 0)
            {
                bool discovered = _serverReadyEvent.Wait(DiscoveryTimeout);
                if (!discovered)
                    throw new InvalidOperationException(
                        $"[DdsIdAllocator] ID allocator server not discovered within " +
                        $"{DiscoveryTimeout.TotalSeconds:0}s. " +
                        "Ensure DdsIdAllocatorServer is running before the first AllocateId() call.");
            }

            // ── Step 2: poll for any pending responses ───────────────────────
            ProcessResponses();

            // ── Step 3: request more IDs if running low ──────────────────────
            if (_availableIds.Count < LOW_WATER_MARK)
            {
                RequestChunk(CHUNK_SIZE);
            }

            // ── Step 4: spin-wait for IDs to arrive ──────────────────────────
            int attempts = 0;
            while (_availableIds.Count == 0 && attempts < MAX_POLL_ATTEMPTS)
            {
                Thread.Sleep(5); // Short wait
                ProcessResponses();

                // Retry request every ~100 ms in case of packet loss
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
        
        // ── Preview dry-run: restore OUR pool; ⛔ the server is NOT told ──────────────────────

        /// <inheritdoc/>
        /// <remarks>
        /// ⭐⭐⭐ 🔒 <b>The user's model, `2026-08-23`:</b> <i>"each node needs to remember the ids/chunks used
        /// during the run and on world reset to simply reset to their beginning while the central allocatore
        /// stays where it is for potential fresh allocations."</i>
        ///
        /// <para>⛔⛔⛔ <b>THIS IS DELIBERATELY NOT <see cref="Reset"/>.</b> 📐 <c>Reset</c> writes a GLOBAL
        /// <c>Req_Reset</c> to the server, which broadcasts to every client and flushes their pools, and
        /// 📄 <c>docs/designs/mgmt-1/DESIGN.md</c> §5.7 designs that reset to move the high-water mark
        /// <b>FORWARD</b> for collision avoidance. ⇒ 🔴 using it to rewind a preview would drag the WHOLE
        /// CLUSTER backward and flush pools other nodes are mid-way through. ⭐⭐ <b>This capability touches
        /// only <c>_availableIds</c> — this node's own reservation.</b></para>
        ///
        /// <para>⭐⭐ <b>Why the guarantee is EXACT and not "high chance"</b> *(§4c)*: chunks arrive as
        /// contiguous ranges enqueued in order and <see cref="AllocateId"/> dequeues FIFO ⇒ putting the same
        /// queue back yields the same ids in the same order.</para>
        ///
        /// <para>⚠⚠ <b>THE BOUNDARY.</b> Only ids held AT CAPTURE are restored. A preview that drains the
        /// pool triggers <c>RequestChunk(CHUNK_SIZE)</c>, and those fresh ids are spent from the cluster's
        /// point of view — the server advanced past them. ⇒ ⛔ they are NOT re-offered; the prefix repeats
        /// and the tail legitimately differs. 📌 With <c>CHUNK_SIZE = 100</c> a preview spawning fewer than
        /// ~100 entities is inside the exact case.</para>
        ///
        /// <para>⛔ <c>null</c> when the pool is empty — no position to promise, and the bracket reports it.</para>
        /// </remarks>
        public object? CaptureIssuingPosition()
            => _availableIds.Count == 0 ? null : _availableIds.ToArray();

        /// <inheritdoc/>
        public void RestoreIssuingPosition(object snapshot)
        {
            if (snapshot is not long[] held) return;

            // ⭐ Replace: ids fetched during the preview must not be re-offered (see the remarks).
            // ⛔ No writer, no request, no server round-trip.
            _availableIds.Clear();
            foreach (var id in held) _availableIds.Enqueue(id);
        }

        public void Dispose()
        {
            _requestWriter.PublicationMatched -= OnPublicationMatched;
            _serverReadyEvent.Dispose();
            _requestWriter?.Dispose();
            _responseReader?.Dispose();
            _statusReader?.Dispose();
        }
    }
}
