using CycloneDDS.Schema;

namespace Fdp.Examples.DDS
{
    /// <summary>
    /// Replicates <c>WeaponChannel</c> to the turret (physics) node.
    /// Used by the DistributedTank and UrbanCombat (new) scenarios.
    /// </summary>
    [DdsTopic("FDP.Demo_Weapon")]
    public partial struct DemoWeaponMsg
    {
        /// <summary>Unique long-lived network identifier for the entity.</summary>
        [DdsKey]
        public long NetworkId;

        /// <summary>Currently active weapon action identifier.</summary>
        public ushort ActiveAction;

        /// <summary>Doctrine instance governing this weapon command.</summary>
        public uint DoctrineInstanceId;

        /// <summary>Unique ID of the current action instance (for preemption).</summary>
        public uint ActionInstanceId;
    }
}
