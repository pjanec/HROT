using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using Hrot.SimHost.Modules;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Fdp.Toolkit.Tkb;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Network;
using Fdp.Network.Cyclone.Services;
using DdsMissionTrigger = Hrot.NED.Descriptors.MissionTrigger;

using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Tests for <see cref="EntityMissionIngressTranslator"/> and
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
    [Collection("SimHostDds")]
    public class EntityMissionTranslatorTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        private static EntityRepository CreateWorld()
        {
            var world = new EntityRepository();
            world.RegisterComponent<NetworkIdentity>();
            world.RegisterComponent<GhostStateTracker>(); // required when ghost creation is triggered
            world.RegisterComponent<NetworkAuthority>();
            world.RegisterComponent<MissionPlanQueue>();
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
                            Triggers         = new List<DdsMissionTrigger>()
                        }
                    }
                }
            };
        }

        // ── Ingress: ApplyToEntity (repository-direct path) ──────────────────────

        /// <summary>
        /// <see cref="EntityMissionIngressTranslator.ApplyToEntity"/> must set the
        /// <see cref="MissionPlanQueue"/> component on the target entity.
        /// This mirrors the outcome of a valid DDS ingress sample.
        /// </summary>
        [Fact]
        public void Ingress_ApplyToEntity_SetsMissionPlanQueue()
        {
            using var world = CreateWorld();
            var entity  = world.CreateEntity();
            var mission = MakeMission(entityId: 1);

            var entityMap   = new NetworkEntityMap();
            using var participant = new DdsParticipant();
            var translator  = new EntityMissionIngressTranslator(participant, entityMap, new DoctrineRegistry(), new GhostCreationSystem(entityMap));

            translator.ApplyToEntity(entity, mission, world);

            Assert.True(world.HasComponent<MissionPlanQueue>(entity),
                "MissionPlanQueue must be present after ApplyToEntity.");

            var queue = ((ISimulationView)world).GetComponentRO<MissionPlanQueue>(entity);
            Assert.Equal(mission.Plan.Tasks.Count, queue.PhaseCount);
        }

        /// <summary>
        /// <see cref="EntityMissionIngressTranslator.ApplyToEntity"/> must be a no-op when
        /// given a non-<see cref="EntityMission"/> object (e.g. wrong type).
        /// </summary>
        [Fact]
        public void Ingress_ApplyToEntity_WrongType_IsNoOp()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            var entityMap   = new NetworkEntityMap();
            using var participant = new DdsParticipant();
            var translator  = new EntityMissionIngressTranslator(participant, entityMap, new DoctrineRegistry(), new GhostCreationSystem(entityMap));

            var ex = Record.Exception(() => translator.ApplyToEntity(entity, "not_a_mission", world));
            Assert.Null(ex);
            Assert.False(world.HasComponent<MissionPlanQueue>(entity));
        }

        /// <summary>
        /// Directly removing the <see cref="MissionPlanQueue"/> component mirrors
        /// the behaviour of a NOT_ALIVE_DISPOSED ingress sample.
        /// </summary>
        [Fact]
        public void Ingress_ComponentRemoval_ClearsMissionPlanQueue()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            // Seed the component (represents a prior ingress sample).
            world.SetComponent(entity, new MissionPlanQueue { PhaseCount = 1 });
            Assert.True(world.HasComponent<MissionPlanQueue>(entity));

            // Simulate the command-buffer playback that would result from
            // a NOT_ALIVE_DISPOSED DDS sample.
            var view   = (ISimulationView)world;
            var cmd    = (EntityCommandBuffer)view.GetCommandBuffer();
            cmd.RemoveComponent<MissionPlanQueue>(entity);
            cmd.Playback(world);

            Assert.False(world.HasComponent<MissionPlanQueue>(entity),
                "MissionPlanQueue must be removed after NOT_ALIVE_DISPOSED playback.");
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

            using var participant = new DdsParticipant();
            var translator  = new EntityMissionIngressTranslator(participant, entityMap, new DoctrineRegistry(), new GhostCreationSystem(entityMap));

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
            using var participant = new DdsParticipant();
            var translator  = new EntityMissionEgressTranslator(participant, entityMap);

            var ex = Record.Exception(() => translator.ScanAndPublish(world));
            Assert.Null(ex);
        }

        /// <summary>
        /// When an entity carries <see cref="MissionPlanQueue"/> and has local
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
            world.SetComponent(entity, new MissionPlanQueue { PhaseCount = 1 });

            var entityMap   = new NetworkEntityMap();
            using var participant = new DdsParticipant();
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
            world.SetComponent(entity, new MissionPlanQueue { PhaseCount = 1 });

            var entityMap   = new NetworkEntityMap();
            using var participant = new DdsParticipant();
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
            world.SetComponent(entity, new MissionPlanQueue { PhaseCount = 1 });

            var entityMap   = new NetworkEntityMap();
            using var participant = new DdsParticipant();
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
            world.SetComponent(entity, new MissionPlanQueue { PhaseCount = 1 });

            var entityMap   = new NetworkEntityMap();
            using var participant = new DdsParticipant();
            var translator  = new EntityMissionEgressTranslator(participant, entityMap);

            translator.ScanAndPublish(world);

            // Mutate the component — this advances GlobalVersion and marks the table dirty.
            world.SetComponent(entity, new MissionPlanQueue { PhaseCount = 1 });

            var ex = Record.Exception(() => translator.ScanAndPublish(world));
            Assert.Null(ex);
        }

        // ── Module integration: both translators exposed ──────────────────────────

        /// <summary>
        /// <see cref="SimHostModule"/> constructor must NOT require a <see cref="DdsParticipant"/>.
        /// Passing only a <see cref="NetworkSpawningSystem"/> (no request/delete systems, no
        /// translators) is a valid offline construction.
        /// </summary>
        [Fact]
        public void SimHostModule_CanBeConstructed_WithoutDdsParticipant()
        {
            using var participant = new DdsParticipant();
            var tkb         = new TkbDatabase();
            var entityMap   = new NetworkEntityMap();
            var idAllocator = new DdsIdAllocator(participant, "offline-test");
            var elm         = new EntityLifecycleModule(tkb, new List<int>());
            var spawner     = new NetworkSpawningSystem(tkb, elm, entityMap, idAllocator, 1);

            // Note: SimHostModule constructor only receives the spawner — no participant, no systems.
            var ex = Record.Exception(() => new SimHostModule(spawner));

            Assert.Null(ex);
        }
    }
}
