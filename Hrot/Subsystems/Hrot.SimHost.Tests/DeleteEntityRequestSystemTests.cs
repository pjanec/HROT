using System;
using System.Collections.Generic;
using Hrot.CGF.Systems;
using Hrot.Core.Network;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="DeleteEntityRequestSystem"/>.
    ///
    /// Verifies that Phase-1 InProgress ACKs are dispatched immediately, error
    /// ACKs are sent for unknown entities, and <see cref="DestroyEntityCommand"/>
    /// events are published when deletion is valid.
    /// </summary>
    public class DeleteEntityRequestSystemTests
    {
        // ── Stubs ──────────────────────────────────────────────────────────────

        private sealed class StubDeleteRequestSource : IEntityDeletionRequestSource
        {
            private readonly List<EntityDeletionRequest> _pending = new();

            public void Enqueue(EntityDeletionRequest r) => _pending.Add(r);

            public void ProcessRequests(Action<EntityDeletionRequest> handler)
            {
                foreach (var req in _pending)
                    handler(req);
                _pending.Clear();
            }

            public void Dispose() { }
        }

        // ── Factory helpers ────────────────────────────────────────────────────

        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            return repo;
        }

        private static (
            DeleteEntityRequestSystem       system,
            StubAckSink                     ackSink,
            StubDeleteRequestSource         requestSource,
            NetworkEntityMap                map,
            EntityRequestFinalizationSystem    finalizationSystem)
            BuildSystem()
        {
            var ackSink             = new StubAckSink();
            var requestSource       = new StubDeleteRequestSource();
            var map                 = new NetworkEntityMap();
            var finalizationSystem  = new EntityRequestFinalizationSystem(ackSink, map);
            var system              = new DeleteEntityRequestSystem(requestSource, ackSink, map, finalizationSystem);
            return (system, ackSink, requestSource, map, finalizationSystem);
        }

        // ── Tests ──────────────────────────────────────────────────────────────

        /// <summary>
        /// A delete request for an entity not in the map receives an immediate
        /// EntityNotFound error ACK with no subsequent processing.
        /// </summary>
        [Fact]
        public void ProcessRequest_UnknownEntity_SendsEntityNotFoundAck()
        {
            var world = CreateWorld();
            var (system, ackSink, requestSource, _, _) = BuildSystem();
            var requestId = Guid.NewGuid();

            requestSource.Enqueue(new EntityDeletionRequest
            {
                RequestId = requestId,
                EntityId  = 9999,   // not registered
            });

            system.Execute(world, 0f);

            Assert.Single(ackSink.WrittenAcks);
            var ack = ackSink.WrittenAcks[0];
            Assert.Equal(requestId,                             ack.RequestId);
            Assert.Equal(9999,                                  ack.EntityId);
            Assert.Equal((int)EntityOperationStatus.EntityNotFound, ack.StatusCode);

            // No DestroyEntityCommand should have been published.
            world.Bus.SwapBuffers();
            var cmds = ((ISimulationView)world).ConsumeManagedEvents<DestroyEntityCommand>();
            Assert.Empty(cmds);
        }

        /// <summary>
        /// A delete request for a known entity sends a Phase-1 InProgress ACK and
        /// publishes a <see cref="DestroyEntityCommand"/> to the event bus.
        /// </summary>
        [Fact]
        public void ProcessRequest_KnownEntity_SendsInProgressAckAndPublishesCommand()
        {
            var world = CreateWorld();
            var (system, ackSink, requestSource, map, _) = BuildSystem();
            var requestId = Guid.NewGuid();
            const int entityId = 1001;

            // Register a live entity.
            var entity = world.CreateEntity();
            map.Register(entityId, entity);

            requestSource.Enqueue(new EntityDeletionRequest
            {
                RequestId = requestId,
                EntityId  = entityId,
            });

            system.Execute(world, 0f);

            // Phase-1 InProgress ACK must be sent immediately.
            Assert.Single(ackSink.WrittenAcks);
            var ack = ackSink.WrittenAcks[0];
            Assert.Equal(requestId,                           ack.RequestId);
            Assert.Equal(entityId,                            ack.EntityId);
            Assert.Equal((int)EntityOperationStatus.InProgress, ack.StatusCode);

            // DestroyEntityCommand must be on the event bus.
            world.Bus.SwapBuffers();
            var cmds = ((ISimulationView)world).ConsumeManagedEvents<DestroyEntityCommand>();
            Assert.Single(cmds);
            Assert.Equal(entityId, cmds[0].NetworkId);
        }
    }
}

