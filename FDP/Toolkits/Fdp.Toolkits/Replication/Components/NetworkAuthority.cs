using System;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// Defines ownership and authority for a networked entity.
    /// Used to determine if the local node should simulate or replicate this entity.
    /// </summary>
    [ComponentId(GlobalComponentIds.NetworkAuthority)]
    // CE-277(e): network-assigned ownership, re-established by the spawn/ownership systems on
    // load; restoring stale authority from a file would contradict the live cluster topology.
    // The PrimaryOwnerId axis GATES what is saved (per-entity), but the component itself is
    // never written into the scenario DOM.
    [DataPolicy(DataPolicy.NoScenario)]
    public struct NetworkAuthority
    {
        /// <summary>
        /// ID of the node that owns this entity (Authoritative simulator).
        /// </summary>
        public int PrimaryOwnerId;

        /// <summary>
        /// ID of the local node.
        /// </summary>
        public int LocalNodeId;

        /// <summary>
        /// True if the local node has authority over this entity.
        /// </summary>
        public bool HasAuthority => PrimaryOwnerId == LocalNodeId;

        public NetworkAuthority(int primaryOwnerId, int localNodeId)
        {
            PrimaryOwnerId = primaryOwnerId;
            LocalNodeId = localNodeId;
        }
    }
}