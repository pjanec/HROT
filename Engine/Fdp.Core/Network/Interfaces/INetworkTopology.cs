using System.Collections.Generic;

namespace Fdp.ModuleHost.Core.Network.Interfaces
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
