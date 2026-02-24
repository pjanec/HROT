using System;
using Fdp.Kernel;
using Fhsm.Kernel;
using FDP.Toolkit.Behavior.Components;

namespace FDP.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Context struct passed to FastHSM action delegates via <see cref="HsmTickSystem{T}"/>.
    ///
    /// <b>DEBT-007 fix:</b> Added <see cref="World"/> field so that action delegates
    /// can read and write ECS components without ambient state or thread-locals.
    ///
    /// <b>Constraint note (DEBT-007 Q1):</b>
    /// <see cref="HsmKernel.Update{TInstance,TContext}"/> requires
    /// <c>where TContext : unmanaged</c>.  Because <see cref="EntityRepository"/>
    /// is a reference type, <c>FdpHsmContext</c> can no longer satisfy that constraint
    /// directly.  <see cref="HsmTickSystem{T}"/> therefore uses a thin internal
    /// <see cref="HsmKernelBridge"/> (unmanaged) for the kernel call, while
    /// <c>FdpHsmContext</c> remains the user-facing context available to action delegates
    /// via the <c>HsmTickSystem</c>'s stored reference (Phase 3+ wiring).
    /// Option C (static/thread-local) was explicitly rejected.
    /// </summary>
    public struct FdpHsmContext
    {
        /// <summary>The entity whose HSM brain is currently being stepped.</summary>
        public Entity Self;

        /// <summary>
        /// ECS world — allows HSM action delegates to read and write components.
        /// Populated by <see cref="HsmTickSystem{T}"/> before each entity tick.
        /// </summary>
        public EntityRepository World;
    }

    /// <summary>
    /// Minimal unmanaged bridge passed to <see cref="HsmKernel.Update{TInstance,TContext}"/>.
    /// Must satisfy <c>where TContext : unmanaged</c> — cannot hold managed references.
    /// </summary>
    internal struct HsmKernelBridge
    {
        public Entity Self;
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
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ChannelArbitrationSystem))]
    public class HsmTickSystem<T> : ComponentSystem
        where T : unmanaged
    {
        private readonly DoctrineRegistry _registry;

        public HsmTickSystem(DoctrineRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        protected override void OnUpdate()
        {
            var q = World.Query()
                .With<DoctrineState>()
                .With<T>()
                .Build();

            foreach (var entity in q)
            {
                var doctrine = World.GetComponent<DoctrineState>(entity);

                // Only process HSM-tier entities.
                if (doctrine.BrainTier != BehaviorConstants.BrainTierHsm)
                    continue;

                // Skip if doctrine is unknown or has no HSM definition.
                if (!_registry.TryGetDefinition(doctrine.ActiveDoctrineHash, out var def)
                    || def.HsmDefinition == null)
                    continue;

                ref var component = ref World.GetComponentRW<T>(entity);

                // DEBT-007: populate FdpHsmContext with ECS World for action delegate access.
                // HsmKernelBridge (unmanaged) is used for the HsmKernel.Update call since
                // FdpHsmContext can no longer satisfy 'where TContext : unmanaged'.
                var fdpContext = new FdpHsmContext { Self = entity, World = World };
                var bridge = new HsmKernelBridge { Self = fdpContext.Self };

                // sizeof(T) determines the tier (64 / 128 / 256) inside HsmKernelCore.
                HsmKernel.Update(def.HsmDefinition, ref component, bridge, DeltaTime);
            }
        }
    }
}
