using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.ScenarioRules;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐ CE-2383 (T2; docs/DESIGN_Evaluation_Cost_And_Cadence.md §3.2–§3.4, R-2083) — THE RULES' CADENCE POLICY on the generic brain cadence
    /// (<see cref="CadenceGate"/>, CE-2382). After each tick of a rule it reads the status the tick left and sets WHEN the rule ticks next:
    /// <list type="bullet">
    /// <item>closed / final ⇒ asleep until woken (a reset, an edit, an operator request);</item>
    /// <item>a trigger <c>Acting</c> ⇒ every frame (its <c>do</c> list waits on one-frame acknowledgements, <c>CommandVerbs.cs:152</c>);</item>
    /// <item>otherwise ⇒ its next staggered 5 Hz tick (D1), or its own <c>ExpectedAt</c> if that comes first (a trigger's delay end, its holdFor end,
    /// a cooldown end, a deadline) — so a known time is hit exactly, never rounded to the period.</item>
    /// </list>
    /// And it WAKES THE PARENT when the rule's State / Value / Score / Closed / TriggerState changed (§3.4), so a change climbs a tree one frame per level.
    /// ⭐ The rule kinds need no code of their own: everything is read from the shared <c>ObjectiveStatus</c>. Events: each rule reads them through its
    /// own window (the gate), so a kill on a frame it skipped is counted at its next tick.
    /// </summary>
    public static class RuleCadence
    {
        /// <summary>The rules' id in <see cref="CadencePolicies"/>.</summary>
        public const byte PolicyId = 1;

        static RuleCadence() => CadencePolicies.Register(PolicyId, AfterTick);

        private readonly record struct Seen(ObjectiveState State, float Value, float Score, byte Closed, TriggerState Trigger);
        private static readonly ConditionalWeakTable<EntityRepository, Dictionary<int, Seen>> s_seen = new();

        /// <summary>Called by <see cref="ObjectiveHost.Enter"/> on every rule tick: the rule's task slot is ours (one read when it already is).</summary>
        public static void Own(EntityRepository world, Entity self)
        {
            if (!world.HasComponent<BehaviorState>(self) || world.GetComponentRO<BehaviorState>(self).CadencePolicy == PolicyId) return;
            world.GetComponentRW<BehaviorState>(self).CadencePolicy = PolicyId;
        }

        /// <summary>Tick <paramref name="rule"/> at the next frame, whatever its cadence (a no-op for anything that is not a brain).</summary>
        public static void Wake(EntityRepository world, Entity rule)
        {
            if (rule.IsNull || !world.IsAlive(rule) || !world.HasComponent<BehaviorState>(rule)) return;
            if (world.GetComponentRO<BehaviorState>(rule).CadenceWake == 0) world.GetComponentRW<BehaviorState>(rule).CadenceWake = 1;
        }

        /// <summary>The mode and next tick for a rule whose status is <paramref name="s"/>, at <paramref name="now"/> (pure — a rail asks it directly).</summary>
        public static (CadenceMode Mode, double Next) ModeOf(in ObjectiveStatus s, double now, int entityIndex)
        {
            if (s.Closed != 0 || s.IsFinal) return (CadenceMode.Asleep, CadenceGate.Never);
            if (s.Role == ObjectiveRole.Trigger && s.TriggerState == TriggerState.Acting) return (CadenceMode.EveryFrame, 0);
            double next = CadenceGate.Next(now, CadenceGate.DefaultPeriod, entityIndex);
            if (s.ExpectedAt > now && s.ExpectedAt < next) next = s.ExpectedAt;
            if (s.Role == ObjectiveRole.Trigger && s.TriggerState is TriggerState.Waiting or TriggerState.Cooldown)
                next = s.ExpectedAt > now ? s.ExpectedAt : now;   // nothing to sample while it waits: its own time only
            return (CadenceMode.Asleep, next);   // a TIMED sleep: the gate keeps its event window (CadenceGate.KeepsWindow)
        }

        private static void AfterTick(EntityRepository world, Entity self, double now)
        {
            if (!world.HasComponent<ObjectiveStatus>(self)) return;
            ref readonly var s = ref world.GetComponentRO<ObjectiveStatus>(self);
            var seen = s_seen.GetValue(world, static _ => new Dictionary<int, Seen>());
            var snap = new Seen(s.State, s.Value, s.Score, s.Closed, s.TriggerState);
            if (!seen.TryGetValue(self.Index, out var before) || before != snap)
            {
                seen[self.Index] = snap;
                if (world.HasComponent<ObjectiveLinks>(self)) Wake(world, world.GetComponentRO<ObjectiveLinks>(self).Parent.Resolve(world));
            }
            var (mode, next) = ModeOf(in s, now, self.Index);
            ref readonly var b = ref world.GetComponentRO<BehaviorState>(self);
            if (b.Cadence == mode && (mode != CadenceMode.Asleep || b.CadenceNextTick == next)) return;   // unchanged: no write, no chunk bump
            ref var w = ref world.GetComponentRW<BehaviorState>(self);
            w.Cadence = mode;
            if (mode == CadenceMode.Asleep) w.CadenceNextTick = next;
        }
    }

    /// <summary>
    /// ⭐ CE-2383 (T2; §7a S4) — wakes rules for what happens OUTSIDE their own tick: an operator's request (<see cref="RuleControl"/> with a <c>Seq</c>
    /// not yet <c>Done</c> — Fire / Rearm / Reset / Activate / Disarm, applied by the rule's next tick) and the exercise phase changing (closing ⇒ every rule
    /// settles at once, SR-116). Registered by <c>CgfLogicPack</c> before the brain tick. A replica holds no rule brain and is not ticked.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class RuleWakeSystem : IEcsModuleSystem
    {
        private ExercisePhase _phase = ExercisePhase.Running;
        private bool _phaseKnown;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<ObjectiveStatus>() || !repo.IsComponentTypeRegistered<BehaviorState>()) return;
            var phase = ExerciseResult.PhaseOf(repo);
            bool all = _phaseKnown && phase != _phase;
            _phase = phase; _phaseKnown = true;
            bool control = repo.IsComponentTypeRegistered<RuleControl>();
            if (!all && !control) return;
            var rules = RuleTree.Of(repo).All;
            for (int i = 0; i < rules.Count; i++)
            {
                var e = rules[i];
                if (!repo.IsAlive(e)) continue;
                if (all) { RuleCadence.Wake(repo, e); continue; }
                if (repo.HasComponent<RuleControl>(e))
                {
                    ref readonly var c = ref repo.GetComponentRO<RuleControl>(e);
                    if (c.Seq != c.Done) RuleCadence.Wake(repo, e);
                }
            }
        }
    }
}
