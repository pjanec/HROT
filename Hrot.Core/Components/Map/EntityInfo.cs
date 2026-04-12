using Hrot.Map.Definitions;
using Fdp.Kernel;

namespace Hrot.IG.Components
{
    [ComponentId(HrotComponentIds.IgEntityData)]
    public struct EntityInfo
    {
        public FixedString64 Name;
        public ForceId ForceId;
        public int CommanderId;
    }
}
