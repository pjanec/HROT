using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fhsm.Kernel;
using Fdp.Toolkit.Behavior.Components;

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

        public string ProfileName => $"HsmTickSystem<{typeof(T).Name}>";

        public HsmTickSystem(DoctrineRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(HsmTickSystem<T>)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var q = repo.Query()
                .With<DoctrineState>()
                .With<T>()
                .Build();

            // Early-exit: skip the per-entity overhead when no HSM entities exist.
            if (q.IsEmpty) return;

            foreach (var entity in q)
            {
                var doctrine = repo.GetComponent<DoctrineState>(entity);

                // Only process HSM-tier entities.
                if (doctrine.BrainTier != BehaviorConstants.BrainTierHsm)
                    continue;

                // Skip if doctrine is unknown or has no HSM definition.
                if (!_registry.TryGetDefinition(doctrine.ActiveDoctrineHash, out var def)
                    || def.HsmDefinition == null)
                    continue;

                ref var component = ref repo.GetComponentRW<T>(entity);

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
            }
        }
    }
}
