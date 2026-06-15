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
                            def.ParseParams(evt.JsonParams, dst);
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
        /// S2-2: Synchronously provisions BlueprintBlackboard* tier and eagerly allocates
        /// every stateful working-state slot from the behavior manifest.
        /// Selects the smallest tier that can fit the manifest slots (considering existing
        /// occupancy when an entity already carries a tier). Performs tier upgrade inline
        /// (synchronous structural mutation is safe in Input phase, outside the Simulation lock).
        /// </summary>
        private static unsafe void ProvisionStatefulSlots(
            EntityRepository repo, Entity entity,
            IReadOnlyList<StatefulSlotInfo> slots)
        {
            // Compute aggregate required payload for the new manifest:
            // each slot at alignment-padded size + one BlueprintSlotEntry header per slot.
            int requiredPayload = 0;
            int requiredSlots   = slots.Count;
            foreach (var s in slots)
                requiredPayload += AlignUp(s.PayloadSize, BlueprintBlackboardPartitions.Alignment)
                                 + BlueprintBlackboardPartitions.SlotEntrySize;

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
                // Tier exists: check if it has enough FREE SPACE and remaining slot entries.
                // We must consider existing occupancy so a partially-full tier can trigger upgrade.
                int freePayload  = GetTierFreePayload(repo, entity, currentTier);
                int freeSlots    = GetTierFreeSlotCount(repo, entity, currentTier);
                bool tierFits    = freePayload >= requiredPayload && freeSlots >= requiredSlots;

                if (!tierFits)
                {
                    // Current tier cannot accommodate manifest: compute total needed
                    // (existing used + new manifest) and select the smallest larger tier.
                    int usedPayload  = GetTierUsedPayload(repo, entity, currentTier);
                    int usedSlots    = GetTierUsedSlotCount(repo, entity, currentTier);
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

            // Eager-allocate every manifest slot (skip if already attached).
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

        private static unsafe void AttachSlotsToMemory(byte* mem, IReadOnlyList<StatefulSlotInfo> slots)
        {
            foreach (var s in slots)
            {
                // Skip if already attached (idempotency guard).
                if (BlueprintBlackboardPartitions.TryGetSlotOffset(mem, s.SlotKey, out _))
                    continue;
                BlueprintBlackboardPartitions.TryAttach(mem, s.SlotKey, s.PayloadSize, s.StructureHash, out _);
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
