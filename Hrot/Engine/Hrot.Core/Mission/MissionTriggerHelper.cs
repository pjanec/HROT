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
        /// <summary>
        /// Resolves the first trigger in <paramref name="triggers"/> to the corresponding
        /// <see cref="EcsMissionTrigger"/> and numeric parameter.
        /// Returns <c>(TimerElapsed, float.MaxValue)</c> when triggers is null or empty
        /// so that a phase with no trigger holds indefinitely.
        /// </summary>
        public static (EcsMissionTrigger Trigger, float Param) ResolveTrigger(List<MissionTrigger>? triggers)
        {
            if (triggers == null || triggers.Count == 0)
                return (EcsMissionTrigger.TimerElapsed, float.MaxValue);

            var trigger = triggers[0];
            var type = trigger.Type ?? string.Empty;

            return type switch
            {
                "TimerElapsed"       => (EcsMissionTrigger.TimerElapsed,     ParseTriggerParam(trigger.Params)),
                // "ReachedDestination" is the legacy wire string for the navigation-arrival trigger.
                // Per BS1-T022, arrival is now signalled via the DoctrineFinished path.
                // Map to DoctrineFinished at ingress to preserve backward wire compatibility.
                "ReachedDestination" => (EcsMissionTrigger.DoctrineFinished, 0f),
                "HealthCritical"     => (EcsMissionTrigger.HealthCritical,   ParseTriggerParam(trigger.Params)),
                "DoctrineFinished"   => (EcsMissionTrigger.DoctrineFinished, 0f),
                "UnderAttack"        => (EcsMissionTrigger.UnderAttack,      0f),
                _                    => (EcsMissionTrigger.TimerElapsed,     0f)
            };
        }

        /// <summary>
        /// Parses a float parameter string (e.g. "10.5") using invariant culture.
        /// Returns 0f for null, empty, or unparseable input.
        /// </summary>
        public static float ParseTriggerParam(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return 0f;

            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0f;
        }
    }
}
