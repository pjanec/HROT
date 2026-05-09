using CycloneDDS.Schema;

namespace GizmoMap.Network
{
    // Publishes each interned string as its own DDS instance keyed by hash,
    // allowing TransientLocal history to replay the full dictionary to late joiners.
    [DdsTopic("StringInternEntry")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct StringInternEntry
    {
        [DdsKey]     public byte NodeId;
        [DdsKey]     public uint Hash;
        [DdsManaged] public string Text;
    }
}
