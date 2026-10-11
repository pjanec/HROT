using System.Numerics;
using System.Text.Json.Serialization;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2123</c> — the authored contract of the library's <c>AdvanceTo</c>: go to <see cref="Objective"/>, firing at the top
    /// threat on the way, and end there. ⛔ No posture decision — a superior's MOVE order. 📐 Measured why it exists: <c>CombatPosture</c>
    /// with the enemy in sight chooses Suppress and stays put, so it cannot carry a "go there" order under contact.
    /// A zero field keeps its default. 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §3.6.
    /// </summary>
    [BehaviorContract(BehaviorId, BehaviorCategory.Infantry | BehaviorCategory.Insurgent)]
    public struct AdvanceToParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.AdvanceTo;

        /// <summary>Where to go (sim metres, Z up).</summary>
        [JsonPropertyName("Objective")]
        public Vector3 Objective { get; set; }

        /// <summary>The asked speed (m/s); 0 = 3. The body's stance caps it.</summary>
        [JsonPropertyName("Speed")]
        public float Speed { get; set; }

        /// <summary>How close counts as arrived (m); 0 = 3.</summary>
        [JsonPropertyName("ArrivalRadius")]
        public float ArrivalRadius { get; set; }

        /// <summary>Seconds between shots on the way; 0 = 1.</summary>
        [JsonPropertyName("CooldownSeconds")]
        public float CooldownSeconds { get; set; }
    }
}
