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
    /// Consumes <see cref="AssignDoctrineEvent"/>s and applies them to the relevant entities:
    /// <list type="number">
    ///   <item>Sets <see cref="DoctrineState.ActiveDoctrineHash"/> and <see cref="DoctrineState.BrainTier"/>.</item>
    ///   <item>Increments <see cref="DoctrineState.InstanceId"/> (deliberate wrapping via <c>unchecked</c>).
    ///         This bumps the preemption token so <see cref="ChannelArbitrationSystem"/> clears stale
    ///         channels on the next simulation tick.</item>
    ///   <item>Resets <see cref="BrainBTreeState.State"/> to <c>default</c> (execution pointer → 0).</item>
    ///   <item>Calls <see cref="DoctrineDefinition.ParseParams"/> to write blackboard parameters.</item>
    /// </list>
    ///
    /// Runs in <see cref="InputSystemGroup"/> so doctrine changes are visible to all brain tick
    /// systems (which run in <see cref="SimulationSystemGroup"/>) within the same frame.
    ///
    /// <para>
    /// DEBT-035 fix: all ECS component writes (<see cref="DoctrineState"/>, <see cref="BrainBTreeState"/>)
    /// now happen AFTER <see cref="DoctrineDefinition.ParseParams"/> succeeds.  A parse failure leaves
    /// the entity entirely on its previous doctrine — no partial transition.
    /// A stackalloc shadow copy of the blackboard is used so the live component is only updated
    /// when parsing succeeds, keeping the operation atomic from the ECS perspective.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public class DoctrineIngressSystem : IEcsModuleSystem
    {
        private readonly DoctrineRegistry _registry;

        public DoctrineIngressSystem(DoctrineRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(DoctrineIngressSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var events = repo.Bus.ReadManaged<AssignDoctrineEvent>();

            // Shadow buffer allocated once per OnUpdate call (outside the loop) to avoid
            // CA2014 stack-overflow risk. BrainBlackboardByteSize is a compile-time constant.
            Span<byte> shadow = stackalloc byte[BehaviorConstants.BrainBlackboardByteSize];

            foreach (var evt in events)
            {
                if (evt == null) continue;
                if (!repo.HasComponent<DoctrineState>(evt.Entity)) continue;

                // DEBT-006: use stable int ID from registry — no GetHashCode().
                if (!_registry.TryGetId(evt.DoctrineName, out int doctrineId)) continue;
                if (!_registry.TryGetDefinition(doctrineId, out var def)) continue;

                // DEBT-035 fix: attempt ParseParams BEFORE writing DoctrineState/BrainBTreeState.
                // Strategy: shadow-copy the live blackboard into stack memory, attempt parse on the
                // shadow, and only on success write the shadow back + commit the doctrine transition.
                // This ensures a ParseParams failure leaves the entity 100% on the old doctrine.
                if (def.ParseParams != null)
                {
                    if (!repo.HasComponent<BrainBlackboard>(evt.Entity))
                    {
                        // Doctrine requires params but entity has no blackboard — skip.
                        continue;
                    }

                    // Reuse the pre-allocated shadow buffer (cleared per iteration below).

                    ref readonly var bbRO = ref repo.GetComponentRO<BrainBlackboard>(evt.Entity);
                    fixed (byte* src = &bbRO.Memory[0], dst = shadow)
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

                    if (!parseOk) continue; // ParseParams failed — entity stays on old doctrine entirely.

                    // Parse succeeded: commit shadow back to the live blackboard.
                    ref var bbW = ref repo.GetComponentRW<BrainBlackboard>(evt.Entity);
                    fixed (byte* src = shadow, dst = &bbW.Memory[0])
                    {
                        Buffer.MemoryCopy(src, dst, BehaviorConstants.BrainBlackboardByteSize,
                            BehaviorConstants.BrainBlackboardByteSize);
                    }
                }

                // ParseParams succeeded (or was not required). Commit doctrine transition.

                // 1. Update DoctrineState.
                ref var doctrine = ref repo.GetComponentRW<DoctrineState>(evt.Entity);
                doctrine.ActiveDoctrineHash = doctrineId;
                // Intentional unsigned wrap — InstanceId is a monotonic preemption token.
                unchecked { doctrine.InstanceId++; }
                doctrine.BrainTier = def.BrainTier;

                // 2. Reset BTree execution pointer so the new doctrine starts from the root.
                if (repo.HasComponent<BrainBTreeState>(evt.Entity))
                {
                    ref var btState = ref repo.GetComponentRW<BrainBTreeState>(evt.Entity);
                    btState.State = default;
                }

                // 3. BHU-016: Reset HSM instance so the new doctrine starts clean.
                ResetHsmComponents(repo, evt.Entity);
            }

            // ── ClearDoctrineEvent handler ────────────────────────────────────────────────
            // Forcibly resets the active doctrine to DoctrineIds.None (brain-death).
            // Published top-down by MissionDirectorSystem (plan exhausted) and
            // MissionControlRequestSystem (CMD_ABORT_ALL).
            var clearEvents = repo.Bus.Read<ClearDoctrineEvent>();
            foreach (var evt in clearEvents)
            {
                if (!repo.HasComponent<DoctrineState>(evt.Entity)) continue;

                ref var doctrine = ref repo.GetComponentRW<DoctrineState>(evt.Entity);
                doctrine.ActiveDoctrineHash = DoctrineIds.None;
                unchecked { doctrine.InstanceId++; }
                doctrine.BrainTier = 0;

                if (repo.HasComponent<BrainBTreeState>(evt.Entity))
                    repo.GetComponentRW<BrainBTreeState>(evt.Entity).State = default;
            }

            // ── AssignDoctrineHashEvent handler ──────────────────────────────────────────
            // Activates a doctrine by integer hash — published by MissionDirectorSystem
            // during phase transitions where only the hash (not the name) is known.
            // Increments InstanceId so ChannelArbitrationSystem preempts stale channels.
            var hashEvents = repo.Bus.Read<AssignDoctrineHashEvent>();
            foreach (var evt in hashEvents)
            {
                if (!repo.HasComponent<DoctrineState>(evt.Entity)) continue;

                ref var doctrine = ref repo.GetComponentRW<DoctrineState>(evt.Entity);
                doctrine.ActiveDoctrineHash = evt.DoctrineHash;
                unchecked { doctrine.InstanceId++; }

                // Resolve the definition from the registry and restore the BrainTier.
                // Without this, entities remain brain-dead (BrainTier = 0) after a ClearDoctrineEvent.
                if (_registry.TryGetDefinition(evt.DoctrineHash, out var def))
                {
                    doctrine.BrainTier = def.BrainTier;
                }

                // Reset BTree execution pointer so the new phase starts from the root.
                if (repo.HasComponent<BrainBTreeState>(evt.Entity))
                    repo.GetComponentRW<BrainBTreeState>(evt.Entity).State = default;

                // BHU-016: Reset HSM instance so the new doctrine starts clean.
                ResetHsmComponents(repo, evt.Entity);
            }
        }

        // ── HSM reset helper ─────────────────────────────────────────────────────

        private static unsafe void ResetHsmComponents(EntityRepository repo, Entity entity)
        {
            if (repo.HasComponent<BrainHsm64>(entity))
            {
                ref var hsm64 = ref repo.GetComponentRW<BrainHsm64>(entity);
                InstanceHeader* hdr = (InstanceHeader*)Unsafe.AsPointer(ref hsm64);
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
