using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Components
{
    [ComponentId(GlobalComponentIds.ChildMap)]
    public class ChildMap
    {
        // ⭐ CE-3305 — `private set`, not get-only: a get-only member was LOST from every recording (the recorder writes settable members).
        public Dictionary<int, Entity> InstanceToEntity { get; private set; } = new Dictionary<int, Entity>();
    }
}
