using System.Numerics;
using System.Text.Json.Serialization;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2123</c> — the authored contract of a squad commander's <c>SuppressAndManeuver</c>: the members who see the squad's
    /// top identified contact hold and fire on it; the rest go to a flank the commander asks the terrain for, then assault through
    /// the enemy's position. A zero field keeps its default. 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §3.
    /// </summary>
    public struct SuppressAndManeuverParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.SuppressAndManeuver;

        /// <summary>How far round the enemy the flank query looks (m); 0 = 40.</summary>
        [JsonPropertyName("FlankSearchRadius")]
        public float FlankSearchRadius;

        /// <summary>The members' asked speed (m/s); 0 = 3. Their stance caps it.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>How close the assault must get to the enemy's position to have gone through it (m); 0 = 8.</summary>
        [JsonPropertyName("ArrivalRadius")]
        public float ArrivalRadius;

        /// <summary>A phase is complete after this long even if a mover never arrives (s); 0 = 90.</summary>
        [JsonPropertyName("PhaseTimeoutSeconds")]
        public float PhaseTimeoutSeconds;
    }
}
