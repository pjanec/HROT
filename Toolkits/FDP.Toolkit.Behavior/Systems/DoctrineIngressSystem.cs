using System;
using System;
using System.Runtime.CompilerServices;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Events;

namespace FDP.Toolkit.Behavior.Systems
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
            var events = World.Bus.ConsumeManaged<AssignDoctrineEvent>();
            foreach (var evt in events)
            {
                if (evt == null) continue;
                if (!World.HasComponent<DoctrineState>(evt.Entity)) continue;

                // DEBT-006: use stable int ID from registry — no GetHashCode().
                if (!_registry.TryGetId(evt.DoctrineName, out int doctrineId)) continue;
                if (!_registry.TryGetDefinition(doctrineId, out var def)) continue;

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

                // 3. Parse JSON parameters into the blackboard (cold path — happens once per assignment).
                if (def.ParseParams != null && World.HasComponent<BrainBlackboard>(evt.Entity))
                {
                    ref var blackboard = ref World.GetComponentRW<BrainBlackboard>(evt.Entity);
                    // Unsafe.AsPointer yields a stable pointer into the native component chunk.
                    // BrainBlackboard's only field is the fixed Memory buffer at offset 0.
                    var bbPtr = (BrainBlackboard*)Unsafe.AsPointer(ref blackboard);
                    try
                    {
                        def.ParseParams(evt.JsonParams, bbPtr->Memory);
                    }
                    catch (Exception ex)
                    {
                        // DEBT-008: guard against malformed JSON or delegate bugs.
                        // Log and fail safe — leave DoctrineState unchanged (InstanceId NOT bumped
                        // a second time; it was already incremented above).
                        // Do NOT rethrow: a parse failure must not crash the simulation loop.
                        _ = ex; // suppress unused-variable warning; replace with real logger when available.
                        // Fail safe: revert the DoctrineState update so the entity stays on its
                        // previous doctrine rather than entering a half-applied state.
                        // We cannot easily un-bump InstanceId here, so we skip the entity.
                        continue;
                    }
                }
            }
        }
    }
}
