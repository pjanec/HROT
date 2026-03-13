using Bagira.BDC.SSTD;
using Fdp.Kernel;

namespace Bagira.IG.Components
{
    [ComponentId(IgComponentIds.IgMissionHolder)]
    public class IgMissionHolder
    {
        public EntityMission Mission { get; set; } = new EntityMission();
    }
    
    public static class IgComponentIds
    {
        public const int IgMissionHolder = 221;
    }
}