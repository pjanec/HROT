using System.Collections.Generic;

namespace ModuleHost.Core.Network.Interfaces
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
