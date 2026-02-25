using System;
using System.Collections.Generic;
using Bagira.BDC.SSTD;
using Bagira.SimHost;
using Bagira.SimHost.Components;
using Bagira.SimHost.Systems;
using Fdp.Kernel;
using Fbt;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Tests for <see cref="MissionAdapterSystem"/> (TASK-S4.3).
    ///
    /// Each test follows the pattern:
    /// <list type="bullet">
    ///   <item>Create a minimal <see cref="EntityRepository"/> and register components.</item>
    ///   <item>Seed an entity with <see cref="EntityMissionHolder"/>, <see cref="DoctrineState"/>,
    ///         <see cref="BrainBlackboard"/>, and optionally <see cref="LocomotionChannel"/>.</item>
    ///   <item>Create and run the system once.</item>
    ///   <item>Assert the resulting component state.</item>
    /// </list>
    /// </summary>
    public class MissionAdapterSystemTests
    {
        // ── World factory ─────────────────────────────────────────────────────

        private static EntityRepository CreateWorld()
        {
            var world = new EntityRepository();

            // Managed component for the DDS mission payload.
            world.RegisterManagedComponent<EntityMissionHolder>();

            // Behavior toolkit unmanaged components.
            world.RegisterComponent<DoctrineState>();
            world.RegisterComponent<BrainBlackboard>();
            world.RegisterComponent<LocomotionChannel>();

            // GlobalTime singleton required by ComponentSystem infrastructure.
            world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });

            return world;
        }

        /// <summary>
        /// Constructs a minimal <see cref="DoctrineRegistry"/> with the "MoveToLocation"
        /// behavior registered under <see cref="SimHostDoctrineIds.MoveTo_BT"/>.
        /// </summary>
        private static DoctrineRegistry BuildRegistry()
        {
            var registry = new DoctrineRegistry();
            registry.Register(SimHostDoctrineIds.MoveTo_BT, "MoveToLocation", new DoctrineDefinition
            {
                Name       = "MoveToLocation",
                BrainTier  = BehaviorConstants.BrainTierBTree,
                ParseParams = null,     // No JSON params in this test fixture
            });
            return registry;
        }

        /// <summary>
        /// Creates a single-task <see cref="EntityMission"/> whose active task has the
        /// specified <paramref name="behaviorId"/>.
        /// </summary>
        private static EntityMission MakeSingleTaskMission(
            string behaviorId, out Guid taskId, long entityId = 1)
        {
            taskId = Guid.NewGuid();
            return new EntityMission
            {
                EntityId = entityId,
                Plan     = new MissionPlan
                {
                    ActiveTaskId = taskId,
                    Tasks        = new List<MissionTask>
                    {
                        new MissionTask
                        {
                            TaskId          = taskId,
                            BehaviorId      = behaviorId,
                            BehaviorParams  = "{}",
                            ExecutingEngine = "CGFX",
                            State           = eTaskState.TASK_ACTIVE,
                            Triggers        = new List<Bagira.BDC.SSTD.MissionTrigger>(),
                        }
                    }
                }
            };
        }

        /// <summary>
        /// Creates a two-task <see cref="EntityMission"/> where task 1 is active.
        /// </summary>
        private static EntityMission MakeTwoTaskMission(
            string behaviorId,
            out Guid task1Id,
            out Guid task2Id,
            long entityId = 1)
        {
            task1Id = Guid.NewGuid();
            task2Id = Guid.NewGuid();
            return new EntityMission
            {
                EntityId = entityId,
                Plan     = new MissionPlan
                {
                    ActiveTaskId = task1Id,
                    Tasks        = new List<MissionTask>
                    {
                        new MissionTask
                        {
                            TaskId          = task1Id,
                            BehaviorId      = behaviorId,
                            BehaviorParams  = "{}",
                            ExecutingEngine = "CGFX",
                            State           = eTaskState.TASK_ACTIVE,
                            Triggers        = new List<Bagira.BDC.SSTD.MissionTrigger>(),
                        },
                        new MissionTask
                        {
                            TaskId          = task2Id,
                            BehaviorId      = behaviorId,
                            BehaviorParams  = "{}",
                            ExecutingEngine = "CGFX",
                            State           = eTaskState.TASK_PLANNED,
                            Triggers        = new List<Bagira.BDC.SSTD.MissionTrigger>(),
                        }
                    }
                }
            };
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Given <c>BehaviorId = "MoveToLocation"</c> and <c>DoctrineState.ActiveDoctrineHash == 0</c>,
        /// the system must resolve the ID and set
        /// <c>DoctrineState.ActiveDoctrineHash == <see cref="SimHostDoctrineIds.MoveTo_BT"/></c>.
        /// </summary>
        [Fact]
        public void MissionAdapter_ResolvesDoctrineId()
        {
            // ── Arrange ───────────────────────────────────────────────────────
            using var world = CreateWorld();
            var registry    = BuildRegistry();
            var entity      = world.CreateEntity();

            world.SetManagedComponent(entity, new EntityMissionHolder
            {
                Mission = MakeSingleTaskMission("MoveToLocation", out _)
            });
            world.AddComponent(entity, new DoctrineState { ActiveDoctrineHash = 0 });
            world.AddComponent(entity, new BrainBlackboard());

            var system = new MissionAdapterSystem(registry, new NetworkEntityMap());
            system.Create(world);

            // ── Act ───────────────────────────────────────────────────────────
            system.Run();

            // ── Assert ────────────────────────────────────────────────────────
            var doctrine = world.GetComponent<DoctrineState>(entity);
            Assert.Equal(SimHostDoctrineIds.MoveTo_BT, doctrine.ActiveDoctrineHash);
        }

        /// <summary>
        /// When <c>LocomotionChannel.Status == Success</c> on the entity whose first task
        /// is active, the system must:
        /// <list type="bullet">
        ///   <item>Mark task 1 as <see cref="eTaskState.TASK_DONE"/>.</item>
        ///   <item>Advance <c>ActiveTaskId</c> to task 2.</item>
        ///   <item>Mark task 2 as <see cref="eTaskState.TASK_ACTIVE"/>.</item>
        /// </list>
        /// </summary>
        [Fact]
        public void MissionAdapter_AdvancesTaskOnSuccess()
        {
            // ── Arrange ───────────────────────────────────────────────────────
            using var world = CreateWorld();
            var registry    = BuildRegistry();
            var entity      = world.CreateEntity();

            var mission = MakeTwoTaskMission("MoveToLocation", out var task1Id, out var task2Id);
            world.SetManagedComponent(entity, new EntityMissionHolder { Mission = mission });
            world.AddComponent(entity, new DoctrineState
            {
                ActiveDoctrineHash = SimHostDoctrineIds.MoveTo_BT   // Already resolved — skip parse step
            });
            world.AddComponent(entity, new BrainBlackboard());
            world.AddComponent(entity, new LocomotionChannel { Status = NodeStatus.Success });

            var system = new MissionAdapterSystem(registry, new NetworkEntityMap());
            system.Create(world);

            // ── Act ───────────────────────────────────────────────────────────
            system.Run();

            // ── Assert ────────────────────────────────────────────────────────
            Assert.True(world.HasManagedComponent<EntityMissionHolder>(entity),
                "EntityMissionHolder should still exist (two tasks, only first completed).");

            var holder  = ((ISimulationView)world).GetManagedComponentRO<EntityMissionHolder>(entity);
            var tasks   = holder.Mission.Plan.Tasks;

            Assert.Equal(eTaskState.TASK_DONE,   tasks[0].State);
            Assert.Equal(eTaskState.TASK_ACTIVE,  tasks[1].State);
            Assert.Equal(task2Id, holder.Mission.Plan.ActiveTaskId);
        }

        /// <summary>
        /// When <c>LocomotionChannel.Status == Failure</c>, the active task must be
        /// marked <see cref="eTaskState.TASK_FAILED"/>.
        /// </summary>
        [Fact]
        public void MissionAdapter_MarksFailedOnChannelFailure()
        {
            // ── Arrange ───────────────────────────────────────────────────────
            using var world = CreateWorld();
            var registry    = BuildRegistry();
            var entity      = world.CreateEntity();

            world.SetManagedComponent(entity, new EntityMissionHolder
            {
                Mission = MakeSingleTaskMission("MoveToLocation", out var taskId)
            });
            world.AddComponent(entity, new DoctrineState
            {
                ActiveDoctrineHash = SimHostDoctrineIds.MoveTo_BT
            });
            world.AddComponent(entity, new BrainBlackboard());
            world.AddComponent(entity, new LocomotionChannel { Status = NodeStatus.Failure });

            var system = new MissionAdapterSystem(registry, new NetworkEntityMap());
            system.Create(world);

            // ── Act ───────────────────────────────────────────────────────────
            system.Run();

            // ── Assert ────────────────────────────────────────────────────────
            var holder = ((ISimulationView)world).GetManagedComponentRO<EntityMissionHolder>(entity);
            Assert.Equal(eTaskState.TASK_FAILED, holder.Mission.Plan.Tasks[0].State);
        }

        /// <summary>
        /// An unknown <c>BehaviorId</c> string (not registered in the
        /// <see cref="DoctrineRegistry"/>) must NOT throw — the system should log a
        /// warning and silently skip the entity.
        /// </summary>
        [Fact]
        public void MissionAdapter_UnknownBehaviorId_DoesNotThrow()
        {
            // ── Arrange ───────────────────────────────────────────────────────
            using var world = CreateWorld();
            var registry    = new DoctrineRegistry(); // empty registry — no doctrines
            var entity      = world.CreateEntity();

            world.SetManagedComponent(entity, new EntityMissionHolder
            {
                Mission = MakeSingleTaskMission("NonExistentBehavior", out _)
            });
            world.AddComponent(entity, new DoctrineState());
            world.AddComponent(entity, new BrainBlackboard());

            var system = new MissionAdapterSystem(registry, new NetworkEntityMap());
            system.Create(world);

            // ── Act + Assert ──────────────────────────────────────────────────
            // Must not throw even though the BehaviorId is unregistered.
            var ex = Record.Exception(() => system.Run());
            Assert.Null(ex);

            // DoctrineState must remain at default (0) since no doctrine was resolved.
            var doctrine = world.GetComponent<DoctrineState>(entity);
            Assert.Equal(0, doctrine.ActiveDoctrineHash);
        }

        /// <summary>
        /// When the only task completes (Success), the
        /// <see cref="EntityMissionHolder"/> component must be removed (mission complete).
        /// </summary>
        [Fact]
        public void MissionAdapter_MissionComplete_RemovesEntityMissionHolder()
        {
            // ── Arrange ───────────────────────────────────────────────────────
            using var world = CreateWorld();
            var registry    = BuildRegistry();
            var entity      = world.CreateEntity();

            world.SetManagedComponent(entity, new EntityMissionHolder
            {
                Mission = MakeSingleTaskMission("MoveToLocation", out _)
            });
            world.AddComponent(entity, new DoctrineState
            {
                ActiveDoctrineHash = SimHostDoctrineIds.MoveTo_BT
            });
            world.AddComponent(entity, new BrainBlackboard());
            world.AddComponent(entity, new LocomotionChannel { Status = NodeStatus.Success });

            var system = new MissionAdapterSystem(registry, new NetworkEntityMap());
            system.Create(world);

            // ── Act ───────────────────────────────────────────────────────────
            system.Run();

            // ── Assert ────────────────────────────────────────────────────────
            Assert.False(world.HasManagedComponent<EntityMissionHolder>(entity),
                "EntityMissionHolder must be removed when the last task completes.");
        }
    }
}
