using System;
using System.Collections.Generic;
using Bagira.BDC.SSTM;
using Bagira.BDC.SSTD;
using Bagira.DDS.DM;
using Bagira.SimHost.Systems;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;
using ModuleHost.Core.Network.Interfaces;

namespace Bagira.SimHost.Tests
{
    // ─── Stubs ────────────────────────────────────────────────────────────────

    internal sealed class StubIdAllocator : INetworkIdAllocator
    {
        private long _next;
        public long LastAllocatedId { get; private set; }

        public StubIdAllocator(long startId = 100) => _next = startId;
        public long AllocateId() { LastAllocatedId = _next; return _next++; }
        public void Reset(long startId = 0) => _next = startId;
        public void Dispose() { }
    }

    internal sealed class StubRequestSource : ICreateEntityRequestSource
    {
        private readonly List<CreateEntityRequest> _pending = new();

        public void Enqueue(CreateEntityRequest r) => _pending.Add(r);

        public List<CreateEntityRequest> TakeRequests()
        {
            var result = new List<CreateEntityRequest>(_pending);
            _pending.Clear();
            return result;
        }
    }

    internal sealed class StubAckSink : ICreateEntityAckSink
    {
        public List<CreateEntityAck> WrittenAcks { get; } = new();
        public void WriteAck(CreateEntityAck ack) => WrittenAcks.Add(ack);
    }

    // ─── Tests ────────────────────────────────────────────────────────────────

    public class CreateEntityRequestSystemTests
    {
        private const long  ValidTkbType  = 42L;
        private const ulong ValidDisType  = 0x0100_0000_0000_0001UL;
        private const int   LocalNodeId   = 7;

        // ── Fixture helpers ──────────────────────────────────────────────────

        private static TkbDatabase CreateTkb()
        {
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("TestVehicle", ValidTkbType));
            return db;
        }

        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            // Register component types that SpawnEntityCommand will carry through the bus
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkOwnership>();
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            // Register events used by NetworkSpawningSystem if it were running
            repo.RegisterEvent<FDP.Toolkit.Lifecycle.Events.ConstructionOrder>();
            repo.RegisterEvent<FDP.Toolkit.Lifecycle.Events.DestructionOrder>();
            return repo;
        }

        private static CreateEntityRequest MakeValidRequest(long tkbType = ValidTkbType) =>
            new CreateEntityRequest
            {
                RequestId = Guid.NewGuid(),
                Owner = new NodeId { AppDomainId = 1, AppInstanceId = 2 },
                Flags = 0,
                InitialDescriptors = new List<EntityDescriptorUnion>
                {
                    new EntityDescriptorUnion
                    {
                        _d = EDescriptorType.dtEntityMaster,
                        EntityMaster = new EntityMaster { EntityId = 0, TkbType = tkbType, DisType = ValidDisType },
                    },
                },
            };

        private static (CreateEntityRequestSystem system, StubAckSink ackSink, StubIdAllocator idAlloc)
            BuildSystem(ITkbDatabase tkb, StubRequestSource requestSource)
        {
            var idAlloc = new StubIdAllocator(startId: 100);
            var ackSink = new StubAckSink();
            var system  = new CreateEntityRequestSystem(
                requestSource, ackSink, tkb, idAlloc, LocalNodeId);
            return (system, ackSink, idAlloc);
        }

        // ── Tests ────────────────────────────────────────────────────────────

        [Fact]
        public void ProcessRequest_ValidTkbType_PublishesSpawnEntityCommand()
        {
            // Arrange
            var repo    = CreateWorld();
            var tkb     = CreateTkb();
            var source  = new StubRequestSource();
            var request = MakeValidRequest();
            source.Enqueue(request);

            var (system, _, _) = BuildSystem(tkb, source);

            // Act
            system.Execute(repo, 0f);

            // Assert: SpawnEntityCommand must be in the write buffer — swap to make it visible
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ConsumeManagedEvents<SpawnEntityCommand>();

            Assert.Single(commands);
            var cmd = commands[0];
            Assert.Equal(ValidTkbType,  cmd.TkbType);
            Assert.Equal(ValidDisType,  cmd.DisType);
            Assert.Equal(LocalNodeId,   cmd.OwnerNodeId);
            Assert.Equal(request.RequestId, cmd.RequestId);
        }

        [Fact]
        public void ProcessRequest_UnknownTkbType_SendsErrorAck()
        {
            // Arrange
            var repo    = CreateWorld();
            var tkb     = CreateTkb();        // only ValidTkbType is registered
            var source  = new StubRequestSource();
            var request = MakeValidRequest(tkbType: 9999L);  // unknown type
            source.Enqueue(request);

            var (system, ackSink, _) = BuildSystem(tkb, source);

            // Act
            system.Execute(repo, 0f);

            // Assert: error ACK sent, no SpawnEntityCommand published
            Assert.Single(ackSink.WrittenAcks);
            Assert.Equal(404, ackSink.WrittenAcks[0].ErrorCode);
            Assert.Equal(request.RequestId, ackSink.WrittenAcks[0].RequestId);

            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ConsumeManagedEvents<SpawnEntityCommand>();
            Assert.Empty(commands);
        }

        [Fact]
        public void ProcessRequest_AllocatesNewNetworkId()
        {
            // Arrange
            var repo    = CreateWorld();
            var tkb     = CreateTkb();
            var source  = new StubRequestSource();
            source.Enqueue(MakeValidRequest());

            var (system, ackSink, idAlloc) = BuildSystem(tkb, source);

            // Act
            system.Execute(repo, 0f);

            // Assert: allocator was called and ACK carries the allocated ID
            Assert.Equal(100L, idAlloc.LastAllocatedId);
            Assert.Single(ackSink.WrittenAcks);
            Assert.Equal(100, ackSink.WrittenAcks[0].NewEntityId);
            Assert.Equal(0,   ackSink.WrittenAcks[0].ErrorCode);
        }
    }
}
