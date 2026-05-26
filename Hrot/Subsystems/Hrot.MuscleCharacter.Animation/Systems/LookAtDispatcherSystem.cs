using System;
using Fdp.Core;
using Fbt;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Lifecycle.Events;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Executors;

namespace Hrot.MuscleCharacter.Animation.Systems
{
    /// <summary>
    /// Dispatcher system for LookAtChannel commands (ANC-P3-02, DD-1 §8).
    /// Runs in PreSimulation. Routes LookAtPoint, LookAtEntity, ReleaseLook commands.
    /// LookAtPoint and LookAtEntity require CanAim capability; ReleaseLook does not.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class LookAtDispatcherSystem : DispatcherSystemBase<LookAtChannel>
    {
        public LookAtDispatcherSystem(IAnimationBackend backend)
        {
            RegisterExecutor(LookAtActionIds.LookAtPoint, new LookAtPointExecutor(backend));
            RegisterExecutor(LookAtActionIds.LookAtEntity, new LookAtEntityExecutor(backend));
            RegisterExecutor(LookAtActionIds.ReleaseLook, new ReleaseLookExecutor(backend));
        }

        public override void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(LookAtDispatcherSystem)} requires direct EntityRepository access.");

            foreach (var evt in view.ReadEvents<DestructionOrder>())
            {
                if (repo.HasComponent<LookAtChannel>(evt.Entity))
                {
                    ref var ch = ref repo.GetComponentRW<LookAtChannel>(evt.Entity);
                    if (ch.ActiveAction != 0)
                    {
                        _executors[ch.ActiveAction]?.OnExit(evt.Entity, ref ch, repo);
                        ch.ActiveAction = 0;
                    }
                }
            }

            var q = repo.Query()
                .With<LookAtChannel>()
                .With<ActorCapabilityState>()
                .Build();

            foreach (var entity in q)
            {
                ref var channel = ref repo.GetComponentRW<LookAtChannel>(entity);
                var caps = repo.GetComponent<ActorCapabilityState>(entity);

                // ReleaseLook does not require CanAim; all other actions do
                bool requiresAim = channel.ActiveAction != LookAtActionIds.ReleaseLook;
                if (requiresAim && !caps.Capabilities.HasFlag(ActorCapabilities.CanAim))
                {
                    channel.Status = NodeStatus.Failure;
                    continue;
                }

                if (channel.ActionInstanceId != channel.DispatchedInstanceId)
                {
                    EnsurePreviousActionCapacity(entity.Index + 1);
                    ushort oldAction = _previousAction[entity.Index];

                    _executors[oldAction]?.OnExit(entity, ref channel, repo);
                    _executors[channel.ActiveAction]?.OnEnter(entity, ref channel, repo);

                    channel.DispatchedInstanceId = channel.ActionInstanceId;
                    _previousAction[entity.Index] = channel.ActiveAction;
                }

                if (channel.ActiveAction != 0 && channel.Status == NodeStatus.Running)
                {
                    _executors[channel.ActiveAction]?.Execute(entity, ref channel, repo, deltaTime);
                }
            }
        }
    }
}
