using Bagira.BDC.SSTD;
using Bagira.Map.Definitions;
using Fdp.Kernel;

namespace Bagira.IG.Components
{
    [ComponentId(BagiraComponentIds.IgEntityData)]
    public class EntityInfo
    {
        public string Name { get; set; } = string.Empty;
        public ForceId ForceId { get; set; } = ForceId.Unknown;
        public int CommanderId { get; set; } = 0;
    }
}
