using System;
using Fdp.Kernel;
using Fdp.ModuleHost_Core.Abstractions;

namespace Fdp.Network.Cyclone.Abstractions
{
    public interface INetworkReplayTarget
    {
        long DescriptorOrdinal { get; }
        
        // Accepts raw bytes from the replay file
        void InjectReplayData(ReadOnlySpan<byte> rawData, IEntityCommandBuffer cmd, ISimulationView view);
    }
}
