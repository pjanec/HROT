using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Replication.Services;

namespace Bagira.SimHost.Systems
{
    /// <summary>
    /// Stub for MissionAdapterSystem.
    /// Full implementation deferred to TASK-S4.3.
    ///
    /// Runs first each frame. Will eventually map the active
    /// <c>MissionTask.BehaviorId</c> string to a <c>DoctrineId</c>, write
    /// parameters into <c>BrainBlackboard</c>, and monitor
    /// <c>LocomotionChannel.Status</c> to advance <c>ActiveTaskId</c>.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public class MissionAdapterSystem : ComponentSystem
    {
        private readonly DoctrineRegistry _doctrineRegistry;
        private readonly NetworkEntityMap _entityMap;

        public MissionAdapterSystem(DoctrineRegistry doctrineRegistry, NetworkEntityMap entityMap)
        {
            _doctrineRegistry = doctrineRegistry;
            _entityMap        = entityMap;
        }

        protected override void OnUpdate()
        {
            // TODO (TASK-S4.3): implement doctrine-to-task mapping and task advancement.
        }
    }
}
