using Hrot.NED.Descriptors;
using Fdp.Kernel;

namespace Hrot.IG.Components
{
    [ComponentId(IgComponentIds.IgMissionHolder)]
    public class IgMissionHolder
    {
        public EntityMission Mission { get; set; } = new EntityMission();
    }
    
    public static class IgComponentIds
    {
        public const int IgMissionHolder = 123;
    }
}