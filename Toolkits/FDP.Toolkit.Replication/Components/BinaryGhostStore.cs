using System.Collections.Generic;
using Fdp.Kernel;

namespace FDP.Toolkit.Replication.Components
{
    [ComponentId(GlobalComponentIds.BinaryGhostStore)]
    public class BinaryGhostStore
    {
        /// <summary>
        /// Stores packed keys mapped to binary descriptor data.
        /// </summary>
        public Dictionary<long, byte[]> StashedData = new Dictionary<long, byte[]>();
        
        /// <summary>
        /// Frame number when the ghost was first created.
        /// </summary>
        public uint FirstSeenFrame;
        
        /// <summary>
        /// Frame number when the ghost's identity (NetworkSpawnRequest/Type) was resolved.
        /// </summary>
        public uint IdentifiedAtFrame;
    }
}
