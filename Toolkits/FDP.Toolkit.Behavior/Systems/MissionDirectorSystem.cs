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
    ///   <item><see cref="MissionTrigger.HealthCritical"/> — not implemented in this phase:
    ///         <c>FDP.Toolkit.Behavior</c> cannot reference <c>FDP.Toolkit.Combat</c> without
    ///         creating a circular dependency (Combat already references Behavior). The trigger
    ///         will never fire until the combat health is exposed via a shared interface or
    ///         a generic component.</item>
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

                var phase = queue.Phases[queue.CurrentPhase];
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
                        // TODO (DEBT): FDP.Toolkit.Behavior cannot reference FDP.Toolkit.Combat
                        // (circular dependency). HealthCritical trigger requires combat Health
                        // component access and will be implemented when a shared health interface
                        // or a refactored assembly structure is available.
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
                        doctrine.ActiveDoctrineHash = queue.Phases[queue.CurrentPhase].DoctrineId;
                    }
                }
            }
        }
    }
}
