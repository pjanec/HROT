using System;
using Fdp.Kernel;

namespace FDP.Toolkit.Replication.Components
{
    /// <summary>
    /// Defines ownership and authority for a networked entity.
    /// Used to determine if the local node should simulate or replicate this entity.
    /// </summary>
    [ComponentId(GlobalComponentIds.NetworkAuthority)]
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