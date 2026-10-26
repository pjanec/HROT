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
    [TickMode(TickPolicy.WhenTimeAdvances)]
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
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // ⭐ CE-3329 P2 — a replay: the recording owns the world (DESIGN_Replay_Time_Axis_And_Review §12l)
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
                var work = Load(repo, evt.Entity);
                ushort entered = work.Report.DispatchedAction;
                if (entered == 0) continue;

                _executors[entered]?.OnExit(evt.Entity, ref work, repo);
                work.Report.Status = NodeStatus.Failure;
                work.Report.DispatchedAction = 0;
                Store(repo, evt.Entity, work.Report);
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
                        ushort oldAction = work.Report.DispatchedAction;

                        _executors[oldAction]?.OnExit(entity, ref work, repo);
                        _executors[work.Request.ActiveAction]?.OnEnter(entity, ref work, repo);

                        work.Report.DispatchedInstanceId = work.Request.ActionInstanceId;
                        work.Report.DispatchedAction = work.Request.ActiveAction;
                    }

                    if (work.Request.ActiveAction != 0 && work.Report.Status == NodeStatus.Running)
                    {
                        _executors[work.Request.ActiveAction]?.Execute(entity, ref work, repo, deltaTime);
                    }
                }

                if (work.Report.Status != before.Status || work.Report.DispatchedInstanceId != before.DispatchedInstanceId
                    || work.Report.DispatchedAction != before.DispatchedAction)
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
