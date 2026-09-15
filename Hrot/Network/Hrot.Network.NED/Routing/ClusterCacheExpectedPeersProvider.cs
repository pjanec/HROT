using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Abstractions;
using Hrot.Map.Common;

namespace Hrot.Network.Routing
{
    /// <summary>
    /// Role-filtered <see cref="IExpectedPeersProvider"/> (CE-283 piece C, production-correct): a creator
    /// waits only on present peers whose role INITIALISES one of the entity's <c>[RequiresPeerInit]</c>
    /// components. Replaces slice A's proof-only "all present peers except local".
    ///
    /// <para>The derivation (DESIGN_Cross_Node_Construction_Barrier.md §3b): the entity's live component
    /// mask ∩ <see cref="ComponentAttributeSets.RequiresPeerInit"/> = the components that need peer init;
    /// a present node's role must have <see cref="HrotRoleComponentSets.Initialises"/> intersecting that set
    /// for the node to be waited on. The role comes from <c>NodeCapability.Role</c> (CE-282, the NED cluster
    /// cache), so a Map2D display peer that initialises nothing for a ground unit is correctly excluded.</para>
    /// </summary>
    public sealed class ClusterCacheExpectedPeersProvider : IExpectedPeersProvider
    {
        private readonly IClusterStateCache _cache;

        // The [RequiresPeerInit] component ids as a mask, resolved once (attribute scan is process-wide).
        private static readonly BitMask512 _requiresPeerInitMask = BuildRequiresPeerInitMask();

        public ClusterCacheExpectedPeersProvider(IClusterStateCache cache)
        {
            _cache = cache;
        }

        public IReadOnlyList<int> GetExpectedPeers(in BitMask512 entityComponentMask, int localNodeId)
        {
            // What of THIS entity needs peer init: its components ∩ the [RequiresPeerInit] set.
            var requiredInit = entityComponentMask;
            requiredInit.BitwiseAnd(in _requiresPeerInitMask);

            var peers = new List<int>();
            if (requiredInit.IsEmpty())
                return peers; // nothing needs peer init → no cross-node wait

            foreach (var node in _cache.AllNodes())
            {
                if (node.NodeId == localNodeId)
                    continue;
                if (RoleInitialisesAny(node.Role, in requiredInit))
                    peers.Add(node.NodeId);
            }
            return peers;
        }

        // True if any role this node declares initialises at least one of the required-init components.
        private static bool RoleInitialisesAny(NodeRole nodeRole, in BitMask512 requiredInit)
        {
            foreach (var kv in HrotRoleComponentSets.Initialises)
            {
                if ((nodeRole & kv.Key) == 0)
                    continue; // the node does not declare this role
                if (BitMask512.HasAny(kv.Value, in requiredInit))
                    return true;
            }
            return false;
        }

        private static BitMask512 BuildRequiresPeerInitMask()
        {
            var mask = default(BitMask512);
            foreach (int id in ComponentAttributeSets.RequiresPeerInit)
                mask.SetBit(id);
            return mask;
        }
    }
}
