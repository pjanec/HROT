using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.NED.Messages;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using Hrot.IG.Components;
using Hrot.SimHost.Systems;
using Hrot.SimHost.Installers;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Patching;
using Fdp.Toolkit.Tkb;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;
using ModuleHost.Core.Network.Interfaces;

namespace Hrot.SimHost.Tests
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

        public void ProcessRequests(Action<CreateEntityRequest> processor)
        {
            foreach (var req in _pending)
                processor(req);
            _pending.Clear();
        }
    }

    internal sealed class StubAckSink : ICreateUpdateDeleteEntityAckSink
    {
        public List<CreateUpdateDeleteEntityAck> WrittenAcks { get; } = new();
        public void WriteAck(CreateUpdateDeleteEntityAck ack) => WrittenAcks.Add(ack);
    }

    // ─── Tests ────────────────────────────────────────────────────────────────

    public class CreateEntityRequestSystemTests
    {
        private const long  ValidTkbType  = 42L;
        // ValidDisType is kept as ulong for engine-side assertions; the wire DisTypeStruct
        // decomposes it as Kind=1, Extra=1 (0x01_00_0000_0000_0001).
        private const ulong ValidDisType  = 0x0100_0000_0000_0001UL;
        private static readonly DisTypeStruct ValidDisTypeStruct = new DisTypeStruct { Kind = 1, Extra = 1 };
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
                        EntityMaster = new EntityMaster { EntityId = 0, TkbType = tkbType, DisType = ValidDisTypeStruct },
                    },
                },
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
            Assert.Equal((int)NedStatusCode.UnknownDescriptorType, ackSink.WrittenAcks[0].StatusCode);
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

            // Assert: allocator was called and Phase-1 InProgress ACK carries the allocated ID
            Assert.Equal(100L, idAlloc.LastAllocatedId);
            Assert.Single(ackSink.WrittenAcks);
            Assert.Equal(100, ackSink.WrittenAcks[0].EntityId);
            Assert.Equal((int)NedStatusCode.InProgress, ackSink.WrittenAcks[0].StatusCode);
        }

        // ── GC02: Struct component extraction ────────────────────────────────

        /// <summary>
        /// A trivial geographic transform stub: lat→Y, lon→X, alt→Z.
        /// </summary>
        private sealed class StubGeoTransform : IGeographicTransform
        {
            public void SetOrigin(double lat, double lon, double alt) { }
            public Vector3 ToCartesian(double lat, double lon, double alt)
                => new Vector3((float)lon, (float)lat, (float)alt);
            public (double lat, double lon, double alt) ToGeodetic(Vector3 p)
                => (p.Y, p.X, p.Z);
        }

        private static CreateEntityRequest MakeRequestWithGeoSpatial(
            long tkbType = ValidTkbType,
            double lat = 10.0,
            double lon = 20.0) =>
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
                        EntityMaster = new EntityMaster { EntityId = 0, TkbType = tkbType, DisType = ValidDisTypeStruct },
                    },
                    new EntityDescriptorUnion
                    {
                        _d = EDescriptorType.dtWorldPos,
                        WorldPos = new WorldPos { Pos = new GeoPoint { Latitude = lat, Longitude = lon, Altitude = 0 } },
                    },
                },
            };

        [Fact]
        public void ProcessRequest_SimTransformDescriptor_PromotedToTypedField_NotInFallbackList()
        {
            // Arrange — supply a GeoSpatial descriptor so DescriptorMapper emits a SimTransform.
            var repo    = CreateWorld();
            var tkb     = CreateTkb();
            var source  = new StubRequestSource();
            var request = MakeRequestWithGeoSpatial(lat: 10.0, lon: 20.0);
            source.Enqueue(request);

            var geoTransform = new StubGeoTransform();
            var idAlloc  = new StubIdAllocator(startId: 200);
            var ackSink  = new StubAckSink();
            var system   = new CreateEntityRequestSystem(
                source, ackSink, tkb, idAlloc, LocalNodeId, geoTransform,
                jsonAttributeCompiler: null);

            // Act
            system.Execute(repo, 0f);

            // Assert: SpawnEntityCommand has InitialTransform set; SimTransform NOT in fallback list.
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ConsumeManagedEvents<SpawnEntityCommand>();

            Assert.Single(commands);
            var cmd = commands[0];

            // Typed field must be populated.
            Assert.True(cmd.InitialTransform.HasValue,
                "SimTransform should be promoted to InitialTransform, not left in InitialComponents.");

            // StubGeoTransform: X=lon=20, Y=lat=10, Z=alt=0.
            Assert.Equal(20f, cmd.InitialTransform!.Value.Position.X, precision: 3);
            Assert.Equal(10f, cmd.InitialTransform!.Value.Position.Y, precision: 3);

            // SimTransform must NOT appear in the fallback list.
            if (cmd.InitialComponents != null)
            {
                foreach (var c in cmd.InitialComponents)
                    Assert.False(c is SimTransform,
                        "SimTransform must not be boxed into the fallback InitialComponents list.");
            }
        }

        [Fact]
        public void ProcessRequest_NoSimTransformDescriptor_InitialTransformIsNull()
        {
            // Arrange — no GeoSpatial descriptor, so no SimTransform should be generated.
            var repo    = CreateWorld();
            var tkb     = CreateTkb();
            var source  = new StubRequestSource();
            source.Enqueue(MakeValidRequest());

            var (system, _, _) = BuildSystem(tkb, source);

            // Act
            system.Execute(repo, 0f);

            // Assert
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ConsumeManagedEvents<SpawnEntityCommand>();

            Assert.Single(commands);
            Assert.False(commands[0].InitialTransform.HasValue,
                "InitialTransform should be null when no GeoSpatial descriptor is present.");
        }

        // ── GC04: Time-slicing ────────────────────────────────────────────────

        [Fact]
        public void TimeSlicing_OnThousandRequests_DispatchesExactlyMaxPerTickOnFirstFrame()
        {
            // Arrange — enqueue 1000 valid requests.
            const int TotalRequests = 1000;
            var repo   = CreateWorld();
            var tkb    = CreateTkb();
            var source = new StubRequestSource();
            for (int i = 0; i < TotalRequests; i++)
                source.Enqueue(MakeValidRequest());

            var (system, _, _) = BuildSystem(tkb, source);

            // Act — single tick.
            system.Execute(repo, 0f);

            // Assert: exactly MaxRequestsPerTick commands published this tick.
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ConsumeManagedEvents<SpawnEntityCommand>();
            Assert.Equal(CreateEntityRequestSystem.MaxRequestsPerTick, commands.Count);

            // Assert: remaining requests are still in the queue.
            int expectedRemaining = TotalRequests - CreateEntityRequestSystem.MaxRequestsPerTick;
            Assert.Equal(expectedRemaining, system.PendingQueueCount);
        }

        [Fact]
        public void TimeSlicing_OnThousandRequests_AcksAllSentOnFirstFrame()
        {
            // Arrange — enqueue 1000 valid requests.
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

            // Act — single tick.
            system.Execute(repo, 0f);

            // Assert: all 1000 Phase-1 InProgress ACKs sent synchronously, even though
            // only MaxRequestsPerTick SpawnEntityCommands were dispatched this frame.
            Assert.Equal(TotalRequests, ackSink.WrittenAcks.Count);

            // Every Phase-1 ACK must be InProgress.
            foreach (var ack in ackSink.WrittenAcks)
                Assert.Equal((int)NedStatusCode.InProgress, ack.StatusCode);
        }

        [Fact]
        public void TimeSlicing_SecondFrame_ProcessesRemainingRequests()
        {            // Arrange — enqueue exactly MaxRequestsPerTick + 1 requests.
            int totalRequests = CreateEntityRequestSystem.MaxRequestsPerTick + 1;
            var repo   = CreateWorld();
            var tkb    = CreateTkb();
            var source = new StubRequestSource();
            for (int i = 0; i < totalRequests; i++)
                source.Enqueue(MakeValidRequest());

            var (system, _, _) = BuildSystem(tkb, source);

            // Act — first tick: should dispatch MaxRequestsPerTick, leave 1 in queue.
            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            var firstFrameCmds = ((ISimulationView)repo).ConsumeManagedEvents<SpawnEntityCommand>();
            // Capture count *before* a second swap clears the backing buffer.
            int firstFrameCount = firstFrameCmds.Count;

            // Act — second tick: source is empty, queue has 1 item.
            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            var secondFrameCmds = ((ISimulationView)repo).ConsumeManagedEvents<SpawnEntityCommand>();

            // Assert
            Assert.Equal(CreateEntityRequestSystem.MaxRequestsPerTick, firstFrameCount);
            Assert.Single(secondFrameCmds);
            Assert.Equal(0, system.PendingQueueCount);
        }

        // ── ATTR2-P5T1: Binary attribute records path ─────────────────────────

        private static BinaryInterpreter<AttributeRecord> BuildBinaryInterpreter()
            => AttributeCompilerFactory.BuildBinaryInterpreter(null);

        private static EntityRepository CreateWorldWithIgEntityData()
        {
            var repo = CreateWorld();
            repo.RegisterComponent<IG.Components.EntityInfo>();
            return repo;
        }

		/// <summary>
		/// A <see cref="CreateEntityRequest"/> with <c>InitialAttributeRecords</c> containing
		/// a Name record must produce a <c>SpawnEntityCommand</c> whose
		/// <see cref="SpawnEntityCommand.InitialComponents"/> includes an
		/// <see cref="IG.Components.EntityInfo"/> with the expected name.
		/// </summary>
		[Fact]
        public void ProcessRequest_BinaryRecords_NameRecord_EntitySpawnedWithCorrectName()
        {
            // Arrange
            var repo   = CreateWorldWithIgEntityData();
            var tkb    = CreateTkb();
            var source = new StubRequestSource();
            var request = MakeValidRequest();
            request.InitialAttributeRecords = new List<AttributeRecord>
            {
                new AttributeRecord
                {
                    AttributeId = AttributeIds.Name,
                    Value = new AttributeValueUnion
                    {
                        ValueType   = AttributeValueType.KindString,
                        StringValue = "Gamma",
                    }
                }
            };
            source.Enqueue(request);

            var idAlloc  = new StubIdAllocator(startId: 100);
            var ackSink  = new StubAckSink();
            var system   = new CreateEntityRequestSystem(
                source, ackSink, tkb, idAlloc, LocalNodeId,
                binaryInterpreter: BuildBinaryInterpreter());

            // Act
            system.Execute(repo, 0f);

            // Assert: SpawnEntityCommand carries IgEntityData with Name = "Gamma".
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ConsumeManagedEvents<SpawnEntityCommand>();
            Assert.Single(commands);

            var initialComponents = commands[0].InitialComponents;
            Assert.NotNull(initialComponents);
            var entityData = Assert.Single(initialComponents!.OfType<IG.Components.EntityInfo>());
            Assert.Equal("Gamma", entityData.Name.ToString());
        }

        /// <summary>
        /// When <c>InitialAttributeRecords</c> is null, the system falls back to the JSON path
        /// and the entity spawns with the name from <c>InitialAttributesJson</c>.
        /// </summary>
        [Fact]
        public void ProcessRequest_NullBinaryRecords_FallsBackToJsonPath()
        {
            // Arrange
            var repo   = CreateWorldWithIgEntityData();
            var tkb    = CreateTkb();
            var source = new StubRequestSource();
            var request = MakeValidRequest();
            request.InitialAttributeRecords = null;
            request.InitialAttributesJson   = "{\"Name\":\"Delta\"}";
            source.Enqueue(request);

            var jsonCompiler = AttributeCompilerFactory.Build(null);
            var idAlloc      = new StubIdAllocator(startId: 100);
            var ackSink      = new StubAckSink();
            var system       = new CreateEntityRequestSystem(
                source, ackSink, tkb, idAlloc, LocalNodeId,
                jsonAttributeCompiler: jsonCompiler,
                binaryInterpreter:     BuildBinaryInterpreter());

            // Act
            system.Execute(repo, 0f);

            // Assert: JSON fallback produces entity with Name = "Delta".
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ConsumeManagedEvents<SpawnEntityCommand>();
            Assert.Single(commands);

            var initialComponents2 = commands[0].InitialComponents;
            Assert.NotNull(initialComponents2);
            var entityData2 = Assert.Single(initialComponents2!.OfType<IG.Components.EntityInfo>());
            Assert.Equal("Delta", entityData2.Name.ToString());
        }

        /// <summary>
        /// When both <c>InitialAttributeRecords</c> and <c>InitialAttributesJson</c> are null,
        /// the entity still spawns without exception (TKB defaults apply).
        /// </summary>
        [Fact]
        public void ProcessRequest_BothNull_EntitySpawnsWithoutException()
        {
            // Arrange
            var repo   = CreateWorldWithIgEntityData();
            var tkb    = CreateTkb();
            var source = new StubRequestSource();
            var request = MakeValidRequest();
            request.InitialAttributeRecords = null;
            request.InitialAttributesJson   = null;
            source.Enqueue(request);

            var idAlloc = new StubIdAllocator(startId: 100);
            var ackSink = new StubAckSink();
            var system  = new CreateEntityRequestSystem(
                source, ackSink, tkb, idAlloc, LocalNodeId,
                binaryInterpreter: BuildBinaryInterpreter());

            // Act — should not throw.
            system.Execute(repo, 0f);

            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ConsumeManagedEvents<SpawnEntityCommand>();
            Assert.Single(commands);
        }

        /// <summary>
        /// When both binary records AND JSON are provided, the binary records take precedence
        /// and the JSON payload is ignored.
        /// </summary>
        [Fact]
        public void ProcessRequest_BinaryAndJson_BinaryTakesPrecedence()
        {
            // Arrange
            var repo   = CreateWorldWithIgEntityData();
            var tkb    = CreateTkb();
            var source = new StubRequestSource();
            var request = MakeValidRequest();
            request.InitialAttributeRecords = new List<AttributeRecord>
            {
                new AttributeRecord
                {
                    AttributeId = AttributeIds.Name,
                    Value = new AttributeValueUnion
                    {
                        ValueType   = AttributeValueType.KindString,
                        StringValue = "BinaryWins",
                    }
                }
            };
            request.InitialAttributesJson = "{\"Name\":\"JsonLoses\"}";
            source.Enqueue(request);

            var jsonCompiler = AttributeCompilerFactory.Build(null);
            var idAlloc      = new StubIdAllocator(startId: 100);
            var ackSink      = new StubAckSink();
            var system       = new CreateEntityRequestSystem(
                source, ackSink, tkb, idAlloc, LocalNodeId,
                jsonAttributeCompiler: jsonCompiler,
                binaryInterpreter:     BuildBinaryInterpreter());

            // Act
            system.Execute(repo, 0f);

            // Assert: name comes from binary records, NOT from JSON.
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ConsumeManagedEvents<SpawnEntityCommand>();
            Assert.Single(commands);

            var initialComponents3 = commands[0].InitialComponents;
            Assert.NotNull(initialComponents3);
            var entityData3 = Assert.Single(initialComponents3!.OfType<IG.Components.EntityInfo>());
            Assert.Equal("BinaryWins", entityData3.Name.ToString());
        }

        // ── BD1-P7T1: Delegate caching ────────────────────────────────────────

        /// <summary>
        /// BD1-P7T1 SC1: The delegate passed to <c>ProcessRequests</c> must be the same
        /// cached instance on every <c>Execute</c> call — ReferenceEquals must return
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
        /// BD1-P7T1 SC2: Refactored path must preserve existing behaviour —
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

            // ACK sent (Phase-1 InProgress — Phase-2 is dispatched by NedRequestFinalizationSystem).
            Assert.Single(ackSink.WrittenAcks);
            Assert.Equal((int)NedStatusCode.InProgress, ackSink.WrittenAcks[0].StatusCode);

            // SpawnEntityCommand produced.
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ConsumeManagedEvents<SpawnEntityCommand>();
            Assert.Single(commands);
        }
    }

    // ── Delegate capture test helper ─────────────────────────────────────────

    /// <summary>
    /// A <see cref="ICreateEntityRequestSource"/> stub that records the delegate
    /// instance passed to <see cref="ProcessRequests"/> on each call, allowing
    /// tests to verify the delegate is cached (same instance on repeated calls).
    /// </summary>
    internal sealed class CapturingRequestSource : ICreateEntityRequestSource
    {
        public List<Action<CreateEntityRequest>> CapturedDelegates { get; } = new();

        public void ProcessRequests(Action<CreateEntityRequest> processor)
            => CapturedDelegates.Add(processor);
    }
}
