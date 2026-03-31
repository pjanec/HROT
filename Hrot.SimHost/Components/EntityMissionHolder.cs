using Hrot.NED.Descriptors;
using Hrot.Map.Definitions;
using Fdp.Kernel;

namespace Hrot.SimHost.Components
{
    /// <summary>
    /// Managed wrapper carrying the latest <see cref="EntityMission"/> payload.
    /// </summary>
    [ComponentId(HrotComponentIds.EntityMissionHolder)]
    public class EntityMissionHolder
    {
        public EntityMission Mission { get; set; } = new EntityMission();
    }
}
