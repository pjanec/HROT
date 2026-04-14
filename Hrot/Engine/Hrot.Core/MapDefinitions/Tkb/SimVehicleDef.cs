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
