using CycloneDDS.Schema;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Network
{
    // Publishes all debug primitives for the current frame so remote subscribers
    // can render gizmos without running the simulation locally.
    [DdsTopic("DebugPrimitivesBatch")]
    [DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.Volatile,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct DebugPrimitivesBatch
    {
        [DdsKey] public uint FrameNumber;
        [DdsKey] public byte NodeId;
        [DdsManaged] public DebugPrimitive[] Primitives;
    }
}
