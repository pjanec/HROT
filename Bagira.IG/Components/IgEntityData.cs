using Bagira.BDC.SSTD;
using Fdp.Kernel;

namespace Bagira.IG.Components
{
    [ComponentId(GlobalComponentIds.IgEntityData)]
    public class IgEntityData
    {
        public string Name { get; set; } = string.Empty;
        public ForceId ForceId { get; set; } = ForceId.Unknown;
        public int CommanderId { get; set; } = 0;
    }
}
