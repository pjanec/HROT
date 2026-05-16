using System;
using System.Collections.Generic;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Evaluates the trigger condition of the current <see cref="MissionPhase"/> for every entity
    /// that has a <see cref="MissionPlanQueue"/> and a <see cref="BehaviorState"/>.
    /// When a trigger fires the system advances <see cref="MissionPlanQueue.CurrentPhase"/>,
    /// resets <see cref="MissionPlanQueue.PhaseElapsedSeconds"/>, and updates
    /// <see cref="BehaviorState.ActiveBehaviorHash"/> + increments
    /// <see cref="BehaviorState.InstanceId"/> so that <see cref="ChannelArbitrationSystem"/>
    /// preempts stale action channels naturally.
    /// <para>
    /// <b>Execution phase:</b> <see cref="SimulationSystemGroup"/>, before
    /// <see cref="ChannelArbitrationSystem"/> so the new behavior takes effect within the
    /// same frame.
    /// </para>
    /// <para>
    /// <b>One-frame activation delay (BD1-BATCH-02):</b>
    /// When a phase trigger fires this system publishes <see cref="AssignBehaviorHashEvent"/>
    /// to the event bus.  <see cref="BehaviorIngressSystem"/> — the sole owner of
    /// <see cref="BehaviorState"/> writes — runs in <see cref="InputSystemGroup"/>, which
    /// executes <em>before</em> <see cref="SimulationSystemGroup"/> each frame.  Therefore
    /// <c>BehaviorState.ActiveBehaviorHash</c> is updated on the frame <em>after</em> the
    /// trigger fires, and <see cref="ChannelArbitrationSystem"/> preempts stale action
    /// channels one tick later.
    /// This one-frame gap is by design: it preserves single-owner semantics for
    /// <see cref="BehaviorState"/> and ensures atomic blackboard parameter parsing in
    /// <see cref="BehaviorIngressSystem"/> (DEBT-035).  Callers that require same-frame
    /// behavior application (e.g. tests) must manually trigger
    /// <see cref="BehaviorIngressSystem.OnUpdate"/> after <see cref="MissionDirectorSystem"/>
    /// in their tick loop.
    /// </para>
    /// <para>
    /// <b>Supported triggers:</b>
    /// <list type="bullet">
    ///   <item><see cref="MissionTrigger.TimerElapsed"/> — accumulates delta time.</item>
    ///   <item><see cref="MissionTrigger.ReachedDestination"/> — delegated to the
    ///         <see cref="MissionTrigger.BehaviorFinished"/> path (BS1-T022); retained for
    ///         backward compatibility with serialised mission plans.</item>
    ///   <item><see cref="MissionTrigger.UnderAttack"/> — checks <c>TargetMemory</c> for entries
    ///         with ThreatScore &gt; 0.</item>
    ///   <item><see cref="MissionTrigger.HealthCritical"/> — fires when
    ///         <c>Health.Current / Health.Max</c> is &lt;= <c>TriggerParam</c>.  The entity must have
    ///         a <c>Health</c> component (from <c>FDP.Toolkit.Combat.Contracts</c>);
    ///         if absent the trigger never fires.
    ///         <b>BUG2-A001.</b></item>
    ///   <item><see cref="MissionTrigger.BehaviorFinished"/> — fires when a
    ///         <see cref="BehaviorFinishedEvent"/> is received for this entity, indicating
    ///         the behavior's BTree root evaluated to terminal (Success or Failure).</item>
    /// </list>
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    // [UpdateBefore(typeof(ChannelArbitrationSystem))] -- ordering maintained by array position in MissionControlModule.
    public class MissionDirectorSystem : IEcsModuleSystem
    {
        /// <summary>
        /// Entities for which a <see cref="BehaviorFinishedEvent"/> arrived this frame,
        /// built once per <see cref="OnUpdate"/> call to allow O(1) per-entity lookup.
        /// </summary>
        private readonly HashSet<int> _behaviorFinishedThisFrame = new();

        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(MissionDirectorSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            float dt = deltaTime;

            // -- Build BehaviorFinished lookup for this frame --
            // Consume all BehaviorFinishedEvents once, cache the entity indices, then
            // look them up in O(1) during the entity query loop below.
            _behaviorFinishedThisFrame.Clear();
            var behaviorFinishedEvents = repo.Bus.Read<BehaviorFinishedEvent>();
            foreach (var finishedEvt in behaviorFinishedEvents)
            {
                _behaviorFinishedThisFrame.Add(finishedEvt.Entity.Index);
            }

            var query = repo.Query()
                .With<MissionPlanQueue>()
                .With<BehaviorState>()
                .Build();

            foreach (var entity in query)
            {
                ref var queue = ref repo.GetComponentRW<MissionPlanQueue>(entity);
                var behavior  = repo.GetComponent<BehaviorState>(entity);

                // Mission complete — nothing left to do.
                if (queue.CurrentPhase >= queue.PhaseCount) continue;

                // Safe access to the inline Phases buffer: cast to Span to avoid the
                // C#/[InlineArray] defensive-copy trap when indexing a nested value-type.
                Span<MissionPhase> phases = queue.Phases;
                var phase = phases[queue.CurrentPhase];

                bool triggered = false;

                switch (phase.Trigger)
                {
                    case MissionTrigger.TimerElapsed:
                        queue.PhaseElapsedSeconds += dt;
                        if (queue.PhaseElapsedSeconds >= phase.TriggerParam)
                            triggered = true;
                        break;

#pragma warning disable CS0618 // intentional use of obsolete enum value for backward compat
                    case MissionTrigger.ReachedDestination:
                        // BS1-T022: Previously polled NavState.HasArrived (Muscle-tier physics
                        // component not available on a Brain node).  Now evaluated identically
                        // to BehaviorFinished so that MissionDirectorSystem remains CQRS-clean.
                        // The enum value is kept for backward compatibility with serialised
                        // mission plans; new UI code should emit BehaviorFinished instead.
#pragma warning restore CS0618
#pragma warning disable CS0618 // intentional use of obsolete enum value for backward compat
                        if (_behaviorFinishedThisFrame.Contains(entity.Index))
                            triggered = true;
#pragma warning restore CS0618
                        break;

                    case MissionTrigger.UnderAttack:
                        if (repo.HasComponent<TargetMemory>(entity))
                        {
                            ref readonly var mem = ref repo.GetComponentRO<TargetMemory>(entity);
                            for (int i = 0; i < mem.Count; i++)
                            {
                                if (mem.ThreatScores[i] > 0f)
                                {
                                    triggered = true;
                                    break;
                                }
                            }
                        }
                        break;

                    case MissionTrigger.HealthCritical:
                        // BUG2-A001: Read Health directly from FDP.Toolkit.Combat.Contracts.
                        // HealthData mirror (DEBT-033) is no longer needed.
                        if (repo.HasComponent<Health>(entity))
                        {
                            var h = repo.GetComponent<Health>(entity);
                            float fraction = h.Max > 0f ? h.Current / h.Max : 0f;
                            if (fraction <= phase.TriggerParam)
                                triggered = true;
                        }
                        break;

                    case MissionTrigger.BehaviorFinished:
                        // BehaviorFinishedEvent is consumed once per frame into
                        // _behaviorFinishedThisFrame (built at the top of OnUpdate),
                        // so this lookup is O(1).
                        if (_behaviorFinishedThisFrame.Contains(entity.Index))
                            triggered = true;
                        break;
                }

                if (triggered)
                {
                    queue.CurrentPhase++;
                    queue.PhaseElapsedSeconds = 0f;

                    if (queue.CurrentPhase < queue.PhaseCount)
                    {
                        // More phases remain: tell BehaviorIngressSystem which behavior to activate.
                        repo.Bus.Publish(new AssignBehaviorHashEvent
                        {
                            Entity       = entity,
                            BehaviorHash = phases[queue.CurrentPhase].BehaviorId,
                        });
                    }
                    else
                    {
                        // Plan exhausted: clear the active behavior so the entity goes brain-dead.
                        repo.Bus.Publish(new ClearBehaviorEvent { Entity = entity });
                    }
                }
            }
        }
    }
}
