using System;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;

namespace ModuleHost.Network.Cyclone.Abstractions
{
    public interface INetworkReplayTarget
    {
        long DescriptorOrdinal { get; }
        
        // Accepts raw bytes from the replay file
        void InjectReplayData(ReadOnlySpan<byte> rawData, IEntityCommandBuffer cmd, ISimulationView view);
    }
}
