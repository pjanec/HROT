using Bagira.Map.Definitions;
using Fdp.Kernel;

namespace Bagira.IG.Components
{
    [ComponentId(BagiraComponentIds.IgEntityData)]
    public struct EntityInfo
    {
        public FixedString64 Name;
        public ForceId ForceId;
        public int CommanderId;
    }
}
