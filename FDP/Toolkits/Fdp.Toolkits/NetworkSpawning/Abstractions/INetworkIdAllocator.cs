using System;

namespace Fdp.Toolkit.NetworkSpawning
{
    public interface INetworkIdAllocator : IDisposable
    {
        long AllocateId();
        void Reset( long startId = 0 );
    }
}
