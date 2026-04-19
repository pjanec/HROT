using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.CGF.Systems;
using Hrot.Core.Network;
using Hrot.SimHost.Installers;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Tkb;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.NetworkSpawning;

namespace Hrot.SimHost.Tests
{
    // â”€â”€â”€ Stubs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    internal sealed class StubIdAllocator : INetworkIdAllocator
    {
        private long _next;
        public long LastAllocatedId { get; private set; }

        public StubIdAllocator(long startId = 100) => _next = startId;
        public long AllocateId() { LastAllocatedId = _next; return _next++; }
        public void Reset(long startId = 0) => _next = startId;
        public void Dispose() { }
    }

    internal sealed class StubRequestSource : IEntityCreationRequestSource
    {
        private readonly List<EntityCreationRequest> _pending = new();

        public void Enqueue(EntityCreationRequest r) => _pending.Add(r);

        public void ProcessRequests(Action<EntityCreationRequest> handler)
        {
            foreach (var req in _pending)
                handler(req);
            _pending.Clear();
        }

        public void Dispose() { }
    }

    internal struct AckRecord
    {
        public Guid RequestId;
        public int  EntityId;   // stored as int to match pre-existing assertions
        public int  StatusCode; // same values as EntityOperationStatus
    }

    internal sealed class StubAckSink : IEntityAckSink
    {
        public List<AckRecord> WrittenAcks { get; } = new();

        public void WriteAck(Guid requestId, long entityId, EntityOperationStatus status)
            => WrittenAcks.Add(new AckRecord
            {
                RequestId  = requestId,
                EntityId   = (int)entityId,
                StatusCode = (int)status,
            });

        public void Dispose() { }
    }

    internal sealed class StubOwnershipStrategy : Fdp.Toolkit.Replication.Abstractions.IOwnershipDistributionStrategy
    {
        private readonly List<DescriptorGrant> _grants = new();

        public void AddGrant(long descriptorTypeId, int nodeId)
            => _grants.Add(new DescriptorGrant { DescriptorTypeId = descriptorTypeId, NodeId = nodeId });

        public IReadOnlyList<DescriptorGrant> GetInitialGrants(Fdp.Core.DISEntityType entityType, int masterNodeId)
            => _grants;
    }

    // â”€â”€â”€ Tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public class CreateEntityRequestSystemTests
    {
        private const long  ValidTkbType  = 42L;
        // ValidDisType is kept as ulong for engine-side assertions.
        private const ulong ValidDisType  = 0x0100_0000_0000_0001UL;
        private const int   LocalNodeId   = 7;

        // â”€â”€ Fixture helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
            repo.RegisterEvent<Fdp.Toolkit.Lifecycle.Events.ConstructionOrder>();
            repo.RegisterEvent<Fdp.Toolkit.Lifecycle.Events.DestructionOrder>();
            return repo;
        }

        private static EntityCreationRequest MakeValidRequest(long tkbType = ValidTkbType) =>
            new EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = LocalNodeId,
                TkbType            = tkbType,
                DisType            = ValidDisType,
            };

        private static (CreateEntityRequestSystem system, StubAckSink ackSink, StubIdAllocator idAlloc)
            BuildSystem(ITkbDatabase tkb, StubRequestSource requestSource)
        {
            var idAlloc = new StubIdAllocator(startId: 100);
            var ackSink = new StubAckSink();
            var system  = new CreateEntityRequestSystem(
                requestSource, ackSink, tkb, idAlloc, LocalNodeId,
                jsonAttributeCompiler: null);
            return (system, ackSink, idAlloc);
        }

        // â”€â”€ Tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

            // Assert: SpawnEntityCommand must be in the write buffer â€” swap to make it visible
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();

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
            Assert.Equal((int)EntityOperationStatus.UnknownDescriptorType, ackSink.WrittenAcks[0].StatusCode);
            Assert.Equal(request.RequestId, ackSink.WrittenAcks[0].RequestId);

            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
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

            // Assert: allocator was called and Phase-1 InProgress ACK carries the allocated ID
            Assert.Equal(100L, idAlloc.LastAllocatedId);
            Assert.Single(ackSink.WrittenAcks);
            Assert.Equal(100, ackSink.WrittenAcks[0].EntityId);
            Assert.Equal((int)EntityOperationStatus.InProgress, ackSink.WrittenAcks[0].StatusCode);
        }

        // â”€â”€ GC04: Time-slicing â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void TimeSlicing_OnThousandRequests_DispatchesExactlyMaxPerTickOnFirstFrame()
        {
            // Arrange â€” enqueue 1000 valid requests.
            const int TotalRequests = 1000;
            var repo   = CreateWorld();
            var tkb    = CreateTkb();
            var source = new StubRequestSource();
            for (int i = 0; i < TotalRequests; i++)
                source.Enqueue(MakeValidRequest());

            var (system, _, _) = BuildSystem(tkb, source);

            // Act â€” single tick.
            system.Execute(repo, 0f);

            // Assert: exactly MaxRequestsPerTick commands published this tick.
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            Assert.Equal(CreateEntityRequestSystem.MaxRequestsPerTick, commands.Count);

            // Assert: remaining requests are still in the queue.
            int expectedRemaining = TotalRequests - CreateEntityRequestSystem.MaxRequestsPerTick;
            Assert.Equal(expectedRemaining, system.PendingQueueCount);
        }

        [Fact]
        public void TimeSlicing_OnThousandRequests_AcksAllSentOnFirstFrame()
        {
            // Arrange â€” enqueue 1000 valid requests.
            const int TotalRequests = 1000;
            var repo   = CreateWorld();
            var tkb    = CreateTkb();
            var source = new StubRequestSource();
            for (int i = 0; i < TotalRequests; i++)
                source.Enqueue(MakeValidRequest());

            var idAlloc = new StubIdAllocator(startId: 1);
            var ackSink = new StubAckSink();
            var system  = new CreateEntityRequestSystem(
                source, ackSink, tkb, idAlloc, LocalNodeId,
                jsonAttributeCompiler: null);

            // Act â€” single tick.
            system.Execute(repo, 0f);

            // Assert: all 1000 Phase-1 InProgress ACKs sent synchronously, even though
            // only MaxRequestsPerTick SpawnEntityCommands were dispatched this frame.
            Assert.Equal(TotalRequests, ackSink.WrittenAcks.Count);

            // Every Phase-1 ACK must be InProgress.
            foreach (var ack in ackSink.WrittenAcks)
                Assert.Equal((int)EntityOperationStatus.InProgress, ack.StatusCode);
        }

        [Fact]
        public void TimeSlicing_SecondFrame_ProcessesRemainingRequests()
        {            // Arrange â€” enqueue exactly MaxRequestsPerTick + 1 requests.
            int totalRequests = CreateEntityRequestSystem.MaxRequestsPerTick + 1;
            var repo   = CreateWorld();
            var tkb    = CreateTkb();
            var source = new StubRequestSource();
            for (int i = 0; i < totalRequests; i++)
                source.Enqueue(MakeValidRequest());

            var (system, _, _) = BuildSystem(tkb, source);

            // Act â€” first tick: should dispatch MaxRequestsPerTick, leave 1 in queue.
            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            var firstFrameCmds = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            // Capture count *before* a second swap clears the backing buffer.
            int firstFrameCount = firstFrameCmds.Count;

            // Act â€” second tick: source is empty, queue has 1 item.
            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            var secondFrameCmds = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();

            // Assert
            Assert.Equal(CreateEntityRequestSystem.MaxRequestsPerTick, firstFrameCount);
            Assert.Single(secondFrameCmds);
            Assert.Equal(0, system.PendingQueueCount);
        }

        // â”€â”€ ATTR-JSON: JSON attribute path â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static EntityRepository CreateWorldWithIgEntityData()
        {
            var repo = CreateWorld();
            repo.RegisterComponent<EntityInfo>();
            return repo;
        }

        /// <summary>
        /// When <c>InitialAttributesJson</c> contains a Name patch the entity spawns
        /// with the correct name via the JSON compiler path.
        /// </summary>
        [Fact]
        public void ProcessRequest_Json_PatchesName()
        {
            // Arrange
            var repo   = CreateWorldWithIgEntityData();
            var tkb    = CreateTkb();
            var source = new StubRequestSource();
            var request = new EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = LocalNodeId,
                TkbType            = ValidTkbType,
                DisType            = ValidDisType,
                InitialAttributesJson = "{\"Name\":\"Delta\"}",
            };
            source.Enqueue(request);

            var jsonCompiler = AttributeCompilerFactory.Build(null);
            var idAlloc      = new StubIdAllocator(startId: 100);
            var ackSink      = new StubAckSink();
            var system       = new CreateEntityRequestSystem(
                source, ackSink, tkb, idAlloc, LocalNodeId,
                jsonAttributeCompiler: jsonCompiler);

            // Act
            system.Execute(repo, 0f);

            // Assert: SpawnEntityCommand carries EntityInfo with Name = "Delta".
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            Assert.Single(commands);

            var initialComponents = commands[0].InitialComponents;
            Assert.NotNull(initialComponents);
            var entityData = Assert.Single(initialComponents!.OfType<EntityInfo>());
            Assert.Equal("Delta", entityData.Name.ToString());
        }

        // â”€â”€ BD1-P7T1: Delegate caching â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// BD1-P7T1 SC1: The delegate passed to <c>ProcessRequests</c> must be the same
        /// cached instance on every <c>Execute</c> call â€” ReferenceEquals must return
        /// <c>true</c> across two separate Execute invocations.
        /// </summary>
        [Fact]
        public void ProcessRequests_UsesPreCachedDelegate()
        {
            var repo   = CreateWorld();
            var tkb    = CreateTkb();
            var source = new CapturingRequestSource();
            var ackSink = new StubAckSink();
            var idAlloc = new StubIdAllocator(startId: 100);

            var system = new CreateEntityRequestSystem(
                source, ackSink, tkb, idAlloc, LocalNodeId,
                jsonAttributeCompiler: null);

            // Execute twice so the source can capture the delegate both times.
            system.Execute(repo, 0f);
            system.Execute(repo, 0f);

            Assert.Equal(2, source.CapturedDelegates.Count);
            Assert.True(
                ReferenceEquals(source.CapturedDelegates[0], source.CapturedDelegates[1]),
                "The delegate passed to ProcessRequests must be the same cached instance on every Execute call.");
        }

        /// <summary>
        /// BD1-P7T1 SC2: Refactored path must preserve existing behaviour â€”
        /// a valid request still produces a SpawnEntityCommand and an ACK.
        /// </summary>
        [Fact]
        public void ProcessRequests_DelegateCache_BehaviourRegression()
        {
            var repo    = CreateWorld();
            var tkb     = CreateTkb();
            var source  = new StubRequestSource();
            source.Enqueue(MakeValidRequest());

            var (system, ackSink, _) = BuildSystem(tkb, source);
            system.Execute(repo, 0f);

            // ACK sent (Phase-1 InProgress â€” Phase-2 is dispatched by NedRequestFinalizationSystem).
            Assert.Single(ackSink.WrittenAcks);
            Assert.Equal((int)EntityOperationStatus.InProgress, ackSink.WrittenAcks[0].StatusCode);

            // SpawnEntityCommand produced.
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            Assert.Single(commands);
        }

        [Fact]
        public void ProcessRequest_DefaultProcessor_PublishesDeferredTakeOwnershipForNavigationStatus()
        {
            var repo    = CreateWorld();
            var tkb     = CreateTkb();
            var source  = new StubRequestSource();
            source.Enqueue(MakeValidRequest());

            var ackSink = new StubAckSink();
            var idAlloc = new StubIdAllocator(startId: 100);
            var ownershipStrategy = new StubOwnershipStrategy();
            ownershipStrategy.AddGrant(DescriptorTypeOrdinals.WorldPos, 11);
            ownershipStrategy.AddGrant(DescriptorTypeOrdinals.NavigationStatus, 11);

            var system = new CreateEntityRequestSystem(
                source, ackSink, tkb, idAlloc, LocalNodeId,
                jsonAttributeCompiler: null,
                finalizationSystem: null,
                isDefaultProcessor: true,
                ownershipStrategy: ownershipStrategy);

            system.Execute(repo, 0f);

            repo.Bus.SwapBuffers();
            var dtoCommands = ((ISimulationView)repo).ReadManagedEvents<DeferredTakeOwnershipCommand>();

            Assert.Single(dtoCommands);
            Assert.Contains(dtoCommands[0].Grants, g => g.DescriptorTypeId == DescriptorTypeOrdinals.WorldPos && g.NodeId == 11);
            Assert.Contains(dtoCommands[0].Grants, g => g.DescriptorTypeId == DescriptorTypeOrdinals.NavigationStatus && g.NodeId == 11);
        }
    }

    // â”€â”€ Delegate capture test helper â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// A <see cref="ICreateEntityRequestSource"/> stub that records the delegate
    /// instance passed to <see cref="ProcessRequests"/> on each call, allowing
    /// tests to verify the delegate is cached (same instance on repeated calls).
    /// </summary>
    internal sealed class CapturingRequestSource : IEntityCreationRequestSource
    {
        public List<Action<EntityCreationRequest>> CapturedDelegates { get; } = new();

        public void ProcessRequests(Action<EntityCreationRequest> handler)
            => CapturedDelegates.Add(handler);

        public void Dispose() { }
    }
}
