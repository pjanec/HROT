using CycloneDDS.Schema;

namespace GizmoMap.Network
{
    // One record per SimHost instance. Keyed by NodeId.
    // Carries the full JSON schema of entity attributes supported by this node's JsonAttributeCompiler.
    // TransientLocal: late-joining ExCon subscribers immediately receive the latest published schema.
    [DdsTopic("EntityAttributeSchema")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    [DdsManaged]
    public partial struct EntityAttributeSchema
    {
        [DdsKey]
        public int NodeId;

        // JSON Schema document describing all attribute paths, types, and validation rules
        // known to this node's JsonAttributeCompiler instance.
        [DdsManaged] public string SchemaJson;
    }
}
