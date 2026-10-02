using Fdp.Core;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// ⭐ The ECS part link: this entity is part <see cref="InstanceId"/> of <see cref="ParentEntity"/>. Network-agnostic
    /// (Q79 §0.10): a network layer that sends parts REUSES <see cref="InstanceId"/> as the descriptor instance id and
    /// maps <c>(root, descriptor, instance)</c> to the part through its own descriptor→component map.
    /// <para>⛔ S6 removed <c>DescriptorOrdinal</c>: every writer set 0 and nothing read it, and one EQS part is an
    /// instance of TWO descriptor types (config and result), so no single ordinal could name it.</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.PartMetadata)]
    public struct PartMetadata
    {
        public Entity ParentEntity;
        public int InstanceId;
    }
}
