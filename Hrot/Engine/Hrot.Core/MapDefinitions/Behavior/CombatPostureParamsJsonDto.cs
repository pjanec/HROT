using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-3160</c> (L4, R-266) — the authored contract of the library's <c>CombatPosture</c>: where to advance and how fast.
    /// Every other value is the behaviour's own default (📄 docs/DESIGN_Decision_Layer.md §3.3h). ⚠ The shape is the one every
    /// scenario already writes — <c>{"advance":{"Objective":[x,y,z],"Speed":3,"ArrivalRadius":5,"CooldownSeconds":1}}</c> — so the
    /// retired JSON tree's payloads load unchanged. A zero field keeps the default.
    /// </summary>
    [BehaviorContract(BehaviorId, BehaviorCategory.Infantry | BehaviorCategory.Insurgent)]
    public struct CombatPostureParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.CombatPosture;

        /// <summary>The advance-and-attack leg.</summary>
        [JsonPropertyName("advance")]
        public CombatPostureAdvanceJsonDto Advance { get; set; }
    }

    /// <summary>⭐ <c>CE-3160</c> — the advance of <see cref="CombatPostureParamsJsonDto"/>.</summary>
    public struct CombatPostureAdvanceJsonDto
    {
        /// <summary>Where to advance to (sim metres, Z up).</summary>
        [JsonPropertyName("Objective")]
        public Vector3 Objective { get; set; }

        /// <summary>The asked speed (m/s); 0 = 3. The body's stance caps it (AQ88).</summary>
        [JsonPropertyName("Speed")]
        public float Speed { get; set; }

        /// <summary>How close counts as arrived (m); 0 = 5.</summary>
        [JsonPropertyName("ArrivalRadius")]
        public float ArrivalRadius { get; set; }

        /// <summary>Seconds between shots while advancing; 0 = 1.</summary>
        [JsonPropertyName("CooldownSeconds")]
        public float CooldownSeconds { get; set; }
    }
}
