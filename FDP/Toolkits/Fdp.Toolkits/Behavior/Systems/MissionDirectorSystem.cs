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
    /// Evaluates the trigger condition of the current <see cref="MissionTaskSlot"/> for every entity
    /// that has a <see cref="MissionPlanQueue"/> and a <see cref="BehaviorState"/>.
    /// When a trigger fires the system advances <see cref="MissionPlanQueue.CurrentTask"/>,
    /// resets <see cref="MissionPlanQueue.TaskElapsedSeconds"/>, and updates
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
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public class MissionDirectorSystem : IEcsModuleSystem
    {
        /// <summary>
        /// ⭐⭐⭐ <c>P3</c> step <c>3b</c> — the EXECUTION gate; see
        /// <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.5. ⚠ Defaults to <c>false</c>: a promoted
        /// ghost owns nothing until a policy or grant says otherwise, so enabling it before the node has
        /// a policy would stop it processing every entity it did not create.
        /// </summary>
        private readonly bool _gateOnAuthority;

        public MissionDirectorSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        /// <summary>⭐ <c>CE-483</c>: how each entity's run ended this frame (entity index ⇒ outcome).</summary>
        private readonly Dictionary<int, BehaviorOutcome> _outcomeThisFrame = new();

        /// <summary>⭐ <c>CE-4298</c> — each task's compiled then / give-up (D3).</summary>
        private readonly PlanConditionCache _conditions = new();

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
            _outcomeThisFrame.Clear();
            var behaviorFinishedEvents = repo.ReadEvents<BehaviorFinishedEvent>();
            foreach (var finishedEvt in behaviorFinishedEvents)
            {
                // ⭐ CE-2078 — a REACTION ending is not the phase's task ending (the paused task restarts after it).
                if (BehaviorOriginRank.Pauses(finishedEvt.Origin)) continue;   // ⭐ CE-3172 — nor is a possession ending
                // A fault outranks any other end reported for the same entity in the same frame.
                if (!_outcomeThisFrame.TryGetValue(finishedEvt.Entity.Index, out var seen) || seen != BehaviorOutcome.Faulted)
                    _outcomeThisFrame[finishedEvt.Entity.Index] = finishedEvt.Outcome;
            }

            // ⭐ P3 step 3b — advances mission phases (GetComponentRW<MissionPlanQueue>), which is
            //   Brain work: two nodes advancing one mission would double-step it. Gated on
            //   BehaviorState, which this query already required.
            var query = repo.Query()
                .With<MissionPlanQueue>()
                .WithOwnedWhen<BehaviorState>(_gateOnAuthority)
                .Build();

            foreach (var entity in query)
            {
                // ⭐ The parallel experiment, E-4 (M-1: MissionPlanQueue 99 % unchanged dirty chunks) — the early-outs read; the queue is taken
                //   writable only for a plan that can advance.
                ref readonly var peek = ref repo.GetComponentRO<MissionPlanQueue>(entity);

                // Mission complete — nothing left to do.
                if (peek.CurrentTask >= peek.TaskCount) continue;

                // ⭐ CE-483 — a halted plan waits for an operator command (a jump or a new plan). 📄
                //   DESIGN_Behaviour_Fault_And_Teardown.md §1 D3.
                if (peek.Halted != 0) continue;

                ref var queue = ref repo.GetComponentRW<MissionPlanQueue>(entity);
                var behavior  = repo.GetComponent<BehaviorState>(entity);

                // Safe access to the inline Phases buffer: cast to Span to avoid the
                // C#/[InlineArray] defensive-copy trap when indexing a nested value-type.
                Span<MissionTaskSlot> phases = queue.Tasks;
                Span<MissionPhaseOutcome> outcomes = queue.Outcomes;
                var phase = phases[queue.CurrentTask];

                // ⭐ CE-483 — record how this phase's run ended: Succeeded ⇒ Done · Failed ⇒ Failed, and the plan goes on (3a) ·
                //   Faulted ⇒ Failed AND HALT — the run told us it could not do its job; ⛔ do not quietly start the next phase.
                if (_outcomeThisFrame.TryGetValue(entity.Index, out var outcome))
                {
                    outcomes[queue.CurrentTask] = outcome == BehaviorOutcome.Succeeded ? MissionPhaseOutcome.Done : MissionPhaseOutcome.Failed;
                    if (outcome == BehaviorOutcome.Faulted)
                    {
                        queue.Halted = 1;
                        continue;
                    }
                }

                // ⭐⭐ CE-4298 (mission plan D3/D4, R-4160) — the task's then and give-up are ONE shared condition each (PlanConditionCache: a stored
                //   condition, or the old trigger type translated to the condition it means), compiled once, evaluated here with no allocation. The
                //   give-up is asked FIRST: it ends the task Failed. ⛔ SUPERSEDED: the switch over five built-in trigger kinds.
                queue.TaskElapsedSeconds += dt;   // every task's elapsed time ("after N s" reads it, whatever its condition)
                _conditions.Get(repo, entity, queue.CurrentTask, phase, out var then, out var giveUp);
                bool gaveUp = giveUp != null && giveUp(repo, entity);
                bool triggered = gaveUp || then(repo, entity);
                if (gaveUp) outcomes[queue.CurrentTask] = MissionPhaseOutcome.Failed;

                if (triggered)
                {
                    // A phase advanced by its trigger (a timer, a health threshold) while nothing reported an end ⇒ Done.
                    if (outcomes[queue.CurrentTask] == MissionPhaseOutcome.None)
                        outcomes[queue.CurrentTask] = MissionPhaseOutcome.Done;
                    queue.CurrentTask++;
                    queue.TaskElapsedSeconds = 0f;

                    // ⭐⭐ CE-4298 (D2) — the director only ADVANCES a named plan: the adapter starts the next task by name (and clears at the end, and
                    //   for a Wait task) — ⛔ never both. Only a RAW queue (no plan beside it) is started here, by its stored id. MissionTaskStart.ByName.
                    if (MissionTaskStart.ByName(repo, entity, queue.CurrentTask - 1)) { }
                    else if (queue.CurrentTask < queue.TaskCount)
                    {
                        // More phases remain: tell BehaviorIngressSystem which behavior to activate.
                        repo.Bus.Publish(new AssignBehaviorHashEvent
                        {
                            Entity       = entity,
                            BehaviorHash = phases[queue.CurrentTask].BehaviorId,
                            Origin       = Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Superior,   // ⭐ CE-3034 — a mission plan (R-193)
                        });
                    }
                    else
                    {
                        // Plan exhausted: clear the active behavior so the entity goes brain-dead.
                        repo.Bus.Publish(new ClearBehaviorEvent { Entity = entity, Origin = Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Superior });   // ⭐ CE-3034
                    }
                }
            }
        }
    }
}
