using System.Collections.Generic;
using Fdp.Kernel;

namespace FDP.Toolkit.Replication.Components
{
    [ComponentId(GlobalComponentIds.ChildMap)]
    public class ChildMap
    {
        public Dictionary<int, Entity> InstanceToEntity { get; } = new Dictionary<int, Entity>();
    }
}
