using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2123</c> S2 — the authored contract of a squad commander's <c>ReactToContact</c> (usually started by its standing orders, <c>BasicSquadSop</c>): every member holds and returns fire for <c>DecideAfterSeconds</c>, then the commander decides ONCE — assault (suppress and manoeuvre), break contact, or hold until clear. A zero field keeps its default. 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §4.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct ReactToContactParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.ReactToContact;

        /// <summary>How long the squad holds and returns fire before the commander decides (s); 0 = 3.</summary>
        [JsonPropertyName("DecideAfterSeconds")]
        public float DecideAfterSeconds;

        /// <summary>The assault's flank query radius (m); 0 = 40.</summary>
        [JsonPropertyName("FlankSearchRadius")]
        public float FlankSearchRadius;

        /// <summary>The break-contact retreat query radius (m); 0 = 60.</summary>
        [JsonPropertyName("RetreatSearchRadius")]
        public float RetreatSearchRadius;

        /// <summary>The members' asked speed (m/s); 0 = 3.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>How close a drill must get to its point (m); 0 = 8.</summary>
        [JsonPropertyName("ArrivalRadius")]
        public float ArrivalRadius;

        /// <summary>A drill phase is complete after this long even if a mover never arrives (s); 0 = 90.</summary>
        [JsonPropertyName("PhaseTimeoutSeconds")]
        public float PhaseTimeoutSeconds;
    }
}
