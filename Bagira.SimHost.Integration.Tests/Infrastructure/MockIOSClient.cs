using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.IG.Components;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;

namespace Bagira.SimHost.Integration.Tests.Infrastructure
{
    /// <summary>
    /// Simulated IOS client that communicates with a <see cref="SimHostInstance"/>
    /// through the same stub request/ack stubs used by the test harness.
    ///
    /// Mirrors the IOS node's interaction pattern:
    /// 1. <see cref="SendCreateRequest"/> → publishes a <see cref="CreateEntityRequest"/>.
    /// 2. <see cref="WaitForAckAsync"/>   → polls the stub ACK sink until the matching
    ///    <see cref="CreateEntityAck"/> arrives (or the timeout elapses).
    /// 3. <see cref="ReadTkbIdentity"/> → inspects the ECS world via the
    ///    <see cref="SimHostInstance"/> to verify spawn metadata is present.
    /// </summary>
    public sealed class MockIOSClient
    {
        private readonly SimHostInstance _host;

        public MockIOSClient(SimHostInstance host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        // ── IOS → SimHost ─────────────────────────────────────────────────────────

        /// <summary>
        /// Enqueues a <see cref="CreateEntityRequest"/> on the SimHost's stub request
        /// source, exactly as an IOS DDS writer would publish it to the
        /// <c>CreateEntityRequest</c> topic.
        /// </summary>
        public void SendCreateRequest(CreateEntityRequest request)
            => _host.RequestSource.Enqueue(request);

        // ── SimHost → IOS ─────────────────────────────────────────────────────────

        /// <summary>
        /// Polls the <see cref="StubAckSink"/> until a <see cref="CreateEntityAck"/>
        /// matching <paramref name="requestId"/> arrives or <paramref name="timeoutMs"/>
        /// milliseconds have elapsed.
        ///
        /// Each poll step runs one simulation tick so the host advances its state.
        /// </summary>
        /// <param name="requestId">The request GUID to match.</param>
        /// <param name="timeoutMs">Maximum wait time in milliseconds (default 3 000 ms).</param>
        /// <returns>
        /// The matching <see cref="CreateEntityAck"/>, or <c>null</c> if the timeout
        /// elapses without a matching ACK.
        /// </returns>
        public async Task<CreateEntityAck?> WaitForAckAsync(Guid requestId, int timeoutMs = 3000)
        {
            // Each simulated "millisecond" is one tick (1/60 s in sim-time).
            // For deterministic polling we just run ticks and check synchronously.
            int maxTicks = Math.Max(1, timeoutMs / 16);   // ~16 ms per frame

            for (int i = 0; i < maxTicks; i++)
            {
                var ack = _host.AckSink.TryGetAck(requestId);
                if (ack.HasValue)
                    return ack.Value;

                // Advance simulation one step so the request is processed.
                _host.RunForTicks(1);

                // Yield control to the caller (async test infra) without blocking.
                await Task.Yield();
            }

            return _host.AckSink.TryGetAck(requestId);
        }

        // ── World inspection (simulates IOS ingesting DDS topics) ────────────────

        /// <summary>
        /// Reads the <see cref="TkbIdentity"/> state from the ECS world for the entity
        /// with network-id <paramref name="networkId"/>.
        ///
        /// In a live DDS environment the IOS client would read the <c>EntityMaster</c>
        /// topic.  In the integration test the world is queried directly.
        /// </summary>
        /// <returns>
        /// The <see cref="TkbIdentity"/> component, or <c>null</c> if no matching
        /// entity is found.
        /// </returns>
        public TkbIdentity? ReadTkbIdentity(int networkId)
        {
            // Resolve network-id → ECS entity via the entity map exposed by SimHostInstance.
            if (!_host.EntityMap.TryGetEntity(networkId, out var entity))
                return null;

            if (!_host.World.HasComponent<TkbIdentity>(entity))
                return null;

            ref readonly var tkbId = ref _host.World.GetComponentRO<TkbIdentity>(entity);
            return tkbId;
        }

        /// <summary>
        /// Reads the <see cref="IgEntityData"/> component from the ECS world for the entity
        /// with network-id <paramref name="networkId"/>.
        /// Returns <c>null</c> if no such entity exists or the component has not been assigned.
        /// </summary>
        public IgEntityData? ReadIgEntityData(int networkId)
        {
            if (!_host.EntityMap.TryGetEntity(networkId, out var entity))
                return null;

            if (!_host.World.HasManagedComponent<IgEntityData>(entity))
                return null;

            return ((ISimulationView)_host.World).GetManagedComponentRO<IgEntityData>(entity);
        }
    }
}