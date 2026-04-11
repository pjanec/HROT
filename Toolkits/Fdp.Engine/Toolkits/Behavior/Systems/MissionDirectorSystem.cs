using System;
using System.Collections.Generic;
using Fdp.Kernel;
using Fbt;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Events;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Perception.Components;

namespace FDP.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Evaluates the trigger condition of the current <see cref="MissionPhase"/> for every entity
    /// that has a <see cref="MissionPlanQueue"/> and a <see cref="DoctrineState"/>.
    /// When a trigger fires the system advances <see cref="MissionPlanQueue.CurrentPhase"/>,
    /// resets <see cref="MissionPlanQueue.PhaseElapsedSeconds"/>, and updates
    /// <see cref="DoctrineState.ActiveDoctrineHash"/> + increments
    /// <see cref="DoctrineState.InstanceId"/> so that <see cref="ChannelArbitrationSystem"/>
    /// preempts stale action channels naturally.
    /// <para>
    /// <b>Execution phase:</b> <see cref="SimulationSystemGroup"/>, before
    /// <see cref="ChannelArbitrationSystem"/> so the new doctrine takes effect within the
    /// same frame.
    /// </para>
    /// <para>
    /// <b>One-frame activation delay (BD1-BATCH-02):</b>
    /// When a phase trigger fires this system publishes <see cref="AssignDoctrineHashEvent"/>
    /// to the event bus.  <see cref="DoctrineIngressSystem"/> — the sole owner of
    /// <see cref="DoctrineState"/> writes — runs in <see cref="InputSystemGroup"/>, which
    /// executes <em>before</em> <see cref="SimulationSystemGroup"/> each frame.  Therefore
    /// <c>DoctrineState.ActiveDoctrineHash</c> is updated on the frame <em>after</em> the
    /// trigger fires, and <see cref="ChannelArbitrationSystem"/> preempts stale action
    /// channels one tick later.
    /// This one-frame gap is by design: it preserves single-owner semantics for
    /// <see cref="DoctrineState"/> and ensures atomic blackboard parameter parsing in
    /// <see cref="DoctrineIngressSystem"/> (DEBT-035).  Callers that require same-frame
    /// doctrine application (e.g. tests) must manually trigger
    /// <see cref="DoctrineIngressSystem.OnUpdate"/> after <see cref="MissionDirectorSystem"/>
    /// in their tick loop.
    /// </para>
    /// <para>
    /// <b>Supported triggers:</b>
    /// <list type="bullet">
    ///   <item><see cref="MissionTrigger.TimerElapsed"/> — accumulates delta time.</item>
    ///   <item><see cref="MissionTrigger.ReachedDestination"/> — delegated to the
    ///         <see cref="MissionTrigger.DoctrineFinished"/> path (BS1-T022); retained for
    ///         backward compatibility with serialised mission plans.</item>
    ///   <item><see cref="MissionTrigger.UnderAttack"/> — checks <c>TargetMemory</c> for entries
    ///         with ThreatScore &gt; 0.</item>
    ///   <item><see cref="MissionTrigger.HealthCritical"/> — fires when
    ///         <c>Health.Current / Health.Max</c> is &lt;= <c>TriggerParam</c>.  The entity must have
    ///         a <c>Health</c> component (from <c>FDP.Toolkit.Combat.Contracts</c>);
    ///         if absent the trigger never fires.
    ///         <b>BUG2-A001.</b></item>
    ///   <item><see cref="MissionTrigger.DoctrineFinished"/> — fires when a
    ///         <see cref="DoctrineFinishedEvent"/> is received for this entity, indicating
    ///         the doctrine's BTree root evaluated to terminal (Success or Failure).</item>
    /// </list>
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ChannelArbitrationSystem))]
    public class MissionDirectorSystem : ComponentSystem
    {
        /// <summary>
        /// Entities for which a <see cref="DoctrineFinishedEvent"/> arrived this frame,
        /// built once per <see cref="OnUpdate"/> call to allow O(1) per-entity lookup.
        /// </summary>
        private readonly HashSet<int> _doctrineFinishedThisFrame = new();

        protected override unsafe void OnUpdate()
        {
            float dt = DeltaTime;

            // ── Build DoctrineFinished lookup for this frame ───────────────────────────
            // Consume all DoctrineFinishedEvents once, cache the entity indices, then
            // look them up in O(1) during the entity query loop below.
            _doctrineFinishedThisFrame.Clear();
            var doctrineFinishedEvents = World.Bus.Consume<DoctrineFinishedEvent>();
            foreach (var finishedEvt in doctrineFinishedEvents)
            {
                _doctrineFinishedThisFrame.Add(finishedEvt.Entity.Index);
            }

            var query = World.Query()
                .With<MissionPlanQueue>()
                .With<DoctrineState>()
                .Build();

            foreach (var entity in query)
            {
                ref var queue = ref World.GetComponentRW<MissionPlanQueue>(entity);
                var doctrine  = World.GetComponent<DoctrineState>(entity);

                // Mission complete — nothing left to do.
                if (queue.CurrentPhase >= queue.PhaseCount) continue;

                // Safe access to the inline Phases buffer: cast to Span to avoid the
                // C#/[InlineArray] defensive-copy trap when indexing a nested value-type.
                Span<MissionPhase> phases = queue.Phases;
                var phase = phases[queue.CurrentPhase];

                // Activate the current phase's doctrine if it hasn't been activated yet.
                // Delegate the write to DoctrineIngressSystem via the event bus so that
                // DoctrineState has a single owner.
                if (doctrine.ActiveDoctrineHash != phase.DoctrineId)
                {
                    World.Bus.Publish(new AssignDoctrineHashEvent
                    {
                        Entity      = entity,
                        DoctrineHash = phase.DoctrineId,
                    });
                }

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
                        // to DoctrineFinished so that MissionDirectorSystem remains CQRS-clean.
                        // The enum value is kept for backward compatibility with serialised
                        // mission plans; new UI code should emit DoctrineFinished instead.
#pragma warning restore CS0618
#pragma warning disable CS0618 // intentional use of obsolete enum value for backward compat
                        if (_doctrineFinishedThisFrame.Contains(entity.Index))
                            triggered = true;
#pragma warning restore CS0618
                        break;

                    case MissionTrigger.UnderAttack:
                        if (World.HasComponent<TargetMemory>(entity))
                        {
                            ref readonly var mem = ref World.GetComponentRO<TargetMemory>(entity);
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
                        if (World.HasComponent<Health>(entity))
                        {
                            var h = World.GetComponent<Health>(entity);
                            float fraction = h.Max > 0f ? h.Current / h.Max : 0f;
                            if (fraction <= phase.TriggerParam)
                                triggered = true;
                        }
                        break;

                    case MissionTrigger.DoctrineFinished:
                        // DoctrineFinishedEvent is consumed once per frame into
                        // _doctrineFinishedThisFrame (built at the top of OnUpdate),
                        // so this lookup is O(1).
                        if (_doctrineFinishedThisFrame.Contains(entity.Index))
                            triggered = true;
                        break;
                }

                if (triggered)
                {
                    queue.CurrentPhase++;
                    queue.PhaseElapsedSeconds = 0f;

                    // Load the next phase's doctrine if there is one.
                    if (queue.CurrentPhase < queue.PhaseCount)
                    {
                        // Use the NEW phase index — `phase` still refers to the old slot.
                        // Delegate the write to DoctrineIngressSystem via the event bus.
                        World.Bus.Publish(new AssignDoctrineHashEvent
                        {
                            Entity       = entity,
                            DoctrineHash = phases[queue.CurrentPhase].DoctrineId,
                        });
                    }
                    else
                    {
                        // Plan exhausted — delegate doctrine teardown via the event bus
                        // so DoctrineIngressSystem (the sole owner of DoctrineState writes)
                        // performs the brain-death reset. Do NOT mutate DoctrineState here.
                        World.Bus.Publish(new ClearDoctrineEvent { Entity = entity });
                    }
                }
            }
        }
    }
}
