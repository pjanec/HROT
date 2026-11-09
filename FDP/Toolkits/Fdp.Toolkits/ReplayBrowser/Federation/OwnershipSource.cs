using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    /// <summary>One node's copy of a networked entity: the node and its handle in THAT node's sandbox.</summary>
    public readonly record struct NodeEntity(int NodeId, Entity Entity);

    /// <summary>
    /// ⭐ G-7 / CE-4354 (docs/UX/DESIGN_Review_Perspective.md §6.4 I8, §6.12) — WHO OWNS each component of a networked entity at this frame, as its own
    /// piece so the rule lives in one place. <b>S1: today's rule</b> (<c>TransientMasterBuilder</c> step 5, frankenstein §7.3): a node owns a component when
    /// the node HOLDS it AND its cold <c>AuthorityMask</c> carries the bit, and a component is taken by the FIRST owner in the order <i>primary owner
    /// (<c>NetworkAuthority.PrimaryOwnerId</c>) first, then ascending node id</i>. S2 swaps the SOURCE of "owned" (the recorded ownership components, the
    /// mask a cross-check) by overriding <see cref="OwnedBy"/> — the claim order and the merge stay as they are.
    /// </summary>
    public class OwnershipSource
    {
        /// <summary>
        /// Orders <paramref name="nodes"/> (primary owner first, then ascending node id — IN PLACE) and writes into <paramref name="extracts"/>, one mask per entry
        /// in that order, the components that node SUPPLIES: owned and not already taken by an earlier node.
        /// </summary>
        public virtual void Claim(FederatedReplayManager manager, List<NodeEntity> nodes, List<BitMask512> extracts)
        {
            extracts.Clear();
            int primary = PrimaryOwner(manager, nodes);
            nodes.Sort((a, b) =>
            {
                bool aIsPrimary = a.NodeId == primary, bIsPrimary = b.NodeId == primary;
                if (aIsPrimary && !bIsPrimary) return -1;
                if (bIsPrimary && !aIsPrimary) return 1;
                return a.NodeId.CompareTo(b.NodeId);
            });

            var claimed = new BitMask512();
            foreach (var n in nodes)
            {
                var owned = OwnedBy(manager.Contexts[n.NodeId].SandboxRepo, n.Entity);
                owned.BitwiseAndNot(claimed);
                claimed.BitwiseOr(owned);
                extracts.Add(owned);
            }
        }

        /// <summary>The components <paramref name="entity"/> holds AND owns in <paramref name="repo"/> (today: presence AND the cold authority mask).</summary>
        protected virtual BitMask512 OwnedBy(EntityRepository repo, Entity entity)
        {
            var presence = repo.GetComponentMask(entity.Index);                     // struct copy
            var authority = repo.GetMetadata(entity.Index).AuthorityMask;           // struct copy
            presence.BitwiseAnd(authority);
            return presence;
        }

        /// <summary>The primary owner named by the first node that holds <see cref="NetworkAuthority"/>, or -1.</summary>
        protected static int PrimaryOwner(FederatedReplayManager manager, List<NodeEntity> nodes)
        {
            int typeId = ComponentTypeRegistry.GetId(typeof(NetworkAuthority));
            if (typeId < 0) return -1;
            foreach (var n in nodes)
            {
                var repo = manager.Contexts[n.NodeId].SandboxRepo;
                if (!repo.GetComponentMask(n.Entity.Index).IsSet(typeId)) continue;
                return repo.GetComponent<NetworkAuthority>(n.Entity).PrimaryOwnerId;
            }
            return -1;
        }
    }
}
