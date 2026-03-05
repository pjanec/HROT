using System;
using CarKinem.Core;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
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
    /// <b>Supported triggers:</b>
    /// <list type="bullet">
    ///   <item><see cref="MissionTrigger.TimerElapsed"/> — accumulates delta time.</item>
    ///   <item><see cref="MissionTrigger.ReachedDestination"/> — checks <c>NavState.HasArrived</c>.</item>
    ///   <item><see cref="MissionTrigger.UnderAttack"/> — checks <c>TargetMemory</c> for entries
    ///         with ThreatScore &gt; 0.</item>
    ///   <item><see cref="MissionTrigger.HealthCritical"/> — fires when
    ///         <c>HealthData.Fraction</c> (a <c>Fdp.Kernel</c> mirror written by
    ///         <c>DamageSystem</c>) is &lt;= <c>TriggerParam</c>.  The entity must have
    ///         a <c>HealthData</c> component; if absent the trigger never fires.
    ///         <b>DEBT-033 resolved.</b></item>
    /// </list>
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ChannelArbitrationSystem))]
    public class MissionDirectorSystem : ComponentSystem
    {
        protected override unsafe void OnUpdate()
        {
            float dt = DeltaTime;

            var query = World.Query()
                .With<MissionPlanQueue>()
                .With<DoctrineState>()
                .Build();

            foreach (var entity in query)
            {
                ref var queue   = ref World.GetComponentRW<MissionPlanQueue>(entity);
                ref var doctrine = ref World.GetComponentRW<DoctrineState>(entity);

                // Mission complete — nothing left to do.
                if (queue.CurrentPhase >= queue.PhaseCount) continue;

                // Safe access to the inline Phases buffer: cast to Span to avoid the
                // C#/[InlineArray] defensive-copy trap when indexing a nested value-type.
                Span<MissionPhase> phases = queue.Phases;
                var phase = phases[queue.CurrentPhase];

                // Activate the current phase's doctrine if it hasn't been activated yet.
                // This handles the initial assignment of a mission (phase 0 was never
                // set by a trigger transition) and prevents the entity from staying idle
                // when a single-phase mission has TriggerParam = float.MaxValue.
                if (doctrine.ActiveDoctrineHash != phase.DoctrineId)
                {
                    unchecked { doctrine.InstanceId++; }
                    doctrine.ActiveDoctrineHash = phase.DoctrineId;
                }

                bool triggered = false;

                switch (phase.Trigger)
                {
                    case MissionTrigger.TimerElapsed:
                        queue.PhaseElapsedSeconds += dt;
                        if (queue.PhaseElapsedSeconds >= phase.TriggerParam)
                            triggered = true;
                        break;

                    case MissionTrigger.ReachedDestination:
                        if (World.HasComponent<NavState>(entity))
                        {
                            var nav = World.GetComponent<NavState>(entity);
                            if (nav.HasArrived == 1)
                                triggered = true;
                        }
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
                        // DEBT-033 resolved: HealthData (Fdp.Kernel) is written by
                        // DamageSystem each frame after damage is applied, so this
                        // assembly does not need to reference FDP.Toolkit.Combat.
                        if (World.HasComponent<HealthData>(entity))
                        {
                            var hd = World.GetComponent<HealthData>(entity);
                            if (hd.Fraction <= phase.TriggerParam)
                                triggered = true;
                        }
                        break;
                }

                if (triggered)
                {
                    queue.CurrentPhase++;
                    queue.PhaseElapsedSeconds = 0f;

                    // Load the next phase's doctrine if there is one.
                    if (queue.CurrentPhase < queue.PhaseCount)
                    {
                        // Increment InstanceId so ChannelArbitrationSystem preempts stale channels.
                        unchecked { doctrine.InstanceId++; }
                        doctrine.ActiveDoctrineHash = phase.DoctrineId;
                    }
                }
            }
        }
    }
}
