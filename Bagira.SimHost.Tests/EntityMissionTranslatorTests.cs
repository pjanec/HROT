using System;
using System.Collections.Generic;
using Bagira.BDC.SSTD;
using Bagira.SimHost.Components;
using Bagira.SimHost.Modules;
using Bagira.SimHost.Translators;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;
using ModuleHost.Network.Cyclone.Services;

using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Tests for <see cref="EntityMissionTranslator"/> and
    /// <see cref="EntityMissionEgressTranslator"/>.
    ///
    /// DDS readers/writers cannot be mocked without a live participant, so the
    /// ingress component-application path is exercised via
    /// <see cref="IDescriptorTranslator.ApplyToEntity"/> (the repository-direct
    /// overload used by the replay and snapshot systems) and via direct
    /// <see cref="EntityRepository"/> manipulation.
    ///
    /// Egress and smoke tests exercise <see cref="IDescriptorTranslator.ScanAndPublish"/>
    /// with a real <see cref="DdsParticipant"/> to verify query/filter logic does
    /// not throw.
    /// </summary>
    public class EntityMissionTranslatorTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        private static EntityRepository CreateWorld()
        {
            var world = new EntityRepository();
            world.RegisterComponent<NetworkIdentity>();
            world.RegisterComponent<NetworkAuthority>();
            world.RegisterManagedComponent<EntityMissionHolder>();
            return world;
        }

        private static EntityMission MakeMission(long entityId = 42)
        {
            return new EntityMission
            {
                EntityId = entityId,
                Plan = new MissionPlan
                {
                    ActiveTaskId = Guid.NewGuid(),
                    Tasks        = new List<MissionTask>
                    {
                        new MissionTask
                        {
                            TaskId           = Guid.NewGuid(),
                            BehaviorId       = "MoveToLocation",
                            BehaviorParams   = "{}",
                            ExecutingEngine  = "CGFX",
                            State            = eTaskState.TASK_ACTIVE,
                            Triggers         = new List<MissionTrigger>()
                        }
                    }
                }
            };
        }

        // ── Ingress: ApplyToEntity (repository-direct path) ──────────────────────

        /// <summary>
        /// <see cref="EntityMissionTranslator.ApplyToEntity"/> must set the
        /// <see cref="EntityMissionHolder"/> managed component on the target entity.
        /// This mirrors the outcome of a valid DDS ingress sample.
        /// </summary>
        [Fact]
        public void Ingress_ApplyToEntity_SetsEntityMissionHolder()
        {
            using var world = CreateWorld();
            var entity  = world.CreateEntity();
            var mission = MakeMission(entityId: 1);

            var entityMap   = new NetworkEntityMap();
            var participant = new DdsParticipant();
            var translator  = new EntityMissionTranslator(participant, entityMap);

            translator.ApplyToEntity(entity, mission, world);

            Assert.True(world.HasManagedComponent<EntityMissionHolder>(entity),
                "EntityMissionHolder must be present after ApplyToEntity.");

            var holder = ((ISimulationView)world).GetManagedComponentRO<EntityMissionHolder>(entity);
            Assert.Equal(mission.EntityId, holder.Mission.EntityId);
            Assert.Equal(mission.Plan.Tasks.Count, holder.Mission.Plan.Tasks.Count);
        }

        /// <summary>
        /// <see cref="EntityMissionTranslator.ApplyToEntity"/> must be a no-op when
        /// given a non-<see cref="EntityMission"/> object (e.g. wrong type).
        /// </summary>
        [Fact]
        public void Ingress_ApplyToEntity_WrongType_IsNoOp()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            var entityMap   = new NetworkEntityMap();
            var participant = new DdsParticipant();
            var translator  = new EntityMissionTranslator(participant, entityMap);

            var ex = Record.Exception(() => translator.ApplyToEntity(entity, "not_a_mission", world));
            Assert.Null(ex);
            Assert.False(world.HasManagedComponent<EntityMissionHolder>(entity));
        }

        /// <summary>
        /// Directly removing the <see cref="EntityMissionHolder"/> component mirrors
        /// the behaviour of a NOT_ALIVE_DISPOSED ingress sample.
        /// </summary>
        [Fact]
        public void Ingress_ComponentRemoval_ClearsEntityMissionHolder()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            // Seed the component (represents a prior ingress sample).
            world.SetManagedComponent(entity, new EntityMissionHolder { Mission = MakeMission() });
            Assert.True(world.HasManagedComponent<EntityMissionHolder>(entity));

            // Simulate the command-buffer playback that would result from
            // a NOT_ALIVE_DISPOSED DDS sample.
            var view   = (ISimulationView)world;
            var cmd    = (EntityCommandBuffer)view.GetCommandBuffer();
            cmd.RemoveManagedComponent<EntityMissionHolder>(entity);
            cmd.Playback(world);

            Assert.False(world.HasManagedComponent<EntityMissionHolder>(entity),
                "EntityMissionHolder must be removed after NOT_ALIVE_DISPOSED playback.");
        }

        // ── Ingress: Unknown entity ID ────────────────────────────────────────────

        /// <summary>
        /// An EntityId not present in the <see cref="NetworkEntityMap"/> must be
        /// silently skipped — the translator must not throw or create stray entities.
        /// </summary>
        [Fact]
        public void Ingress_UnknownEntityId_SkippedWithoutException()
        {
            var entityMap = new NetworkEntityMap();
            // Do NOT register entity 99 in the map.

            var participant = new DdsParticipant();
            var translator  = new EntityMissionTranslator(participant, entityMap);

            // PollIngress will Take() from an empty DDS reader, so there is nothing
            // to process — this test confirms construction and polling do not throw
            // for unknown IDs.
            using var world = CreateWorld();
            var view   = (ISimulationView)world;
            var cmd    = view.GetCommandBuffer();

            var ex = Record.Exception(() => translator.PollIngress(cmd, view));
            Assert.Null(ex);
        }

        // ── Egress: ScanAndPublish smoke tests ───────────────────────────────────

        /// <summary>
        /// <see cref="EntityMissionEgressTranslator.ScanAndPublish"/> must not throw
        /// on an empty world (no entities at all).
        /// </summary>
        [Fact]
        public void Egress_EmptyWorld_ScanAndPublishDoesNotThrow()
        {
            using var world = CreateWorld();
            var entityMap   = new NetworkEntityMap();
            var participant = new DdsParticipant();
            var translator  = new EntityMissionEgressTranslator(participant, entityMap);

            var ex = Record.Exception(() => translator.ScanAndPublish(world));
            Assert.Null(ex);
        }

        /// <summary>
        /// When an entity carries <see cref="EntityMissionHolder"/> and has local
        /// authority, <see cref="EntityMissionEgressTranslator.ScanAndPublish"/> must
        /// not throw (smoke test — DDS write is a side-effect we cannot inspect
        /// without a live subscriber).
        /// </summary>
        [Fact]
        public void Egress_AuthorityEntity_ScanAndPublishDoesNotThrow()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new NetworkIdentity(42));
            world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1)); // HasAuthority = true
            world.SetManagedComponent(entity, new EntityMissionHolder { Mission = MakeMission(entityId: 42) });

            var entityMap   = new NetworkEntityMap();
            var participant = new DdsParticipant();
            var translator  = new EntityMissionEgressTranslator(participant, entityMap);

            var ex = Record.Exception(() => translator.ScanAndPublish(world));
            Assert.Null(ex);
        }

        /// <summary>
        /// An entity whose <see cref="NetworkAuthority.HasAuthority"/> is <c>false</c>
        /// must not trigger a DDS write — calling ScanAndPublish must not throw.
        /// </summary>
        [Fact]
        public void Egress_NonAuthorityEntity_ScanAndPublishDoesNotThrow()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new NetworkIdentity(10));
            world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1)); // HasAuthority = false
            world.SetManagedComponent(entity, new EntityMissionHolder { Mission = MakeMission(entityId: 10) });

            var entityMap   = new NetworkEntityMap();
            var participant = new DdsParticipant();
            var translator  = new EntityMissionEgressTranslator(participant, entityMap);

            var ex = Record.Exception(() => translator.ScanAndPublish(world));
            Assert.Null(ex);
        }

        // ── Egress: Dirty-flag optimisation ──────────────────────────────────────

        /// <summary>
        /// After the first <see cref="EntityMissionEgressTranslator.ScanAndPublish"/>
        /// call, the internal <c>_lastPublishedVersion</c> is advanced to
        /// <see cref="EntityRepository.GlobalVersion"/>. A second call with no
        /// intervening component writes must hit the early-out and not throw.
        /// </summary>
        [Fact]
        public void Egress_NoNewChanges_SecondScanSkipsPublish()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new NetworkIdentity(7));
            world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            world.SetManagedComponent(entity, new EntityMissionHolder { Mission = MakeMission(entityId: 7) });

            var entityMap   = new NetworkEntityMap();
            var participant = new DdsParticipant();
            var translator  = new EntityMissionEgressTranslator(participant, entityMap);

            // First scan — processes the dirty component.
            translator.ScanAndPublish(world);

            // Second scan — no mutations since the first; early-out path exercised.
            var ex = Record.Exception(() => translator.ScanAndPublish(world));
            Assert.Null(ex);
        }

        /// <summary>
        /// After a component write the dirty flag is raised again;
        /// a subsequent <see cref="EntityMissionEgressTranslator.ScanAndPublish"/>
        /// must process the entity without throwing.
        /// </summary>
        [Fact]
        public void Egress_ComponentMutatedBetweenScans_SecondScanRuns()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new NetworkIdentity(5));
            world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            world.SetManagedComponent(entity, new EntityMissionHolder { Mission = MakeMission(entityId: 5) });

            var entityMap   = new NetworkEntityMap();
            var participant = new DdsParticipant();
            var translator  = new EntityMissionEgressTranslator(participant, entityMap);

            translator.ScanAndPublish(world);

            // Mutate the component — this advances GlobalVersion and marks the table dirty.
            world.SetManagedComponent(entity, new EntityMissionHolder { Mission = MakeMission(entityId: 5) });

            var ex = Record.Exception(() => translator.ScanAndPublish(world));
            Assert.Null(ex);
        }

        // ── Module integration: both translators exposed ──────────────────────────

        /// <summary>
        /// <see cref="SimHostModule"/> must expose non-null
        /// <see cref="SimHostModule.MissionIngressTranslator"/> and
        /// <see cref="SimHostModule.MissionEgressTranslator"/> regardless of whether
        /// a geographic transform is provided.
        /// </summary>
        [Fact]
        public void SimHostModule_ExposesNonNullMissionTranslators()
        {
            var participant = new DdsParticipant();
            var tkb         = new TkbDatabase();
            var entityMap   = new NetworkEntityMap();
            var idAllocator = new DdsIdAllocator(participant, "test-alloc");
            var elm         = new EntityLifecycleModule(tkb, new List<int>());
            var spawner     = new NetworkSpawningSystem(tkb, elm, entityMap, idAllocator, 1);

            // geoTransform = null → GeoEgressTranslator will be null, but mission translators must still be created.
            var module = new SimHostModule(
                participant,
                tkb,
                idAllocator,
                localNodeId: 1,
                spawner,
                entityMap,
                geoTransform: null);

            Assert.NotNull(module.MissionIngressTranslator);
            Assert.NotNull(module.MissionEgressTranslator);
        }
    }
}
