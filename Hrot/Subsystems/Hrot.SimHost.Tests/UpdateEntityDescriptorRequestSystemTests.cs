using System;
using System.Numerics;
using System.Threading;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using Hrot.Map.Common.Replication.Ingress;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using Fdp.ModuleHost_Core.Network;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Integration tests for <see cref="UpdateEntityDescriptorRequestSystem"/> verifying the
    /// Silent Bystander Rule (BUG1-N001): non-authoritative paths must emit no
    /// <see cref="UpdateEntityDescriptorAck"/>, only debug log entries.
    /// </summary>
    [Collection("SimHostDds")]
    public class UpdateEntityDescriptorRequestSystemTests
    {
        private const int EntityId = 42;

        // ── Helpers ───────────────────────────────────────────────────────────

        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkOwnership>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<SimTransform>();
            return repo;
        }

        private sealed class StubGeoTransform : IGeographicTransform
        {
            public void SetOrigin(double lat, double lon, double alt) { }
            public Vector3 ToCartesian(double lat, double lon, double alt)
                => new Vector3((float)lon, (float)lat, (float)alt);
            public (double lat, double lon, double alt) ToGeodetic(Vector3 p)
                => (p.Y, p.X, p.Z);
        }

        private static UpdateEntityDescriptorRequest MakeGeoRequest(int entityId)
            => new UpdateEntityDescriptorRequest
            {
                RequestId    = Guid.NewGuid(),
                EntityId     = entityId,
                DescriptorType = EDescriptorType.dtWorldPos,
                Payload      = new EntityDescriptorUnion
                {
                    _d        = EDescriptorType.dtWorldPos,
                    WorldPos = new WorldPos
                    {
                        EntityId = entityId,
                        Pos      = new GeoPoint { Latitude = 10.0, Longitude = 20.0, Altitude = 0.0 }
                    }
                }
            };

        private static UpdateEntityDescriptorRequest MakeUnsupportedTypeRequest(int entityId)
            => new UpdateEntityDescriptorRequest
            {
                RequestId      = Guid.NewGuid(),
                EntityId       = entityId,
                // dtEntityMaster is not handled in the switch → hits default (unsupported) path
                DescriptorType = EDescriptorType.dtEntityMaster,
                Payload        = new EntityDescriptorUnion
                {
                    _d           = EDescriptorType.dtEntityMaster,
                    EntityMaster = new EntityMaster { EntityId = entityId }
                }
            };

        /// <summary>Reads all valid ACK samples from a reader; returns count.</summary>
        /// <summary>Stub IDdsWriter that captures all Write calls for assertion.</summary>
        private sealed class StubAckWriter : Hrot.Map.Common.Dds.IDdsWriter<UpdateEntityDescriptorAck>
        {
            public List<UpdateEntityDescriptorAck> Written { get; } = new();
            public void Write(UpdateEntityDescriptorAck value) => Written.Add(value);
            public void DisposeInstance(UpdateEntityDescriptorAck key) { }
        }

        private static int CountAcks(DdsReader<UpdateEntityDescriptorAck> reader)
        {
            int count = 0;
            using var loan = reader.Take();
            foreach (var sample in loan)
            {
                if (sample.IsValid) count++;
            }
            return count;
        }

        // ── BUG1-N001 Tests ───────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Integration")]
        public void EntityNotFound_EmitsNoAck()
        {
            // SETUP: entity 999 is NOT in the entity map
            const uint domain = 210u;
            using var participant = new DdsParticipant(domain);
            var entityMap  = new NetworkEntityMap();
            var system     = new UpdateEntityDescriptorRequestSystem(participant, entityMap, new StubGeoTransform());
            var repo       = CreateWorld();
            system.Create(repo);

            using var reqWriter = new DdsWriter<UpdateEntityDescriptorRequest>(participant, "UpdateEntityDescriptorRequest");
            using var ackReader = new DdsReader<UpdateEntityDescriptorAck>(participant, "UpdateEntityDescriptorAck");

            Thread.Sleep(100); // allow pub/sub matching

            reqWriter.Write(MakeGeoRequest(entityId: 999));
            Thread.Sleep(30);

            system.Run();
            Thread.Sleep(10);

            // ASSERT: no ACK should be written
            Assert.Equal(0, CountAcks(ackReader));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void NotAuthoritative_GeoSpatial_EmitsNoAck()
        {
            // SETUP: entity exists but this node is NOT authoritative
            const uint domain = 211u;
            using var participant = new DdsParticipant(domain);
            var entityMap = new NetworkEntityMap();
            var system    = new UpdateEntityDescriptorRequestSystem(participant, entityMap, new StubGeoTransform());
            var repo      = CreateWorld();
            system.Create(repo);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(EntityId));
            // PrimaryOwner=2, Local=1 → HasAuthority=false
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            entityMap.Register(EntityId, entity);

            using var reqWriter = new DdsWriter<UpdateEntityDescriptorRequest>(participant, "UpdateEntityDescriptorRequest");
            using var ackReader = new DdsReader<UpdateEntityDescriptorAck>(participant, "UpdateEntityDescriptorAck");

            Thread.Sleep(50);

            reqWriter.Write(MakeGeoRequest(EntityId));
            Thread.Sleep(30);

            system.Run();
            Thread.Sleep(10);

            Assert.Equal(0, CountAcks(ackReader));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void UnsupportedDescriptorType_EmitsNoAck()
        {
            // SETUP: entity exists and IS authoritative, but descriptor type is unrecognised
            const uint domain = 212u;
            using var participant = new DdsParticipant(domain);
            var entityMap = new NetworkEntityMap();
            var system    = new UpdateEntityDescriptorRequestSystem(participant, entityMap, new StubGeoTransform());
            var repo      = CreateWorld();
            system.Create(repo);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(EntityId));
            // Authoritative — but unsupported type should still yield no ACK
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            entityMap.Register(EntityId, entity);

            using var reqWriter = new DdsWriter<UpdateEntityDescriptorRequest>(participant, "UpdateEntityDescriptorRequest");
            using var ackReader = new DdsReader<UpdateEntityDescriptorAck>(participant, "UpdateEntityDescriptorAck");

            Thread.Sleep(50);

            reqWriter.Write(MakeUnsupportedTypeRequest(EntityId));
            Thread.Sleep(30);

            system.Run();
            Thread.Sleep(10);

            Assert.Equal(0, CountAcks(ackReader));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void Authoritative_GeoSpatial_EmitsSuccessAck()
        {
            // SETUP: entity exists AND this node IS authoritative → Success ACK expected
            const uint domain = 213u;
            using var participant = new DdsParticipant(domain);
            var entityMap = new NetworkEntityMap();
            var system    = new UpdateEntityDescriptorRequestSystem(participant, entityMap, new StubGeoTransform());
            var repo      = CreateWorld();
            system.Create(repo);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(EntityId));
            // PrimaryOwner=1, Local=1 → HasAuthority=true
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            entityMap.Register(EntityId, entity);

            using var reqWriter = new DdsWriter<UpdateEntityDescriptorRequest>(participant, "UpdateEntityDescriptorRequest");
            using var ackReader = new DdsReader<UpdateEntityDescriptorAck>(participant, "UpdateEntityDescriptorAck");

            Thread.Sleep(50);

            var req = MakeGeoRequest(EntityId);
            reqWriter.Write(req);
            Thread.Sleep(30);

            system.Run();
            Thread.Sleep(30);

            // ASSERT: exactly one Success ACK
            int ackCount = 0;
            bool hasSuccess = false;
            using var loan = ackReader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ackCount++;
                if (sample.Data.EntityId == EntityId &&
                    sample.Data.ErrorCode == (int)NedStatusCode.Success)
                    hasSuccess = true;
            }

            Assert.Equal(1, ackCount);
            Assert.True(hasSuccess, "Expected exactly one Success ACK for authoritative GeoSpatial update.");
        }

        // ── BUG1-T001: IDdsWriter injection ───────────────────────────────────

        [Fact]
        [Trait("Category", "Integration")]
        public void InjectedAckWriter_NotAuthoritative_WriterNotCalled()
        {
            // SETUP: entity is NOT authoritative — injected stub must receive zero writes
            const uint domain = 214u;
            var stub      = new StubAckWriter();
            using var participant = new DdsParticipant(domain);
            var entityMap = new NetworkEntityMap();
            var system    = new UpdateEntityDescriptorRequestSystem(participant, entityMap, new StubGeoTransform(), stub);
            var repo      = CreateWorld();
            system.Create(repo);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(EntityId));
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1)); // NOT auth
            entityMap.Register(EntityId, entity);

            using var reqWriter = new DdsWriter<UpdateEntityDescriptorRequest>(participant, "UpdateEntityDescriptorRequest");
            Thread.Sleep(50);
            reqWriter.Write(MakeGeoRequest(EntityId));
            Thread.Sleep(30);
            system.Run();
            Thread.Sleep(10);

            Assert.Equal(0, stub.Written.Count);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void InjectedAckWriter_Authoritative_WriterCalledWithSuccessAck()
        {
            // SETUP: entity IS authoritative — injected stub must receive exactly one write
            const uint domain = 215u;
            var stub      = new StubAckWriter();
            using var participant = new DdsParticipant(domain);
            var entityMap = new NetworkEntityMap();
            var system    = new UpdateEntityDescriptorRequestSystem(participant, entityMap, new StubGeoTransform(), stub);
            var repo      = CreateWorld();
            system.Create(repo);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(EntityId));
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1)); // auth
            entityMap.Register(EntityId, entity);

            using var reqWriter = new DdsWriter<UpdateEntityDescriptorRequest>(participant, "UpdateEntityDescriptorRequest");
            Thread.Sleep(50);
            reqWriter.Write(MakeGeoRequest(EntityId));
            Thread.Sleep(30);
            system.Run();
            Thread.Sleep(10);

            Assert.Equal(1, stub.Written.Count);
            Assert.Equal(EntityId, stub.Written[0].EntityId);
            Assert.Equal((int)NedStatusCode.Success, stub.Written[0].ErrorCode);
        }
    }
}
