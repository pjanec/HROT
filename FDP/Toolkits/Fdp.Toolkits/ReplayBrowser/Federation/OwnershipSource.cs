using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;

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
    /// <para>⭐ <b>S2 (I8) — "owned" is read from the RECORDED ownership components</b> when the entity has them (a <see cref="NetworkAuthority"/>):
    /// a node owns a component it holds when ITS OWN record names it — the owner of the component's descriptor (<c>DescriptorOwnership.Map[(d,0)]</c>,
    /// <c>d</c> from the ONE component → descriptor map, <see cref="DescriptorOwnershipMap.GetDescriptorsForComponentId"/> — the lookup
    /// <c>AuthorityExtensions.HasAuthority</c> uses to decide who sends), else the entity's primary owner (<see cref="NetworkAuthority.PrimaryOwnerId"/>, the
    /// same fallback step 3 of <c>HasAuthority</c>). The cold mask is the CROSS-CHECK: a component the two disagree on is counted in
    /// <see cref="MaskDisagreements"/> and the components win. An entity with no <see cref="NetworkAuthority"/> (local, older recordings) — and a component
    /// whose record names no owner (≤ 0) — keeps S1's mask rule. ⚠ The map the host passes decides how many descriptors are known: a descriptor it does not
    /// map falls to the primary owner (the creator keeps what no group moved).</para>
    /// </summary>
    public class OwnershipSource
    {
        private readonly DescriptorOwnershipMap? _descriptors;

        /// <param name="descriptors">The component → descriptor map (⭐ the replication's own class — no second mapping); null ⇒ per-descriptor owners are unknown and only the primary owner is read from the components.</param>
        public OwnershipSource(DescriptorOwnershipMap? descriptors = null) => _descriptors = descriptors;

        /// <summary>Components, summed over every <see cref="Claim"/> since <see cref="BeginPass"/>, on which the recorded ownership components and the cold authority mask DISAGREED (the components were used).</summary>
        public int MaskDisagreements { get; private set; }

        /// <summary>Components the recorded ownership components decided (rather than the mask) since <see cref="BeginPass"/>.</summary>
        public int ComponentDecided { get; private set; }

        /// <summary>Starts a merge pass: zeroes the counters.</summary>
        public void BeginPass() { MaskDisagreements = 0; ComponentDecided = 0; }

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
                var owned = OwnedBy(manager.Contexts[n.NodeId].SandboxRepo, n.Entity, n.NodeId);
                owned.BitwiseAndNot(claimed);
                claimed.BitwiseOr(owned);
                extracts.Add(owned);
            }
        }

        /// <summary>
        /// The components <paramref name="entity"/> holds AND node <paramref name="nodeId"/> owns in <paramref name="repo"/>: from the recorded ownership
        /// components when the entity has a <see cref="NetworkAuthority"/> (S2), else presence AND the cold authority mask (S1).
        /// </summary>
        protected virtual BitMask512 OwnedBy(EntityRepository repo, Entity entity, int nodeId)
        {
            var presence = repo.GetComponentMask(entity.Index);                     // struct copy
            var cold = presence;
            cold.BitwiseAnd(repo.GetMetadata(entity.Index).AuthorityMask);          // the S1 answer (and the cross-check)

            int authId = ComponentTypeRegistry.GetId(typeof(NetworkAuthority));
            if (authId < 0 || !presence.IsSet(authId)) return cold;                 // no recorded ownership ⇒ today's rule

            int primary = repo.GetComponentRO<NetworkAuthority>(entity).PrimaryOwnerId;
            DescriptorOwnership? record = null;
            int recId = ComponentTypeRegistry.GetId(typeof(DescriptorOwnership));
            if (recId >= 0 && presence.IsSet(recId)) record = repo.GetManagedComponentRO<DescriptorOwnership>(entity);

            var owned = new BitMask512();
            for (int w = 0; w < 8; w++)
            {
                ulong bits = presence.Word(w);
                while (bits != 0)
                {
                    int id = w * 64 + BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;

                    int owner = primary;                                            // HasAuthority step 3
                    if (record != null && _descriptors != null)                     // step 2: the descriptor's own entry
                    {
                        var ds = _descriptors.GetDescriptorsForComponentId(id);
                        for (int i = 0; i < ds.Length; i++)
                            if (record.TryGetOwner(OwnershipExtensions.PackKey(ds[i], 0), out int specific)) { owner = specific; break; }
                    }

                    if (owner <= 0) { if (cold.IsSet(id)) owned.SetBit(id); continue; }   // the record names no owner ⇒ the mask answers
                    ComponentDecided++;
                    if (owner == nodeId) owned.SetBit(id);
                }
            }

            var onlyRecord = owned; onlyRecord.BitwiseAndNot(cold);
            var onlyMask = cold; onlyMask.BitwiseAndNot(owned);
            MaskDisagreements += CountBits(onlyRecord) + CountBits(onlyMask);
            return owned;
        }

        private static int CountBits(in BitMask512 m)
        {
            int n = 0;
            for (int w = 0; w < 8; w++) n += BitOperations.PopCount(m.Word(w));
            return n;
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
