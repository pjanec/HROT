using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
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
    [UpdateInGroup(typeof(InputSystemGroup))]
    public class DoctrineIngressSystem : ComponentSystem
    {
        private readonly DoctrineRegistry _registry;

        public DoctrineIngressSystem(DoctrineRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        protected override unsafe void OnUpdate()
        {
            var events = World.Bus.ReadManaged<AssignDoctrineEvent>();

            // Shadow buffer allocated once per OnUpdate call (outside the loop) to avoid
            // CA2014 stack-overflow risk. BrainBlackboardByteSize is a compile-time constant.
            Span<byte> shadow = stackalloc byte[BehaviorConstants.BrainBlackboardByteSize];

            foreach (var evt in events)
            {
                if (evt == null) continue;
                if (!World.HasComponent<DoctrineState>(evt.Entity)) continue;

                // DEBT-006: use stable int ID from registry — no GetHashCode().
                if (!_registry.TryGetId(evt.DoctrineName, out int doctrineId)) continue;
                if (!_registry.TryGetDefinition(doctrineId, out var def)) continue;

                // DEBT-035 fix: attempt ParseParams BEFORE writing DoctrineState/BrainBTreeState.
                // Strategy: shadow-copy the live blackboard into stack memory, attempt parse on the
                // shadow, and only on success write the shadow back + commit the doctrine transition.
                // This ensures a ParseParams failure leaves the entity 100% on the old doctrine.
                if (def.ParseParams != null)
                {
                    if (!World.HasComponent<BrainBlackboard>(evt.Entity))
                    {
                        // Doctrine requires params but entity has no blackboard — skip.
                        continue;
                    }

                    // Reuse the pre-allocated shadow buffer (cleared per iteration below).

                    ref readonly var bbRO = ref World.GetComponentRO<BrainBlackboard>(evt.Entity);
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
                    ref var bbW = ref World.GetComponentRW<BrainBlackboard>(evt.Entity);
                    fixed (byte* src = shadow, dst = &bbW.Memory[0])
                    {
                        Buffer.MemoryCopy(src, dst, BehaviorConstants.BrainBlackboardByteSize,
                            BehaviorConstants.BrainBlackboardByteSize);
                    }
                }

                // ParseParams succeeded (or was not required). Commit doctrine transition.

                // 1. Update DoctrineState.
                ref var doctrine = ref World.GetComponentRW<DoctrineState>(evt.Entity);
                doctrine.ActiveDoctrineHash = doctrineId;
                // Intentional unsigned wrap — InstanceId is a monotonic preemption token.
                unchecked { doctrine.InstanceId++; }
                doctrine.BrainTier = def.BrainTier;

                // 2. Reset BTree execution pointer so the new doctrine starts from the root.
                if (World.HasComponent<BrainBTreeState>(evt.Entity))
                {
                    ref var btState = ref World.GetComponentRW<BrainBTreeState>(evt.Entity);
                    btState.State = default;
                }
            }

            // ── ClearDoctrineEvent handler ────────────────────────────────────────────────
            // Forcibly resets the active doctrine to DoctrineIds.None (brain-death).
            // Published top-down by MissionDirectorSystem (plan exhausted) and
            // MissionControlRequestSystem (CMD_ABORT_ALL).
            var clearEvents = World.Bus.Read<ClearDoctrineEvent>();
            foreach (var evt in clearEvents)
            {
                if (!World.HasComponent<DoctrineState>(evt.Entity)) continue;

                ref var doctrine = ref World.GetComponentRW<DoctrineState>(evt.Entity);
                doctrine.ActiveDoctrineHash = DoctrineIds.None;
                unchecked { doctrine.InstanceId++; }
                doctrine.BrainTier = 0;

                if (World.HasComponent<BrainBTreeState>(evt.Entity))
                    World.GetComponentRW<BrainBTreeState>(evt.Entity).State = default;
            }

            // ── AssignDoctrineHashEvent handler ──────────────────────────────────────────
            // Activates a doctrine by integer hash — published by MissionDirectorSystem
            // during phase transitions where only the hash (not the name) is known.
            // Increments InstanceId so ChannelArbitrationSystem preempts stale channels.
            var hashEvents = World.Bus.Read<AssignDoctrineHashEvent>();
            foreach (var evt in hashEvents)
            {
                if (!World.HasComponent<DoctrineState>(evt.Entity)) continue;

                ref var doctrine = ref World.GetComponentRW<DoctrineState>(evt.Entity);
                doctrine.ActiveDoctrineHash = evt.DoctrineHash;
                unchecked { doctrine.InstanceId++; }

                // Reset BTree execution pointer so the new phase starts from the root.
                if (World.HasComponent<BrainBTreeState>(evt.Entity))
                    World.GetComponentRW<BrainBTreeState>(evt.Entity).State = default;
            }
        }
    }
}
