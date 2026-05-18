using CycloneDDS.Schema;

namespace Fdp.Examples.DDS
{
    /// <summary>
    /// Cross-process fire/hit notification.
    /// Used by the DistributedTank and UrbanCombat (new) scenarios.
    /// </summary>
    [DdsTopic("FDP.Demo_CombatInteraction")]
    public partial struct DemoCombatInteractionMsg
    {
        /// <summary>Network ID of the firing entity.</summary>
        public long ShooterNetId;

        /// <summary>Network ID of the target entity.</summary>
        public long TargetNetId;

        /// <summary><c>true</c> when the projectile successfully hit the target.</summary>
        public bool IsHit;

        /// <summary>Damage applied to the target (hit points).</summary>
        public float Damage;
    }
}
