using System;

namespace Fdp.ModuleHost.Network.Interfaces
{
    public interface INetworkIdAllocator : IDisposable
    {
        long AllocateId();
        void Reset(long startId = 0);
    }
}
