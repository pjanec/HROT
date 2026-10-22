using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Components
{
    [ComponentId(GlobalComponentIds.ChildMap)]
    [DataPolicy(DataPolicy.NoPreview)]   // ⭐ CE-3299 D7 — declared: world state, a mutable class: recorded and saved, kept out of the background snapshot (the convention it got by default)
    public class ChildMap
    {
        // ⭐ CE-3305 — `private set`, not get-only: a get-only member was LOST from every recording (the recorder writes settable members).
        public Dictionary<int, Entity> InstanceToEntity { get; private set; } = new Dictionary<int, Entity>();
    }
}
