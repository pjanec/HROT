using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.IG.Components;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;

namespace Hrot.SimHost.Integration.Tests.Infrastructure
{
    /// <summary>
    /// Simulated ExCon client that communicates with a <see cref="SimHostInstance"/>
    /// through the same stub request/ack stubs used by the test harness.
    ///
    /// Mirrors the ExCon node's interaction pattern:
    /// 1. <see cref="SendCreateRequest"/> → publishes a <see cref="CreateEntityRequest"/>.
    /// 2. <see cref="WaitForAckAsync"/>   → polls the stub ACK sink until the matching
    ///    <see cref="CreateUpdateDeleteEntityAck"/> with a final status code arrives
    ///    (or the timeout elapses).
    /// 3. <see cref="ReadTkbIdentity"/> → inspects the ECS world via the
    ///    <see cref="SimHostInstance"/> to verify spawn metadata is present.
    /// </summary>
    public sealed class MockExConClient
    {
        private readonly SimHostInstance _host;

        public MockExConClient(SimHostInstance host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        // ── ExCon → SimHost ─────────────────────────────────────────────────────────

        /// <summary>
        /// Enqueues a <see cref="CreateEntityRequest"/> on the SimHost's stub request
        /// source, exactly as an ExCon DDS writer would publish it to the
        /// <c>CreateEntityRequest</c> topic.
        /// </summary>
        public void SendCreateRequest(CreateEntityRequest request)
            => _host.RequestSource.Enqueue(request);

        // ── SimHost → ExCon ─────────────────────────────────────────────────────────

        /// <summary>
        /// Polls the <see cref="StubAckSink"/> until a <see cref="CreateUpdateDeleteEntityAck"/>
        /// matching <paramref name="requestId"/> with a final status code (Success or error)
        /// arrives or <paramref name="timeoutMs"/> milliseconds have elapsed.
        ///
        /// Each poll step runs one simulation tick so the host advances its state.
        /// </summary>
        /// <param name="requestId">The request GUID to match.</param>
        /// <param name="timeoutMs">Maximum wait time in milliseconds (default 3 000 ms).</param>
        /// <returns>
        /// The final <see cref="CreateUpdateDeleteEntityAck"/> (StatusCode != InProgress),
        /// or <c>null</c> if the timeout elapses without a matching final ACK.
        /// </returns>
        public async Task<CreateUpdateDeleteEntityAck?> WaitForAckAsync(Guid requestId, int timeoutMs = 3000)
        {
            // Each simulated "millisecond" is one tick (1/60 s in sim-time).
            // For deterministic polling we just run ticks and check synchronously.
            int maxTicks = Math.Max(1, timeoutMs / 16);   // ~16 ms per frame

            for (int i = 0; i < maxTicks; i++)
            {
                var ack = _host.AckSink.TryGetTerminalAck(requestId);
                if (ack.HasValue)
                    return ack.Value;

                // Advance simulation one step so the request is processed.
                _host.RunForTicks(1);

                // Yield control to the caller (async test infra) without blocking.
                await Task.Yield();
            }

            return _host.AckSink.TryGetTerminalAck(requestId);
        }

        // ── World inspection (simulates ExCon ingesting DDS topics) ────────────────

        /// <summary>
        /// Reads the <see cref="TkbIdentity"/> state from the ECS world for the entity
        /// with network-id <paramref name="networkId"/>.
        ///
        /// In a live DDS environment the ExCon client would read the <c>EntityMaster</c>
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
		/// Reads the <see cref="IG.Components.EntityInfo"/> component from the ECS world for the entity
		/// with network-id <paramref name="networkId"/>.
		/// Returns <c>null</c> if no such entity exists or the component has not been assigned.
		/// </summary>
		public IG.Components.EntityInfo? ReadIgEntityData(int networkId)
        {
            if (!_host.EntityMap.TryGetEntity(networkId, out var entity))
                return null;

            if (!_host.World.HasComponent<IG.Components.EntityInfo>( entity ) )
                return null;

            return ((ISimulationView)_host.World).GetComponentRO<IG.Components.EntityInfo>( entity );
        }
    }
}