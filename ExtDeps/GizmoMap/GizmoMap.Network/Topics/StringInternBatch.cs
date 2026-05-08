using CycloneDDS.Schema;

namespace GizmoMap.Network
{
    // Publishes all string intern entries for the current frame so remote subscribers
    // can resolve DrawTextLong hashes without the full StringInternMap.
    [DdsTopic("StringInternBatch")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct StringInternBatch
    {
        [DdsKey]     public uint FrameNumber;
        [DdsManaged] public uint[] Hashes;
        [DdsManaged] public string[] Texts;
    }
}
