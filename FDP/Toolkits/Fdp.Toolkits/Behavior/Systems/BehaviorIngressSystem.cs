using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Consumes <see cref="AssignBehaviorEvent"/>s and applies them to the relevant entities:
    /// <list type="number">
    ///   <item>Sets <see cref="BehaviorState.ActiveBehaviorHash"/> and <see cref="BehaviorState.BrainTier"/>.</item>
    ///   <item>Increments <see cref="BehaviorState.InstanceId"/> (deliberate wrapping via <c>unchecked</c>).
    ///         This bumps the preemption token so <see cref="ChannelArbitrationSystem"/> clears stale
    ///         channels on the next simulation tick.</item>
    ///   <item>Resets <see cref="BrainBTreeState.State"/> to <c>default</c> (execution pointer → 0).</item>
    ///   <item>Calls <see cref="BehaviorDefinition.ParseParams"/> to write blackboard parameters.</item>
    /// </list>
    ///
    /// Runs in <see cref="InputSystemGroup"/> so behavior changes are visible to all brain tick
    /// systems (which run in <see cref="SimulationSystemGroup"/>) within the same frame.
    ///
    /// <para>
    /// DEBT-035 fix: all ECS component writes (<see cref="BehaviorState"/>, <see cref="BrainBTreeState"/>)
    /// now happen AFTER <see cref="BehaviorDefinition.ParseParams"/> succeeds.  A parse failure leaves
    /// the entity entirely on its previous behavior — no partial transition.
    /// A stackalloc shadow copy of the blackboard is used so the live component is only updated
    /// when parsing succeeds, keeping the operation atomic from the ECS perspective.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public class BehaviorIngressSystem : IEcsModuleSystem
    {
        private readonly BehaviorRegistry _registry;

        public BehaviorIngressSystem(BehaviorRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(BehaviorIngressSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var events = repo.Bus.ReadManaged<AssignBehaviorEvent>();

            // Shadow buffer allocated once per OnUpdate call (outside the loop) to avoid
            // CA2014 stack-overflow risk. BrainBlackboardByteSize is a compile-time constant.
            Span<byte> shadow = stackalloc byte[BehaviorConstants.BrainBlackboardByteSize];

            foreach (var evt in events)
            {
                if (evt == null) continue;
                if (!repo.HasComponent<BehaviorState>(evt.Entity)) continue;

                // DEBT-006: use stable int ID from registry — no GetHashCode().
                if (!_registry.TryGetId(evt.BehaviorName, out int behaviorId)) continue;
                if (!_registry.TryGetDefinition(behaviorId, out var def)) continue;

                // DEBT-035 fix: attempt ParseParams BEFORE writing BehaviorState/BrainBTreeState.
                // Strategy: parse into stack memory and commit only on success, so a ParseParams
                // failure leaves the entity 100% on the old behavior.
                //
                // ⭐⭐⭐ P3-C — THE CLEAN CUT (2026-09-21). 🔴 The shadow used to be copied FROM and
                //   committed back TO a per-entity BrainBlackboard component. Both halves are gone:
                //   the parsed bytes now land in the entity's ROOT PARAMS OCCURRENCE SLOT, which is
                //   attached further down once the store is provisioned (§29.6 / §29.7 P3-C).
                //
                // ⛔ NOT a dual write. R-132's second producer, and in this codebase a temporary one
                //   becomes permanent — the user ruled CLEAN CUT for exactly that reason.
                if (def.ParseParams != null)
                {
                    // ⭐⭐ SEED THE SHADOW FROM THE CURRENT ROOT SLOT, exactly as the blackboard copy
                    //   did. It is the PREVIOUS behaviour's region — BehaviorState still names it,
                    //   this line runs before the transition is committed — which is what makes a
                    //   partial parse (an emitted parser writes only the variables the JSON mentions)
                    //   behave as it always has.
                    // ⛔ And ZERO it when there is none: the buffer is allocated ONCE outside the
                    //   loop, so a stale event's bytes would otherwise leak into this one.
                    // ⚠ The length comes from the PREVIOUS slot's own guard, never from the NEW
                    //   behaviour's extent — the two differ, and using the new one overreads the old
                    //   slot into whatever occurrence follows it.
                    fixed (byte* dst = shadow)
                    {
                        shadow.Clear();
                        if (RootParamsAccess.TryGetRootBytes(repo, evt.Entity, out byte* prev, out int prevLen)
                            && prevLen > 0)
                        {
                            int copy = Math.Min(prevLen, BehaviorConstants.BrainBlackboardByteSize);
                            Buffer.MemoryCopy(prev, dst, BehaviorConstants.BrainBlackboardByteSize, copy);
                        }
                    }

                    // Attempt parse on the shadow.
                    bool parseOk;
                    fixed (byte* dst = shadow)
                    {
                        try
                        {
                            // ⭐ G1/E7 — `host` is null: this is a ROOT behaviour, which is its
                            //   defined value (DESIGN_Parameter_Model.md §3.4). A HOSTED occurrence
                            //   will pass its host's variable access here, at E7a, without another
                            //   signature change.
                            def.ParseParams(evt.JsonParams, dst, repo, evt.Entity, host: null);
                            parseOk = true;
                        }
                        catch (Exception ex)
                        {
                            // Suppress — do NOT rethrow; a parse failure must not crash the loop.
                            _ = ex;
                            parseOk = false;
                        }
                    }

                    if (!parseOk) continue; // ParseParams failed — entity stays on old behavior entirely.
                }

                // ParseParams succeeded (or was not required). Commit behavior transition.

                // 1. Update BehaviorState.
                // Read previous behavior hash before overwriting (needed for S2-2 detach).
                int previousBehaviorId = repo.GetComponentRW<BehaviorState>(evt.Entity).ActiveBehaviorHash;
                ref var behavior = ref repo.GetComponentRW<BehaviorState>(evt.Entity);
                behavior.ActiveBehaviorHash = behaviorId;
                // Intentional unsigned wrap — InstanceId is a monotonic preemption token.
                unchecked { behavior.InstanceId++; }
                behavior.BrainTier = def.BrainTier;
                // ⛔ P4-① (2026-09-22): the Blackboard1024 add is GONE with the component. It was
                //    gated on `def.HeavyDtoType != null`, which is null at every production site and
                //    in all 30 shipped assets ⇒ this branch never ran. 📄 §30.13.

                // S2-2: Synchronously provision stateful working-state partition slots.
                // Must happen BEFORE the same frame's Simulation tick (§10 Flaw 1 fix).
                if (def.StatefulWorkingSlots != null && def.StatefulWorkingSlots.Count > 0)
                {
                    // Detach previous behavior's slots to avoid leaking them.
                    if (previousBehaviorId != BehaviorIds.None &&
                        previousBehaviorId != behaviorId &&
                        _registry.TryGetDefinition(previousBehaviorId, out var prevDef) &&
                        prevDef.StatefulWorkingSlots != null && prevDef.StatefulWorkingSlots.Count > 0)
                    {
                        DetachStatefulSlots(repo, evt.Entity, prevDef.StatefulWorkingSlots);
                    }

                    // A3/D1': declare WHAT these occurrences are, so O0's walker can filter on a
                    // declared Kind instead of on a BlueprintRegistry miss (F7 -- an accident, not
                    // a filter). The behaviour's tier IS the kind for its stateful working slots.
                    // O7b-3: the behaviour's HOSTED occurrences need room in the same tier.
                    // CE-302: and so does the ROOT PARAMS slot attached a few lines below.
                    _registry.TryGetHostedOccurrenceDemand(evt.BehaviorName, out var hosted);
                    ProvisionStatefulSlots(repo, evt.Entity, def.StatefulWorkingSlots, KindOf(def),
                                           hosted, RootParamsCost(def));
                }
                else
                {
                    _registry.TryGetHostedOccurrenceDemand(evt.BehaviorName, out var hosted);
                    EnsureOccurrenceStore(repo, evt.Entity, def, hosted, RootParamsCost(def));
                }

                // E3a: drop the PREVIOUS assign's lazily-attached hosted occurrences, so their params
                // re-seed from the JSON just parsed. ⛔ Omitting this makes new JSON a no-op (§28.4).
                DetachHostedOccurrenceSlots(repo, evt.Entity, def.StatefulWorkingSlots);

                // ⭐ CE-302: and the PREVIOUS behaviour's ROOT PARAMS slot, which the sweep above
                //   cannot reach on a BTree brain — its kind is BTree, not Hsm/Blueprint. ⛔ Without
                //   this, every behaviour change leaks one slot, and an entity reassigned a few times
                //   exhausts MaxSlots (3 on the 256 tier) and then silently loses its params.
                if (previousBehaviorId != BehaviorIds.None && previousBehaviorId != behaviorId)
                    RootParamsAccess.DetachRoot(repo, evt.Entity, previousBehaviorId);

                // ⭐⭐⭐ P3 — THE ROOT BEHAVIOUR'S PARAMS GET THEIR OWN SLOT.
                //   §29.6: ONE slot holds the WHOLE packed table, exactly as BehaviorParameters does,
                //   so every per-state seed offset (E3b-0) keeps meaning what it means. ⛔ It is NOT a
                //   scatter — scattering would destroy that indexing.
                //
                // ⚠ It must run AFTER provisioning: the occurrence store is what we attach into, and
                //   ProvisionStatefulSlots/EnsureOccurrenceStore above is what guarantees one exists.
                //
                // 🔴🔴 AND AFTER DetachHostedOccurrenceSlots — measured 2026-09-21, this ordering is
                //   LOAD-BEARING and the first version had it wrong. An HSM behaviour's root slot is
                //   attached with OccurrenceKind.Hsm (KindOf follows the brain tier, A3/D1′), and the
                //   sweep detaches exactly "kind Hsm|Blueprint and not named by the manifest" ⇒ it
                //   swept the root slot away on the very same assign that created it. ⛔ Harmless only
                //   while the blackboard commit still ran; after P3-C it is total params loss on every
                //   HSM brain. ⚠ Do NOT move this block back above the sweep.
                if (def.ParseParams != null)
                {
                    int rootBytes = RootParamsAccess.RootParamsBytes(def);
                    if (rootBytes > 0)
                    {
                        byte* rootParams = RootParamsAccess.ResolveOrAttachRoot(
                            repo, evt.Entity, behaviorId, rootBytes, KindOf(def), out _);

                        // ⛔⛔ A null here USED TO BE TOLERABLE — the blackboard still carried the
                        //   params, so the entity ran correctly and the miss was invisible. 🔴 After
                        //   P3-C the slot is the ONLY home, so tolerating it means the behaviour runs
                        //   on an all-zero params region: it does not crash, it just quietly does the
                        //   wrong thing. ⇒ THROW, and name the two causes.
                        // ⚠ This is the one place the toolkit's narrow contract (E-cap §27.2 — skip
                        //   when the tier components are not registered) becomes visible to a host. It
                        //   is deliberate: skipping is fine for a behaviour that merely MIGHT host an
                        //   occurrence, and is not fine for one that HAS parameters.
                        if (rootParams == null)
                            throw new InvalidOperationException(
                                $"Behaviour '{evt.BehaviorName}' parses {rootBytes} bytes of parameters, " +
                                $"but entity {evt.Entity.Index} has nowhere to put them. Either the " +
                                "BlueprintBlackboard* tier components are not registered on this world " +
                                "(Hrot registers them in HrotSharedComponentRegistry; a bare test world " +
                                "needs BlueprintTierTable.RegisterAll), or the entity's store had no " +
                                "room, which means the tier demand under-counted (CE-302).");

                        fixed (byte* src = shadow)
                            Buffer.MemoryCopy(src, rootParams, rootBytes, rootBytes);
                    }
                }

                // 2. Reset BTree execution pointer so the new behavior starts from the root.
                if (repo.HasComponent<BrainBTreeState>(evt.Entity))
                {
                    ref var btState = ref repo.GetComponentRW<BrainBTreeState>(evt.Entity);
                    btState.State = default;
                }
                ResetHostedTreeStates(repo, evt.Entity, def);

                // 3. BHU-016 / CRITICAL FIX: Reset HSM instance bound to the new behavior's topology.
                // Supplying the StructureHash keeps InstanceHeader.MachineId in sync with the new
                // HsmDefinitionBlob so HsmKernelCore.ValidateInstance passes on the very next tick.
                if (def.BrainTier == BehaviorConstants.BrainTierHsm && def.HsmDefinition != null)
                {
                    ResetHsmComponents(repo, evt.Entity, def.HsmDefinition.Header.StructureHash);
                }
            }

            // ── ClearBehaviorEvent handler ────────────────────────────────────────────────
            // Forcibly resets the active behavior to BehaviorIds.None (brain-death).
            // Published top-down by MissionDirectorSystem (plan exhausted) and
            // MissionControlRequestSystem (CMD_ABORT_ALL).
            var clearEvents = repo.Bus.Read<ClearBehaviorEvent>();
            foreach (var evt in clearEvents)
            {
                if (!repo.HasComponent<BehaviorState>(evt.Entity)) continue;

                // S3-5: detach the outgoing behavior's stateful slots BEFORE clearing.
                // The switch path (AssignBehaviorEvent) already detaches on switch, but a clear-
                // without-successor previously only nulled ActiveBehaviorHash, leaking the slots
                // until the next assign. Capture the previous behavior id and reclaim its slots.
                // DetachStatefulSlots frees by the manifest's SlotKey, which is scope-aware (S3-4),
                // so this reclaims Node- and Behavior-scoped slots alike.
                int previousBehaviorId = repo.GetComponentRW<BehaviorState>(evt.Entity).ActiveBehaviorHash;
                if (previousBehaviorId != BehaviorIds.None &&
                    _registry.TryGetDefinition(previousBehaviorId, out var prevDef) &&
                    prevDef.StatefulWorkingSlots != null && prevDef.StatefulWorkingSlots.Count > 0)
                {
                    DetachStatefulSlots(repo, evt.Entity, prevDef.StatefulWorkingSlots);
                }

                // E3a: a clear-without-successor must reclaim the lazily-attached hosted occurrences
                // too — the same leak S3-5 fixed for manifest slots.
                DetachHostedOccurrenceSlots(repo, evt.Entity, manifest: null);

                ref var behavior = ref repo.GetComponentRW<BehaviorState>(evt.Entity);
                behavior.ActiveBehaviorHash = BehaviorIds.None;
                unchecked { behavior.InstanceId++; }
                behavior.BrainTier = 0;

                if (repo.HasComponent<BrainBTreeState>(evt.Entity))
                    repo.GetComponentRW<BrainBTreeState>(evt.Entity).State = default;
            }

            // ── AssignBehaviorHashEvent handler ──────────────────────────────────────────
            // Activates a behavior by integer hash — published by MissionDirectorSystem
            // during phase transitions where only the hash (not the name) is known.
            // Increments InstanceId so ChannelArbitrationSystem preempts stale channels.
            var hashEvents = repo.Bus.Read<AssignBehaviorHashEvent>();
            foreach (var evt in hashEvents)
            {
                if (!repo.HasComponent<BehaviorState>(evt.Entity)) continue;

                ref var behavior = ref repo.GetComponentRW<BehaviorState>(evt.Entity);
                behavior.ActiveBehaviorHash = evt.BehaviorHash;
                unchecked { behavior.InstanceId++; }

                // Resolve the definition from the registry and restore the BrainTier.
                // Without this, entities remain brain-dead (BrainTier = 0) after a ClearBehaviorEvent.
                if (_registry.TryGetDefinition(evt.BehaviorHash, out var def))
                {
                    behavior.BrainTier = def.BrainTier;
                    // ⛔ P4-①: the second Blackboard1024 add, gone for the same reason as the first.
                }

                // Reset BTree execution pointer so the new phase starts from the root.
                if (repo.HasComponent<BrainBTreeState>(evt.Entity))
                    repo.GetComponentRW<BrainBTreeState>(evt.Entity).State = default;
                ResetHostedTreeStates(repo, evt.Entity, def);

                // BHU-016 / CRITICAL FIX: Reset HSM instance bound to the new behavior's topology.
                // Supplying the StructureHash keeps InstanceHeader.MachineId in sync with the new
                // HsmDefinitionBlob so HsmKernelCore.ValidateInstance passes on the very next tick.
                if (def != null && def.BrainTier == BehaviorConstants.BrainTierHsm && def.HsmDefinition != null)
                {
                    ResetHsmComponents(repo, evt.Entity, def.HsmDefinition.Header.StructureHash);
                }
            }
        }

        /// <summary>
        /// 🔴🔴🔴 <b><c>E-cap</c> — A BEHAVIOUR WITH NO MANIFEST STILL NEEDS A STORE.</b>
        /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §27.
        ///
        /// <para>⛔⛔ <b>Why this is not speculative housekeeping.</b> A hosted occurrence attaches its
        /// working state <b>lazily</b>, on first dispatch (§24.8) — and it <b>cannot add a tier
        /// component to do so</b>, because that is a STRUCTURAL change and must never happen inside a
        /// tick. ⇒ without a store already present, the very first dispatch of an HSM- or BTree-hosted
        /// blueprint <b>throws</b>.</para>
        ///
        /// <para>🔴 <b>Measured: that gap was real and shipped.</b> <c>ProvisionStatefulSlots</c> ran
        /// only when <c>def.StatefulWorkingSlots</c> was non-empty, so a behaviour that hosts a
        /// blueprint but declares no stateful slots of its own got no store at all. <c>O7b</c>'s HSM
        /// path passed its tests only because the fixture adds one by hand — ⚠ <b>the production path
        /// would have thrown</b>, which is exactly the shape §26.1's rule exists to catch.</para>
        ///
        /// <para>⭐ <b>The cost is the smallest tier</b> — <c>SelectTierForPayload(0, 0)</c> — which is
        /// why <c>O3b</c> added the 256 tier: §5a measured it as <b>load-bearing, not an
        /// optimisation</b>. ⚠ Entities whose behaviour never hosts anything do carry it; that is
        /// accepted, and it is reversible once a manifest can say who actually hosts (<c>O7b-3</c>).</para>
        ///
        /// <para>⚠ <b>Brain tiers only.</b> A behaviour that is neither BTree nor HSM cannot host an
        /// occurrence, so it gets nothing — ⛔ this is not "a store for every entity".</para>
        /// </summary>
        private static void EnsureOccurrenceStore(
            EntityRepository repo, Entity entity, BehaviorDefinition def,
            HostedOccurrenceDemand? hosted = null, int rootParamsCost = 0)
        {
            if (def.BrainTier != BehaviorConstants.BrainTierBTree &&
                def.BrainTier != BehaviorConstants.BrainTierHsm)
                return;

            if (GetCurrentTierSize(repo, entity) != 0) return;   // already has one — idempotent

            // ⭐⭐ O7b-3: size it for what this behaviour will actually host. ⚠ A null demand means
            //   "nobody computed one" (§27.7), and the smallest tier is the same answer E-cap gave —
            //   so this is additive, never a regression.
            // ⭐ CE-302: the root params slot is attached after this returns, so its cost is added here
            //   or it is never counted at all.
            int targetTier = SelectTierForPayload(
                HostedPayloadCost(hosted) + rootParamsCost,
                (hosted?.SlotCount ?? 0) + (rootParamsCost > 0 ? 1 : 0));

            // ⛔⛔ DO NOT WIDEN THE TOOLKIT'S CONTRACT. Registering the tier components is Hrot-wide
            //   (HrotSharedComponentRegistry, CE-161) but this system lives in Fdp.Toolkits, which
            //   hosts may use WITHOUT them. ⇒ a host that never hosts an occurrence must not start
            //   failing at assign just because this provisioning was added.
            // ⭐ The skip is SAFE because it is not silent where it matters: a host that skips here
            //   and then DOES host an occurrence gets OccurrenceWorkingState's loud "carries no
            //   occurrence store", whose message names tier registration as a cause.
            if (!BlueprintTierTable.ByTotalSize(targetTier).IsRegistered(repo)) return;

            AddAndInitializeTier(repo, entity, targetTier);
        }

        /// <summary>
        /// 🔴🔴 <b><c>F14b</c> — the EXTERNAL reset path: a hosted occurrence's cursor must follow
        /// the host's.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §21.2.
        ///
        /// <para>⛔⛔ <b>Why the two <c>D4</c> halves do not cover this.</b>
        /// <c>HostedSubtree.Tick</c> resets a child that COMPLETED, and the deactivator resets a child
        /// the host ABANDONED mid-tick — both reached through <c>Interpreter.SweepExitedNodes</c>.
        /// ⚠ This system zeroes <c>BrainBTreeState.State</c> <b>without a tick</b>, so neither fires:
        /// the host restarts at the root while a hosted child resumes mid-tree. ⭐ Before <c>O4</c> the
        /// child shared the host's 64-byte state, so zeroing the host cleared it by ACCIDENT — own
        /// state removes that, and this is the bill (the same bill <c>F14</c> paid for the sweep).</para>
        ///
        /// <para>⭐ <b>Only the hosted tree-state slots</b> (<c>HostedSubtree.IsTreeStateSlot</c>).
        /// ⛔ Author working state is deliberately NOT cleared: <c>AttachSlotsToMemory</c>'s idempotent
        /// arm preserves it across a no-op re-assign on purpose, and a cursor is not working state.</para>
        ///
        /// <para>⚠ <b><c>ClearBehaviorEvent</c> (<c>:204</c>) needs no call</b> — measured: it
        /// <c>DetachStatefulSlots</c> first, and <c>TryAttach</c> ZEROES the payload it hands out
        /// (<c>SlotAttachZeroingTests</c>), so the next assign gets a clean cursor either way.</para>
        /// </summary>
        /// <summary>
        /// 🔴🔴🔴 <b><c>E3a</c> — drop the LAZILY-ATTACHED hosted occurrences on a behaviour assign, so
        /// their params are re-seeded from the JSON this assign just parsed.</b> 📄 §28.4.
        ///
        /// <para>⛔⛔ <b>This is NOT housekeeping — omitting it is a REGRESSION.</b> Before <c>E3a</c> an
        /// HSM thunk read its params from <c>BrainBlackboard</c> <b>live</b>, so a re-assign with new
        /// JSON took effect on the very next dispatch. ⭐ After <c>E3a</c> the slot holds a <b>copy</b>
        /// ⇒ without this, <b>new JSON would silently stop taking effect</b> and the occurrence would
        /// run forever on the first assign's values.</para>
        ///
        /// <para>⭐⭐ <b><c>A3</c>/<c>D1′</c>'s <c>Kind</c> nibble is what makes this PRECISE.</b> A
        /// lazily-attached hosted occurrence declares <see cref="OccurrenceKind.Hsm"/> or
        /// <see cref="OccurrenceKind.Blueprint"/>; a manifest slot is provisioned with the behaviour's
        /// own kind. ⛔ Detaching by kind ALONE would also take manifest slots — hence <i>kind AND not
        /// named by the manifest</i>. ⚠ Without the nibble this sweep could not be written at all,
        /// which is what <c>F7</c> predicted.</para>
        ///
        /// <para>⚠ <b>Walks DOWNWARD.</b> <c>TryDetach</c> dense-compacts the slot table
        /// (<c>:188-199</c>), so an ascending walk would skip the entry that slid into the hole.</para>
        /// </summary>
        private static unsafe void DetachHostedOccurrenceSlots(
            EntityRepository repo, Entity entity, IReadOnlyList<StatefulSlotInfo>? manifest)
        {
            byte* store = OccurrenceStoreAccess.TryGetStore(repo, entity, out _);
            if (store == null) return;

            for (int i = BlueprintBlackboardPartitions.GetSlotCount(store) - 1; i >= 0; i--)
            {
                var kind = BlueprintBlackboardPartitions.GetSlotKind(store, i);
                if (kind != OccurrenceKind.Hsm && kind != OccurrenceKind.Blueprint) continue;

                int key = BlueprintBlackboardPartitions.GetSlot(store, i).BlueprintId;
                if (IsNamedByManifest(manifest, key)) continue;   // provisioned, not lazily attached

                BlueprintBlackboardPartitions.TryDetach(store, key);
            }
        }

        private static bool IsNamedByManifest(IReadOnlyList<StatefulSlotInfo>? manifest, int slotKey)
        {
            if (manifest == null) return false;
            for (int i = 0; i < manifest.Count; i++)
                if (manifest[i].SlotKey == slotKey) return true;
            return false;
        }

        private static void ResetHostedTreeStates(EntityRepository repo, Entity entity, BehaviorDefinition? def)
        {
            var slots = def?.StatefulWorkingSlots;
            if (slots == null) return;

            for (int i = 0; i < slots.Count; i++)
                if (HostedSubtree.IsTreeStateSlot(slots[i]))
                    HostedSubtree.Reset(repo, entity, slots[i].SlotKey);
        }

        // ── S2-2: stateful slot provisioning helpers ─────────────────────────────

        /// <summary>
        /// S2-2/S2-3: Synchronously provisions BlueprintBlackboard* tier and eagerly allocates
        /// every stateful working-state slot from the behavior manifest.
        /// Selects the smallest tier that can fit the manifest slots (considering existing
        /// occupancy when an entity already carries a tier). Performs tier upgrade inline
        /// (synchronous structural mutation is safe in Input phase, outside the Simulation lock).
        ///
        /// S2-3 addition: when an existing tier carries slots from this manifest that will be
        /// detached-and-reattached (size/hash mismatch), their current payload is treated as
        /// "to-be-freed" when computing available space, so tier selection is correct even when
        /// a WorkingState grows on a hard reload.
        /// </summary>
        /// <summary>
        /// A3/<c>D1′</c> — the <see cref="OccurrenceKind"/> a behaviour's stateful working slots get.
        /// ⚠ <see cref="OccurrenceKind.Invalid"/> for an unknown tier is deliberate: an undeclared
        /// occurrence must read <i>"nobody declared one"</i>, never a guess.
        /// </summary>
        /// <summary>
        /// <c>O7b-3</c> — the store bytes a behaviour's HOSTED occurrences will cost, in the SAME
        /// arithmetic <see cref="ProvisionStatefulSlots"/> uses for a manifest slot: aligned payload
        /// plus one <c>BlueprintSlotEntry</c> each.
        ///
        /// <para>⭐ The demand already carries its payload ALIGNED (<c>HostedOccurrenceDemand.Of</c>),
        /// so only the slot-entry overhead is added here. ⛔ Two places computing this would be two
        /// places to get it wrong — that is why the split is stated on the record and not invented at
        /// each call site.</para>
        ///
        /// <para>⚠ A <c>null</c> demand costs ZERO, which reproduces <c>E-cap</c>'s smallest tier
        /// exactly. ⛔ Not an error: <i>"nobody computed one"</i> is a real state (§27.7).</para>
        /// </summary>
        private static int HostedPayloadCost(HostedOccurrenceDemand? hosted)
            => hosted is null
                 ? 0
                 : hosted.PayloadBytes + hosted.SlotCount * BlueprintBlackboardPartitions.SlotEntrySize;

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-302</c> — the store bytes the ROOT PARAMS slot costs</b>, in the same
        /// arithmetic <see cref="HostedPayloadCost"/> uses: aligned payload plus one
        /// <c>BlueprintSlotEntry</c>. <b>Zero means "this behaviour has no root params".</b>
        ///
        /// <para>⛔⛔ <b>Why this had to exist before <c>P3-C</c>'s clean cut.</b> The root params slot
        /// is attached AFTER provisioning, so nothing that sizes the tier ever saw it. ⭐ While the
        /// blackboard commit still ran that was harmless — a full store simply returned <c>null</c>
        /// and the entity kept using <c>BrainBlackboard</c>. ⇒ once the blackboard is gone, the same
        /// <c>null</c> is <b>lost params</b>, so the room has to be reserved up front.</para>
        ///
        /// <para>⚠ <b>The slot count is implicitly ONE.</b> §29.6: the root params region is ONE slot
        /// holding the WHOLE packed table — scattering it per state would destroy <c>E3b-0</c>'s seed
        /// offsets, which index INTO that table. ⇒ callers add <c>cost &gt; 0 ? 1 : 0</c> slots, and
        /// there is no shape in which this returns a cost for more than one.</para>
        ///
        /// <para>⚠ <b>Gated on <c>ParseParams</c>, exactly as the attach is.</b> A behaviour with a
        /// manifest but no parser never attaches a root slot, so reserving for it would push entities
        /// up a tier for nothing.</para>
        /// </summary>
        private static int RootParamsCost(BehaviorDefinition def)
        {
            if (def?.ParseParams == null) return 0;

            int bytes = RootParamsAccess.RootParamsBytes(def);
            if (bytes <= 0) return 0;

            return AlignUp(bytes, BlueprintBlackboardPartitions.Alignment)
                 + BlueprintBlackboardPartitions.SlotEntrySize;
        }

        private static OccurrenceKind KindOf(BehaviorDefinition def) => def.BrainTier switch
        {
            BehaviorConstants.BrainTierBTree => OccurrenceKind.BTree,
            BehaviorConstants.BrainTierHsm   => OccurrenceKind.Hsm,
            _                                => OccurrenceKind.Invalid,
        };

        private static unsafe void ProvisionStatefulSlots(
            EntityRepository repo, Entity entity,
            IReadOnlyList<StatefulSlotInfo> slots,
            OccurrenceKind kind,
            HostedOccurrenceDemand? hosted = null,
            int rootParamsCost = 0)
        {
            // Compute aggregate required payload for the new manifest:
            // each slot at alignment-padded size + one BlueprintSlotEntry header per slot.
            int requiredPayload = 0;
            foreach (var s in slots)
                requiredPayload += AlignUp(s.PayloadSize, BlueprintBlackboardPartitions.Alignment)
                                 + BlueprintBlackboardPartitions.SlotEntrySize;
            int requiredSlots = slots.Count;

            // ⭐⭐⭐ O7b-3: the behaviour's HOSTED occurrences need room too, and they attach LAZILY —
            //   so nothing here will ever see them, and nothing later can grow the tier (a structural
            //   change inside a tick). ⇒ their demand is ADDITIVE to the manifest's, in BOTH branches
            //   below. ⛔ Sizing only the `EnsureOccurrenceStore` branch is the cheap wrong fix: it
            //   never runs for a behaviour that declares stateful slots of its own (rail O7_R18).
            requiredPayload += HostedPayloadCost(hosted);
            requiredSlots   += hosted?.SlotCount ?? 0;

            // ⭐⭐ CE-302: and the ROOT PARAMS slot, attached after this returns (§29.6 — ONE slot for
            //   the whole packed table). ⛔ Same reason as the hosted demand above: nothing later can
            //   grow the tier, because that is a structural change inside a tick.
            requiredPayload += rootParamsCost;
            requiredSlots   += rootParamsCost > 0 ? 1 : 0;

            // Determine the entity's current tier (0 = none, else TotalSize).
            int currentTier = GetCurrentTierSize(repo, entity);

            if (currentTier == 0)
            {
                // No tier present: select smallest tier whose abstract capacity fits the manifest.
                int targetTier = SelectTierForPayload(requiredPayload, requiredSlots);
                AddAndInitializeTier(repo, entity, targetTier);
            }
            else
            {
                // S2-3: compute how much space will be *freed* by detaching existing manifest slots
                // that are already attached (they will be detach+reattach'd if size/hash differs,
                // or are idempotently kept if identical). Only detach candidates contribute freed space.
                int toBeFreedPayload = GetManifestSlotsToBeFreedPayload(repo, entity, slots);
                int toBeReusedSlots  = GetManifestSlotsAlreadyAttachedCount(repo, entity, slots);

                // Effective free space: current free + what will be freed by detach.
                // Effective required slots: requiredSlots - already-attached (those reuse their slot entries).
                int freePayload       = GetTierFreePayload(repo, entity) + toBeFreedPayload;
                int freeSlots         = GetTierFreeSlotCount(repo, entity) + toBeReusedSlots;
                bool tierFits         = freePayload >= requiredPayload && freeSlots >= requiredSlots;

                if (!tierFits)
                {
                    // Current tier cannot accommodate manifest: compute total needed
                    // (existing used - freed-by-detach + new manifest) and select the smallest tier.
                    int usedPayload  = GetTierUsedPayload(repo, entity) - toBeFreedPayload;
                    int usedSlots    = GetTierUsedSlotCount(repo, entity) - toBeReusedSlots;
                    int totalPayload = usedPayload + requiredPayload;
                    int totalSlots   = usedSlots   + requiredSlots;
                    int targetTier   = SelectTierForPayload(totalPayload, totalSlots);

                    if (targetTier > currentTier)
                        UpgradeTier(repo, entity, currentTier, targetTier);
                    // If targetTier == currentTier (shouldn't happen since tierFits was false),
                    // we proceed; TryAttach will fail silently (not enough space).
                }
                // If tierFits: leave existing tier in place.
            }

            // Eager-allocate every manifest slot (idempotent for same-size+hash; detach+reattach for mismatch).
            AttachManifestSlots(repo, entity, slots, kind);
        }

        // ═══ O3a / B3 — THE SIX LADDERS THAT WERE NEVER TIER BRANCHING ═════════════════════════
        //  📄 DESIGN_Occurrence_Scoped_Storage.md §17.1 N3.
        //  ⛔⛔ Each of these took (repo, entity, tierSize) and re-spelled the three-way probe to
        //     reach ONE entity's store — while `tierSize` was GetCurrentTierSize(repo, entity), i.e.
        //     OccurrenceStoreAccess.GetStoreSize, computed on that same entity one line earlier at
        //     the single call site (:287). ⇒ they asked a question the seam already answers.
        //  ⭐⭐ And BlueprintBlackboardHeader is SELF-DESCRIBING — MaxSlots, SlotCount, PayloadSize
        //     and PayloadFree all live in the 32-byte header — so even the per-tier PayloadSize
        //     constant GetTierUsedPayload used was redundant: the store states its own capacity.
        //  ✅ Provably equivalent: every one of these is called only inside the `currentTier != 0`
        //     branch (:289), so TryGetStore can never return null here. The tierSize parameter is
        //     kept ONLY where it still documents the caller's intent — see StoreOf.

        /// <summary>
        /// The entity's store pointer. ⛔ Valid for the CALLING expression only — see the
        /// <c>OccurrenceStoreAccess</c> LIFETIME RULE.
        /// </summary>
        private static unsafe byte* StoreOf(EntityRepository repo, Entity entity)
            => Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess
                   .TryGetStore(repo, entity, out _);

        /// <summary>Returns the current free payload bytes in the entity's tier.</summary>
        private static unsafe int GetTierFreePayload(EntityRepository repo, Entity entity)
            => Unsafe.AsRef<BlueprintBlackboardHeader>(StoreOf(repo, entity)).PayloadFree;

        /// <summary>Returns the number of free slot entries (MaxSlots - SlotCount) in the entity's tier.</summary>
        private static unsafe int GetTierFreeSlotCount(EntityRepository repo, Entity entity)
        {
            ref var h = ref Unsafe.AsRef<BlueprintBlackboardHeader>(StoreOf(repo, entity));
            return h.MaxSlots - h.SlotCount;
        }

        /// <summary>Returns the used payload bytes = (PayloadSize - PayloadFree) in the entity's tier.</summary>
        private static unsafe int GetTierUsedPayload(EntityRepository repo, Entity entity)
        {
            // ⭐ O3a: was a three-constant ternary over BlueprintBlackboard*.PayloadSize. The header
            //   records PayloadSize per tier, so the store answers for itself.
            ref var h = ref Unsafe.AsRef<BlueprintBlackboardHeader>(StoreOf(repo, entity));
            return h.PayloadSize - h.PayloadFree;
        }

        /// <summary>
        /// S2-3: Sums the aligned PayloadSize of manifest slots that are already attached
        /// AND will be detached+reattached (PayloadSize or StructureHash mismatch).
        /// This is the amount of space that will be freed before reattachment, so the
        /// tier-fit calculation in ProvisionStatefulSlots can use it as "available extra space".
        /// </summary>
        private static unsafe int GetManifestSlotsToBeFreedPayload(
            EntityRepository repo, Entity entity, IReadOnlyList<StatefulSlotInfo> slots)
            => ComputeToBeFreedPayload(StoreOf(repo, entity), slots);

        private static unsafe int ComputeToBeFreedPayload(byte* mem, IReadOnlyList<StatefulSlotInfo> slots)
        {
            ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
            byte* slotTable = mem + Unsafe.SizeOf<BlueprintBlackboardHeader>();
            int freed = 0;
            foreach (var s in slots)
            {
                for (int i = 0; i < header.SlotCount; i++)
                {
                    ref var entry = ref Unsafe.AsRef<BlueprintSlotEntry>(
                        slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);
                    if (entry.BlueprintId == s.SlotKey)
                    {
                        // Will this slot be detached? Only if size or hash mismatches.
                        int alignedManifestSize = AlignUp(s.PayloadSize, BlueprintBlackboardPartitions.Alignment);
                        if (entry.PayloadSize != alignedManifestSize ||
                            entry.StructureHash != (uint)s.StructureHash)
                        {
                            freed += entry.PayloadSize; // this size will be returned to free list
                        }
                        break;
                    }
                }
            }
            return freed;
        }

        /// <summary>
        /// S2-3: Counts manifest slots already attached in the tier regardless of mismatch.
        /// These slots will either be kept (idempotent) or freed+reattached, but either way
        /// they do not consume an additional slot entry beyond what's already allocated.
        /// This count is used to adjust the free-slot-entry count when computing tier fit.
        /// </summary>
        private static unsafe int GetManifestSlotsAlreadyAttachedCount(
            EntityRepository repo, Entity entity, IReadOnlyList<StatefulSlotInfo> slots)
            => ComputeAlreadyAttachedCount(StoreOf(repo, entity), slots);

        private static unsafe int ComputeAlreadyAttachedCount(byte* mem, IReadOnlyList<StatefulSlotInfo> slots)
        {
            ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
            byte* slotTable = mem + Unsafe.SizeOf<BlueprintBlackboardHeader>();
            int count = 0;
            foreach (var s in slots)
            {
                for (int i = 0; i < header.SlotCount; i++)
                {
                    ref var entry = ref Unsafe.AsRef<BlueprintSlotEntry>(
                        slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);
                    if (entry.BlueprintId == s.SlotKey)
                    {
                        count++;
                        break;
                    }
                }
            }
            return count;
        }

        /// <summary>Returns the used slot count (SlotCount) in the entity's tier.</summary>
        private static unsafe int GetTierUsedSlotCount(EntityRepository repo, Entity entity)
            => Unsafe.AsRef<BlueprintBlackboardHeader>(StoreOf(repo, entity)).SlotCount;

        /// <summary>
        /// S2-2: Detaches the previous behavior's stateful slots from the entity's tier.
        /// Called before attaching new behavior's slots to prevent slot leaks.
        /// </summary>
        private static unsafe void DetachStatefulSlots(
            EntityRepository repo, Entity entity,
            IReadOnlyList<StatefulSlotInfo> slots)
        {
            // A2: the three-tier ladder, once, in OccurrenceStoreAccess.
            // ⛔ The pointer is valid for THIS CALL only — see the seam's LIFETIME RULE.
            byte* mem = Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess
                            .TryGetStore(repo, entity, out _);
            if (mem == null) return;

            foreach (var s in slots)
                BlueprintBlackboardPartitions.TryDetach(mem, s.SlotKey);
        }

        /// <summary>Returns the TotalSize constant of the entity's active tier, or 0 if none.</summary>
        private static int GetCurrentTierSize(EntityRepository repo, Entity entity)
        {
            // A2: identical to OccurrenceStoreAccess.GetStoreSize — delegated, not re-spelled.
            return Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.GetStoreSize(repo, entity);
        }

        // ═══ O3a / B3 — THE THREE THAT GENUINELY NEED A TIER TYPE ══════════════════════════════
        //  📄 DESIGN_Occurrence_Scoped_Storage.md §17.2. Select / Add / Upgrade are the sites where
        //  a COMPONENT TYPE must be named, so these go to BlueprintTierTable rather than the seam.
        //  ⛔⛔ UpgradeTier was the QUADRATIC one: 3 tiers ⇒ 3 arms, 4 tiers ⇒ 6. It is now one body.

        /// <summary>
        /// Selects the TotalSize of the smallest tier whose abstract capacity fits the given
        /// payload and slot count. Falls through to the largest tier if nothing smaller fits.
        /// </summary>
        private static int SelectTierForPayload(int requiredPayload, int requiredSlots)
            => BlueprintTierTable.Select(requiredPayload, requiredSlots).TotalSize;

        /// <summary>Adds a fresh tier component of the given size and initializes its allocator.</summary>
        private static unsafe void AddAndInitializeTier(EntityRepository repo, Entity entity, int tierSize)
        {
            var spec = BlueprintTierTable.ByTotalSize(tierSize);
            spec.Add(repo, entity);
            BlueprintBlackboardPartitions.Initialize(
                spec.Memory(repo, entity), spec.TotalSize, (byte)spec.MaxSlots);
        }

        /// <summary>
        /// Upgrades from a smaller tier to a larger one synchronously:
        /// AddComponent(larger), CopyToLargerTier, RemoveComponent(smaller).
        /// Preserves existing slots and their payloads.
        ///
        /// <para>⛔⛔ <c>CopyToLargerTier</c> is where <c>H1</c> lives — it copies the header's
        /// <c>Reserved</c>, which since <c>A3</c> carries the per-slot <c>Kind</c> nibble array. Rail
        /// <c>A3_R2</c> pins it. ⚠ This method must keep going THROUGH that helper; a hand-rolled
        /// copy here would zero every slot's kind and the tick walker would then skip the entity.</para>
        /// </summary>
        private static unsafe void UpgradeTier(EntityRepository repo, Entity entity, int srcTierSize, int dstTierSize)
        {
            // No downgrade path (current >= target means no-op, handled by caller).
            if (dstTierSize <= srcTierSize) return;

            BlueprintTierTable.Promote(
                repo, entity,
                BlueprintTierTable.ByTotalSize(srcTierSize),
                BlueprintTierTable.ByTotalSize(dstTierSize));
        }

        /// <summary>
        /// Attaches each manifest slot to the entity's active tier.
        /// Skips slots already attached (TryAttach is not idempotent; guard via TryGetSlotOffset).
        /// </summary>
        private static unsafe void AttachManifestSlots(
            EntityRepository repo, Entity entity, IReadOnlyList<StatefulSlotInfo> slots,
            OccurrenceKind kind)
        {
            // A2: the three-tier ladder, once, in OccurrenceStoreAccess.
            // ⛔ The pointer is valid for THIS CALL only — see the seam's LIFETIME RULE.
            byte* mem = Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess
                            .TryGetStore(repo, entity, out _);
            if (mem != null)
                AttachSlotsToMemory(mem, slots, kind);
        }

        /// <summary>
        /// S2-3: Ghost-slot-safe slot attachment.
        /// For each manifest slot:
        /// <list type="bullet">
        ///   <item>Not attached → attach (as before).</item>
        ///   <item>Attached with SAME PayloadSize AND StructureHash → leave it (idempotent;
        ///         working state is preserved — no churn on soft reload / no-op re-assign).</item>
        ///   <item>Attached with DIFFERENT PayloadSize OR StructureHash → TryDetach then
        ///         TryAttach at the manifest size/hash. The resized slot's working state
        ///         resets (expected on a structural reload). Adjacent slots remain intact
        ///         because TryDetach dense-compacts the slot table and returns payload to
        ///         the free list before TryAttach takes new space.</item>
        /// </list>
        /// Caller (ProvisionStatefulSlots) must have already ensured the tier has enough total
        /// space to satisfy the manifest (accounting for slots that will be freed before reattach).
        /// </summary>
        private static unsafe void AttachSlotsToMemory(
            byte* mem, IReadOnlyList<StatefulSlotInfo> slots, OccurrenceKind kind)
        {
            foreach (var s in slots)
            {
                if (!BlueprintBlackboardPartitions.TryGetSlotOffset(mem, s.SlotKey, out int existingOffset))
                {
                    // Not attached — attach fresh.
                    BlueprintBlackboardPartitions.TryAttach(mem, s.SlotKey, s.PayloadSize, s.StructureHash, kind, out _);
                    continue;
                }

                // Already attached — locate the slot entry to compare size and hash.
                ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
                int slotCount = header.SlotCount;
                byte* slotTable = mem + Unsafe.SizeOf<BlueprintBlackboardHeader>();

                bool mismatch = false;
                for (int i = 0; i < slotCount; i++)
                {
                    ref var entry = ref Unsafe.AsRef<BlueprintSlotEntry>(
                        slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);
                    if (entry.BlueprintId == s.SlotKey)
                    {
                        // Compare manifest PayloadSize (may be unaligned) against the aligned
                        // allocated size stored in the entry, and the hash.
                        int alignedManifestSize = AlignUp(s.PayloadSize, BlueprintBlackboardPartitions.Alignment);
                        if (entry.PayloadSize == alignedManifestSize &&
                            entry.StructureHash == (uint)s.StructureHash)
                        {
                            // Same size AND same hash → idempotent; preserve working state.
                            mismatch = false;
                        }
                        else
                        {
                            mismatch = true;
                        }
                        break;
                    }
                }

                if (mismatch)
                {
                    // S2-3 ghost-slot fix: detach the old (possibly wrong-sized) slot and
                    // re-attach at the manifest-specified size. This correctly re-provisions
                    // a slot that grew (or otherwise changed layout) on a hard reload.
                    // TryDetach dense-compacts the slot table — adjacent slots remain intact.
                    BlueprintBlackboardPartitions.TryDetach(mem, s.SlotKey);
                    BlueprintBlackboardPartitions.TryAttach(mem, s.SlotKey, s.PayloadSize, s.StructureHash, kind, out _);
                }
                // else: same size + hash → idempotent leave-it path (no churn, working state preserved).
            }
        }

        /// <summary>Aligns a size up to the given alignment boundary.</summary>
        private static int AlignUp(int size, int alignment)
            => (size + alignment - 1) & ~(alignment - 1);

        // ── HSM reset helper ─────────────────────────────────────────────────────

        private static unsafe void ResetHsmComponents(EntityRepository repo, Entity entity, uint newMachineId)
        {
            if (repo.HasComponent<BrainHsm64>(entity))
            {
                ref var hsm64 = ref repo.GetComponentRW<BrainHsm64>(entity);
                InstanceHeader* hdr = (InstanceHeader*)Unsafe.AsPointer(ref hsm64);
                // CRITICAL FIX: Bind the execution state to the new definition's topology.
                hdr->MachineId = newMachineId;
                hdr->Flags &= unchecked((InstanceFlags)(byte)~(byte)InstanceFlags.Terminated);
                hdr->Phase  = InstancePhase.Idle;
                hdr->QueueHead   = 0;
                hdr->ActiveTail  = 0;
                hdr->DeferredTail = 0;
                hdr->MicroStep   = 0;
                HsmInstance64* inst = (HsmInstance64*)hdr;
                inst->ActiveLeafIds[0] = 0xFFFF;
                inst->ActiveLeafIds[1] = 0xFFFF;
                inst->EventCount = 0;
            }

            if (repo.HasComponent<BrainHsm128>(entity))
            {
                ref var hsm128 = ref repo.GetComponentRW<BrainHsm128>(entity);
                InstanceHeader* hdr = (InstanceHeader*)Unsafe.AsPointer(ref hsm128);
                // CRITICAL FIX: Bind the execution state to the new definition's topology.
                hdr->MachineId = newMachineId;
                hdr->Flags &= unchecked((InstanceFlags)(byte)~(byte)InstanceFlags.Terminated);
                hdr->Phase  = InstancePhase.Idle;
                hdr->QueueHead   = 0;
                hdr->ActiveTail  = 0;
                hdr->DeferredTail = 0;
                hdr->MicroStep   = 0;
                HsmInstance128* inst = (HsmInstance128*)hdr;
                for (int i = 0; i < 4; i++) inst->ActiveLeafIds[i] = 0xFFFF;
                inst->EventCount      = 0;
                inst->InterruptSlotUsed = 0;
            }
        }
    }
}
