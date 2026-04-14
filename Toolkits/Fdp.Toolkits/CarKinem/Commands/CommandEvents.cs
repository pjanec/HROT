using System.Numerics;
using CarKinem.Core;
using CarKinem.Formation;
using Fdp.Core;

namespace CarKinem.Commands
{
    [EventId(2104)]
    public struct CmdJoinFormation
    {
        public Entity Entity;        // Follower entity
        public Entity LeaderEntity;  // Formation leader entity (has FormationRoster)
        public int SlotIndex;        // Desired slot (0-15)
    }
    
    [EventId(2105)]
    public struct CmdLeaveFormation
    {
        public Entity Entity;
    }

    [EventId(2108)]
    public struct CmdSpawnVehicle
    {
        public Entity Entity;           // Pre-allocated entity (see notes)
        public Vector2 Position;        // Initial position
        public Vector2 Heading;         // Initial heading vector (normalized)
        public VehicleClass Class;      // Vehicle class (PersonalCar, Truck, etc.)
    }

    [EventId(2109)]
    public struct CmdCreateFormation
    {
        public Entity LeaderEntity;     // Entity to become formation leader
        public FormationType Type;      // Column, Wedge, Line, Custom
        public FormationParams Params;  // Formation parameters
    }
}
