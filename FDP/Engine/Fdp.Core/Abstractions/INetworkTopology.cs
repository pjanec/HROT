using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Replication
{
    [ComponentId(GlobalComponentIds.INetworkTopology)]
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]   // ⭐ CE-3299 D7 — declared: a service / session object, not world state (D7)
    public interface INetworkTopology
    {
        /// <summary>
        /// This node's unique identifier.
        /// </summary>
        int LocalNodeId { get; }
        
        /// <summary>
        /// Gets the list of peer node IDs that should acknowledge construction
        /// of an entity of the given type.
        /// </summary>
        /// <param name="tkbType">Entity type identifier</param>
        /// <returns>Collection of node IDs that must ACK</returns>
        IEnumerable<int> GetExpectedPeers(long tkbType);
        
        /// <summary>
        /// Gets all known node IDs in the network.
        /// </summary>
        IEnumerable<int> GetAllNodes();
    }
}
