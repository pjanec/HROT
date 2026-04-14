using System.Collections.Generic;

namespace Fdp.ModuleHost_Core.Network.Interfaces
{
    public enum ReliableInitType
    {
        None,
        PhysicsServer,
        AllPeers
    }

    public interface INetworkTopology
    {
        IEnumerable<int> GetExpectedPeers(ReliableInitType type);
    }
}
