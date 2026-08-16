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
                // Strategy: shadow-copy the live blackboard into stack memory, attempt parse on the
                // shadow, and only on success write the shadow back + commit the behavior transition.
                // This ensures a ParseParams failure leaves the entity 100% on the old behavior.
                if (def.ParseParams != null)
                {
                    if (!repo.HasComponent<BrainBlackboard>(evt.Entity))
                    {
                        // Behavior requires params but entity has no blackboard — skip.
                        continue;
                    }

                    // Reuse the pre-allocated shadow buffer (cleared per iteration below).

                    ref readonly var bbRO = ref repo.GetComponentRO<BrainBlackboard>(evt.Entity);
                    fixed (BrainBlackboard* src = &bbRO)
                    fixed (byte* dst = shadow)
                    {
                        Buffer.MemoryCopy(src, dst, BehaviorConstants.BrainBlackboardByteSize,
                            BehaviorConstants.BrainBlackboardByteSize);
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

                    // Parse succeeded: commit shadow back to the live blackboard.
                    ref var bbW = ref repo.GetComponentRW<BrainBlackboard>(evt.Entity);
                    fixed (byte* src = shadow)
                    fixed (BrainBlackboard* dst = &bbW)
                    {
                        Buffer.MemoryCopy(src, dst, BehaviorConstants.BrainBlackboardByteSize,
                            BehaviorConstants.BrainBlackboardByteSize);
                    }
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
                if (def.HeavyDtoType != null &&
                    repo.IsComponentTypeRegistered<Blackboard1024>() &&
                    !repo.HasComponent<Blackboard1024>(evt.Entity))
                {
                    repo.AddComponent(evt.Entity, new Blackboard1024());
                }

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

                    ProvisionStatefulSlots(repo, evt.Entity, def.StatefulWorkingSlots);
                }

                // 2. Reset BTree execution pointer so the new behavior starts from the root.
                if (repo.HasComponent<BrainBTreeState>(evt.Entity))
                {
                    ref var btState = ref repo.GetComponentRW<BrainBTreeState>(evt.Entity);
                    btState.State = default;
                }

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
                    if (def.HeavyDtoType != null &&
                        repo.IsComponentTypeRegistered<Blackboard1024>() &&
                        !repo.HasComponent<Blackboard1024>(evt.Entity))
                    {
                        repo.AddComponent(evt.Entity, new Blackboard1024());
                    }
                }

                // Reset BTree execution pointer so the new phase starts from the root.
                if (repo.HasComponent<BrainBTreeState>(evt.Entity))
                    repo.GetComponentRW<BrainBTreeState>(evt.Entity).State = default;

                // BHU-016 / CRITICAL FIX: Reset HSM instance bound to the new behavior's topology.
                // Supplying the StructureHash keeps InstanceHeader.MachineId in sync with the new
                // HsmDefinitionBlob so HsmKernelCore.ValidateInstance passes on the very next tick.
                if (def != null && def.BrainTier == BehaviorConstants.BrainTierHsm && def.HsmDefinition != null)
                {
                    ResetHsmComponents(repo, evt.Entity, def.HsmDefinition.Header.StructureHash);
                }
            }
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
        private static unsafe void ProvisionStatefulSlots(
            EntityRepository repo, Entity entity,
            IReadOnlyList<StatefulSlotInfo> slots)
        {
            // Compute aggregate required payload for the new manifest:
            // each slot at alignment-padded size + one BlueprintSlotEntry header per slot.
            int requiredPayload = 0;
            foreach (var s in slots)
                requiredPayload += AlignUp(s.PayloadSize, BlueprintBlackboardPartitions.Alignment)
                                 + BlueprintBlackboardPartitions.SlotEntrySize;
            int requiredSlots = slots.Count;

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
                int toBeFreedPayload = GetManifestSlotsToBeFreedPayload(repo, entity, currentTier, slots);
                int toBeReusedSlots  = GetManifestSlotsAlreadyAttachedCount(repo, entity, currentTier, slots);

                // Effective free space: current free + what will be freed by detach.
                // Effective required slots: requiredSlots - already-attached (those reuse their slot entries).
                int freePayload       = GetTierFreePayload(repo, entity, currentTier) + toBeFreedPayload;
                int freeSlots         = GetTierFreeSlotCount(repo, entity, currentTier) + toBeReusedSlots;
                bool tierFits         = freePayload >= requiredPayload && freeSlots >= requiredSlots;

                if (!tierFits)
                {
                    // Current tier cannot accommodate manifest: compute total needed
                    // (existing used - freed-by-detach + new manifest) and select the smallest tier.
                    int usedPayload  = GetTierUsedPayload(repo, entity, currentTier) - toBeFreedPayload;
                    int usedSlots    = GetTierUsedSlotCount(repo, entity, currentTier) - toBeReusedSlots;
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
            AttachManifestSlots(repo, entity, slots);
        }

        /// <summary>Returns the current free payload bytes in the entity's tier.</summary>
        private static unsafe int GetTierFreePayload(EntityRepository repo, Entity entity, int tierSize)
        {
            if (tierSize == BlueprintBlackboard16384.TotalSize)
            {
                ref var t = ref repo.GetComponentRW<BlueprintBlackboard16384>(entity);
                fixed (byte* mem = t.Memory)
                    return Unsafe.AsRef<BlueprintBlackboardHeader>(mem).PayloadFree;
            }
            if (tierSize == BlueprintBlackboard4096.TotalSize)
            {
                ref var t = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity);
                fixed (byte* mem = t.Memory)
                    return Unsafe.AsRef<BlueprintBlackboardHeader>(mem).PayloadFree;
            }
            {
                ref var t = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
                fixed (byte* mem = t.Memory)
                    return Unsafe.AsRef<BlueprintBlackboardHeader>(mem).PayloadFree;
            }
        }

        /// <summary>Returns the number of free slot entries (MaxSlots - SlotCount) in the entity's tier.</summary>
        private static unsafe int GetTierFreeSlotCount(EntityRepository repo, Entity entity, int tierSize)
        {
            if (tierSize == BlueprintBlackboard16384.TotalSize)
            {
                ref var t = ref repo.GetComponentRW<BlueprintBlackboard16384>(entity);
                fixed (byte* mem = t.Memory)
                {
                    ref var h = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
                    return h.MaxSlots - h.SlotCount;
                }
            }
            if (tierSize == BlueprintBlackboard4096.TotalSize)
            {
                ref var t = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity);
                fixed (byte* mem = t.Memory)
                {
                    ref var h = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
                    return h.MaxSlots - h.SlotCount;
                }
            }
            {
                ref var t = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
                fixed (byte* mem = t.Memory)
                {
                    ref var h = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
                    return h.MaxSlots - h.SlotCount;
                }
            }
        }

        /// <summary>Returns the used payload bytes = (PayloadSize - PayloadFree) in the entity's tier.</summary>
        private static unsafe int GetTierUsedPayload(EntityRepository repo, Entity entity, int tierSize)
        {
            int payloadSize = tierSize == BlueprintBlackboard16384.TotalSize ? BlueprintBlackboard16384.PayloadSize
                            : tierSize == BlueprintBlackboard4096.TotalSize  ? BlueprintBlackboard4096.PayloadSize
                            : BlueprintBlackboard1024.PayloadSize;
            return payloadSize - GetTierFreePayload(repo, entity, tierSize);
        }

        /// <summary>
        /// S2-3: Sums the aligned PayloadSize of manifest slots that are already attached
        /// AND will be detached+reattached (PayloadSize or StructureHash mismatch).
        /// This is the amount of space that will be freed before reattachment, so the
        /// tier-fit calculation in ProvisionStatefulSlots can use it as "available extra space".
        /// </summary>
        private static unsafe int GetManifestSlotsToBeFreedPayload(
            EntityRepository repo, Entity entity, int tierSize,
            IReadOnlyList<StatefulSlotInfo> slots)
        {
            if (tierSize == BlueprintBlackboard16384.TotalSize)
            {
                ref var t = ref repo.GetComponentRW<BlueprintBlackboard16384>(entity);
                fixed (byte* mem = t.Memory)
                    return ComputeToBeFreedPayload(mem, slots);
            }
            if (tierSize == BlueprintBlackboard4096.TotalSize)
            {
                ref var t = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity);
                fixed (byte* mem = t.Memory)
                    return ComputeToBeFreedPayload(mem, slots);
            }
            {
                ref var t = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
                fixed (byte* mem = t.Memory)
                    return ComputeToBeFreedPayload(mem, slots);
            }
        }

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
            EntityRepository repo, Entity entity, int tierSize,
            IReadOnlyList<StatefulSlotInfo> slots)
        {
            if (tierSize == BlueprintBlackboard16384.TotalSize)
            {
                ref var t = ref repo.GetComponentRW<BlueprintBlackboard16384>(entity);
                fixed (byte* mem = t.Memory)
                    return ComputeAlreadyAttachedCount(mem, slots);
            }
            if (tierSize == BlueprintBlackboard4096.TotalSize)
            {
                ref var t = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity);
                fixed (byte* mem = t.Memory)
                    return ComputeAlreadyAttachedCount(mem, slots);
            }
            {
                ref var t = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
                fixed (byte* mem = t.Memory)
                    return ComputeAlreadyAttachedCount(mem, slots);
            }
        }

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
        private static unsafe int GetTierUsedSlotCount(EntityRepository repo, Entity entity, int tierSize)
        {
            if (tierSize == BlueprintBlackboard16384.TotalSize)
            {
                ref var t = ref repo.GetComponentRW<BlueprintBlackboard16384>(entity);
                fixed (byte* mem = t.Memory)
                    return Unsafe.AsRef<BlueprintBlackboardHeader>(mem).SlotCount;
            }
            if (tierSize == BlueprintBlackboard4096.TotalSize)
            {
                ref var t = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity);
                fixed (byte* mem = t.Memory)
                    return Unsafe.AsRef<BlueprintBlackboardHeader>(mem).SlotCount;
            }
            {
                ref var t = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
                fixed (byte* mem = t.Memory)
                    return Unsafe.AsRef<BlueprintBlackboardHeader>(mem).SlotCount;
            }
        }

        /// <summary>
        /// S2-2: Detaches the previous behavior's stateful slots from the entity's tier.
        /// Called before attaching new behavior's slots to prevent slot leaks.
        /// </summary>
        private static unsafe void DetachStatefulSlots(
            EntityRepository repo, Entity entity,
            IReadOnlyList<StatefulSlotInfo> slots)
        {
            // Try each tier in order (check which one the entity has).
            if (repo.HasComponent<BlueprintBlackboard16384>(entity))
            {
                ref var tier = ref repo.GetComponentRW<BlueprintBlackboard16384>(entity);
                fixed (byte* mem = tier.Memory)
                {
                    foreach (var s in slots)
                        BlueprintBlackboardPartitions.TryDetach(mem, s.SlotKey);
                }
                return;
            }
            if (repo.HasComponent<BlueprintBlackboard4096>(entity))
            {
                ref var tier = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity);
                fixed (byte* mem = tier.Memory)
                {
                    foreach (var s in slots)
                        BlueprintBlackboardPartitions.TryDetach(mem, s.SlotKey);
                }
                return;
            }
            if (repo.HasComponent<BlueprintBlackboard1024>(entity))
            {
                ref var tier = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
                fixed (byte* mem = tier.Memory)
                {
                    foreach (var s in slots)
                        BlueprintBlackboardPartitions.TryDetach(mem, s.SlotKey);
                }
            }
        }

        /// <summary>Returns the TotalSize constant of the entity's active tier, or 0 if none.</summary>
        private static int GetCurrentTierSize(EntityRepository repo, Entity entity)
        {
            if (repo.HasComponent<BlueprintBlackboard16384>(entity)) return BlueprintBlackboard16384.TotalSize;
            if (repo.HasComponent<BlueprintBlackboard4096>(entity))  return BlueprintBlackboard4096.TotalSize;
            if (repo.HasComponent<BlueprintBlackboard1024>(entity))  return BlueprintBlackboard1024.TotalSize;
            return 0;
        }

        /// <summary>
        /// Selects the TotalSize of the smallest tier whose abstract capacity fits the given
        /// payload and slot count. Falls through to 16384 if nothing smaller fits.
        /// </summary>
        private static int SelectTierForPayload(int requiredPayload, int requiredSlots)
        {
            if (requiredPayload <= BlueprintBlackboard1024.PayloadSize &&
                requiredSlots   <= BlueprintBlackboard1024.MaxSlots)
                return BlueprintBlackboard1024.TotalSize;
            if (requiredPayload <= BlueprintBlackboard4096.PayloadSize &&
                requiredSlots   <= BlueprintBlackboard4096.MaxSlots)
                return BlueprintBlackboard4096.TotalSize;
            return BlueprintBlackboard16384.TotalSize;
        }

        /// <summary>Adds a fresh tier component of the given size and initializes its allocator.</summary>
        private static unsafe void AddAndInitializeTier(EntityRepository repo, Entity entity, int tierSize)
        {
            if (tierSize == BlueprintBlackboard1024.TotalSize)
            {
                repo.AddComponent(entity, new BlueprintBlackboard1024());
                ref var tier = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
                fixed (byte* mem = tier.Memory)
                    BlueprintBlackboardPartitions.Initialize(mem, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);
            }
            else if (tierSize == BlueprintBlackboard4096.TotalSize)
            {
                repo.AddComponent(entity, new BlueprintBlackboard4096());
                ref var tier = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity);
                fixed (byte* mem = tier.Memory)
                    BlueprintBlackboardPartitions.Initialize(mem, BlueprintBlackboard4096.TotalSize, BlueprintBlackboard4096.MaxSlots);
            }
            else
            {
                repo.AddComponent(entity, new BlueprintBlackboard16384());
                ref var tier = ref repo.GetComponentRW<BlueprintBlackboard16384>(entity);
                fixed (byte* mem = tier.Memory)
                    BlueprintBlackboardPartitions.Initialize(mem, BlueprintBlackboard16384.TotalSize, BlueprintBlackboard16384.MaxSlots);
            }
        }

        /// <summary>
        /// Upgrades from a smaller tier to a larger one synchronously:
        /// AddComponent(larger), CopyToLargerTier, RemoveComponent(smaller).
        /// Preserves existing slots and their payloads.
        /// </summary>
        private static unsafe void UpgradeTier(EntityRepository repo, Entity entity, int srcTierSize, int dstTierSize)
        {
            if (srcTierSize == BlueprintBlackboard1024.TotalSize &&
                dstTierSize == BlueprintBlackboard4096.TotalSize)
            {
                repo.AddComponent(entity, new BlueprintBlackboard4096());
                ref var src = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
                ref var dst = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity);
                fixed (byte* srcMem = src.Memory)
                fixed (byte* dstMem = dst.Memory)
                    BlueprintBlackboardPartitions.CopyToLargerTier(srcMem, BlueprintBlackboard1024.TotalSize,
                        dstMem, BlueprintBlackboard4096.TotalSize, BlueprintBlackboard4096.MaxSlots);
                repo.RemoveComponent<BlueprintBlackboard1024>(entity);
            }
            else if (srcTierSize == BlueprintBlackboard1024.TotalSize &&
                     dstTierSize == BlueprintBlackboard16384.TotalSize)
            {
                repo.AddComponent(entity, new BlueprintBlackboard16384());
                ref var src = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
                ref var dst = ref repo.GetComponentRW<BlueprintBlackboard16384>(entity);
                fixed (byte* srcMem = src.Memory)
                fixed (byte* dstMem = dst.Memory)
                    BlueprintBlackboardPartitions.CopyToLargerTier(srcMem, BlueprintBlackboard1024.TotalSize,
                        dstMem, BlueprintBlackboard16384.TotalSize, BlueprintBlackboard16384.MaxSlots);
                repo.RemoveComponent<BlueprintBlackboard1024>(entity);
            }
            else if (srcTierSize == BlueprintBlackboard4096.TotalSize &&
                     dstTierSize == BlueprintBlackboard16384.TotalSize)
            {
                repo.AddComponent(entity, new BlueprintBlackboard16384());
                ref var src = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity);
                ref var dst = ref repo.GetComponentRW<BlueprintBlackboard16384>(entity);
                fixed (byte* srcMem = src.Memory)
                fixed (byte* dstMem = dst.Memory)
                    BlueprintBlackboardPartitions.CopyToLargerTier(srcMem, BlueprintBlackboard4096.TotalSize,
                        dstMem, BlueprintBlackboard16384.TotalSize, BlueprintBlackboard16384.MaxSlots);
                repo.RemoveComponent<BlueprintBlackboard4096>(entity);
            }
            // No downgrade path (current >= target means no-op, handled by caller).
        }

        /// <summary>
        /// Attaches each manifest slot to the entity's active tier.
        /// Skips slots already attached (TryAttach is not idempotent; guard via TryGetSlotOffset).
        /// </summary>
        private static unsafe void AttachManifestSlots(
            EntityRepository repo, Entity entity, IReadOnlyList<StatefulSlotInfo> slots)
        {
            // Dispatch to whichever tier component the entity has.
            if (repo.HasComponent<BlueprintBlackboard16384>(entity))
            {
                ref var tier = ref repo.GetComponentRW<BlueprintBlackboard16384>(entity);
                fixed (byte* mem = tier.Memory)
                    AttachSlotsToMemory(mem, slots);
                return;
            }
            if (repo.HasComponent<BlueprintBlackboard4096>(entity))
            {
                ref var tier = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity);
                fixed (byte* mem = tier.Memory)
                    AttachSlotsToMemory(mem, slots);
                return;
            }
            if (repo.HasComponent<BlueprintBlackboard1024>(entity))
            {
                ref var tier = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
                fixed (byte* mem = tier.Memory)
                    AttachSlotsToMemory(mem, slots);
            }
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
        private static unsafe void AttachSlotsToMemory(byte* mem, IReadOnlyList<StatefulSlotInfo> slots)
        {
            foreach (var s in slots)
            {
                if (!BlueprintBlackboardPartitions.TryGetSlotOffset(mem, s.SlotKey, out int existingOffset))
                {
                    // Not attached — attach fresh.
                    BlueprintBlackboardPartitions.TryAttach(mem, s.SlotKey, s.PayloadSize, s.StructureHash, out _);
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
                    BlueprintBlackboardPartitions.TryAttach(mem, s.SlotKey, s.PayloadSize, s.StructureHash, out _);
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
