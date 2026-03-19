using System.Threading;
using Bagira.BDC.SSTD;
using Bagira.Map.Common.Replication.Egress;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using Xunit;

namespace Bagira.SimHost.Tests
{
    public class EntityMasterEgressTranslatorTests
    {
        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<TkbIdentity>();
            return repo;
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void ScanAndPublish_LocallyOwnedEntity_PublishesEntityMasterDTO()
        {
            const uint domainId = 200u;
            using var participant = new DdsParticipant(domainId);
            using var reader = new DdsReader<EntityMaster>(participant, "EntityMaster");
            var entityMap = new NetworkEntityMap();
            var translator = new EntityMasterEgressTranslator(participant, entityMap, localNodeId: 1);

            var repo = CreateWorld();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(42));
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            repo.AddComponent(entity, new TkbIdentity { TkbType = 777 });
            repo.SetDisType(entity, new DISEntityType { Value = 0 });

            Thread.Sleep(200);
            translator.ScanAndPublish(repo);
            Thread.Sleep(200);

            using var loan = reader.Take();
            bool found = false;
            foreach (var sample in loan)
            {
                if (!sample.IsValid)
                    continue;

                if (sample.Data.EntityId == 42)
                {
                    Assert.Equal(777, sample.Data.TkbType);
                    found = true;
                    break;
                }
            }

            Assert.True(found, "Expected EntityMaster sample for locally-owned entity.");
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void ScanAndPublish_RemotelyOwnedEntity_DoesNotPublish()
        {
            const uint domainId = 205u; // 201 fails on some machines; use 205 instead
            using var participant = new DdsParticipant(domainId);
            using var reader = new DdsReader<EntityMaster>(participant, "EntityMaster");
            var entityMap = new NetworkEntityMap();
            var translator = new EntityMasterEgressTranslator(participant, entityMap, localNodeId: 1);

            var repo = CreateWorld();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(42));
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            repo.AddComponent(entity, new TkbIdentity { TkbType = 777 });

            Thread.Sleep(200);
            translator.ScanAndPublish(repo);
            Thread.Sleep(200);

            using var loan = reader.Take();
            bool found = false;
            foreach (var sample in loan)
            {
                if (sample.IsValid)
                {
                    found = true;
                    break;
                }
            }

            Assert.False(found, "Remote entities must not publish EntityMaster samples.");
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void Dispose_CallsWriterDispose()
        {
            const uint domainId = 202u;
            using var participant = new DdsParticipant(domainId);
            using var reader = new DdsReader<EntityMaster>(participant, "EntityMaster");
            var entityMap = new NetworkEntityMap();
            var translator = new EntityMasterEgressTranslator(participant, entityMap, localNodeId: 1);

            var repo = CreateWorld();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(42));
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            repo.AddComponent(entity, new TkbIdentity { TkbType = 777 });
            repo.SetDisType(entity, new DISEntityType { Value = 0 });
            translator.ScanAndPublish(repo);
            Thread.Sleep(200);

            using (reader.Take()) { }

            translator.Dispose(42);
            Thread.Sleep(200);

            using var loan = reader.Take();
            bool disposed = false;
            foreach (var sample in loan)
            {
                if (sample.Info.InstanceState != DdsInstanceState.NotAliveDisposed)
                    continue;

                int entityId = sample.IsValid
                    ? sample.Data.EntityId
                    : EntityMaster.FromNative(sample.NativePtr).EntityId;

                if (entityId == 42)
                {
                    disposed = true;
                    break;
                }
            }

            Assert.True(disposed, "Expected a NOT_ALIVE_DISPOSED sample for EntityMaster 42.");
        }

        // ── BD1-P5T1: DisTypeStruct round-trip ────────────────────────────────

        /// <summary>
        /// BD1-P5T1 SC1: Egress translator must map each field of <c>DISEntityType</c>
        /// to the corresponding field in the published <c>DisTypeStruct</c>.
        /// </summary>
        [Fact]
        [Trait("Category", "Integration")]
        public void DisType_EgressFieldsMappedCorrectly()
        {
            const uint domainId = 210u;
            using var participant = new DdsParticipant(domainId);
            using var reader     = new DdsReader<EntityMaster>(participant, "EntityMaster");
            var entityMap  = new NetworkEntityMap();
            var translator = new EntityMasterEgressTranslator(participant, entityMap, localNodeId: 1);

            var repo   = CreateWorld();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(55));
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            repo.AddComponent(entity, new TkbIdentity { TkbType = 1001 });
            repo.SetDisType(entity, new DISEntityType
            {
                Kind        = 1,
                Domain      = 2,
                Country     = 225,
                Category    = 3,
                Subcategory = 4,
                Specific    = 5,
                Extra       = 6,
            });

            Thread.Sleep(200);
            translator.ScanAndPublish(repo);
            Thread.Sleep(300);

            using var loan = reader.Take();
            bool found = false;
            foreach (var sample in loan)
            {
                if (!sample.IsValid || sample.Data.EntityId != 55) continue;

                var d = sample.Data.DisType;
                Assert.Equal(1,   (int)d.Kind);
                Assert.Equal(2,   (int)d.Domain);
                Assert.Equal(225, (int)d.Country);
                Assert.Equal(3,   (int)d.Category);
                Assert.Equal(4,   (int)d.Subcategory);
                Assert.Equal(5,   (int)d.Specific);
                Assert.Equal(6,   (int)d.Extra);
                found = true;
                break;
            }

            Assert.True(found, "Expected EntityMaster sample with matching DisTypeStruct fields.");
        }
    }
}
