using System.Text.Json.Serialization;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2123</c> S2 — the authored contract of a squad commander's <c>BreakContact</c>: the commander asks the terrain for a point the enemy cannot see; the squad bounds to it, one element moving while the other covers. A zero field keeps its default. 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §4.
    /// </summary>
    [BehaviorContract(BehaviorId, BehaviorCategory.Commander)]
    public struct BreakContactParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.BreakContact;

        /// <summary>How far round the squad the retreat query looks (m); 0 = 60.</summary>
        [JsonPropertyName("RetreatSearchRadius")]
        public float RetreatSearchRadius { get; set; }

        /// <summary>The members' asked speed (m/s); 0 = 3. Their stance caps it.</summary>
        [JsonPropertyName("Speed")]
        public float Speed { get; set; }

        /// <summary>How close both elements must be to the point for the order to be done (m); 0 = 8.</summary>
        [JsonPropertyName("ArrivalRadius")]
        public float ArrivalRadius { get; set; }

        /// <summary>A bound is complete after this long even if a mover never arrives (s); 0 = 60.</summary>
        [JsonPropertyName("BoundTimeoutSeconds")]
        public float BoundTimeoutSeconds { get; set; }
    }
}
