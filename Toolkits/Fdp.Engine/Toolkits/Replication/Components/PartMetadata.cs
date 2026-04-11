using Fdp.Kernel;

namespace FDP.Toolkit.Replication.Components
{
    [ComponentId(GlobalComponentIds.PartMetadata)]
    public struct PartMetadata
    {
        public Entity ParentEntity;
        public int InstanceId;
        public int DescriptorOrdinal;
    }
}
