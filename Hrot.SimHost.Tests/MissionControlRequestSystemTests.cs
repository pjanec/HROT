using System;
using System.Collections.Generic;
using Hrot.NED.Messages;
using Hrot.NED.Descriptors;
using Hrot.Common.Events;
using Hrot.Map.Common.Helpers;
using Hrot.SimHost.Systems;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Events;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using Xunit;
using DdsMissionTrigger = Hrot.NED.Descriptors.MissionTrigger;
using EcsMissionTrigger = FDP.Toolkit.Behavior.Components.MissionTrigger;

namespace Hrot.SimHost.Tests
{
    public class MissionControlRequestSystemTests
    {
        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<MissionPlanQueue>();
            repo.RegisterComponent<DoctrineState>();
            repo.RegisterComponent<BrainBTreeState>();
            repo.RegisterManagedComponent<ActiveMissionPlan>();
            repo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });
            repo.RegisterEvent<MissionControlAckEvent>();
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
            var entityMap = new NetworkEntityMap();
            using var repo = CreateWorld();
            var entity = repo.CreateEntity();
            entityMap.Register(1, entity);

            var taskA = Guid.NewGuid();
            var taskB = Guid.NewGuid();
            var taskC = Guid.NewGuid();

            var system = new MissionControlExecutionSystem(entityMap, CreateDoctrineRegistry());
            system.Create(repo);

            system.TestHook_ProcessIntent(repo, new MissionControlIntent
            {
                RequestId      = Guid.NewGuid(),
                TargetEntityId = 1,
                BaseVersion    = 0,
                Payload = new MissionCommandUnion
                {
                    _d              = eMissionCommandType.CMD_REPLACE_MISSION,
                    FullMissionData = MakePlan(taskA, taskB, taskC)
                }
            });

            var requestId = Guid.NewGuid();
            system.TestHook_ProcessIntent(repo, new MissionControlIntent
            {
                RequestId      = requestId,
                TargetEntityId = 1,
                BaseVersion    = 0,
                Payload = new MissionCommandUnion
                {
                    _d           = eMissionCommandType.CMD_JUMP_TO_TASK,
                    TargetTaskId = taskC
                }
            });

            ref var queue = ref repo.GetComponentRW<MissionPlanQueue>(entity);
            Assert.Equal(2, queue.CurrentPhase);

            repo.Bus.SwapBuffers();
            Assert.True(BusHasAck(repo, requestId, errorCode: null));
        }

        [Fact]
        public void ProcessRequest_AbortAll_ClearsPlan()
        {
            var entityMap = new NetworkEntityMap();
            using var repo = CreateWorld();
            var entity = repo.CreateEntity();
            entityMap.Register(1, entity);

            var taskA = Guid.NewGuid();

            var system = new MissionControlExecutionSystem(entityMap, CreateDoctrineRegistry());
            system.Create(repo);

            system.TestHook_ProcessIntent(repo, new MissionControlIntent
            {
                RequestId      = Guid.NewGuid(),
                TargetEntityId = 1,
                BaseVersion    = 0,
                Payload = new MissionCommandUnion
                {
                    _d              = eMissionCommandType.CMD_REPLACE_MISSION,
                    FullMissionData = MakePlan(taskA)
                }
            });

            system.TestHook_ProcessIntent(repo, new MissionControlIntent
            {
                RequestId      = Guid.NewGuid(),
                TargetEntityId = 1,
                BaseVersion    = 0,
                Payload = new MissionCommandUnion
                {
                    _d                = eMissionCommandType.CMD_ABORT_ALL,
                    UnusedPlaceholder = true
                }
            });

            ref var queue = ref repo.GetComponentRW<MissionPlanQueue>(entity);
            Assert.Equal(0, queue.PhaseCount);
            Assert.Equal(0, queue.CurrentPhase);
        }

        [Fact]
        public void ProcessRequest_WritesAck()
        {
            var entityMap = new NetworkEntityMap();
            using var repo = CreateWorld();
            var entity = repo.CreateEntity();
            entityMap.Register(1, entity);

            var system = new MissionControlExecutionSystem(entityMap, CreateDoctrineRegistry());
            system.Create(repo);

            var requestId = Guid.NewGuid();
            system.TestHook_ProcessIntent(repo, new MissionControlIntent
            {
                RequestId      = requestId,
                TargetEntityId = 1,
                BaseVersion    = 0,
                Payload = new MissionCommandUnion
                {
                    _d              = eMissionCommandType.CMD_REPLACE_MISSION,
                    FullMissionData = new MissionPlan { Tasks = new List<MissionTask>() }
                }
            });

            repo.Bus.SwapBuffers();
            Assert.True(BusHasAck(repo, requestId, errorCode: 0));
        }

        [Fact]
        public void ProcessRequest_UnknownEntity_WritesNackAfterRetrying()
        {
            // With the retry-queue fix, an unknown entity is queued for up to
            // MaxEntityWaitFrames (10) retry frames before the NACK is emitted.
            var entityMap = new NetworkEntityMap();
            using var repo = CreateWorld();

            var system = new MissionControlExecutionSystem(entityMap, CreateDoctrineRegistry());
            system.Create(repo);

            var requestId = Guid.NewGuid();
            system.TestHook_ProcessIntent(repo, new MissionControlIntent
            {
                RequestId      = requestId,
                TargetEntityId = 999,
                BaseVersion    = 0,
                Payload = new MissionCommandUnion
                {
                    _d                = eMissionCommandType.CMD_ABORT_ALL,
                    UnusedPlaceholder = true
                }
            });

            // First call queues with framesLeft=10.
            // Drain 11 more cycles to exhaust the queue and emit the NACK.
            const int TotalDrains = 11; // MaxEntityWaitFrames(10) + 1
            for (int i = 0; i < TotalDrains; i++)
                system.TestHook_DrainRetryQueue(repo);

            repo.Bus.SwapBuffers();
            Assert.True(BusHasAck(repo, requestId, errorCode: (int)NedStatusCode.EntityNotFound));
        }

        private static bool BusHasAck(EntityRepository repo, Guid requestId, int? errorCode)
        {
            foreach (var evt in repo.Bus.Consume<MissionControlAckEvent>())
            {
                if (evt.RequestId != requestId)
                    continue;

                if (errorCode.HasValue && evt.ErrorCode != errorCode.Value)
                    continue;

                return true;
            }

            return false;
        }

        // ── Task-5 Tests: CMD_ABORT_ALL Doctrine Clear ────────────────────────────

        [Fact]
        public void AbortAll_PublishesClearDoctrineEvent()
        {
            var entityMap = new NetworkEntityMap();
            using var repo = CreateWorld();
            var entity = repo.CreateEntity();
            entityMap.Register(2, entity);

            // Give entity a non-empty plan and a DoctrineState.
            repo.AddComponent(entity, new DoctrineState { ActiveDoctrineHash = 2001, InstanceId = 3 });

            var system = new MissionControlExecutionSystem(entityMap, CreateDoctrineRegistry());
            system.Create(repo);

            // First assign a mission so PhaseCount > 0.
            var taskA = Guid.NewGuid();
            system.TestHook_ProcessIntent(repo, new MissionControlIntent
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

            // Now abort.
            system.TestHook_ProcessIntent(repo, new MissionControlIntent
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

            // ClearDoctrineEvent should be in the write buffer after processing.
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
            var entityMap = new NetworkEntityMap();
            using var repo = CreateWorld();
            var entity = repo.CreateEntity();
            entityMap.Register(3, entity);
            // No DoctrineState added.

            var system = new MissionControlExecutionSystem(entityMap, CreateDoctrineRegistry());
            system.Create(repo);

            var exception = Record.Exception(() => system.TestHook_ProcessIntent(repo, new MissionControlIntent
            {
                RequestId      = Guid.NewGuid(),
                TargetEntityId = 3,
                BaseVersion    = 0,
                Payload = new MissionCommandUnion
                {
                    _d                = eMissionCommandType.CMD_ABORT_ALL,
                    UnusedPlaceholder = true
                }
            }));
            Assert.Null(exception);

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
            var entityMap = new NetworkEntityMap();
            using var repo = CreateWorld();
            var entity = repo.CreateEntity();
            entityMap.Register(4, entity);

            var system = new MissionControlExecutionSystem(entityMap, CreateDoctrineRegistry());
            system.Create(repo);

            var requestId = Guid.NewGuid();
            system.TestHook_ProcessIntent(repo, new MissionControlIntent
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

            repo.Bus.SwapBuffers();
            Assert.True(BusHasAck(repo, requestId, errorCode: 0)); // NedStatusCode.Success == 0
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
