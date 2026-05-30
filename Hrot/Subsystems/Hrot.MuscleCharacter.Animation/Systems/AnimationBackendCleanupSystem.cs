using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle.Events;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;

namespace Hrot.MuscleCharacter.Animation.Systems
{
    /// <summary>
    /// Cleans up backend resources when an entity is destroyed.
    /// Watches for <see cref="DestructionOrder"/> events targeting entities with
    /// <see cref="CharacterAnimationDefRuntime"/> and calls
    /// <see cref="IAnimationBackend.UnregisterEntity(AnimationBackendHandle)"/>
    /// to release per-entity backend resources. Runs late in PostSimulation, after
    /// notify draining but before chunk reaper.
    /// (ANC-P3-08, DD-1 §14, §20.5, §17)
    ///
    /// <para>
    /// Implementation note: DD-1 §20.5 originally specified a <c>PendingDestroy</c>
    /// tag-component pattern, but the v239 engine uses the lifecycle event
    /// <see cref="DestructionOrder"/> (matching <c>LocomotionDispatcherSystem</c> /
    /// <see cref="AnimationDispatcherSystem"/>) — the 1-frame ELM delay guarantees
    /// the entity and its components are still intact when this system runs.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class AnimationBackendCleanupSystem : IEcsModuleSystem
    {
        private readonly IAnimationBackend _backend;

        public AnimationBackendCleanupSystem(IAnimationBackend backend)
        {
            _backend = backend;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(AnimationBackendCleanupSystem)} requires direct EntityRepository access.");

            foreach (var evt in view.ReadEvents<DestructionOrder>())
            {
                if (!repo.HasComponent<CharacterAnimationDefRuntime>(evt.Entity))
                    continue;

                var def = repo.GetComponent<CharacterAnimationDefRuntime>(evt.Entity);
                var handle = new AnimationBackendHandle
                {
                    Index      = (uint)(def.BackendHandle & 0xFFFFFFFF),
                    Generation = (uint)((def.BackendHandle >> 32) & 0xFFFFFFFF),
                };

                // Idempotent: backends ignore stale/unknown handles via generation check.
                _backend.UnregisterEntity(handle);
            }
        }
    }
}
