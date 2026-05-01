using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fbt;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Minimal unmanaged bridge passed to <see cref="HsmKernel.Update{TInstance,TContext}"/>.
    /// Must satisfy <c>where TContext : unmanaged</c> — cannot hold managed references.
    /// <c>WorldHandle</c> is an <see cref="System.IntPtr"/> (unmanaged) holding the GCHandle
    /// table index for the <see cref="EntityRepository"/>; recover with
    /// <c>GCHandle.FromIntPtr(bridge->WorldHandle).Target</c>.
    /// See DEBT-007-HSM-ANALYSIS.md for full explanation.
    /// </summary>
    public struct HsmKernelBridge
    {
        public Entity Self;
        public IntPtr WorldHandle;   // IntPtr is unmanaged; holds GCHandle table index
    }

    /// <summary>
    /// Generic system that steps FastHSM instances of type <typeparamref name="T"/> for
    /// every entity whose <see cref="DoctrineState.BrainTier"/> equals
    /// <see cref="BehaviorConstants.BrainTierHsm"/>.
    ///
    /// Register twice in the world:
    /// <code>
    ///   group.AddSystem(new HsmTickSystem&lt;BrainHsm64&gt;(registry));
    ///   group.AddSystem(new HsmTickSystem&lt;BrainHsm128&gt;(registry));
    /// </code>
    ///
    /// Ordering: must run AFTER <see cref="ChannelArbitrationSystem"/>.
    /// </summary>
    /// <typeparam name="T">
    /// ECS component that wraps an HSM instance (<see cref="BrainHsm64"/> or
    /// <see cref="BrainHsm128"/>).  The component's memory layout must start with the
    /// corresponding <c>HsmInstance64/128</c> so that
    /// <see cref="HsmKernel.Update{TInstance,TContext}"/> can identify the tier from
    /// <c>sizeof(T)</c>.
    /// </typeparam>
    [UpdateInPhase(SystemPhase.Simulation)]
    // [UpdateAfter(typeof(ChannelArbitrationSystem))] -- ordering maintained by array position in CognitiveRuntimeModule.
    public class HsmTickSystem<T> : IEcsModuleSystem, IProfiledSystem 
        where T : unmanaged
    {
        private readonly DoctrineRegistry _registry;

        /// <summary>
        /// Tracks the <see cref="DoctrineState.InstanceId"/> for which a terminal
        /// <see cref="DoctrineFinishedEvent"/> was last published, keyed by entity index.
        /// Prevents repeated publication when the same HSM doctrine stays terminated
        /// across consecutive ticks.
        /// </summary>
        private readonly Dictionary<int, uint> _publishedTerminalForInstanceId = new();

        // Reusable collections for dead-entity pruning — pre-allocated to avoid per-frame heap pressure.
        private readonly HashSet<int> _seenThisFrame = new();
        private readonly List<int>    _staleKeys     = new();

        public string ProfileName => $"HsmTickSystem<{typeof(T).Name}>";

        // Exposed for unit-testing: number of entities currently being tracked for
        // deduplication of DoctrineFinishedEvent. Should drop to zero after an entity
        // is destroyed and one additional Execute() tick has elapsed (stale pruning).
        internal int TrackedEntityCount => _publishedTerminalForInstanceId.Count;

        public HsmTickSystem(DoctrineRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(HsmTickSystem<T>)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var q = repo.Query()
                .With<DoctrineState>()
                .With<T>()
                .Build();

            _seenThisFrame.Clear();

            // Early-exit: skip per-entity overhead when no HSM entities are present.
            // Still clear the deduplication dict so destroyed entities are pruned immediately.
            if (q.IsEmpty)
            {
                _publishedTerminalForInstanceId.Clear();
                return;
            }

            var mobilityLostEvent = new HsmEvent { EventId = BehaviorConstants.EventId_MobilityLost };

            foreach (var entity in q)
            {
                _seenThisFrame.Add(entity.Index);

                var doctrine = repo.GetComponent<DoctrineState>(entity);

                // Only process HSM-tier entities.
                if (doctrine.BrainTier != BehaviorConstants.BrainTierHsm)
                    continue;

                // Skip if doctrine is unknown or has no HSM definition.
                if (!_registry.TryGetDefinition(doctrine.ActiveDoctrineHash, out var def)
                    || def.HsmDefinition == null)
                    continue;

                ref var component = ref repo.GetComponentRW<T>(entity);

                // BHU-009: Inject MobilityLost interrupt if blackboard byte 126 is set.
                if (repo.HasComponent<BrainBlackboard>(entity))
                {
                    ref var bb = ref repo.GetComponentRW<BrainBlackboard>(entity);
                    if (bb.Memory[CognitiveInterruptSystem.InterruptRegister_MobilityLost] == 1)
                    {
                        T* instPtr = (T*)Unsafe.AsPointer(ref component);
                        HsmEventQueue.TryEnqueue(instPtr, mobilityLostEvent);
                    }
                }

                // DEBT-007 full resolution: WorldHandle carries the GCHandle IntPtr so that
                // action delegates can recover the EntityRepository via GCHandle.FromIntPtr.
                // IntPtr is an unmanaged value type -- satisfies 'where TContext : unmanaged'.
                var bridge = new HsmKernelBridge
                {
                    Self        = entity,
                    WorldHandle = repo.UnmanagedHandle,  // one property read per entity per tick
                };

                // sizeof(T) determines the tier (64 / 128 / 256) inside HsmKernelCore.
                HsmKernel.Update(def.HsmDefinition, ref component, bridge, deltaTime);

                // BHU-007: Detect terminal state and publish DoctrineFinishedEvent exactly once
                // per doctrine instance. The Terminated flag is cleared so new doctrine
                // assignments don't fire a spurious second event.
                ref var hdr = ref Unsafe.As<T, InstanceHeader>(ref component);
                if ((hdr.Flags & InstanceFlags.Terminated) != 0)
                {
                    int  entityIdx  = entity.Index;
                    uint instanceId = doctrine.InstanceId;
                    if (!_publishedTerminalForInstanceId.TryGetValue(entityIdx, out uint prev)
                        || prev != instanceId)
                    {
                        _publishedTerminalForInstanceId[entityIdx] = instanceId;
                        repo.Bus.Publish(new DoctrineFinishedEvent { Entity = entity });
                        // Terminal latch fix: clear flag so a re-assigned doctrine won't
                        // inherit the Terminated state from the previous one.
                        hdr.Flags &= unchecked((InstanceFlags)(byte)~(byte)InstanceFlags.Terminated);
                        hdr.Phase  = InstancePhase.Idle;
                    }
                }
            }

            // Prune entries for entities that were not seen in this frame (destroyed or
            // their required components removed). Uses pre-allocated collections to avoid
            // per-frame heap allocations.
            _staleKeys.Clear();
            foreach (var key in _publishedTerminalForInstanceId.Keys)
                if (!_seenThisFrame.Contains(key))
                    _staleKeys.Add(key);
            foreach (var key in _staleKeys)
                _publishedTerminalForInstanceId.Remove(key);
        }
    }
}
