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
    public sealed class LookAtDispatcherSystem : DispatcherSystemBase<LookAtChannelWork>
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

            // ⭐ CE-513 / R-180 — this system writes ONLY LookAtChannelStatus (the Muscle's report). The Brain's LookAtChannel
            //   request is read, never written; executors run on a stack LookAtChannelWork and only its Report is stored.
            _tornDown.Clear();

            // Teardown: exit the action this dispatcher last entered and report it failed. ⛔ The request is NOT
            //   cleared any more (it belongs to the Brain); the Failure report is what stops the Running gate below.
            foreach (var evt in view.ReadEvents<DestructionOrder>())
            {
                if (!repo.HasComponent<LookAtChannel>(evt.Entity)) continue;
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
                .With<LookAtChannel>()
                .With<ActorCapabilityState>()
                .Build();

            foreach (var entity in q)
            {
                if (_tornDown.Contains(entity.Index)) continue;

                var work = Load(repo, entity);
                var before = work.Report;
                var caps = repo.GetComponent<ActorCapabilityState>(entity);

                if (work.Request.ActiveAction != LookAtActionIds.ReleaseLook && !caps.Capabilities.HasFlag(ActorCapabilities.CanAim))
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
        private static LookAtChannelWork Load(EntityRepository repo, Entity entity) => new LookAtChannelWork
        {
            Request = repo.GetComponent<LookAtChannel>(entity),
            Report  = repo.HasComponent<LookAtChannelStatus>(entity) ? repo.GetComponent<LookAtChannelStatus>(entity) : default,
        };

        /// <summary>Writes the report; adds the component on first use (a replica built from the request alone).</summary>
        private static void Store(EntityRepository repo, Entity entity, LookAtChannelStatus report)
        {
            if (repo.HasComponent<LookAtChannelStatus>(entity))
                repo.GetComponentRW<LookAtChannelStatus>(entity) = report;
            else
                repo.AddComponent(entity, report);
        }
    }
}
