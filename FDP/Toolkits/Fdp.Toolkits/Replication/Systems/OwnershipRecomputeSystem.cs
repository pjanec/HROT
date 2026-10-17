using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Messages;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Toolkit.Replication.Systems
{
    /// <summary>
    /// ⭐⭐ <b>R-159 — the record follows the claim.</b> 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.1,
    /// §5.6 S5; <c>docs/blueprints/Architect_Question_79_One_Ownership_Truth.md</c> §0.7 ④, §9b.
    ///
    /// <para>🔒 User: <i>"network record must be recomputed on every ownership transfer, independently on if it already
    /// has an entry"</i> · <i>"recompute on promotion AND any other ownership changes"</i>. So for every entity named by
    /// an <see cref="OwnershipUpdate"/> (any origin: a received one, this node's takeover, its transfer) or by a
    /// <see cref="ConstructionOrder"/> (creation and promotion), and for every descriptor this node maps whose
    /// components are on the entity, the record is made to agree with the claim:</para>
    /// <list type="bullet">
    ///   <item>all of the descriptor's present components claimed, record not "mine" ⇒ <c>Map[d] = this node</c>;</item>
    ///   <item>record "mine", some present component unclaimed ⇒ that component is CLAIMED. ⚠ Measured
    ///     (<c>EqsTranslatorTests.T8</c> red): <c>EntityRepository.AddComponent</c> sets no claim, so a component the
    ///     owner adds after birth starts unclaimed. Writing "not me" for it (the first S5 rule) stopped the owner
    ///     publishing a descriptor it owns. Since S5 every path that clears a claim also writes the record (the shared
    ///     applier) or marks the handover (<see cref="OutgoingGrantsPending"/>), so "unclaimed but recorded mine" can
    ///     only be a late component.</item>
    /// </list>
    /// <para>It writes only where the two disagree, so a record that already names the right remote owner keeps that
    /// owner. It never sends anything: the record is this node's local view.</para>
    ///
    /// <para>⭐ <b>Left alone, on purpose:</b></para>
    /// <list type="bullet">
    ///   <item>the master descriptor — its owner is the primary owner, which the protocols write;</item>
    ///   <item>descriptors in <see cref="OutgoingGrantsPending"/> — the creator yielded their claim but must keep
    ///     publishing until the grantee confirms (Q79 F7, P6). The confirming <see cref="OwnershipUpdate"/> removes
    ///     them here;</item>
    ///   <item>a descriptor whose present components are partly claimed — one descriptor split across nodes has no
    ///     single owner to record; counted in <see cref="SplitDescriptorsSkipped"/>;</item>
    ///   <item>entities with no <see cref="NetworkAuthority"/> (a node with no network owns everything).</item>
    /// </list>
    ///
    /// <para>⭐⭐ <b>Parts (S6) go the other way: their CLAIM follows their RECORD, every frame.</b> A part
    /// (<see cref="PartMetadata"/>) has no record of its own — the root's record answers for it, per instance
    /// <c>(d, i)</c>, falling back to the descriptor type <c>(d, 0)</c> and then the root's primary owner — and nothing
    /// claims it at birth: parts are created later by the running logic (an EQS sensor by the Brain, its carrier by the
    /// Muscle), never by the creator's spawn. So each frame every part's components are claimed iff the record says
    /// this node owns that part's instance of their descriptor. That gives a new part the claim of its root's group at
    /// once (Q79 §0.10 ②), makes a per-instance <see cref="OwnershipUpdate"/> move only that part (③), and splits one
    /// EQS part naturally: its config claimed on the Brain, its result on the Perception node.
    /// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.6 S6.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    [UpdateAfter(typeof(OwnershipIngressSystem))]
    public sealed class OwnershipRecomputeSystem : IEcsModuleSystem
    {

        private readonly NetworkEntityMap       _entityMap;
        private readonly int                    _localNodeId;
        private readonly DescriptorOwnershipMap _descriptorMap;

        private readonly HashSet<Entity> _touched = new();
        private readonly List<Entity>    _order   = new();

        // Parts pass: every non-master descriptor with its components (translator targets + group links), built once.
        private (long Ordinal, int[] Components)[]? _descriptorComponents;
        // ⭐ CE-3244 — the same components as masks: one per descriptor, and their union. The two per-frame passes test a whole
        //   entity, then a whole descriptor, with a few 512-bit operations instead of a presence + claim lookup per component.
        //   📐 300 civilians: 600 parts × 36 descriptors + 301 roots × 36 cost 8–9 ms/frame on SimHost and CGF.
        private BitMask512[]? _descriptorMasks;
        private BitMask512 _anyDescriptorComponent;
        private EntityQuery? _parts;
        private EntityQuery? _roots;

        public OwnershipRecomputeSystem(NetworkEntityMap entityMap, int localNodeId, DescriptorOwnershipMap descriptorMap)
        {
            _entityMap     = entityMap     ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId   = localNodeId;
            _descriptorMap = descriptorMap ?? throw new ArgumentNullException(nameof(descriptorMap));
        }

        /// <summary>Diagnostics: descriptors skipped because their present components were only partly claimed.</summary>
        public int SplitDescriptorsSkipped { get; private set; }

        /// <summary>Diagnostics: components added after birth that took the record's "mine".</summary>
        public int LateComponentsClaimed { get; private set; }

        /// <summary>Diagnostics: part component claims the parts pass changed.</summary>
        public int PartClaimsChanged { get; private set; }

        public void Execute(ISimulationView view, float dt)
        {
            if (view is not EntityRepository repo) return;

            _touched.Clear();
            _order.Clear();

            // ── 1. The confirming updates end the creator's F7 window, then every named entity is recomputed ──
            foreach (var update in view.ReadEvents<OwnershipUpdate>())
            {
                if (!_entityMap.TryGetEntity(update.NetworkId.Value, out Entity entity)) continue;
                if (!repo.IsAlive(entity)) continue;

                if (repo.HasManagedComponent<OutgoingGrantsPending>(entity))
                {
                    var pending = repo.GetComponent<OutgoingGrantsPending>(entity);
                    var (typeId, _) = OwnershipExtensions.UnpackKey(update.PackedKey);
                    if (update.NewOwnerNodeId != _localNodeId)
                        pending.Descriptors.Remove(typeId);
                }
                Touch(entity);
            }

            foreach (var order in view.ReadEvents<ConstructionOrder>())
                if (repo.IsAlive(order.Entity)) Touch(order.Entity);

            foreach (var entity in _order)
                Recompute(repo, entity);

            SyncPartClaims(repo);
            ClaimLateRootComponents(repo);
        }

        /// <summary>S6 — every part's claim follows its record (see the class remarks).</summary>
        private void SyncPartClaims(EntityRepository repo)
        {
            EnsureDescriptorComponents();
            _parts ??= repo.Query().With<PartMetadata>().Build();

            foreach (var part in _parts)
            {
                ref readonly var mask = ref repo.GetComponentMask(part.Index);
                if (!BitMask512.HasAny(mask, _anyDescriptorComponent)) continue;   // CE-3244 — no descriptor component at all
                ref readonly var meta = ref repo.GetComponentRO<PartMetadata>(part);
                Entity root = meta.ParentEntity;
                if (!repo.IsAlive(root) || !repo.HasComponent<NetworkAuthority>(root)) continue;
                int instance = meta.InstanceId;

                for (int d = 0; d < _descriptorComponents!.Length; d++)
                {
                    if (!BitMask512.HasAny(mask, _descriptorMasks![d])) continue;  // CE-3244 — none of d's components present
                    var (ordinal, components) = _descriptorComponents[d];
                    bool known = false, desired = false;
                    foreach (int componentId in components)
                    {
                        if (!repo.HasComponentByTypeId(part, componentId)) continue;
                        if (!known)
                        {
                            desired = ((ISimulationView)repo).HasAuthority(part, OwnershipExtensions.PackKey(ordinal, instance));
                            known   = true;
                        }
                        if (repo.HasAuthority(part, componentId) == desired) continue;
                        repo.SetAuthority(part, componentId, desired);
                        PartClaimsChanged++;
                    }
                }
            }
        }

        /// <summary>
        /// ⭐ <c>CE-3001</c> (S8) — a component added to a ROOT after birth takes the record's "mine" in the frame it
        /// appears, not only when an ownership or construction event next touches the entity. <c>AddComponent</c> sets no
        /// claim, and the running logic adds components late (a blueprint's <c>BlueprintBlackboard256</c> on every
        /// hill-attack tank): the owner held them UNCLAIMED, so anything gated on the claim — the ingress skip
        /// (F-5), the <c>WithOwned</c> queries — treated the owner as a replica. 📄 <c>DESIGN_Ownership_Groups_And_Grants.md</c>
        /// §5.6 S8.
        /// <para>⚠ Only the "record mine ⇒ claim" half of <see cref="Recompute"/>: it never clears a claim and never
        /// writes the record, so it cannot fight a handover — and a descriptor whose handover is still pending
        /// (<see cref="OutgoingGrantsPending"/>, the creator's F7 window) is skipped exactly as there.</para>
        /// </summary>
        private void ClaimLateRootComponents(EntityRepository repo)
        {
            EnsureDescriptorComponents();
            _roots ??= repo.Query().With<NetworkAuthority>().Without<PartMetadata>().Build();

            foreach (var entity in _roots)
            {
                // ⭐ CE-3244 — present AND unclaimed, as one mask: almost every root, every frame, has nothing to claim.
                var unclaimedMask = repo.GetComponentMask(entity.Index);
                unclaimedMask.BitwiseAnd(_anyDescriptorComponent);
                unclaimedMask.BitwiseAndNot(repo.GetMetadata(entity.Index).AuthorityMask);
                if (!BitMask512.HasAny(unclaimedMask, _anyDescriptorComponent)) continue;

                OutgoingGrantsPending? pending = null;
                bool pendingRead = false;

                for (int d = 0; d < _descriptorComponents!.Length; d++)
                {
                    // Cheap first: is any component of d present and unclaimed? Only then read the record.
                    if (!BitMask512.HasAny(unclaimedMask, _descriptorMasks![d])) continue;
                    var (ordinal, components) = _descriptorComponents[d];

                    if (!pendingRead)
                    {
                        pending = repo.HasManagedComponent<OutgoingGrantsPending>(entity)
                            ? repo.GetComponent<OutgoingGrantsPending>(entity) : null;
                        pendingRead = true;
                    }
                    if (pending != null && pending.Descriptors.ContainsKey(ordinal)) continue;
                    if (!((ISimulationView)repo).HasAuthority(entity, OwnershipExtensions.PackKey(ordinal, 0))) continue;

                    foreach (int componentId in components)
                        if (repo.HasComponentByTypeId(entity, componentId) && !repo.HasAuthority(entity, componentId))
                        {
                            repo.SetAuthority(entity, componentId, true);
                            LateComponentsClaimed++;
                        }
                }
            }
        }

        /// <summary>Every non-master descriptor with its components (translator targets + group links), built once.</summary>
        private void EnsureDescriptorComponents()
        {
            if (_descriptorComponents != null) return;
            long? master = _descriptorMap.PrimaryOwnerDescriptorOrdinal;
            var list = new List<(long, int[])>();
            foreach (long ordinal in _descriptorMap.RegisteredDescriptors)
            {
                if (master.HasValue && ordinal == master.Value) continue;
                var ids = _descriptorMap.GetComponentIdsForDescriptor(ordinal).ToArray();
                if (ids.Length > 0) list.Add((ordinal, ids));
            }
            _descriptorComponents = list.ToArray();
            _descriptorMasks = new BitMask512[_descriptorComponents.Length];
            _anyDescriptorComponent = default;
            for (int d = 0; d < _descriptorComponents.Length; d++)
                foreach (int componentId in _descriptorComponents[d].Components)
                {
                    _descriptorMasks[d].SetBit(componentId);
                    _anyDescriptorComponent.SetBit(componentId);
                }
        }

        private void Touch(Entity entity)
        {
            if (_touched.Add(entity)) _order.Add(entity);
        }

        private void Recompute(EntityRepository repo, Entity entity)
        {
            if (!repo.HasComponent<NetworkAuthority>(entity)) return;
            if (repo.HasComponent<PartMetadata>(entity)) return;

            OutgoingGrantsPending? pending = repo.HasManagedComponent<OutgoingGrantsPending>(entity)
                ? repo.GetComponent<OutgoingGrantsPending>(entity)
                : null;

            long? master = _descriptorMap.PrimaryOwnerDescriptorOrdinal;
            DescriptorOwnership? ownership = null;

            foreach (long ordinal in _descriptorMap.RegisteredDescriptors)
            {
                if (master.HasValue && ordinal == master.Value) continue;
                if (pending != null && pending.Descriptors.ContainsKey(ordinal)) continue;

                int present = 0, claimed = 0;
                foreach (int componentId in _descriptorMap.GetComponentIdsForDescriptor(ordinal))
                {
                    if (!repo.HasComponentByTypeId(entity, componentId)) continue;
                    present++;
                    if (repo.HasAuthority(entity, componentId)) claimed++;
                }
                if (present == 0) continue;

                long key        = OwnershipExtensions.PackKey(ordinal, 0);
                bool recordMine = ((ISimulationView)repo).HasAuthority(entity, key);

                if (recordMine)
                {
                    // ⭐ The record says this node owns d: any present component it does not claim was added AFTER
                    //   birth (AddComponent sets no claim) — it takes the record's answer. Since S5 no path clears a claim
                    //   without writing the record or marking the handover pending, so this is never a stale record.
                    if (claimed == present) continue;
                    foreach (int componentId in _descriptorMap.GetComponentIdsForDescriptor(ordinal))
                        if (repo.HasComponentByTypeId(entity, componentId) && !repo.HasAuthority(entity, componentId))
                        {
                            repo.SetAuthority(entity, componentId, true);
                            LateComponentsClaimed++;
                        }
                    continue;
                }

                if (claimed == 0) continue;                                       // not mine, nothing claimed: agree
                if (claimed != present) { SplitDescriptorsSkipped++; continue; }  // a split has no single owner

                // Every present component claimed, record not mine ⇒ the record follows the claim.
                if (ownership == null)
                {
                    if (repo.HasManagedComponent<DescriptorOwnership>(entity))
                        ownership = repo.GetComponent<DescriptorOwnership>(entity);
                    else
                    {
                        ownership = new DescriptorOwnership();
                        repo.SetManagedComponent(entity, ownership);
                    }
                }
                ownership.Map[key] = _localNodeId;
            }

            if (pending != null && pending.Descriptors.Count == 0)
                repo.RemoveManagedComponent<OutgoingGrantsPending>(entity);
        }
    }
}
