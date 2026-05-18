using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;

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
