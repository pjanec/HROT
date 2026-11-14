using System;
using System.Collections.Generic;
using Hrot.CGF.Systems;
using Hrot.Common.Systems;   // Q65 obstacle 1: the request tier moved here
using Hrot.Core.Network;
using Fdp.Interfaces;
using Fdp.Core;
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
            var cmds = ((ISimulationView)world).ReadManagedEvents<DestroyEntityCommand>();
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
            var cmds = ((ISimulationView)world).ReadManagedEvents<DestroyEntityCommand>();
            Assert.Single(cmds);
            Assert.Equal(entityId, cmds[0].NetworkId);
            Assert.True(cmds[0].IsRemote);   // ⭐ CE-2247 — off the wire: never forwarded back out as another request
        }
    
        /// <summary>⭐ CE-4276 (edit framework F2) — a node that does not OWN the entity is a bystander: no teardown, no answer (the owner answers, and its
        /// dispose removes the replica). ✅ Red-proof: the authority check removed ⇒ an ack and a command ⇒ red.</summary>
        [Fact]
        public void F2_ABystander_NeitherTearsDownNorAnswers()
        {
            var world = CreateWorld();
            world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkAuthority>();
            var (system, ackSink, requestSource, map, _) = BuildSystem();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new Fdp.Toolkit.Replication.Components.NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            map.Register(1002, entity);

            requestSource.Enqueue(new EntityDeletionRequest { RequestId = Guid.NewGuid(), EntityId = 1002 });
            system.Execute(world, 0f);

            Assert.Empty(ackSink.WrittenAcks);
            world.Bus.SwapBuffers();
            Assert.Empty(((ISimulationView)world).ReadManagedEvents<DestroyEntityCommand>());
        }

        /// <summary>⭐ Edit framework F2 (§2.4) — the lifecycle ack IS the edit outcome: through <c>EditOutcomeEntityAckSink</c> an InProgress is Pending, a
        /// Success Applied, an unknown entity Refused with its reason — under the request's own id, in the world's log; the inner observer still sees the ack.
        /// ✅ Red-proof: the sink not reporting ⇒ the log empty ⇒ red.</summary>
        [Fact]
        public void F2_TheAckIsTheEditOutcome()
        {
            var world = CreateWorld();
            var inner = new StubAckSink();
            var sink = new Hrot.Common.EntityCreation.EditOutcomeEntityAckSink(world, localNode: 3, inner);
            var id = Guid.NewGuid();
            var log = Fdp.Toolkit.Replication.Edits.EditOutcomeLog.Of(world);

            sink.WriteAck(id, 77, EntityOperationStatus.InProgress);
            Assert.True(log.TryGet(id, out var o) && o.Status == Fdp.Toolkit.Replication.Edits.EditStatus.Pending && o.NetworkId == 77 && o.RespondingNode == 3);
            sink.WriteAck(id, 77, EntityOperationStatus.Success);
            Assert.True(log.TryGet(id, out o) && o.Status == Fdp.Toolkit.Replication.Edits.EditStatus.Applied);

            var refused = Guid.NewGuid();
            sink.WriteAck(refused, 0, EntityOperationStatus.UnknownDescriptorType);
            Assert.True(log.TryGet(refused, out o) && o.Status == Fdp.Toolkit.Replication.Edits.EditStatus.Refused);
            Assert.Contains("not known", o.Reason);
            Assert.Equal(3, inner.WrittenAcks.Count);
        }
}
}

