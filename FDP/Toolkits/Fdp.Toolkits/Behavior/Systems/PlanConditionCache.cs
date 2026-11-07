using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-4298</c> — mission plan D3 (📄 <c>docs/UX/DESIGN_Mission_Plan_Editor.md</c> §5.2–§5.4, R-4160): every task's <i>then</i> and
    /// <i>give up</i> is ONE shared condition (the condition language), compiled once and evaluated by the director each frame with no allocation.
    /// <list type="bullet">
    /// <item>a <c>Then</c> / <c>GiveUp</c> entry of the saved plan holds its condition JSON — compiled through <see cref="ConditionCache"/>
    /// (the one compiled-condition cache, shared with the trigger rules);</item>
    /// <item>an OLD trigger type (the five names files carry, or a raw queue's slot) is TRANSLATED to the condition it means — <see cref="Translate"/>,
    /// the one translation. Files load unchanged.</item>
    /// </list>
    /// ⭐ Per entity it remembers what each slot was compiled FROM (the plan object, the slot's kind and parameter); a new plan or a changed slot
    /// recompiles that slot — the steady frame is a dictionary hit and a reference compare. Brain-node state, never recorded or replicated: a world
    /// restore at worst recompiles.
    /// </summary>
    public sealed class PlanConditionCache
    {
        private sealed class Slot
        {
            public object? Plan;
            public MissionTrigger Kind;
            public float Param;
            public bool Built;
            public Func<EntityRepository, Entity, bool>? Then;
            public Func<EntityRepository, Entity, bool>? GiveUp;
        }

        private sealed class Entry
        {
            public int Generation;
            public readonly Slot[] Slots = NewSlots();
            private static Slot[] NewSlots()
            {
                var s = new Slot[MissionPlanQueue.MaxPhases];
                for (int i = 0; i < s.Length; i++) s[i] = new Slot();
                return s;
            }
        }

        private readonly Dictionary<int, Entry> _byEntity = new();

        static PlanConditionCache()
        {
            // the translated presets read standard inputs (HealthFraction) — registered once, so a node with no utility system still evaluates them
            Fdp.Toolkit.Utility.StandardInputs.RegisterAll();
        }

        /// <summary>
        /// The compiled <paramref name="then"/> (never null) and <paramref name="giveUp"/> (null when the task has none) of task <paramref name="task"/>
        /// of <paramref name="entity"/>, whose queue slot is <paramref name="slot"/>.
        /// </summary>
        public void Get(EntityRepository repo, Entity entity, int task, in MissionPhase slot,
            out Func<EntityRepository, Entity, bool> then, out Func<EntityRepository, Entity, bool>? giveUp)
        {
            if (!_byEntity.TryGetValue(entity.Index, out var entry))
                _byEntity[entity.Index] = entry = new Entry { Generation = entity.Generation };
            else if (entry.Generation != entity.Generation)
            {
                entry.Generation = entity.Generation;
                foreach (var s in entry.Slots) s.Built = false;
            }

            var plan = repo.HasManagedComponent<ActiveMissionPlan>(entity)
                ? ((ISimulationView)repo).GetManagedComponentRO<ActiveMissionPlan>(entity)
                : null;
            var c = entry.Slots[task];
            if (!c.Built || !ReferenceEquals(c.Plan, plan) || c.Kind != slot.Trigger || c.Param != slot.TriggerParam)
                Build(c, plan, task, slot);
            then = c.Then!;
            giveUp = c.GiveUp;
        }

        /// <summary>Forgets every entity (a world move: the next frame recompiles what it meets).</summary>
        public void Clear() => _byEntity.Clear();

        private static void Build(Slot c, ActiveMissionPlan? plan, int task, in MissionPhase slot)
        {
            c.Plan = plan; c.Kind = slot.Trigger; c.Param = slot.TriggerParam; c.Built = true;
            c.Then = null; c.GiveUp = null;
            var triggers = plan?.Plan?.Tasks is { } tasks && task < tasks.Count ? tasks[task].Triggers : null;
            if (triggers != null)
                for (int i = 0; i < triggers.Count; i++)
                {
                    var t = triggers[i];
                    if (string.Equals(t.Type, MissionConditionTypes.Then, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(t.Params))
                        c.Then = ConditionCache.Get(t.Params);
                    else if (string.Equals(t.Type, MissionConditionTypes.GiveUp, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(t.Params))
                        c.GiveUp = ConditionCache.Get(t.Params);
                }
            c.Then ??= ConditionCache.Compiler.CompileLevelPredicate(Translate(slot.Trigger == MissionTrigger.Condition ? MissionTrigger.BehaviorFinished : slot.Trigger, slot.TriggerParam));
        }

        /// <summary>
        /// ⭐⭐ THE translation of an old trigger type to the condition it means (D3) — the presets the <i>then</i> picker writes are the same ones:
        /// <list type="table">
        /// <item><c>BehaviorFinished</c> / <c>ReachedDestination</c> / none ⇒ <c>TaskFinished</c> (this unit, its current task) — <i>when done</i></item>
        /// <item><c>TimerElapsed N</c> ⇒ <c>MissionPlanQueue.PhaseElapsedSeconds ≥ N</c> — <i>after N s</i></item>
        /// <item><c>UnderAttack</c> ⇒ <c>TargetMemory.Count &gt; 0</c> — the old meaning (it knows an enemy), kept</item>
        /// <item><c>HealthCritical X</c> ⇒ <c>Health.Max &gt; 0</c> and NOT <c>HealthFraction &gt; X</c> — fraction ≤ X, never without Health</item>
        /// </list>
        /// </summary>
        public static SearchPredicateDto Translate(MissionTrigger kind, float param)
        {
#pragma warning disable CS0618 // the obsolete ReachedDestination is translated, as its doc says
            switch (kind)
            {
                case MissionTrigger.TimerElapsed:
                    return new PropertyMatchDto
                    {
                        ComponentType = typeof(MissionPlanQueue),
                        PropertyPath = nameof(MissionPlanQueue.PhaseElapsedSeconds) + "." + nameof(SimDuration.Seconds),
                        Operator = SearchOperator.Equals,
                        Predicate = new NumericPredicateDto { MinValue = param, MaxValue = double.MaxValue },
                    };
                case MissionTrigger.UnderAttack:
                    return new PropertyMatchDto
                    {
                        ComponentType = typeof(TargetMemory),
                        PropertyPath = nameof(TargetMemory.Count),
                        Operator = SearchOperator.GreaterThan,
                        Predicate = new NumericPredicateDto { MinValue = 0 },
                    };
                case MissionTrigger.HealthCritical:
                    return new CompoundPredicateDto
                    {
                        Operator = LogicalOperator.And,
                        Conditions =
                        {
                            new PropertyMatchDto
                            {
                                ComponentType = typeof(Fdp.Toolkit.Combat.Components.Health),
                                PropertyPath = nameof(Fdp.Toolkit.Combat.Components.Health.Max),
                                Operator = SearchOperator.GreaterThan,
                                Predicate = new NumericPredicateDto { MinValue = 0 },
                            },
                            new CompoundPredicateDto
                            {
                                Operator = LogicalOperator.None,
                                Conditions = { new InputAbovePredicateDto { Input = "HealthFraction", Threshold = param } },
                            },
                        },
                    };
                default:   // BehaviorFinished, ReachedDestination, Condition with no stored condition
                    return new TaskFinishedPredicateDto();
            }
#pragma warning restore CS0618
        }
    }

    /// <summary>⭐ <c>CE-4298</c> (§5.7) — the two trigger-entry types that carry a CONDITION (their params are its JSON). The meaning is in the
    /// TYPE, never the position.</summary>
    public static class MissionConditionTypes
    {
        public const string Then = "Then";
        public const string GiveUp = "GiveUp";
    }
}
