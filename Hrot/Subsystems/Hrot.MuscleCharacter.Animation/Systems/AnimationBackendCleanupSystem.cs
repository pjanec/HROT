using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;

namespace Hrot.MuscleCharacter.Animation.Systems
{
    /// <summary>
    /// Cleans up backend resources when an entity is destroyed.
    /// Watches for entities with CharacterAnimationDefRuntime that are being destroyed.
    /// Calls backend.UnregisterEntity to release per-entity backend resources.
    /// Runs late in PostSimulation, after notify draining but before chunk reaper.
    /// (ANC-P3-08, DD-1 §14, §20.5, §17)
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

            // TODO (Phase 3 Part 2, DD-1 §20.5):
            // This system watches for PendingDestroy tagged entities with CharacterAnimationDefRuntime
            // and calls backend.UnregisterEntity to clean up backend resources before chunk reaper.
            // Implementation deferred pending PendingDestroy component availability in core engine.
            // 
            // When available, the implementation pattern will be:
            //   var q = repo.Query().With<PendingDestroy>().With<CharacterAnimationDefRuntime>().Build();
            //   foreach (var entity in q) {
            //       var def = repo.GetComponent<CharacterAnimationDefRuntime>(entity);
            //       var handle = new AnimationBackendHandle { ... };
            //       _backend.UnregisterEntity(handle);
            //   }
        }
    }
}
