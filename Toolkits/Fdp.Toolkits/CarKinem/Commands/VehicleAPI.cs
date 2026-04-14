using System.Numerics;
using CarKinem.Core;
using CarKinem.Formation;
using Fdp.Kernel;
using Fdp.ModuleHost.Abstractions;

namespace CarKinem.Commands
{
    /// <summary>
    /// High-level API for vehicle commands.
    /// Facade over event system.
    /// </summary>
    public class VehicleAPI
    {
        private readonly ISimulationView _view;
        
        public VehicleAPI(ISimulationView view)
        {
            _view = view;
        }

        /// <summary>
        /// Spawn a new vehicle at the specified position.
        /// Note: Entity must be pre-allocated via command buffer.
        /// </summary>
        public void SpawnVehicle(Entity entity, Vector2 position, Vector2 heading, 
            VehicleClass vehicleClass = VehicleClass.PersonalCar)
        {
            var cmd = _view.GetCommandBuffer();
            cmd.PublishEvent(new CmdSpawnVehicle
            {
                Entity = entity,
                Position = position,
                Heading = heading,
                Class = vehicleClass
            });
        }
        
        /// <summary>
        /// Create a formation with the specified leader.
        /// </summary>
        public void CreateFormation(Entity leaderEntity, FormationType type, 
            FormationParams? parameters = null)
        {
            var cmd = _view.GetCommandBuffer();
            
            // Use default params if not specified
            var params_ = parameters ?? new FormationParams
            {
                Spacing = 5.0f,
                WedgeAngleRad = 0.52f,  // ~30 degrees
                MaxCatchUpFactor = 1.2f,
                BreakDistance = 50.0f,
                ArrivalThreshold = 2.0f,
                SpeedFilterTau = 0.5f
            };
            
            cmd.PublishEvent(new CmdCreateFormation
            {
                LeaderEntity = leaderEntity,
                Type = type,
                Params = params_
            });
        }
        
        /// <summary>
        /// Command vehicle to join a formation.
        /// Updated signature to use leader entity.
        /// </summary>
        public void JoinFormation(Entity followerEntity, Entity leaderEntity, int slotIndex = -1)
        {
            var cmd = _view.GetCommandBuffer();
            
            // Auto-assign slot if not specified
            if (slotIndex < 0)
            {
                // TODO: Query roster to find next available slot
                slotIndex = 1;  // Default to slot 1 (leader is 0)
            }
            
            cmd.PublishEvent(new CmdJoinFormation
            {
                Entity = followerEntity,
                LeaderEntity = leaderEntity,
                SlotIndex = slotIndex
            });
        }
        
        /// <summary>
        /// Command vehicle to leave its formation.
        /// </summary>
        public void LeaveFormation(Entity entity)
        {
            var cmd = _view.GetCommandBuffer();
            cmd.PublishEvent(new CmdLeaveFormation
            {
                Entity = entity
            });
        }
    }
}
