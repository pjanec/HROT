using System;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Messages;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;

namespace Hrot.Network.Systems
{
    /// <summary>
    /// Reactive ECS system that executes the deferred authority handover on the Muscle (SimHost) node.
    ///
    /// <para>
    /// Runs during <see cref="SystemPhase.BeforeSync"/> and queries entities that satisfy BOTH:
    /// <list type="bullet">
    ///   <item>Have a <see cref="PendingAuthorityGrants"/> managed component (routing intent
    ///     cached by <c>DeferredTakeOwnershipIngressTranslator</c>).</item>
    ///   <item>Are in the <see cref="EntityLifecycle.Constructing"/> state (transition triggered
    ///     by <c>GhostPromotionSystem</c> once all mandatory descriptors have physically arrived).</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// When such an entity is found, the system:
    /// <list type="number">
    ///   <item>Looks up each granted descriptor's ECS component IDs through the injected
    ///     <see cref="DescriptorOwnershipMap"/> (populated from
    ///     <c>IDescriptorTranslator.TargetComponentIds</c>) and calls
    ///     <c>SetAuthority(entity, exactComponentId, true)</c> — no try/catch, no mismatch.</item>
    ///   <item>Populates the <see cref="DescriptorOwnership"/> managed dictionary.</item>
    ///   <item>Strips the transient <see cref="PendingAuthorityGrants"/> component.</item>
    ///   <item>Publishes an <see cref="OwnershipUpdate"/> event for each descriptor so the
    ///     creator node's <c>OwnershipIngressSystem</c> drops its own bits (symmetrical yield).</item>
    /// </list>
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.BeforeSync)]
    [UpdateAfter(typeof(GhostPromotionSystem))]
    public sealed class DeferredTakeoverSystem : IEcsModuleSystem
    {
        private readonly NetworkEntityMap      _entityMap;
        private readonly int                   _localNodeId;
        private readonly DescriptorOwnershipMap _ownershipMap;

        private EntityQuery? _readyQuery;   // Constructing lifecycle

        public DeferredTakeoverSystem(
            NetworkEntityMap       entityMap,
            int                    localNodeId,
            DescriptorOwnershipMap ownershipMap,
            ITkbDatabase?          tkbDb = null) // tkbDb kept in signature for backward compatibility; unused
        {
            _entityMap    = entityMap    ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId  = localNodeId;
            _ownershipMap = ownershipMap ?? throw new ArgumentNullException(nameof(ownershipMap));
        }

        public void Execute(ISimulationView view, float dt)
        {
            var repo = view as EntityRepository;
            if (repo == null) return;

            EnsureQuery(repo);
            var cmdBuffer = view.GetCommandBuffer();

            // ── Constructing path ──────────────────────────────────────────────────────────────
            // Entity must be in Constructing state (promoted by GhostPromotionSystem after the
            // initial WorldPos descriptor arrives from Brain) and have PendingAuthorityGrants.
            // The Ghost bypass that used to apply TKB templates prematurely has been removed:
            // the correct flow is that Brain publishes the initial WorldPos before delegating
            // authority, GhostPromotionSystem promotes the ghost, and only then we claim here.
            foreach (var entity in _readyQuery!)
            {
                if (!repo.IsAlive(entity)) continue;
                if (!repo.HasManagedComponent<PendingAuthorityGrants>(entity)) continue;

                var pending = repo.GetComponent<PendingAuthorityGrants>(entity);
                ExecuteTakeover(repo, entity, pending, cmdBuffer);
            }
        }

        // ── Private ──────────────────────────────────────────────────────────────

        private void ExecuteTakeover(
            EntityRepository       repo,
            Entity                 entity,
            PendingAuthorityGrants pending,
            IEntityCommandBuffer   cmdBuffer)
        {
            // 1. Populate / update the DescriptorOwnership managed dictionary.
            DescriptorOwnership ownership = repo.HasManagedComponent<DescriptorOwnership>(entity)
                ? repo.GetComponent<DescriptorOwnership>(entity)
                : new DescriptorOwnership();

            bool ownershipDirty = false;

            // 2. For each descriptor granted to this local node…
            foreach (var kvp in pending.GrantsByDescriptor)
            {
                long descriptorTypeId = kvp.Key;
                int  ownerNodeId      = kvp.Value;
                if (ownerNodeId != _localNodeId) continue;

                long packedKey = OwnershipExtensions.PackKey(descriptorTypeId, 0);
                ownership.SetOwner(packedKey, _localNodeId);
                ownershipDirty = true;

                // 2a. Claim authority on each ECS component the translator maps to this descriptor.
                //     DescriptorOwnershipMap was built from IDescriptorTranslator.TargetComponentIds
                //     — no try/catch, no ordinal↔component-ID guessing.
                foreach (int componentId in _ownershipMap.GetComponentIdsForDescriptor(descriptorTypeId))
                {
                    if (repo.HasComponentByTypeId(entity, componentId))
                        repo.SetAuthority(entity, componentId, true);
                }

                // 2b. Publish OwnershipUpdate so the creator drops its authority bits.
                if (repo.HasComponent<NetworkIdentity>(entity))
                {
                    var netId = repo.GetComponent<NetworkIdentity>(entity);
                    repo.Bus.Publish(new OwnershipUpdate
                    {
                        NetworkId      = new NetworkIdentity(netId.Value),
                        PackedKey      = packedKey,
                        NewOwnerNodeId = _localNodeId,
                    });
                }
            }

            if (ownershipDirty)
                repo.SetManagedComponent(entity, ownership);

            // 3. Strip the transient component — intent has been executed.
            cmdBuffer.RemoveManagedComponent<PendingAuthorityGrants>(entity);

            if (repo.HasComponent<NetworkIdentity>(entity))
            {
                long netId = repo.GetComponent<NetworkIdentity>(entity).Value;
                FdpLog<DeferredTakeoverSystem>.Info(
                    "[Muscle] DeferredTakeover executed: EntityNetId={0} GrantCount={1} LocalNode={2}",
                    netId, pending.GrantsByDescriptor.Count, _localNodeId);
            }
        }

        private void EnsureQuery(EntityRepository repo)
        {
            if (_readyQuery != null) return;
            _readyQuery = repo.Query()
                .WithLifecycle(EntityLifecycle.Constructing)
                .Build();
        }
    }
}
