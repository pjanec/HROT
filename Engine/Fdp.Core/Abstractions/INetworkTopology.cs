using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Interfaces
{
    [ComponentId(GlobalComponentIds.INetworkTopology)]
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
