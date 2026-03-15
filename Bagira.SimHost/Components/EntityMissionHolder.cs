using Bagira.BDC.SSTD;
using Bagira.Map.Definitions;
using Fdp.Kernel;

namespace Bagira.SimHost.Components
{
    /// <summary>
    /// Managed wrapper carrying the latest <see cref="EntityMission"/> payload.
    /// </summary>
    [ComponentId(BagiraComponentIds.EntityMissionHolder)]
    public class EntityMissionHolder
    {
        public EntityMission Mission { get; set; } = new EntityMission();
    }
}
