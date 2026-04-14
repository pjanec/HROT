using System;
using System.Collections.Generic;
using Hrot.CGF.Systems;
using Hrot.Core.Network;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="EntityRequestFinalizationSystem"/>.
    ///
    /// Verifies that Phase-2 ACKs are dispatched at the right lifecycle moment
    /// without requiring DDS or any network infrastructure.
    /// </summary>
    public class EntityRequestFinalizationSystemTests
    {
        // ── Factory helpers ───────────────────────────────────────────────────

        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            return repo;
        }

        private static (EntityRequestFinalizationSystem system, StubAckSink ackSink, NetworkEntityMap map)
            BuildSystem()
        {
            var ackSink = new StubAckSink();
            var map     = new NetworkEntityMap();
            var system  = new EntityRequestFinalizationSystem(ackSink, map);
            return (system, ackSink, map);
        }

        // ── Create path ───────────────────────────────────────────────────────

        /// <summary>
        /// Before the entity appears in the map, Execute should not dispatch any ACK.
        /// </summary>
        [Fact]
        public void Execute_Create_NoAck_WhenEntityNotInMapYet()
        {
            var world = CreateWorld();
            var (system, ackSink, _) = BuildSystem();
            var requestId = Guid.NewGuid();

            system.Track(networkId: 1001, requestId, RequestKind.Create);

            system.Execute(world, 0f);

            Assert.Empty(ackSink.WrittenAcks);
        }

        /// <summary>
        /// While the entity is Constructing (in map, alive, but not yet Active),
        /// Execute should not dispatch a Phase-2 ACK.
        /// </summary>
        [Fact]
        public void Execute_Create_NoAck_WhileEntityIsConstructing()
        {
            var world = CreateWorld();
            var (system, ackSink, map) = BuildSystem();
            var requestId = Guid.NewGuid();
            const long networkId = 2001;

            var entity = world.CreateEntity();
            world.SetLifecycleState(entity, EntityLifecycle.Constructing);
            map.Register(networkId, entity);

            system.Track(networkId, requestId, RequestKind.Create);
            system.Execute(world, 0f);

            Assert.Empty(ackSink.WrittenAcks);
        }

        /// <summary>
        /// Once the entity transitions to Active, Execute dispatches a Phase-2
        /// Success ACK with the correct RequestId and EntityId.
        /// </summary>
        [Fact]
        public void Execute_Create_SendsSuccessAck_WhenEntityBecomesActive()
        {
            var world = CreateWorld();
            var (system, ackSink, map) = BuildSystem();
            var requestId = Guid.NewGuid();
            const long networkId = 3001;

            var entity = world.CreateEntity();
            world.SetLifecycleState(entity, EntityLifecycle.Constructing);
            map.Register(networkId, entity);

            system.Track(networkId, requestId, RequestKind.Create);

            // First frame — still constructing.
            system.Execute(world, 0f);
            Assert.Empty(ackSink.WrittenAcks);

            // Transition to Active.
            world.SetLifecycleState(entity, EntityLifecycle.Active);

            // Second frame — should now send Phase-2 Success.
            system.Execute(world, 0f);

            Assert.Single(ackSink.WrittenAcks);
            var ack = ackSink.WrittenAcks[0];
            Assert.Equal(requestId,                    ack.RequestId);
            Assert.Equal((int)networkId,               ack.EntityId);
            Assert.Equal((int)EntityOperationStatus.Success, ack.StatusCode);
        }

        /// <summary>
        /// After the Phase-2 ACK is sent, calling Execute again must not
        /// re-dispatch the ACK (the tracked entry should be removed).
        /// </summary>
        [Fact]
        public void Execute_Create_DoesNotReDispatch_AfterSuccess()
        {
            var world = CreateWorld();
            var (system, ackSink, map) = BuildSystem();
            const long networkId = 4001;

            var entity = world.CreateEntity();
            world.SetLifecycleState(entity, EntityLifecycle.Active);
            map.Register(networkId, entity);

            system.Track(networkId, Guid.NewGuid(), RequestKind.Create);

            system.Execute(world, 0f);
            system.Execute(world, 0f); // second call — must be no-op

            Assert.Single(ackSink.WrittenAcks);
        }

        // ── Delete path ───────────────────────────────────────────────────────

        /// <summary>
        /// While the entity is still alive and in the map, deletion is not yet
        /// confirmed; no Phase-2 ACK should be sent.
        /// </summary>
        [Fact]
        public void Execute_Delete_NoAck_WhileEntityIsStillAlive()
        {
            var world = CreateWorld();
            var (system, ackSink, map) = BuildSystem();
            const long networkId = 5001;

            var entity = world.CreateEntity();
            map.Register(networkId, entity);

            system.Track(networkId, Guid.NewGuid(), RequestKind.Delete);
            system.Execute(world, 0f);

            Assert.Empty(ackSink.WrittenAcks);
        }

        /// <summary>
        /// Once the entity is removed from the map, Execute should dispatch a
        /// Phase-2 Success ACK confirming the deletion.
        /// </summary>
        [Fact]
        public void Execute_Delete_SendsSuccessAck_WhenEntityGone()
        {
            var world = CreateWorld();
            var (system, ackSink, map) = BuildSystem();
            var requestId = Guid.NewGuid();
            const long networkId = 6001;

            var entity = world.CreateEntity();
            map.Register(networkId, entity);

            system.Track(networkId, requestId, RequestKind.Delete);

            // Entity is alive — no ACK yet.
            system.Execute(world, 0f);
            Assert.Empty(ackSink.WrittenAcks);

            // Simulate teardown: unregister from map and destroy entity.
            map.Unregister(networkId, 0);
            world.DestroyEntity(entity);

            system.Execute(world, 0f);

            Assert.Single(ackSink.WrittenAcks);
            var ack = ackSink.WrittenAcks[0];
            Assert.Equal(requestId,                    ack.RequestId);
            Assert.Equal((int)networkId,               ack.EntityId);
            Assert.Equal((int)EntityOperationStatus.Success, ack.StatusCode);
        }
    }
}

