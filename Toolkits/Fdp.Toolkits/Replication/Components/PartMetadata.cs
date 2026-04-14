using Fdp.Kernel;

namespace Fdp.Toolkit.Replication.Components
{
    [ComponentId(GlobalComponentIds.PartMetadata)]
    public struct PartMetadata
    {
        public Entity ParentEntity;
        public int InstanceId;
        public int DescriptorOrdinal;
    }
}
