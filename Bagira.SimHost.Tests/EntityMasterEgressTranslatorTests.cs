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
    }
}
