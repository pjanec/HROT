using System.Text.Json.Serialization;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2123</c> S2 — the authored contract of a squad commander's <c>SquadHold</c>: every member holds where it stands and fights (the library <c>CombatPosture</c> at its own position). A zero field keeps its default. 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §4.
    /// </summary>
    [BehaviorContract(BehaviorId, BehaviorCategory.Commander)]
    public struct SquadHoldParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.SquadHold;

        /// <summary>The members' asked speed (m/s) when their posture moves them; 0 = 3.</summary>
        [JsonPropertyName("Speed")]
        public float Speed { get; set; }

        /// <summary>Hold this long, then the order is done (s); 0 = until another order.</summary>
        [JsonPropertyName("DurationSeconds")]
        public float DurationSeconds { get; set; }

        /// <summary>The order is done once the squad knows no contact.</summary>
        [JsonPropertyName("EndWhenClear")]
        public bool EndWhenClear { get; set; }
    }
}
