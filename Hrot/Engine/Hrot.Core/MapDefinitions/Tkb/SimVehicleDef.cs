namespace Hrot.Map.Definitions.Tkb
{
    public enum TerrainMobility
    {
        Tracked,   // Tanks, heavy IFVs
        Wheeled,   // Trucks, light vehicles
        Infantry,  // Dismounted soldiers
        Air,       // Helicopters, fixed-wing
        Naval      // Ships, boats
    }
    
    /// <summary>
    /// SimHost physics properties (mass, dimensions, mobility).
    /// </summary>
    public class SimVehicleDef
    {
        /// <summary>
        /// Vehicle mass in kilograms.
        /// </summary>
        public float Mass { get; set; } // kg
        
        /// <summary>
        /// Vehicle length in meters.
        /// </summary>
        public float Length { get; set; } // meters
        
        /// <summary>
        /// Vehicle width in meters.
        /// </summary>
        public float Width { get; set; } // meters
        
        /// <summary>
        /// Vehicle height in meters.
        /// </summary>
        public float Height { get; set; } // meters
        
        /// <summary>
        /// Maximum speed in meters per second.
        /// </summary>
        public float MaxSpeed { get; set; } // m/s

        /// <summary>
        /// Maximum reverse speed in meters per second.
        /// </summary>
        public float MaxSpeedRev { get; set; } // m/s
        
        /// <summary>
        /// ⭐ CE-1208 — a person's WALK in meters per second (the operator's walking gait; <see cref="MaxSpeed"/> is his run). 0 = the
        /// engine's default walk (<c>HumanGait.WalkSpeed</c>).
        /// </summary>
        public float WalkSpeed { get; set; } // m/s

        /// <summary>
        /// Acceleration in meters per second squared.
        /// </summary>
        public float Acceleration { get; set; } // m/s²
        
        /// <summary>
        /// Turn rate in degrees per second.
        /// </summary>
        public float TurnRate { get; set; } // deg/s
        
        /// <summary>
        /// Terrain mobility type.
        /// </summary>
        public TerrainMobility Mobility { get; set; }

        /// <summary>⭐ M9 (DESIGN_Vehicle_Exposure_Cycle §6) — the kinematics preset when <see cref="Mobility"/>'s mapping is not the one
        /// (a pickup is wheeled but drives like a car, not a truck); null = by mobility.</summary>
        public CarKinem.Core.VehicleClass? Preset { get; set; }
        
        /// <summary>
        /// Fuel capacity in liters (0 = unlimited).
        /// </summary>
        public float FuelCapacity { get; set; } = 0;
        
        /// <summary>
        /// Fuel consumption rate in liters per hour at max speed.
        /// </summary>
        public float FuelConsumption { get; set; } = 0;
    }
}
