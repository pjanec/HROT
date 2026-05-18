using CycloneDDS.Schema;

namespace Fdp.Examples.DDS
{
    /// <summary>
    /// Replicates <c>LocomotionChannel</c> to the physics (muscle) node.
    /// Used by the DistributedTank and UrbanCombat (new) scenarios.
    /// </summary>
    [DdsTopic("FDP.Demo_Locomotion")]
    public partial struct DemoLocomotionMsg
    {
        /// <summary>Unique long-lived network identifier for the entity.</summary>
        [DdsKey]
        public long NetworkId;

        /// <summary>Currently active locomotion action identifier.</summary>
        public ushort ActiveAction;

        /// <summary>Behavior instance governing this locomotion command.</summary>
        public uint BehaviorInstanceId;

        /// <summary>Unique ID of the current action instance (for preemption).</summary>
        public uint ActionInstanceId;
    }
}
