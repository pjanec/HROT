using Bagira.BDC.SSTD;
using Fdp.Kernel;

namespace Bagira.SimHost.Components
{
    /// <summary>
    /// Managed wrapper carrying the latest <see cref="EntityMission"/> payload.
    /// </summary>
    [ComponentId(GlobalComponentIds.EntityMissionHolder)]
    public class EntityMissionHolder
    {
        public EntityMission Mission { get; set; } = new EntityMission();
    }
}
