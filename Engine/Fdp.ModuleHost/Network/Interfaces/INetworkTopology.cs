using System.Collections.Generic;

namespace Fdp.ModuleHost.Network.Interfaces
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
