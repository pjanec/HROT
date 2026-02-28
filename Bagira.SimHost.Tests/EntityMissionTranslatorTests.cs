using System;
using System.Collections.Generic;
using Bagira.BDC.SSTD;
using DdsMissionTrigger = Bagira.BDC.SSTD.MissionTrigger;
using Bagira.SimHost.Translators;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using Xunit;

using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Tests for <see cref="EntityMissionTranslator"/> mission ingress mapping.
    /// </summary>
    public class EntityMissionTranslatorTests
    {
        private static EntityRepository CreateWorld()
        {
            var world = new EntityRepository();
            world.RegisterComponent<NetworkIdentity>();
            world.RegisterComponent<NetworkAuthority>();
            world.RegisterComponent<MissionPlanQueue>();
            return world;
        }

        private static DoctrineRegistry CreateDoctrineRegistry()
        {
            var registry = new DoctrineRegistry();
            registry.Register(101, "MoveToLocation",
                new DoctrineDefinition { Name = "MoveToLocation", BrainTier = BehaviorConstants.BrainTierBTree });
            return registry;
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
                            TaskId          = Guid.NewGuid(),
                            BehaviorId      = "MoveToLocation",
                            BehaviorParams  = "{}",
                            ExecutingEngine = "CGFX",
                            State           = eTaskState.TASK_ACTIVE,
                            Triggers        = new List<DdsMissionTrigger>()
                        }
                    }
                }
            };
        }

        [Fact]
        public void Ingress_ApplyToEntity_SetsMissionPlanQueue()
        {
            using var world = CreateWorld();
            var entity  = world.CreateEntity();
            var mission = MakeMission(entityId: 1);

            var entityMap   = new NetworkEntityMap();
            var participant = new DdsParticipant();
            var translator  = new EntityMissionTranslator(participant, entityMap, CreateDoctrineRegistry());

            translator.ApplyToEntity(entity, mission, world);

            Assert.True(world.HasComponent<MissionPlanQueue>(entity),
                "MissionPlanQueue must be present after ApplyToEntity.");

            ref var queue = ref world.GetComponentRW<MissionPlanQueue>(entity);
            Assert.Equal(0, queue.CurrentPhase);
            Assert.Equal((byte)mission.Plan.Tasks.Count, queue.PhaseCount);
            Assert.Equal(101, queue.Phases[0].DoctrineId);
        }

        [Fact]
        public void Ingress_ComponentRemoval_ClearsMissionPlanQueue()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            world.SetComponent(entity, new MissionPlanQueue
            {
                PhaseCount = 1,
                CurrentPhase = 0
            });
            Assert.True(world.HasComponent<MissionPlanQueue>(entity));

            var view = (ISimulationView)world;
            var cmd  = (EntityCommandBuffer)view.GetCommandBuffer();
            cmd.RemoveComponent<MissionPlanQueue>(entity);
            cmd.Playback(world);

            Assert.False(world.HasComponent<MissionPlanQueue>(entity),
                "MissionPlanQueue must be removed after NOT_ALIVE_DISPOSED playback.");
        }
    }
}
