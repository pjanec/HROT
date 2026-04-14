using System.Collections.Generic;
using Hrot.NED.Descriptors;
using Hrot.IG.Components;
using Hrot.Map.Common.Replication.Ingress;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Systems;
using FDP.Toolkit.Replication.Services;
using Fdp.ModuleHost_Core.Abstractions;

namespace Hrot.IG.Tests
{
    public class EntityInfoTranslatorTests
    {
        private static (EntityRepository repo, NetworkEntityMap entityMap, FdpEventBus eventBus, EntityInfoIngressTranslator translator)
            CreateFixture()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<Components.EntityInfo>();
            var entityMap = new NetworkEntityMap();
            var eventBus = new FdpEventBus();
            var ghostCreationSystem = new GhostCreationSystem(entityMap);
            var translator = new EntityInfoIngressTranslator(null, entityMap, eventBus, ghostCreationSystem, localNodeId: 0);
            return (repo, entityMap, eventBus, translator);
        }

        [Fact]
        public void ProcessSample_PublishesUpdateWithIgEntityData()
        {
            var (_, entityMap, eventBus, translator) = CreateFixture();
            var entity = new Entity();
            entityMap.Register(1, entity);

            var info = new Hrot.NED.Descriptors.EntityInfo
            {
                EntityId = 1,
                Name = "Alpha-1",
                ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY,
                CommanderId = 7
            };

            translator.ProcessSample(info, netId: 1);

            eventBus.SwapBuffers();
            var commands = eventBus.ConsumeManaged<UpdateEntityCommand>();

            Assert.Single(commands);
            var update = commands[0];
            Assert.Equal(1, update.NetworkId);
            Assert.Single(update.ComponentsToUpdate);

            var igData = Assert.IsType<Components.EntityInfo>( update.ComponentsToUpdate[0]);
            Assert.Equal("Alpha-1", igData.Name);
            Assert.Equal(ForceId.Friend, igData.ForceId);
        }

        [Fact]
        public void ProcessSample_DoesNotIncludeRawEntityInfo()
        {
            var (_, entityMap, eventBus, translator) = CreateFixture();
            var entity = new Entity();
            entityMap.Register(1, entity);

            var info = new Hrot.NED.Descriptors.EntityInfo
            {
                EntityId = 1,
                Name = "Alpha-1",
                ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY,
                CommanderId = 7
            };

            translator.ProcessSample(info, netId: 1);

            eventBus.SwapBuffers();
            var commands = eventBus.ConsumeManaged<UpdateEntityCommand>();

            Assert.Single(commands);
			Assert.DoesNotContain( commands[0].ComponentsToUpdate, c => c is Hrot.NED.Descriptors.EntityInfo );
        }

        [Fact]
        public void ApplyToEntity_SetsIgEntityData()
        {
            var (repo, _, _, translator) = CreateFixture();
            var entity = repo.CreateEntity();

            translator.ApplyToEntity(entity, new Hrot.NED.Descriptors.EntityInfo
            {
                EntityId = 1,
                Name = "Bravo",
                ForceIdentifier = eForceIdentifier.FORCE_OPPOSING,
                CommanderId = 3
            }, repo);

            ISimulationView view = repo;
            ref readonly var data = ref view.GetComponentRO<Components.EntityInfo>( entity );

            Assert.Equal("Bravo", data.Name.ToString());
            Assert.Equal(ForceId.Hostile, data.ForceId);
        }
    }
}
