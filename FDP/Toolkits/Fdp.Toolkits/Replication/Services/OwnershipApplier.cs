using System;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;

namespace Fdp.Toolkit.Replication.Services
{
    /// <summary>
    /// ⭐⭐ <b>The ONE way a node applies "descriptor key K of entity E is now owned by node N".</b>
    /// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.1, §5.6 S5.
    ///
    /// <para>Extracted from <see cref="Systems.OwnershipIngressSystem"/>'s loop so every path that moves ownership
    /// shares it: the ingress of an <c>OwnershipUpdate</c>, the transfer initiation (CE-276), and the partial-owner
    /// reclaim of S7 (R-167, a direct call, no network message). It writes the record and the claim together:</para>
    /// <list type="number">
    ///   <item>the record: <c>DescriptorOwnership.Map[K] = N</c>;</item>
    ///   <item>the claim: the descriptor's components present on E are claimed iff N is this node;</item>
    ///   <item>for the master descriptor, the primary owner (<c>NetworkAuthority.PrimaryOwnerId</c>) follows, and
    ///     ⭐ every OTHER descriptor that has no record entry is first pinned to the OLD primary owner. Without an
    ///     entry a descriptor's owner is read from the primary owner, so moving only the master would silently
    ///     move all of them in the record while their claims stayed put. Pinning keeps them where they were, as
    ///     <c>DESIGN_Entity_Ownership_Transfer.md</c> §3 requires for <c>MasterOnly</c> ("leave every other
    ///     descriptor where it is"), and every node computes the same pins from the same update;</item>
    ///   <item>when N is this node, a <see cref="Messages.DescriptorAuthorityChanged"/> event.</item>
    /// </list>
    /// <para>Idempotent: applying the same (E, K, N) again changes nothing.</para>
    /// </summary>
    public sealed class OwnershipApplier
    {
        private readonly int                     _localNodeId;
        private readonly DescriptorOwnershipMap? _descriptorMap;

        public OwnershipApplier(int localNodeId, DescriptorOwnershipMap? descriptorMap)
        {
            _localNodeId   = localNodeId;
            _descriptorMap = descriptorMap;
        }

        /// <summary>Applies "<paramref name="packedKey"/> of <paramref name="entity"/> is owned by
        /// <paramref name="newOwnerNodeId"/>". See the class remarks for what it writes.</summary>
        public void Apply(EntityRepository repo, Entity entity, long packedKey, int newOwnerNodeId)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            if (!repo.IsAlive(entity)) return;

            DescriptorOwnership ownership;
            if (repo.HasManagedComponent<DescriptorOwnership>(entity))
                ownership = repo.GetComponent<DescriptorOwnership>(entity);
            else
            {
                ownership = new DescriptorOwnership();
                repo.SetManagedComponent(entity, ownership);
            }

            var (typeId, _) = OwnershipExtensions.UnpackKey(packedKey);
            bool isMaster = _descriptorMap?.PrimaryOwnerDescriptorOrdinal is long masterOrdinal && masterOrdinal == typeId;

            // ── 3a. A master move first pins every un-recorded descriptor to the OLD primary owner ──────
            if (isMaster && _descriptorMap != null && repo.HasComponent<NetworkAuthority>(entity))
            {
                int oldPrimary = repo.GetComponentRO<NetworkAuthority>(entity).PrimaryOwnerId;
                if (oldPrimary != newOwnerNodeId)
                {
                    foreach (long ordinal in _descriptorMap.RegisteredDescriptors)
                    {
                        if (ordinal == typeId) continue;
                        long key = OwnershipExtensions.PackKey(ordinal, 0);
                        if (!ownership.Map.ContainsKey(key))
                            ownership.Map[key] = oldPrimary;
                    }
                }
            }

            // ── 1. The record ──────────────────────────────────────────────────────────────────────────
            ownership.Map[packedKey] = newOwnerNodeId;

            // ── 2. The claim, on the exact component ids the descriptor maps to (translator targets + group links).
            //    No map ⇒ the claim is not touched (safe default).
            bool isAuth = _localNodeId != 0 && newOwnerNodeId == _localNodeId;
            if (_descriptorMap != null)
            {
                foreach (int componentId in _descriptorMap.GetComponentIdsForDescriptor(typeId))
                {
                    if (repo.HasComponentByTypeId(entity, componentId))
                        repo.SetAuthority(entity, componentId, isAuth);
                }
            }

            // ── 3b. Primary-owner mirror (BDC compliance, OQ12 / CE-275 ④). The master descriptor DEFINES entity /
            //    save ownership; it is named network-agnostically through PrimaryOwnerDescriptorOrdinal. Written on
            //    every node (gaining and losing alike), each deriving HasAuthority from PrimaryOwnerId == LocalNodeId.
            //    📄 docs/DESIGN_Distributed_Scenario_Persistence.md §6c.
            if (isMaster)
            {
                if (repo.HasComponent<NetworkAuthority>(entity))
                {
                    int existingLocal = repo.GetComponentRO<NetworkAuthority>(entity).LocalNodeId;
                    repo.SetComponent(entity, new NetworkAuthority(newOwnerNodeId, existingLocal));
                }
                else
                {
                    repo.AddComponent(entity, new NetworkAuthority(newOwnerNodeId, _localNodeId));
                }
            }

            if (isAuth)
            {
                repo.Bus.Publish(new Messages.DescriptorAuthorityChanged
                {
                    Entity          = entity,
                    PackedKey       = packedKey,
                    IsAuthoritative = true
                });
            }
        }
    }
}
