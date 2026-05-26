using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Events;

namespace Hrot.MuscleCharacter.Animation.Systems
{
    /// <summary>
    /// Drains per-entity notify events from the animation backend after Tick and publishes
    /// typed AnimNotifyEvent for each one. Runs in PostSimulation after the bridge system.
    /// (ANC-P7-09, DD-1 §11, §17)
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class NotifyEventEmitterSystem : IEcsModuleSystem
    {
        private readonly IAnimationBackend _backend;

        public NotifyEventEmitterSystem(IAnimationBackend backend)
        {
            _backend = backend;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(NotifyEventEmitterSystem)} requires direct EntityRepository access.");

            var query = repo.Query().With<CharacterAnimationDefRuntime>().Build();

            Span<RawNotifyEvent> buf = stackalloc RawNotifyEvent[16];

            foreach (var entity in query)
            {
                var def = repo.GetComponent<CharacterAnimationDefRuntime>(entity);

                // Skip if not registered: high 32 bits of handle == 0 means unregistered
                if ((def.BackendHandle >> 32) == 0)
                    continue;

                var handle = new AnimationBackendHandle
                {
                    Index = (uint)(def.BackendHandle & 0xFFFFFFFF),
                    Generation = (uint)((def.BackendHandle >> 32) & 0xFFFFFFFF),
                };

                int count = _backend.DrainNotifies(handle, buf);

                for (int i = 0; i < count; i++)
                {
                    ref readonly var n = ref buf[i];
                    repo.Bus.Publish(new AnimNotifyEvent(
                        target: entity,
                        montageId: (int)n.PayloadUint,
                        markerHash: n.MarkerHash,
                        payloadFloat: n.PayloadFloat));
                }
            }
        }
    }
}
