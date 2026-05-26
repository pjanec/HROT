using System;
using Fdp.Core;
using Fbt;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Lifecycle.Events;
using Hrot.MuscleCharacter.Animation.Baking;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Executors;

namespace Hrot.MuscleCharacter.Animation.Systems
{
    /// <summary>
    /// Dispatcher system for AnimationChannel commands (ANC-P3-01, DD-1 §6).
    /// Runs in PreSimulation. Routes PlayMontage, StopMontage, PlayMontageQueue commands
    /// to their executors after capability checking.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class AnimationDispatcherSystem : DispatcherSystemBase<AnimationChannel>
    {
        public AnimationDispatcherSystem(
            IAnimationBackend backend,
            BakedAnimationCache cache)
        {
            RegisterExecutor(
                AnimationActionIds.PlayMontage,
                new PlayMontageExecutor(backend, cache));

            RegisterExecutor(
                AnimationActionIds.StopMontage,
                new StopMontageExecutor(backend));

            RegisterExecutor(
                AnimationActionIds.PlayMontageQueue,
                new PlayMontageQueueExecutor(backend, cache));

            RegisterExecutor(
                AnimationActionIds.EnqueueMontage,
                new EnqueueExecutor(cache));

            RegisterExecutor(
                AnimationActionIds.ClearMontageQueue,
                new ClearQueueExecutor());
        }

        public override void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(AnimationDispatcherSystem)} requires direct EntityRepository access.");

            // Clean up entities entering teardown
            foreach (var evt in view.ReadEvents<DestructionOrder>())
            {
                if (repo.HasComponent<AnimationChannel>(evt.Entity))
                {
                    ref var ch = ref repo.GetComponentRW<AnimationChannel>(evt.Entity);
                    if (ch.ActiveAction != 0)
                    {
                        _executors[ch.ActiveAction]?.OnExit(evt.Entity, ref ch, repo);
                        ch.ActiveAction = 0;
                    }
                }
            }

            var q = repo.Query()
                .With<AnimationChannel>()
                .With<ActorCapabilityState>()
                .Build();

            foreach (var entity in q)
            {
                ref var channel = ref repo.GetComponentRW<AnimationChannel>(entity);
                var caps = repo.GetComponent<ActorCapabilityState>(entity);

                if (!caps.Capabilities.HasFlag(ActorCapabilities.CanPlayAnimations))
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
