using System.Collections.Generic;
using Hrot.NED.Descriptors;
using Hrot.Map.Common.Replication.Ingress;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.IG.Tests
{
    public class EntityInfoTranslatorTests
    {
        private static (EntityRepository repo, NetworkEntityMap entityMap, FdpEventBus eventBus, EntityInfoIngressTranslator translator)
            CreateFixture()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<Fdp.Core.EntityInfo>();
            repo.RegisterComponent<UnitSubordinate>();
            var entityMap = new NetworkEntityMap();
            var eventBus = new FdpEventBus();
            var ghostCreationSystem = new GhostCreationSystem(entityMap);
            var translator = new EntityInfoIngressTranslator(null, entityMap, eventBus, ghostCreationSystem, localNodeId: 0);
            return (repo, entityMap, eventBus, translator);
        }

        // ── Original tests ────────────────────────────────────────────────────

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
            var commands = eventBus.ReadManaged<UpdateEntityCommand>();

            Assert.Single(commands);
            var update = commands[0];
            Assert.Equal(1, update.NetworkId);
            Assert.Single(update.ComponentsToUpdate);

            var igData = Assert.IsType<Fdp.Core.EntityInfo>( update.ComponentsToUpdate[0]);
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
            var commands = eventBus.ReadManaged<UpdateEntityCommand>();

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
            ref readonly var data = ref view.GetComponentRO<Fdp.Core.EntityInfo>( entity );

            Assert.Equal("Bravo", data.Name.ToString());
            Assert.Equal(ForceId.Hostile, data.ForceId);
        }

        // ── CS011 ingress tests ───────────────────────────────────────────────

        /// <summary>
        /// CS011 Test 1: When the commander is already registered in the entity map,
        /// <see cref="CmdAssignSubordinate"/> is published immediately.
        /// </summary>
        [Fact]
        public void CS011_CommanderPresent_ImmediateCmdAssignSubordinate()
        {
            var (repo, entityMap, eventBus, translator) = CreateFixture();

            var cmdEntity = repo.CreateEntity();
            var subEntity = repo.CreateEntity();
            entityMap.Register(10, cmdEntity);
            entityMap.Register(20, subEntity);

            var info = new Hrot.NED.Descriptors.EntityInfo
            {
                EntityId        = 20,
                CommanderId     = 10,
                TacticalDesignation = eTacticalDesignation.Wingman,
                ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY,
            };

            translator.ProcessSample(info, netId: 20, repo: repo);

            eventBus.SwapBuffers();
            var assigns = eventBus.Read<CmdAssignSubordinate>();

            Assert.Equal(1, assigns.Length);
            Assert.Equal(subEntity, assigns[0].Subordinate);
            Assert.Equal(cmdEntity, assigns[0].Commander);
            Assert.Equal(TacticalDesignation.Wingman, assigns[0].Designation);
        }

        /// <summary>
        /// CS011 Test 2: When the commander is not yet registered, the subordinate
        /// is deferred (no <see cref="CmdAssignSubordinate"/> published yet).
        /// </summary>
        [Fact]
        public void CS011_CommanderAbsent_NoImmediateEvent_DeferredByCommander()
        {
            var (repo, entityMap, eventBus, translator) = CreateFixture();

            var subEntity = repo.CreateEntity();
            entityMap.Register(20, subEntity);
            // Commander net ID 10 not registered.

            var info = new Hrot.NED.Descriptors.EntityInfo
            {
                EntityId        = 20,
                CommanderId     = 10,
                TacticalDesignation = eTacticalDesignation.SquadLeader,
                ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY,
            };

            translator.ProcessSample(info, netId: 20, repo: repo);

            eventBus.SwapBuffers();
            var assigns = eventBus.Read<CmdAssignSubordinate>();
            Assert.Equal(0, assigns.Length);
        }

        /// <summary>
        /// CS011 Test 3: After the commander registers, calling PollIngress drains
        /// the pending queue and publishes <see cref="CmdAssignSubordinate"/>.
        /// </summary>
        [Fact]
        public void CS011_DeferredResolvedOnEntityRegistered()
        {
            var (repo, entityMap, eventBus, translator) = CreateFixture();

            var subEntity = repo.CreateEntity();
            entityMap.Register(20, subEntity);

            var info = new Hrot.NED.Descriptors.EntityInfo
            {
                EntityId        = 20,
                CommanderId     = 10,
                TacticalDesignation = eTacticalDesignation.Support,
                ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY,
            };

            translator.ProcessSample(info, netId: 20, repo: repo);

            // Register the commander — fires EntityRegistered → adds to _recentlyRegistered.
            var cmdEntity = repo.CreateEntity();
            entityMap.Register(10, cmdEntity);

            // PollIngress drains _recentlyRegistered even in test mode (_reader == null).
            translator.PollIngress(null!, repo);

            eventBus.SwapBuffers();
            var assigns = eventBus.Read<CmdAssignSubordinate>();

            Assert.Equal(1, assigns.Length);
            Assert.Equal(subEntity, assigns[0].Subordinate);
            Assert.Equal(cmdEntity, assigns[0].Commander);
            Assert.Equal(TacticalDesignation.Support, assigns[0].Designation);
        }

        /// <summary>
        /// CS011 Test 4: A subsequent ProcessSample with a different commander scrubs
        /// the previous pending entry so the old commander does not fire.
        /// </summary>
        [Fact]
        public void CS011_CommanderUpdate_ScrubsOldPendingQueue()
        {
            var (repo, entityMap, eventBus, translator) = CreateFixture();

            var subEntity = repo.CreateEntity();
            entityMap.Register(20, subEntity);

            var info1 = new Hrot.NED.Descriptors.EntityInfo
            {
                EntityId    = 20,
                CommanderId = 10,  // first commander (not yet registered)
                ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY,
            };
            translator.ProcessSample(info1, netId: 20, repo: repo);

            // Now change commander to 11 before either registers.
            var info2 = new Hrot.NED.Descriptors.EntityInfo
            {
                EntityId    = 20,
                CommanderId = 11,  // new commander
                ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY,
            };
            translator.ProcessSample(info2, netId: 20, repo: repo);

            // Register the OLD commander (10) — should produce no event.
            var oldCmd = repo.CreateEntity();
            entityMap.Register(10, oldCmd);
            translator.PollIngress(null!, repo);
            eventBus.SwapBuffers();
            Assert.Equal(0, eventBus.Read<CmdAssignSubordinate>().Length);

            // Register the NEW commander (11) — should produce one event.
            var newCmd = repo.CreateEntity();
            entityMap.Register(11, newCmd);
            translator.PollIngress(null!, repo);
            eventBus.SwapBuffers();
            var assigns = eventBus.Read<CmdAssignSubordinate>();
            Assert.Equal(1, assigns.Length);
            Assert.Equal(newCmd, assigns[0].Commander);
        }

        /// <summary>
        /// CS011 Test 5: Dispose clears any pending subordinates for the disposed net ID.
        /// </summary>
        [Fact]
        public void CS011_Dispose_ClearsPendingSubordinate()
        {
            var (repo, entityMap, eventBus, translator) = CreateFixture();

            var subEntity = repo.CreateEntity();
            entityMap.Register(20, subEntity);

            var info = new Hrot.NED.Descriptors.EntityInfo
            {
                EntityId    = 20,
                CommanderId = 10,  // deferred
                ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY,
            };
            translator.ProcessSample(info, netId: 20, repo: repo);

            // Dispose the subordinate entity.
            translator.Dispose(20);

            // Register the commander — should NOT fire because the sub is disposed.
            var cmdEntity = repo.CreateEntity();
            entityMap.Register(10, cmdEntity);
            translator.PollIngress(null!, repo);
            eventBus.SwapBuffers();
            Assert.Equal(0, eventBus.Read<CmdAssignSubordinate>().Length);
        }

        /// <summary>
        /// CS011 Test 6: CommanderId == 0 for an entity that already has a
        /// <see cref="UnitSubordinate"/> causes <see cref="CmdRemoveSubordinate"/> to fire.
        /// </summary>
        [Fact]
        public void CS011_CommanderIdZero_WithExistingUnitSubordinate_PublishesCmdRemove()
        {
            var (repo, entityMap, eventBus, translator) = CreateFixture();

            var cmdEntity = repo.CreateEntity();
            var subEntity = repo.CreateEntity();
            entityMap.Register(20, subEntity);

            // Give the entity an existing UnitSubordinate component.
            repo.AddComponent(subEntity, new UnitSubordinate { Commander = cmdEntity });

            var info = new Hrot.NED.Descriptors.EntityInfo
            {
                EntityId        = 20,
                CommanderId     = 0,  // remove commander
                ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY,
            };

            translator.ProcessSample(info, netId: 20, repo: repo);

            eventBus.SwapBuffers();
            var removes = eventBus.Read<CmdRemoveSubordinate>();
            Assert.Equal(1, removes.Length);
            Assert.Equal(subEntity, removes[0].Subordinate);
        }

        /// <summary>
        /// CS011 Test 7: CommanderId == 0 for an entity without a
        /// <see cref="UnitSubordinate"/> produces no event.
        /// </summary>
        [Fact]
        public void CS011_CommanderIdZero_WithoutUnitSubordinate_NoEvent()
        {
            var (repo, entityMap, eventBus, translator) = CreateFixture();

            var subEntity = repo.CreateEntity();
            entityMap.Register(20, subEntity);
            // No UnitSubordinate on the entity.

            var info = new Hrot.NED.Descriptors.EntityInfo
            {
                EntityId        = 20,
                CommanderId     = 0,
                ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY,
            };

            translator.ProcessSample(info, netId: 20, repo: repo);

            eventBus.SwapBuffers();
            Assert.Equal(0, eventBus.Read<CmdRemoveSubordinate>().Length);
            Assert.Equal(0, eventBus.Read<CmdAssignSubordinate>().Length);
        }

        /// <summary>
        /// CS011 Test 8: A sample for a subordinate whose entity is not yet spawned
        /// is queued in _pendingUnspawnedSubordinates.  No event fires until the
        /// entity spawns.
        /// </summary>
        [Fact]
        public void CS011_SubordinateUnspawned_QueuesInPendingUnspawned()
        {
            var (repo, entityMap, eventBus, translator) = CreateFixture();
            // Neither subordinate (20) nor commander (10) registered yet.

            var info = new Hrot.NED.Descriptors.EntityInfo
            {
                EntityId    = 20,
                CommanderId = 10,
                ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY,
            };

            translator.ProcessSample(info, netId: 20, repo: repo);

            eventBus.SwapBuffers();
            Assert.Equal(0, eventBus.Read<CmdAssignSubordinate>().Length);
        }

        /// <summary>
        /// CS011 Test 9: When the subordinate entity spawns while its commander is
        /// already alive, PollIngress resolves the pending-unspawned entry immediately.
        /// </summary>
        [Fact]
        public void CS011_SubordinateSpawns_CommanderAlive_ImmediateAssign()
        {
            var (repo, entityMap, eventBus, translator) = CreateFixture();

            var cmdEntity = repo.CreateEntity();
            entityMap.Register(10, cmdEntity);
            // Subordinate (20) not yet spawned.

            var info = new Hrot.NED.Descriptors.EntityInfo
            {
                EntityId    = 20,
                CommanderId = 10,
                TacticalDesignation = eTacticalDesignation.Commander,
                ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY,
            };
            translator.ProcessSample(info, netId: 20, repo: repo);

            // Now the subordinate spawns.
            var subEntity = repo.CreateEntity();
            entityMap.Register(20, subEntity);

            // Drain: subordinate's registration fires EntityRegistered for netId=20.
            translator.PollIngress(null!, repo);

            eventBus.SwapBuffers();
            var assigns = eventBus.Read<CmdAssignSubordinate>();
            Assert.Equal(1, assigns.Length);
            Assert.Equal(subEntity, assigns[0].Subordinate);
            Assert.Equal(cmdEntity, assigns[0].Commander);
        }

        /// <summary>
        /// CS011 Test 10: When the subordinate spawns and its commander is also absent,
        /// the pending-unspawned entry moves to _pendingSubordinates.
        /// </summary>
        [Fact]
        public void CS011_SubordinateSpawns_CommanderMissing_MovesToPendingByCommander()
        {
            var (repo, entityMap, eventBus, translator) = CreateFixture();
            // Neither subordinate (20) nor commander (10) registered initially.

            var info = new Hrot.NED.Descriptors.EntityInfo
            {
                EntityId    = 20,
                CommanderId = 10,
                ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY,
            };
            translator.ProcessSample(info, netId: 20, repo: repo);

            // Subordinate spawns — commander still absent.
            var subEntity = repo.CreateEntity();
            entityMap.Register(20, subEntity);
            translator.PollIngress(null!, repo);

            // No assign yet — moved to _pendingSubordinates.
            eventBus.SwapBuffers();
            Assert.Equal(0, eventBus.Read<CmdAssignSubordinate>().Length);

            // Commander now spawns.
            var cmdEntity = repo.CreateEntity();
            entityMap.Register(10, cmdEntity);
            translator.PollIngress(null!, repo);

            eventBus.SwapBuffers();
            var assigns = eventBus.Read<CmdAssignSubordinate>();
            Assert.Equal(1, assigns.Length);
            Assert.Equal(subEntity, assigns[0].Subordinate);
            Assert.Equal(cmdEntity, assigns[0].Commander);
        }

        /// <summary>
        /// CS011 Test 11: Dispose also cleans entries from _pendingUnspawnedSubordinates.
        /// </summary>
        [Fact]
        public void CS011_Dispose_ClearsPendingUnspawnedSubordinate()
        {
            var (repo, entityMap, eventBus, translator) = CreateFixture();
            // Subordinate 20 not yet spawned.

            var info = new Hrot.NED.Descriptors.EntityInfo
            {
                EntityId    = 20,
                CommanderId = 10,
                ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY,
            };
            translator.ProcessSample(info, netId: 20, repo: repo);

            // Dispose before the entity ever spawns.
            translator.Dispose(20);

            // Spawning the subordinate now should not fire any event.
            var subEntity = repo.CreateEntity();
            entityMap.Register(20, subEntity);
            translator.PollIngress(null!, repo);
            eventBus.SwapBuffers();
            Assert.Equal(0, eventBus.Read<CmdAssignSubordinate>().Length);
        }
    }
}
