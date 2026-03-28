using System;
using System.Collections.Generic;
using System.Threading;
using Bagira.BDC.SSTM;
using Bagira.BDC.SSTD;
using Bagira.Map.Common.Helpers;
using Bagira.SimHost.Systems;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Events;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using Xunit;
using DdsMissionTrigger = Bagira.BDC.SSTD.MissionTrigger;
using EcsMissionTrigger = FDP.Toolkit.Behavior.Components.MissionTrigger;

namespace Bagira.SimHost.Tests
{
    [Collection("SimHostDds")]
    public class MissionControlRequestSystemTests
    {
        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<MissionPlanQueue>();
            repo.RegisterComponent<DoctrineState>();
            repo.RegisterComponent<BrainBTreeState>();
            repo.RegisterManagedComponent<Bagira.SimHost.Components.EntityMissionHolder>();
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
        public void ProcessRequest_UnknownEntity_WritesNackAfterRetrying()
        {
            // With the retry-queue fix, an unknown entity is queued for up to
            // MaxEntityWaitFrames (10) retry frames before the NACK is emitted.
            // That means the ACK arrives on Run 12 (= 1 initial + 10 retries + 1 final).
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

            // Run once to consume the DDS message and enqueue for retry, then
            // run MaxEntityWaitFrames + 1 more times to exhaust framesLeft down to 0
            // and emit the NACK on the final run.
            const int TotalRunsNeeded = 12; // MaxEntityWaitFrames(10) + 2
            for (int i = 0; i < TotalRunsNeeded; i++)
                system.Run();

            Thread.Sleep(200);

            using var loan = reader.Take();
            Assert.True(LoanHasAck(loan, requestId, errorCode: (int)SstStatusCode.EntityNotFound));
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

        // ── Task-5 Tests: CMD_ABORT_ALL Doctrine Clear ────────────────────────────

        [Fact]
        public void AbortAll_PublishesClearDoctrineEvent()
        {
            const uint domainId = 160u;
            using var participant = new DdsParticipant(domainId);
            using var writer = new DdsWriter<MissionControlRequest>(participant);

            var entityMap = new NetworkEntityMap();
            using var repo = CreateWorld();
            var entity = repo.CreateEntity();
            entityMap.Register(2, entity);

            // Give entity a non-empty plan and a DoctrineState.
            repo.AddComponent(entity, new DoctrineState { ActiveDoctrineHash = 2001, InstanceId = 3 });

            var system = new MissionControlRequestSystem(participant, entityMap, CreateDoctrineRegistry());
            system.Create(repo);

            // First assign a mission so PhaseCount > 0.
            var taskA = Guid.NewGuid();
            writer.Write(new MissionControlRequest
            {
                RequestId      = Guid.NewGuid(),
                TargetEntityId = 2,
                BaseVersion    = 0,
                Payload = new MissionCommandUnion
                {
                    _d              = eMissionCommandType.CMD_REPLACE_MISSION,
                    FullMissionData = MakePlan(taskA)
                }
            });
            Thread.Sleep(200);
            system.Run();
            Thread.Sleep(200);

            // Now abort.
            writer.Write(new MissionControlRequest
            {
                RequestId      = Guid.NewGuid(),
                TargetEntityId = 2,
                BaseVersion    = 0,
                Payload = new MissionCommandUnion
                {
                    _d                = eMissionCommandType.CMD_ABORT_ALL,
                    UnusedPlaceholder = true
                }
            });
            Thread.Sleep(200);
            system.Run();
            Thread.Sleep(200);

            // ClearDoctrineEvent should be in the write buffer after ProcessRequest.
            repo.Bus.SwapBuffers();
            bool found = false;
            foreach (var evt in repo.Bus.Consume<ClearDoctrineEvent>())
                if (evt.Entity.Index == entity.Index) found = true;

            Assert.True(found);
            Assert.Equal(0, repo.GetComponent<MissionPlanQueue>(entity).PhaseCount);
        }

        [Fact]
        public void AbortAll_NoDoctrineState_DoesNotThrow()
        {
            // Entity without DoctrineState — ClearDoctrineEvent still published;
            // DoctrineIngressSystem provides the guard against missing components.
            const uint domainId = 161u;
            using var participant = new DdsParticipant(domainId);
            using var writer = new DdsWriter<MissionControlRequest>(participant);

            var entityMap = new NetworkEntityMap();
            using var repo = CreateWorld();
            var entity = repo.CreateEntity();
            entityMap.Register(3, entity);
            // No DoctrineState added.

            var system = new MissionControlRequestSystem(participant, entityMap, CreateDoctrineRegistry());
            system.Create(repo);

            writer.Write(new MissionControlRequest
            {
                RequestId      = Guid.NewGuid(),
                TargetEntityId = 3,
                BaseVersion    = 0,
                Payload = new MissionCommandUnion
                {
                    _d                = eMissionCommandType.CMD_ABORT_ALL,
                    UnusedPlaceholder = true
                }
            });
            Thread.Sleep(200);

            var exception = Record.Exception(() => system.Run());
            Assert.Null(exception);

            Thread.Sleep(200);
            Assert.Equal(0, repo.GetComponent<MissionPlanQueue>(entity).PhaseCount);

            // Guard: ClearDoctrineEvent still published even without DoctrineState component.
            repo.Bus.SwapBuffers();
            bool found = false;
            foreach (var evt in repo.Bus.Consume<ClearDoctrineEvent>())
                if (evt.Entity.Index == entity.Index) found = true;
            Assert.True(found);
        }

        [Fact]
        public void AbortAll_WritesSuccessAck()
        {
            const uint domainId = 162u;
            using var participant = new DdsParticipant(domainId);
            using var writer = new DdsWriter<MissionControlRequest>(participant);
            using var reader = new DdsReader<MissionControlAck>(participant);

            var entityMap = new NetworkEntityMap();
            using var repo = CreateWorld();
            var entity = repo.CreateEntity();
            entityMap.Register(4, entity);

            var system = new MissionControlRequestSystem(participant, entityMap, CreateDoctrineRegistry());
            system.Create(repo);

            var requestId = Guid.NewGuid();
            writer.Write(new MissionControlRequest
            {
                RequestId      = requestId,
                TargetEntityId = 4,
                BaseVersion    = 0,
                Payload = new MissionCommandUnion
                {
                    _d                = eMissionCommandType.CMD_ABORT_ALL,
                    UnusedPlaceholder = true
                }
            });
            Thread.Sleep(200);
            system.Run();
            Thread.Sleep(200);

            using var loan = reader.Take();
            Assert.True(LoanHasAck(loan, requestId, errorCode: 0)); // SstErrorCode.Success == 0
        }

        // ── BUG2-M001 – ResolveTrigger new cases ─────────────────────────────

        [Fact]
        public void ResolveTrigger_DoctrineFinished_ReturnsCorrectEnum()
        {
            var triggers = new List<DdsMissionTrigger>
            {
                new DdsMissionTrigger { Type = "DoctrineFinished", Params = "" }
            };

            var (trigger, param) = MissionTriggerHelper.ResolveTrigger(triggers);

            Assert.Equal(EcsMissionTrigger.DoctrineFinished, trigger);
            Assert.Equal(0f, param);
        }

        [Fact]
        public void ResolveTrigger_UnderAttack_ReturnsCorrectEnum()
        {
            var triggers = new List<DdsMissionTrigger>
            {
                new DdsMissionTrigger { Type = "UnderAttack", Params = "" }
            };

            var (trigger, param) = MissionTriggerHelper.ResolveTrigger(triggers);

            Assert.Equal(EcsMissionTrigger.UnderAttack, trigger);
            Assert.Equal(0f, param);
        }
    }
}
