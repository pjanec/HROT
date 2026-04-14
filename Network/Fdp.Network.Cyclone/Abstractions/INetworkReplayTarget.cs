using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Interfaces;

namespace Fdp.Network.Cyclone.Abstractions
{
    public interface INetworkReplayTarget
    {
        long DescriptorOrdinal { get; }
        
        // Accepts raw bytes from the replay file
        void InjectReplayData(ReadOnlySpan<byte> rawData, IEntityCommandBuffer cmd, ISimulationView view);
    }
}
