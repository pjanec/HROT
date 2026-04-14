using Fdp.Core;

namespace Hrot.Map.Definitions.Tkb
{
    public struct WeaponMount
    {
        /// <summary>
        /// Weapon type identifier (e.g., "120mm_APFSDS", "7.62mm_MG").
        /// </summary>
        public string WeaponType { get; set; }
        
        /// <summary>
        /// Initial ammunition count.
        /// </summary>
        public int Ammunition { get; set; }
        
        /// <summary>
        /// Effective range in meters.
        /// </summary>
        public float Range { get; set; }
        
        /// <summary>
        /// Rate of fire in rounds per minute.
        /// </summary>
        public float RateOfFire { get; set; }
    }
    
    /// <summary>
    /// Combat properties (weapons, armor, sensors).
    /// NOTE: Stubbed for future combat module integration.
    /// </summary>
    [ComponentId(GlobalComponentIds.SimCombatDef)]
    public class SimCombatDef
    {
        /// <summary>
        /// Frontal armor thickness in mm RHA equivalent.
        /// </summary>
        public float ArmorFront { get; set; } // mm RHA
        
        /// <summary>
        /// Side armor thickness in mm RHA equivalent.
        /// </summary>
        public float ArmorSide { get; set; }
        
        /// <summary>
        /// Rear armor thickness in mm RHA equivalent.
        /// </summary>
        public float ArmorRear { get; set; }
        
        /// <summary>
        /// Weapon systems mounted on vehicle.
        /// </summary>
        public List<WeaponMount> Weapons { get; set; } = new();
        
        /// <summary>
        /// Sensor detection range in meters.
        /// </summary>
        public float SensorRange { get; set; } // meters
        
        /// <summary>
        /// Whether entity can engage threats autonomously.
        /// </summary>
        public bool AutonomousEngagement { get; set; } = false;
    }
}
