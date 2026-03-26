using System.Collections.Generic;
using System.Globalization;
using Bagira.BDC.SSTD;
using FDP.Toolkit.Behavior.Components;
using DdsMissionTrigger = Bagira.BDC.SSTD.MissionTrigger;
using EcsMissionTrigger = FDP.Toolkit.Behavior.Components.MissionTrigger;

namespace Bagira.Map.Common.Helpers
{
    /// <summary>
    /// Shared helper for resolving DDS mission trigger strings into ECS trigger enumerations.
    ///
    /// Consolidates the duplicate <c>ResolveTrigger</c> logic that previously existed in both
    /// <c>MissionControlRequestSystem</c> (SimHost) and <c>EntityMissionIngressTranslator</c>
    /// (Bagira.Map.Common) — DEBT item from BUG2-BATCH-01 (BUG2-DEBT-01).
    /// </summary>
    public static class MissionTriggerHelper
    {
        /// <summary>
        /// Resolves the first DDS trigger in <paramref name="triggers"/> to the corresponding
        /// <see cref="EcsMissionTrigger"/> and numeric parameter.
        ///
        /// Returns <c>(TimerElapsed, float.MaxValue)</c> when <paramref name="triggers"/> is
        /// null or empty so that a phase with no trigger holds indefinitely.
        ///
        /// Unknown trigger type strings fall back to <c>TimerElapsed(0f)</c> as a safe,
        /// observable failure mode — an operator or monitoring system can detect a timer
        /// advancing from zero rather than a silent no-op.
        /// </summary>
        public static (EcsMissionTrigger Trigger, float Param) ResolveTrigger(List<DdsMissionTrigger>? triggers)
        {
            if (triggers == null || triggers.Count == 0)
                return (EcsMissionTrigger.TimerElapsed, float.MaxValue); // no trigger = hold phase indefinitely

            var trigger = triggers[0];
            var type = trigger.Type ?? string.Empty;

            return type switch
            {
                "TimerElapsed"       => (EcsMissionTrigger.TimerElapsed,     ParseTriggerParam(trigger.Params)),
                // "ReachedDestination" is the legacy wire string for the navigation-arrival trigger.
                // Per BS1-T022, arrival is now signalled via the DoctrineFinished path.
                // Map to DoctrineFinished at ingress to preserve backward wire compatibility
                // without referencing the [Obsolete] enum member.
                "ReachedDestination" => (EcsMissionTrigger.DoctrineFinished, 0f),
                "HealthCritical"     => (EcsMissionTrigger.HealthCritical,   ParseTriggerParam(trigger.Params)),
                "DoctrineFinished"   => (EcsMissionTrigger.DoctrineFinished, 0f),
                "UnderAttack"        => (EcsMissionTrigger.UnderAttack,      0f),
                // Unknown trigger strings fall back to TimerElapsed(0) as a safe observable failure mode.
                _                    => (EcsMissionTrigger.TimerElapsed,     0f)
            };
        }

        /// <summary>
        /// Parses a float parameter string (e.g. <c>"10.5"</c>) using invariant culture.
        /// Returns <c>0f</c> for null, empty, or unparseable input.
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
