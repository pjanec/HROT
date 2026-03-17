using CycloneDDS.Schema;

namespace Fdp.Examples.DDS
{
    /// <summary>
    /// Spawns or destroys a networked entity without an EntityMaster handshake.
    /// Used by the DistributedTank and UrbanCombat (new) scenarios.
    /// </summary>
    [DdsTopic("FDP.Demo_Spawn")]
    public partial struct DemoSpawnMsg
    {
        /// <summary>Unique long-lived network identifier for the entity.</summary>
        [DdsKey]
        public long NetworkId;

        /// <summary>TKB template type identifier (e.g. DemoTemplateIds.CommandTank = 100).</summary>
        public long TkbType;

        /// <summary>Node ID of the authoritative owner.</summary>
        public int OwnerNodeId;

        /// <summary>When true, the entity should be removed from the world.</summary>
        public bool IsDestroyed;
    }
}
