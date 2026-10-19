using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2123</c> S2 — the authored contract of a squad commander's <c>BreakContact</c>: the commander asks the terrain for a point the enemy cannot see; the squad bounds to it, one element moving while the other covers. A zero field keeps its default. 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §4.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct BreakContactParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.BreakContact;

        /// <summary>How far round the squad the retreat query looks (m); 0 = 60.</summary>
        [JsonPropertyName("RetreatSearchRadius")]
        public float RetreatSearchRadius;

        /// <summary>The members' asked speed (m/s); 0 = 3. Their stance caps it.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>How close both elements must be to the point for the order to be done (m); 0 = 8.</summary>
        [JsonPropertyName("ArrivalRadius")]
        public float ArrivalRadius;

        /// <summary>A bound is complete after this long even if a mover never arrives (s); 0 = 60.</summary>
        [JsonPropertyName("BoundTimeoutSeconds")]
        public float BoundTimeoutSeconds;

        /// <summary>⭐ <c>CE-2276</c> E-3a (DA-68) — where to fall back to (sim metres); zero = ask the terrain for a point the enemy cannot see.
        /// Set, it is the answer: no terrain query and no contact needed (a platoon's fall-back point).</summary>
        [JsonPropertyName("To")]
        public Vector3 To;
    }
}
