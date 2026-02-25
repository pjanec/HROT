using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Executors;
using FDP.Toolkit.Replication.Services;
using CarKinem.Commands;

namespace Bagira.SimHost.Systems
{
    /// <summary>
    /// Stub for JoinFormationExecutor.
    /// Full implementation deferred to TASK-S4.4.
    ///
    /// Will handle the JoinFormation locomotion action by issuing a
    /// <c>CmdJoinFormation</c> command via <c>VehicleAPI</c> and monitoring
    /// formation membership status via <c>NetworkEntityMap</c>.
    /// </summary>
    public sealed class JoinFormationExecutor : IActionExecutor<LocomotionChannel>
    {
        private readonly VehicleAPI?      _vehicleAPI;
        private readonly NetworkEntityMap _entityMap;

        public JoinFormationExecutor(VehicleAPI? vehicleAPI, NetworkEntityMap entityMap)
        {
            _vehicleAPI = vehicleAPI;
            _entityMap  = entityMap;
        }

        /// <inheritdoc/>
        public void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            // TODO (TASK-S4.4): issue CmdJoinFormation via _vehicleAPI.
        }

        /// <inheritdoc/>
        public void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            // TODO (TASK-S4.4): monitor formation status and set channel.Status.
        }

        /// <inheritdoc/>
        public void OnExit(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            // TODO (TASK-S4.4): clean up formation membership on exit.
        }
    }
}
