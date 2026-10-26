using System;
using Fdp.Core;

namespace Fdp.Toolkit.Lifecycle.Components
{
    /// <summary>
    /// ⭐ AQ107 (CE-3323) — the id of the creation request that made this entity (<c>EntityCreationRequest.RequestId</c>), on every node
    /// that holds it: stamped by <c>NetworkSpawningSystem</c> on the node that serviced the request, and by the <c>EntityMaster</c>
    /// ingress on the others (the id rides the wire, APPENDED). The lifecycle module copies it into
    /// <see cref="Events.EntityConstructionCompleted"/> / <see cref="Events.EntityDestructionCompleted"/>, so a requester on ANY node can
    /// tell its entity from the rest. Absent when nothing asked for the entity by request (a scenario load, a direct spawn).
    /// 📄 docs/DESIGN_Entity_Creation_Unification.md §3.4e.
    /// </summary>
    [ComponentId(GlobalComponentIds.CreationRequestId)]
    [DataPolicy(DataPolicy.NoScenario)]   // a fact about how this run made it, not world state a scenario restores
    public struct CreationRequestId
    {
        public Guid Value;
    }
}
