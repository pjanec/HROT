using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Detects when <see cref="ActorCapabilities.CanMove"/> is cleared on an entity that
    /// carries a <see cref="BrainHsm128"/> or <see cref="BrainHsm64"/> component, and
    /// injects a <c>MobilityLost</c> HSM event (ID = <see cref="BehaviorConstants.EventId_MobilityLost"/>)
    /// into the instance's event queue via <see cref="HsmEventQueue.TryEnqueue{T}"/>.
    ///
    /// <para>
    /// <b>Change detection approach:</b> A per-entity shadow component
    /// <see cref="PreviousCapabilities"/> stores the capability bitmask from the last frame.
    /// The bridge compares <c>PreviousCapabilities.Capabilities</c> against the live
    /// <see cref="ActorCapabilityState.Capabilities"/>; a <c>CanMove</c> transition from
    /// set→cleared triggers the injection.
    /// </para>
    ///
    /// <para>
    /// <b>First-frame initialisation:</b> If a newly spawned entity has
    /// <see cref="ActorCapabilityState"/> + <see cref="BrainHsm128"/> but no
    /// <see cref="PreviousCapabilities"/> yet, the system adds the shadow component with the
    /// current capability value (no event fires that tick).  The list used for deferred
    /// structural changes is a managed allocation on the cold path (once per entity lifetime).
    /// </para>
    ///
    /// <para>
    /// <b>Execution order:</b> <see cref="SimulationSystemGroup"/>,
    /// <c>[UpdateBefore(typeof(HsmTickSystem&lt;BrainHsm128&gt;))]</c> — must run before HSM
    /// ticks so the event is available in the same frame.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    // [UpdateBefore(typeof(HsmTickSystem<BrainHsm128>))] -- ordering maintained by array position in CognitiveRuntimeModule.
    // [UpdateBefore(typeof(HsmTickSystem<BrainHsm64>))] -- ordering maintained by array position in CognitiveRuntimeModule.
    public class HsmDamageBridgeSystem : IEcsModuleSystem
    {
        // Reused across frames to avoid per-frame allocation on the (rare) init path.
        private readonly List<(Entity entity, ActorCapabilities caps)> _toInit =
            new List<(Entity, ActorCapabilities)>();

        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(HsmDamageBridgeSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var mobilityLostEvent = new HsmEvent { EventId = BehaviorConstants.EventId_MobilityLost };

            // ── BrainHsm128 ──────────────────────────────────────────────────

            // Pass A-128: initialise PreviousCapabilities for brand-new entities.
            var qNew128 = repo.Query()
                .With<ActorCapabilityState>()
                .With<BrainHsm128>()
                .Without<PreviousCapabilities>()
                .Build();

            foreach (var entity in qNew128)
            {
                var curr = repo.GetComponent<ActorCapabilityState>(entity);
                _toInit.Add((entity, curr.Capabilities));
            }

            foreach (var (e, caps) in _toInit)
                repo.AddComponent(e, new PreviousCapabilities { Capabilities = caps });
            _toInit.Clear();

            // Pass B-128: detect CanMove transitions and enqueue event.
            var q128 = repo.Query()
                .With<ActorCapabilityState>()
                .With<PreviousCapabilities>()
                .With<BrainHsm128>()
                .Build();

            foreach (var entity in q128)
            {
                var curr = repo.GetComponent<ActorCapabilityState>(entity);
                ref var prev = ref repo.GetComponentRW<PreviousCapabilities>(entity);

                bool wasAbleToMove = (prev.Capabilities & ActorCapabilities.CanMove) != 0;
                bool canMoveNow    = (curr.Capabilities & ActorCapabilities.CanMove) != 0;

                if (wasAbleToMove && !canMoveNow)
                {
                    ref var brain = ref repo.GetComponentRW<BrainHsm128>(entity);
                    fixed (HsmInstance128* ptr = &brain.State)
                    {
                        HsmEventQueue.TryEnqueue(ptr, in mobilityLostEvent);
                    }
                }

                prev.Capabilities = curr.Capabilities;
            }

            // ── BrainHsm64 ───────────────────────────────────────────────────

            // Pass A-64: initialise PreviousCapabilities for brand-new entities.
            var qNew64 = repo.Query()
                .With<ActorCapabilityState>()
                .With<BrainHsm64>()
                .Without<PreviousCapabilities>()
                .Build();

            foreach (var entity in qNew64)
            {
                var curr = repo.GetComponent<ActorCapabilityState>(entity);
                _toInit.Add((entity, curr.Capabilities));
            }

            foreach (var (e, caps) in _toInit)
                repo.AddComponent(e, new PreviousCapabilities { Capabilities = caps });
            _toInit.Clear();

            // Pass B-64: detect CanMove transitions and enqueue event.
            var q64 = repo.Query()
                .With<ActorCapabilityState>()
                .With<PreviousCapabilities>()
                .With<BrainHsm64>()
                .Build();

            foreach (var entity in q64)
            {
                var curr = repo.GetComponent<ActorCapabilityState>(entity);
                ref var prev = ref repo.GetComponentRW<PreviousCapabilities>(entity);

                bool wasAbleToMove = (prev.Capabilities & ActorCapabilities.CanMove) != 0;
                bool canMoveNow    = (curr.Capabilities & ActorCapabilities.CanMove) != 0;

                if (wasAbleToMove && !canMoveNow)
                {
                    ref var brain = ref repo.GetComponentRW<BrainHsm64>(entity);
                    fixed (HsmInstance64* ptr = &brain.State)
                    {
                        HsmEventQueue.TryEnqueue(ptr, in mobilityLostEvent);
                    }
                }

                prev.Capabilities = curr.Capabilities;
            }
        }
    }
}
