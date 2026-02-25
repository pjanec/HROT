using System.Linq;
using Bagira.BDC.SSTD;
using Bagira.SimHost.Components;
using FDP.Kernel.Logging;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Replication.Services;
using Fbt;
using ModuleHost.Core.Abstractions;

namespace Bagira.SimHost.Systems
{
    /// <summary>
    /// Thin adapter: DDS <see cref="EntityMissionHolder"/> → <see cref="DoctrineState"/> /
    /// <see cref="BrainBlackboard"/>.
    ///
    /// <para>
    /// Each frame it:
    /// <list type="number">
    ///   <item>Queries all entities with <see cref="EntityMissionHolder"/> (managed),
    ///         <see cref="DoctrineState"/>, and <see cref="BrainBlackboard"/>.</item>
    ///   <item>Resolves <c>MissionTask.BehaviorId</c> → stable integer via
    ///         <see cref="DoctrineRegistry.TryGetId"/>; logs a warning and skips if unknown.</item>
    ///   <item>If the resolved ID differs from <see cref="DoctrineState.ActiveDoctrineHash"/>:
    ///         sets the hash and invokes <see cref="DoctrineDefinition.ParseParams"/> to write
    ///         the JSON parameter payload into <see cref="BrainBlackboard.Memory"/>.</item>
    ///   <item>Reads <see cref="LocomotionChannel.Status"/>:
    ///         <see cref="NodeStatus.Success"/> → <see cref="AdvanceToNextTask"/>;
    ///         <see cref="NodeStatus.Failure"/> → <see cref="MarkTaskFailed"/>.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Does NOT call <c>VehicleAPI</c> directly — all execution is delegated to the
    /// Behavior toolkit pipeline (BTreeTickSystem + Executors).
    /// Pattern: like <c>CombatFeedbackSystem</c> (reads results, updates state).
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public class MissionAdapterSystem : ComponentSystem
    {
        private readonly DoctrineRegistry _doctrineRegistry;
        private readonly NetworkEntityMap _entityMap;

        public MissionAdapterSystem(DoctrineRegistry doctrineRegistry, NetworkEntityMap entityMap)
        {
            _doctrineRegistry = doctrineRegistry ?? throw new System.ArgumentNullException(nameof(doctrineRegistry));
            _entityMap        = entityMap        ?? throw new System.ArgumentNullException(nameof(entityMap));
        }

        protected override unsafe void OnUpdate()
        {
            var query = World.Query()
                .WithManaged<EntityMissionHolder>()
                .With<DoctrineState>()
                .With<BrainBlackboard>()
                .Build();

            var view = (ISimulationView)World;

            foreach (var entity in query)
            {
                var holder  = view.GetManagedComponentRO<EntityMissionHolder>(entity);
                var doctrine = World.GetComponent<DoctrineState>(entity);

                // 1. Find the active task in the mission plan.
                var plan          = holder.Mission.Plan;
                var maybeTask     = plan.Tasks?.FirstOrDefault(t => t.TaskId == plan.ActiveTaskId);
                if (!maybeTask.HasValue) continue;
                var activeTask = maybeTask.Value;

                // 2. Resolve BehaviorId string → stable doctrine integer.
                if (!_doctrineRegistry.TryGetId(activeTask.BehaviorId, out int doctrineId))
                {
                    FdpLog<MissionAdapterSystem>.Warn(
                        $"[MissionAdapter] Unknown BehaviorId: '{activeTask.BehaviorId}' on entity {entity.Index}. " +
                        $"Register the behavior in DoctrineRegistry at startup.");
                    continue;
                }

                // 3. Apply doctrine if the active doctrine has changed (new task or restart).
                if (doctrine.ActiveDoctrineHash != doctrineId)
                {
                    doctrine.ActiveDoctrineHash = doctrineId;
                    World.SetComponent(entity, doctrine);

                    // Parse JSON params into BrainBlackboard inline memory (zero-alloc cold path).
                    if (_doctrineRegistry.TryGetDefinition(doctrineId, out var def)
                        && def.ParseParams != null
                        && !string.IsNullOrEmpty(activeTask.BehaviorParams))
                    {
                        ref var bbRW = ref World.GetComponentRW<BrainBlackboard>(entity);
                        fixed (byte* ptr = &bbRW.Memory[0])
                            def.ParseParams(activeTask.BehaviorParams, ptr);
                    }
                }

                // 4. Monitor LocomotionChannel for task completion or failure.
                if (!World.HasComponent<LocomotionChannel>(entity)) continue;

                var channel = World.GetComponent<LocomotionChannel>(entity);
                if (channel.Status == NodeStatus.Success)
                    AdvanceToNextTask(entity, holder);
                else if (channel.Status == NodeStatus.Failure)
                    MarkTaskFailed(entity, holder, activeTask.TaskId);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Marks the current task as <see cref="eTaskState.TASK_DONE"/>, activates the next
        /// task in the plan, and removes <see cref="EntityMissionHolder"/> if the plan is
        /// exhausted (mission complete).
        /// </summary>
        private void AdvanceToNextTask(Entity entity, EntityMissionHolder holder)
        {
            var tasks  = holder.Mission.Plan.Tasks;
            var active = holder.Mission.Plan.ActiveTaskId;
            int idx    = tasks.FindIndex(t => t.TaskId == active);
            if (idx < 0) return;

            // Mark current task done.
            var done = tasks[idx];
            done.State = eTaskState.TASK_DONE;
            tasks[idx] = done;

            if (idx + 1 < tasks.Count)
            {
                // Activate the next task.
                var next = tasks[idx + 1];
                next.State = eTaskState.TASK_ACTIVE;
                tasks[idx + 1] = next;

                var mission    = holder.Mission;
                var plan       = mission.Plan;
                plan.ActiveTaskId = next.TaskId;
                mission.Plan   = plan;
                holder.Mission = mission;

                // Re-set to bump the managed-component version so the egress translator publishes the update.
                World.SetManagedComponent(entity, holder);
            }
            else
            {
                // All tasks complete — remove the component to stop further execution.
                // The egress translator will detect the absence and publish an appropriate update.
                World.RemoveComponent<EntityMissionHolder>(entity);
            }
        }

        /// <summary>
        /// Marks the specified task as <see cref="eTaskState.TASK_FAILED"/> and persists
        /// the change via <see cref="EntityRepository.SetManagedComponent{T}"/>.
        /// </summary>
        private void MarkTaskFailed(Entity entity, EntityMissionHolder holder, System.Guid taskId)
        {
            var tasks = holder.Mission.Plan.Tasks;
            int idx   = tasks.FindIndex(t => t.TaskId == taskId);
            if (idx < 0) return;

            var failed = tasks[idx];
            failed.State = eTaskState.TASK_FAILED;
            tasks[idx]   = failed;

            // Re-set to bump the managed-component version so the egress translator publishes the update.
            World.SetManagedComponent(entity, holder);
        }
    }
}
