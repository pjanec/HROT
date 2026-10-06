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
    public sealed class AnimationDispatcherSystem : DispatcherSystemBase<AnimationChannelWork>
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
            ForgetLastWorld(view);   // ⭐ CE-3076 — _previousAction is per entity INDEX, reused across worlds
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(AnimationDispatcherSystem)} requires direct EntityRepository access.");

            // ⭐ CE-513 / R-180 — this system writes ONLY AnimationChannelStatus (the Muscle's report). The Brain's AnimationChannel
            //   request is read, never written; executors run on a stack AnimationChannelWork and only its Report is stored.
            _tornDown.Clear();

            // Teardown: exit the action this dispatcher last entered and report it failed. ⛔ The request is NOT
            //   cleared any more (it belongs to the Brain); the Failure report is what stops the Running gate below.
            foreach (var evt in view.ReadEvents<DestructionOrder>())
            {
                if (!repo.HasComponent<AnimationChannel>(evt.Entity)) continue;
                int index = evt.Entity.Index;
                ushort entered = index < _previousAction.Length ? _previousAction[index] : (ushort)0;
                if (entered == 0) continue;

                var work = Load(repo, evt.Entity);
                _executors[entered]?.OnExit(evt.Entity, ref work, repo);
                work.Report.Status = NodeStatus.Failure;
                Store(repo, evt.Entity, work.Report);
                _previousAction[index] = 0;
                _tornDown.Add(index);
            }

            var q = repo.Query()
                .With<AnimationChannel>()
                .With<ActorCapabilityState>()
                .Build();

            foreach (var entity in q)
            {
                if (_tornDown.Contains(entity.Index)) continue;

                var work = Load(repo, entity);
                var before = work.Report;
                var caps = repo.GetComponent<ActorCapabilityState>(entity);

                if (!caps.Capabilities.HasFlag(ActorCapabilities.CanPlayAnimations))
                {
                    work.Report.Status = NodeStatus.Failure;
                }
                else
                {
                    if (work.Request.ActionInstanceId != work.Report.DispatchedInstanceId)
                    {
                        EnsurePreviousActionCapacity(entity.Index + 1);
                        ushort oldAction = _previousAction[entity.Index];

                        _executors[oldAction]?.OnExit(entity, ref work, repo);
                        _executors[work.Request.ActiveAction]?.OnEnter(entity, ref work, repo);

                        work.Report.DispatchedInstanceId = work.Request.ActionInstanceId;
                        _previousAction[entity.Index] = work.Request.ActiveAction;
                    }

                    if (work.Request.ActiveAction != 0 && work.Report.Status == NodeStatus.Running)
                    {
                        _executors[work.Request.ActiveAction]?.Execute(entity, ref work, repo, deltaTime);
                    }
                }

                if (work.Report.Status != before.Status || work.Report.DispatchedInstanceId != before.DispatchedInstanceId)
                    Store(repo, entity, work.Report);
            }
        }

        private readonly System.Collections.Generic.HashSet<int> _tornDown = new();

        /// <summary>The request (copied) and the current report — a missing report is the default one.</summary>
        private static AnimationChannelWork Load(EntityRepository repo, Entity entity) => new AnimationChannelWork
        {
            Request = repo.GetComponent<AnimationChannel>(entity),
            Report  = repo.HasComponent<AnimationChannelStatus>(entity) ? repo.GetComponent<AnimationChannelStatus>(entity) : default,
        };

        /// <summary>Writes the report; adds the component on first use (a replica built from the request alone).</summary>
        private static void Store(EntityRepository repo, Entity entity, AnimationChannelStatus report)
        {
            if (repo.HasComponent<AnimationChannelStatus>(entity))
                repo.GetComponentRW<AnimationChannelStatus>(entity) = report;
            else
                repo.AddComponent(entity, report);
        }
    }
}
