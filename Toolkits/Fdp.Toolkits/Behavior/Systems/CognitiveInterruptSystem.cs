using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Edge-triggered system that writes interrupt bytes into <see cref="BrainBlackboard"/>
    /// when capability transitions are detected.  Uses a paradigm-agnostic blackboard field
    /// rather than injecting HSM events directly, so that BTree behaviors can
    /// also react to the same signal without coupling to the HSM event queue.
    ///
    /// <para>
    /// <b>Interrupt_MobilityLost:</b> Set to 1 on the tick when
    /// <see cref="ActorCapabilities.CanMove"/> transitions from set to cleared.
    /// Remains 1 until cleared by <see cref="CognitiveCleanupSystem"/> at end of frame.
    /// </para>
    ///
    /// <para>
    /// <b>First-frame initialisation:</b> If a newly spawned entity has
    /// <see cref="ActorCapabilityState"/> but no <see cref="PreviousCapabilities"/> yet,
    /// the system adds the shadow component with the current capability value (no interrupt
    /// fires that tick).
    /// </para>
    ///
    /// <para>
    /// Execution order: must run before HSM/BTree tick systems so the interrupt byte is
    /// available in the same frame.  Ordering maintained by array position in
    /// <see cref="Modules.CognitiveRuntimeModule"/>.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    internal sealed class CognitiveInterruptSystem : IEcsModuleSystem
    {

        // Reused list for deferred structural adds (cold path: once per entity lifetime).
        private readonly List<(Entity entity, ActorCapabilities caps)> _toInit =
            new List<(Entity, ActorCapabilities)>();

        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;

            // Pass A: initialise PreviousCapabilities for brand-new entities.
            var qNew = repo.Query()
                .With<ActorCapabilityState>()
                .With<BrainBlackboard>()
                .Without<PreviousCapabilities>()
                .Build();

            foreach (var entity in qNew)
            {
                var curr = repo.GetComponent<ActorCapabilityState>(entity);
                _toInit.Add((entity, curr.Capabilities));
            }

            foreach (var (e, caps) in _toInit)
                repo.AddComponent(e, new PreviousCapabilities { Capabilities = caps });
            _toInit.Clear();

            // Pass B: detect CanMove→cleared transition and set interrupt byte 126.
            var q = repo.Query()
                .With<ActorCapabilityState>()
                .With<PreviousCapabilities>()
                .With<BrainBlackboard>()
                .Build();

            foreach (var entity in q)
            {
                var curr = repo.GetComponent<ActorCapabilityState>(entity);
                ref var prev = ref repo.GetComponentRW<PreviousCapabilities>(entity);

                bool wasAbleToMove = (prev.Capabilities & ActorCapabilities.CanMove) != 0;
                bool canMoveNow    = (curr.Capabilities & ActorCapabilities.CanMove) != 0;

                if (wasAbleToMove && !canMoveNow)
                {
                    ref var bb = ref repo.GetComponentRW<BrainBlackboard>(entity);
                    bb.Interrupt_MobilityLost = 1;
                }

                prev.Capabilities = curr.Capabilities;
            }
        }
    }
}
