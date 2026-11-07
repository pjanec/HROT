using System;
using System.Collections.Generic;
using System.Globalization;
using Fdp.Toolkit.Behavior.Components;
using EcsMissionTrigger = Fdp.Toolkit.Behavior.Components.MissionTrigger;

namespace Hrot.Core.Mission
{
    /// <summary>
    /// Shared helper for resolving neutral mission trigger descriptors into
    /// ECS trigger enumerations (<see cref="EcsMissionTrigger"/>).
    /// </summary>
    public static class MissionTriggerHelper
    {
        /// <summary>The trigger types a phase may name (the one list, quoted by every error).</summary>
        public const string KnownTypes = "TimerElapsed, ReachedDestination, HealthCritical, BehaviorFinished, UnderAttack, Then, GiveUp";

        /// <summary>
        /// ⭐ <c>CE-4157</c> — the trigger of ONE phase. ⭐⭐ <c>CE-4298</c> (mission plan §5.9, R-4161) — null / empty ⇒ <c>BehaviorFinished</c>:
        /// a task with no condition ends <i>when done</i>, so a plan of bare tasks runs one by one (the user: "I would expect that they are executed
        /// one by one"); decided HERE, once — the editors write nothing for it. ⛔ SUPERSEDED: empty ⇒ <c>(TimerElapsed, ∞)</c> (held forever). The queue holds ONE trigger per phase (<c>MissionPhase</c>), so a second is refused loudly, never dropped. Throws
        /// <see cref="MissionPlanException"/> naming <paramref name="task"/> for an unknown type, more than one trigger or a bad parameter.
        /// </summary>
        public static (EcsMissionTrigger Trigger, float Param) ResolveTrigger(List<MissionTrigger>? triggers, string? task)
        {
            if (triggers == null || triggers.Count == 0)
                return (EcsMissionTrigger.BehaviorFinished, 0f);   // ⭐ CE-4298 — no condition = when done
            // ⭐⭐ CE-4298 (mission plan §5.7) — a task holds ONE "then" (an old type, or a `Then` condition) and at most ONE `GiveUp`; the meaning is
            //   in the TYPE, never the position. A give-up is the director's (PlanConditionCache), so it does not change the slot's kind.
            (EcsMissionTrigger Trigger, float Param)? then = null;
            bool giveUp = false;
            foreach (var t in triggers)
            {
                if (t.Type == GiveUpType)
                {
                    if (giveUp) throw new MissionPlanException($"mission task '{task}': more than one GiveUp — CE-4298");
                    giveUp = true;
                    CheckCondition(t, task);
                    continue;
                }
                if (then != null)
                    throw new MissionPlanException(
                        $"mission task '{task}': a task holds ONE then, this one lists {triggers.Count} ({string.Join(", ", triggers.ConvertAll(x => x.Type))}) — CE-4157");
                if (t.Type == ThenType) { CheckCondition(t, task); then = (EcsMissionTrigger.Condition, 0f); }
                else then = Resolve(t.Type, t.Params, task);
            }
            return then ?? (EcsMissionTrigger.BehaviorFinished, 0f);   // a give-up alone: the then is "when done"
        }

        /// <summary>⭐ <c>CE-4298</c> — the two entry types whose params are a CONDITION (the condition language's JSON).</summary>
        public const string ThenType = "Then", GiveUpType = "GiveUp";

        private static void CheckCondition(MissionTrigger t, string? task)
        {
            if (string.IsNullOrWhiteSpace(t.Params))
                throw new MissionPlanException($"mission task '{task}': '{t.Type}' holds no condition — CE-4298");
            try { Fdp.Toolkit.ReplayBrowser.Search.ConditionCache.Parse(t.Params); }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException or InvalidOperationException)
            { throw new MissionPlanException($"mission task '{task}': '{t.Type}' is not a condition ({ex.Message}) — CE-4298"); }
        }

        /// <summary>
        /// ⭐ <c>CE-4157</c> — THE ONE trigger parser (the only switch). Throws <see cref="MissionPlanException"/> for an unknown type or a
        /// NON-EMPTY parameter that does not parse (an empty one stays 0).
        /// </summary>
        public static (EcsMissionTrigger Trigger, float Param) Resolve(string? type, string? @params, string? task)
        {
            return (type ?? string.Empty) switch
            {
                "TimerElapsed"       => (EcsMissionTrigger.TimerElapsed,     Param(type, @params, task)),
                // "ReachedDestination" is the legacy wire string for the navigation-arrival trigger.
                // Per BS1-T022, arrival is now signalled via the BehaviorFinished path.
                "ReachedDestination" => (EcsMissionTrigger.BehaviorFinished, 0f),
                "HealthCritical"     => (EcsMissionTrigger.HealthCritical,   Param(type, @params, task)),
                "BehaviorFinished"   => (EcsMissionTrigger.BehaviorFinished, 0f),
                "UnderAttack"        => (EcsMissionTrigger.UnderAttack,      0f),
                _ => throw new MissionPlanException(
                    $"mission task '{task}': unknown trigger type '{type}' (known: {KnownTypes}) — CE-4157"),
            };
        }

        private static float Param(string? type, string? raw, string? task)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0f;
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return value;
            throw new MissionPlanException($"mission task '{task}': trigger '{type}' has a parameter '{raw}' that is not a number — CE-4157");
        }
    }
}
