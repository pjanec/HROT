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
        public const string KnownTypes = "TimerElapsed, ReachedDestination, HealthCritical, BehaviorFinished, UnderAttack";

        /// <summary>
        /// ⭐ <c>CE-4157</c> — the trigger of ONE phase. Null / empty ⇒ <c>(TimerElapsed, float.MaxValue)</c> (no trigger: the phase holds
        /// indefinitely). The queue holds ONE trigger per phase (<c>MissionPhase</c>), so a second is refused loudly, never dropped. Throws
        /// <see cref="MissionPlanException"/> naming <paramref name="task"/> for an unknown type, more than one trigger or a bad parameter.
        /// </summary>
        public static (EcsMissionTrigger Trigger, float Param) ResolveTrigger(List<MissionTrigger>? triggers, string? task)
        {
            if (triggers == null || triggers.Count == 0)
                return (EcsMissionTrigger.TimerElapsed, float.MaxValue);
            if (triggers.Count > 1)
                throw new MissionPlanException(
                    $"mission task '{task}': a phase holds ONE trigger, this one lists {triggers.Count} ({string.Join(", ", triggers.ConvertAll(t => t.Type))}) — CE-4157");
            return Resolve(triggers[0].Type, triggers[0].Params, task);
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
