using System;
using System.Collections.Generic;
using System.Threading;
using Bagira.BDC.SSTM;
using Bagira.BDC.SSTD;
using DdsMissionTrigger = Bagira.BDC.SSTD.MissionTrigger;
using Bagira.SimHost.Systems;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace Bagira.SimHost.Tests
{
    public class MissionControlRequestSystemTests
    {
        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<MissionPlanQueue>();
            repo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });
            return repo;
        }

        private static MissionPlan MakePlan(params Guid[] taskIds)
        {
            var tasks = new List<MissionTask>();
            foreach (var id in taskIds)
            {
                tasks.Add(new MissionTask
                {
                    TaskId          = id,
                    BehaviorId      = "MoveToLocation",
                    BehaviorParams  = "{}",
                    ExecutingEngine = "CGFX",
                    State           = eTaskState.TASK_PLANNED,
                    Triggers        = new List<DdsMissionTrigger>()
                });
            }

            return new MissionPlan
            {
                ActiveTaskId = taskIds.Length > 0 ? taskIds[0] : Guid.Empty,
                Tasks = tasks
            };
        }

        private static DoctrineRegistry CreateDoctrineRegistry()
        {
            var registry = new DoctrineRegistry();
            registry.Register(101, "MoveToLocation",
                new DoctrineDefinition { Name = "MoveToLocation", BrainTier = BehaviorConstants.BrainTierBTree });
            return registry;
        }

        [Fact]
        public void ProcessRequest_JumpToTask_UpdatesActiveTaskId()
        {
            const uint domainId = 152u;
            using var participant = new DdsParticipant(domainId);
            using var writer = new DdsWriter<MissionControlRequest>(participant);
            using var reader = new DdsReader<MissionControlAck>(participant);

            var entityMap = new NetworkEntityMap();
            using var repo = CreateWorld();
            var entity = repo.CreateEntity();
            entityMap.Register(1, entity);

            var taskA = Guid.NewGuid();
            var taskB = Guid.NewGuid();
            var taskC = Guid.NewGuid();

            var system = new MissionControlRequestSystem(participant, entityMap, CreateDoctrineRegistry());
            system.Create(repo);

            var replaceId = Guid.NewGuid();
            writer.Write(new MissionControlRequest
            {
                RequestId      = replaceId,
                TargetEntityId = 1,
                BaseVersion    = 0,
                Payload = new MissionCommandUnion
                {
                    _d             = eMissionCommandType.CMD_REPLACE_MISSION,
                    FullMissionData = MakePlan(taskA, taskB, taskC)
                }
            });

            Thread.Sleep(200);
            system.Run();
            Thread.Sleep(200);

            var requestId = Guid.NewGuid();
            writer.Write(new MissionControlRequest
            {
                RequestId      = requestId,
                TargetEntityId = 1,
                BaseVersion    = 0,
                Payload = new MissionCommandUnion
                {
                    _d = eMissionCommandType.CMD_JUMP_TO_TASK,
                    TargetTaskId = taskC
                }
            });

            Thread.Sleep(200);
            system.Run();
            Thread.Sleep(200);

            ref var queue = ref repo.GetComponentRW<MissionPlanQueue>(entity);
            Assert.Equal(2, queue.CurrentPhase);

            using var loan = reader.Take();
            Assert.True(LoanHasAck(loan, requestId, errorCode: null));
        }

        [Fact]
        public void ProcessRequest_AbortAll_ClearsPlan()
        {
            const uint domainId = 153u;
            using var participant = new DdsParticipant(domainId);
            using var writer = new DdsWriter<MissionControlRequest>(participant);

            var entityMap = new NetworkEntityMap();
            using var repo = CreateWorld();
            var entity = repo.CreateEntity();
            entityMap.Register(1, entity);

            var taskA = Guid.NewGuid();

            var system = new MissionControlRequestSystem(participant, entityMap, CreateDoctrineRegistry());
            system.Create(repo);

            writer.Write(new MissionControlRequest
            {
                RequestId      = Guid.NewGuid(),
                TargetEntityId = 1,
                BaseVersion    = 0,
                Payload = new MissionCommandUnion
                {
                    _d             = eMissionCommandType.CMD_REPLACE_MISSION,
                    FullMissionData = MakePlan(taskA)
                }
            });

            Thread.Sleep(200);
            system.Run();
            Thread.Sleep(200);

            writer.Write(new MissionControlRequest
            {
                RequestId      = Guid.NewGuid(),
                TargetEntityId = 1,
                BaseVersion    = 0,
                Payload = new MissionCommandUnion
                {
                    _d = eMissionCommandType.CMD_ABORT_ALL,
                    UnusedPlaceholder = true
                }
            });

            Thread.Sleep(200);
            system.Run();
            Thread.Sleep(200);

            ref var queue = ref repo.GetComponentRW<MissionPlanQueue>(entity);
            Assert.Equal(0, queue.PhaseCount);
            Assert.Equal(0, queue.CurrentPhase);
        }

        [Fact]
        public void ProcessRequest_WritesAck()
        {
            const uint domainId = 154u;
            using var participant = new DdsParticipant(domainId);
            using var writer = new DdsWriter<MissionControlRequest>(participant);
            using var reader = new DdsReader<MissionControlAck>(participant);

            var entityMap = new NetworkEntityMap();
            using var repo = CreateWorld();
            var entity = repo.CreateEntity();
            entityMap.Register(1, entity);

            var system = new MissionControlRequestSystem(participant, entityMap, CreateDoctrineRegistry());
            system.Create(repo);

            var requestId = Guid.NewGuid();
            writer.Write(new MissionControlRequest
            {
                RequestId      = requestId,
                TargetEntityId = 1,
                BaseVersion    = 0,
                Payload = new MissionCommandUnion
                {
                    _d = eMissionCommandType.CMD_REPLACE_MISSION,
                    FullMissionData = new MissionPlan { Tasks = new List<MissionTask>() }
                }
            });

            Thread.Sleep(200);
            system.Run();
            Thread.Sleep(200);

            using var loan = reader.Take();
            Assert.True(LoanHasAck(loan, requestId, errorCode: 0));
        }

        [Fact]
        public void ProcessRequest_UnknownEntity_WritesNack()
        {
            const uint domainId = 155u;
            using var participant = new DdsParticipant(domainId);
            using var writer = new DdsWriter<MissionControlRequest>(participant);
            using var reader = new DdsReader<MissionControlAck>(participant);

            var entityMap = new NetworkEntityMap();
            using var repo = CreateWorld();

            var system = new MissionControlRequestSystem(participant, entityMap, CreateDoctrineRegistry());
            system.Create(repo);

            var requestId = Guid.NewGuid();
            writer.Write(new MissionControlRequest
            {
                RequestId      = requestId,
                TargetEntityId = 999,
                BaseVersion    = 0,
                Payload = new MissionCommandUnion
                {
                    _d = eMissionCommandType.CMD_ABORT_ALL,
                    UnusedPlaceholder = true
                }
            });

            Thread.Sleep(200);
            system.Run();
            Thread.Sleep(200);

            using var loan = reader.Take();
            Assert.True(LoanHasAck(loan, requestId, errorCode: 2));
        }

        private static bool LoanHasAck(DdsLoan<MissionControlAck> loan, Guid requestId, int? errorCode)
        {
            foreach (var sample in loan)
            {
                if (!sample.IsValid)
                    continue;

                if (sample.Data.RequestId != requestId)
                    continue;

                if (errorCode.HasValue && sample.Data.ErrorCode != errorCode.Value)
                    continue;

                return true;
            }

            return false;
        }
    }
}
