using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Messages;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Network.Systems
{
    /// <summary>
    /// ⭐⭐⭐ CE-276 — the INITIATION (push) side of an entity-ownership transfer: this node HANDS an entity
    /// (or a chosen subset of its descriptors) to another node. The mirror of <c>DeferredTakeoverSystem</c>
    /// (the pull/claim side) on the same wire.
    ///
    /// <para>Consumes <see cref="TransferEntityOwnershipRequest"/> from the bus and, per descriptor in scope
    /// that THIS node currently owns:
    /// <list type="number">
    ///   <item>writes the new owner into the <see cref="DescriptorOwnership"/> managed dictionary;</item>
    ///   <item>clears this node's <c>AuthorityMask</c> for the descriptor's components (so its egress
    ///     translator stops publishing — the spec's "stop writing, do NOT dispose");</item>
    ///   <item>for the <c>EntityMaster</c> descriptor (identified network-agnostically via
    ///     <see cref="DescriptorOwnershipMap.PrimaryOwnerDescriptorOrdinal"/>) mirrors
    ///     <c>NetworkAuthority.PrimaryOwnerId</c> so the scenario save gate follows; and</item>
    ///   <item>publishes an <see cref="OwnershipUpdate"/> (OriginNodeId = this node) which
    ///     <c>OwnershipUpdateTranslator.ScanAndPublish</c> forwards to DDS. The new owner's
    ///     <c>OwnershipIngressSystem</c> applies it and its <c>EntityMasterEgressTranslator</c> re-publishes
    ///     <c>EntityMaster</c> to confirm.</item>
    /// </list></para>
    ///
    /// <para>The local effect (steps 1-3) is applied explicitly for determinism; the same node's
    /// <c>OwnershipIngressSystem</c> may re-apply the published event, which is idempotent. On a host with no
    /// NED transport this system is never registered, so a request is a no-op (correct — a single-node host
    /// owns everything). 📄 docs/DESIGN_Entity_Ownership_Transfer.md §2.2.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class OwnershipTransferInitiationSystem : IEcsModuleSystem
    {
        private readonly NetworkEntityMap       _entityMap;
        private readonly int                    _localNodeId;
        private readonly DescriptorOwnershipMap _descriptorMap;

        private readonly List<long> _scratch = new();

        public OwnershipTransferInitiationSystem(
            NetworkEntityMap       entityMap,
            int                    localNodeId,
            DescriptorOwnershipMap descriptorMap)
        {
            _entityMap     = entityMap     ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId   = localNodeId;
            _descriptorMap = descriptorMap ?? throw new ArgumentNullException(nameof(descriptorMap));
        }

        public void Execute(ISimulationView view, float dt)
        {
            if (view is not EntityRepository repo) return;

            foreach (var req in view.ReadManagedEvents<TransferEntityOwnershipRequest>())
            {
                if (req.NewOwnerNodeId == _localNodeId) continue;          // already ours — nothing to hand off
                if (!_entityMap.TryGetEntity(req.NetworkId, out Entity entity)) continue;
                if (!repo.IsAlive(entity)) continue;

                ResolveOwnedDescriptors(view, entity, req, _scratch);
                if (_scratch.Count == 0)
                {
                    FdpLog<OwnershipTransferInitiationSystem>.Info(
                        "[OwnershipTransferInitiationSystem] node {0}: transfer of entity {1} to {2} ({3}) moved 0 descriptors (none owned here).",
                        _localNodeId, req.NetworkId, req.NewOwnerNodeId, req.Scope);
                    continue;
                }

                DescriptorOwnership ownership = repo.HasManagedComponent<DescriptorOwnership>(entity)
                    ? repo.GetComponent<DescriptorOwnership>(entity)
                    : new DescriptorOwnership();

                long? master = _descriptorMap.PrimaryOwnerDescriptorOrdinal;
                bool masterMoved = false;

                foreach (long ordinal in _scratch)
                {
                    long packedKey = OwnershipExtensions.PackKey(ordinal, 0);

                    // 1. local ownership dictionary → new owner
                    ownership.SetOwner(packedKey, req.NewOwnerNodeId);

                    // 2. drop our AuthorityMask for the descriptor's components (stop publishing, no dispose)
                    foreach (int componentId in _descriptorMap.GetComponentIdsForDescriptor(ordinal))
                        if (repo.HasComponentByTypeId(entity, componentId))
                            repo.SetAuthority(entity, componentId, false);

                    // 3. save-ownership mirror for the master descriptor
                    if (master.HasValue && master.Value == ordinal)
                        masterMoved = true;

                    // 4. wire: OwnershipUpdate (OriginNodeId = us) → OwnershipUpdateTranslator egress → DDS
                    repo.Bus.Publish(new OwnershipUpdate
                    {
                        NetworkId      = new NetworkIdentity { Value = req.NetworkId },
                        PackedKey      = packedKey,
                        NewOwnerNodeId = req.NewOwnerNodeId,
                        OriginNodeId   = _localNodeId,
                    });
                }

                repo.SetManagedComponent(entity, ownership);

                if (masterMoved && repo.HasComponent<NetworkAuthority>(entity))
                {
                    int existingLocal = repo.GetComponentRO<NetworkAuthority>(entity).LocalNodeId;
                    repo.SetComponent(entity, new NetworkAuthority(req.NewOwnerNodeId, existingLocal));
                }

                FdpLog<OwnershipTransferInitiationSystem>.Info(
                    "[OwnershipTransferInitiationSystem] node {0}: handed entity {1} to {2} — {3}.",
                    _localNodeId, req.NetworkId, req.NewOwnerNodeId,
                    $"{_scratch.Count} descriptor(s), master={masterMoved}");
            }
        }

        /// <summary>Fills <paramref name="into"/> with the descriptor ordinals this node currently owns for
        /// <paramref name="entity"/> that fall within the request's scope.</summary>
        private void ResolveOwnedDescriptors(
            ISimulationView view, Entity entity, in TransferEntityOwnershipRequest req, List<long> into)
        {
            into.Clear();
            long? master = _descriptorMap.PrimaryOwnerDescriptorOrdinal;

            switch (req.Scope)
            {
                case TransferScope.MasterOnly:
                    if (master.HasValue && Owns(view, entity, master.Value))
                        into.Add(master.Value);
                    break;

                case TransferScope.AllOwnedByThisNode:
                    foreach (long ordinal in _descriptorMap.RegisteredDescriptors)
                        if (Owns(view, entity, ordinal))
                            into.Add(ordinal);
                    break;

                case TransferScope.SpecificDescriptors:
                    if (req.DescriptorTypeIds != null)
                        foreach (long ordinal in req.DescriptorTypeIds)
                            if (Owns(view, entity, ordinal) && !into.Contains(ordinal))
                                into.Add(ordinal);
                    break;
            }
        }

        private static bool Owns(ISimulationView view, Entity entity, long descriptorOrdinal)
            => view.HasAuthority(entity, OwnershipExtensions.PackKey(descriptorOrdinal, 0));
    }
}
