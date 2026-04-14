using Fdp.Interfaces.Abstractions;
using CycloneDDS.Schema;
using Fdp.Core;

namespace Fdp.Examples.NetworkDemo.Components
{
    [FdpDescriptor(201, "FrameAckComponent")]
    [DdsTopic("FrameAckComponent")]
    [ComponentId(254)]
    public partial struct FrameAckComponent
    {
        [DdsKey]
        public long EntityId;

        public int SenderNodeId;
        public long CompletedFrameId;
    }
}
