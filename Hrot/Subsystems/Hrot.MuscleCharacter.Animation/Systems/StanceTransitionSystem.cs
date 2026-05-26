using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;

namespace Hrot.MuscleCharacter.Animation.Systems
{
    /// <summary>
    /// Watches StanceIntent.Version vs StanceStatus.AckVersion and drives stance transitions
    /// via the animation backend (ANC-P3-03, DD-1 §9).
    /// Runs in PreSimulation.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class StanceTransitionSystem : IEcsModuleSystem
    {
        private readonly IAnimationBackend _backend;

        public StanceTransitionSystem(IAnimationBackend backend)
        {
            _backend = backend;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(StanceTransitionSystem)} requires direct EntityRepository access.");

            var q = repo.Query()
                .With<StanceIntent>()
                .With<StanceStatus>()
                .With<CharacterAnimationDefRuntime>()
                .With<ActorCapabilityState>()
                .Build();

            foreach (var entity in q)
            {
                ref var intent = ref repo.GetComponentRW<StanceIntent>(entity);
                ref var status = ref repo.GetComponentRW<StanceStatus>(entity);
                var caps = repo.GetComponent<ActorCapabilityState>(entity);

                if (intent.Version == status.AckVersion)
                    continue;

                // New version detected
                byte targetStance = (byte)intent.TargetStance;
                byte currentStance = (byte)status.CurrentStance;

                if (!caps.Capabilities.HasFlag(ActorCapabilities.CanChangeStance))
                {
                    // No capability: silently acknowledge without backend call
                    status.AckVersion = intent.Version;
                    continue;
                }

                if (targetStance == currentStance)
                {
                    // Same stance: immediately acknowledge
                    status.AckVersion = intent.Version;
                    status.Phase = StanceTransitionPhase.Locked;
                    continue;
                }

                // Different stance: start transition via backend
                var def = repo.GetComponent<CharacterAnimationDefRuntime>(entity);
                var handle = new AnimationBackendHandle
                {
                    Index = (uint)(def.BackendHandle & 0xFFFFFFFF),
                    Generation = (uint)((def.BackendHandle >> 32) & 0xFFFFFFFF),
                };

                _backend.RequestStanceChange(handle, targetStance, intent.BlendTime);

                status.AckVersion = intent.Version;
                status.Phase = StanceTransitionPhase.Transitioning;
                status.TransitionProgress = 0f;
            }
        }
    }
}
