using System;
using System.Numerics;
using System.Threading;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.Map.Common.Systems;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Network;
using Xunit;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Integration tests for <see cref="UpdateEntityDescriptorRequestSystem"/> verifying the
    /// Silent Bystander Rule (BUG1-N001): non-authoritative paths must emit no
    /// <see cref="UpdateEntityDescriptorAck"/>, only debug log entries.
    /// </summary>
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
                DescriptorType = EDescriptorType.dtGeoSpatial,
                Payload      = new EntityDescriptorUnion
                {
                    _d        = EDescriptorType.dtGeoSpatial,
                    GeoSpatial = new GeoSpatial
                    {
                        EntityId = entityId,
                        Pos      = new GeoPosition { Latitude = 10.0, Longitude = 20.0, Altitude = 0.0 }
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

            Thread.Sleep(200); // allow pub/sub matching

            reqWriter.Write(MakeGeoRequest(entityId: 999));
            Thread.Sleep(100);

            system.Run();
            Thread.Sleep(50);

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

            Thread.Sleep(200);

            reqWriter.Write(MakeGeoRequest(EntityId));
            Thread.Sleep(100);

            system.Run();
            Thread.Sleep(50);

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

            Thread.Sleep(200);

            reqWriter.Write(MakeUnsupportedTypeRequest(EntityId));
            Thread.Sleep(100);

            system.Run();
            Thread.Sleep(50);

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

            Thread.Sleep(200);

            var req = MakeGeoRequest(EntityId);
            reqWriter.Write(req);
            Thread.Sleep(100);

            system.Run();
            Thread.Sleep(100);

            // ASSERT: exactly one Success ACK
            int ackCount = 0;
            bool hasSuccess = false;
            using var loan = ackReader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ackCount++;
                if (sample.Data.EntityId == EntityId &&
                    sample.Data.ErrorCode == (int)SstErrorCode.Success)
                    hasSuccess = true;
            }

            Assert.Equal(1, ackCount);
            Assert.True(hasSuccess, "Expected exactly one Success ACK for authoritative GeoSpatial update.");
        }
    }
}
