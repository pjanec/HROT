using System;
using Fdp.Kernel;
using Fhsm.Kernel;
using FDP.Toolkit.Behavior.Components;

namespace FDP.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Minimal unmanaged context carried into FastHSM action delegates.
    /// Must be <c>unmanaged</c> to satisfy <see cref="HsmKernel.Update{TInstance,TContext}"/>'s
    /// generic constraint.  ECS world access from inside HSM action delegates should be
    /// wired through static service locators or separate command buffers (Phase 3+).
    /// </summary>
    public struct FdpHsmContext
    {
        /// <summary>The entity whose HSM brain is currently being stepped.</summary>
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

                // Stack-allocated context — zero heap allocation.
                var context = new FdpHsmContext { Self = entity };

                // sizeof(T) determines the tier (64 / 128 / 256) inside HsmKernelCore.
                HsmKernel.Update(def.HsmDefinition, ref component, context, DeltaTime);
            }
        }
    }
}
