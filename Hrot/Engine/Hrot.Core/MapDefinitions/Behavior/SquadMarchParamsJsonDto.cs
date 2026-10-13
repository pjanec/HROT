using System.Numerics;
using System.Text.Json.Serialization;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2123</c> S3 — the authored contract of a squad commander's <c>SquadMarch</c>: the lead (the first live member) walks
    /// to <see cref="Objective"/>; the others keep a slot of <see cref="Shape"/> on it, and walk in file on the lead's own trail where a
    /// slot does not work. A zero field keeps its default. 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §6.1.
    /// </summary>
    public struct SquadMarchParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.SquadMarch;

        /// <summary>Where the squad goes (sim metres, Z up).</summary>
        [JsonPropertyName("Objective")]
        public Vector3 Objective;

        /// <summary>0 = File (on the lead's trail) · 1 = Wedge · 2 = Line.</summary>
        [JsonPropertyName("Shape")]
        public int Shape;

        /// <summary>The distance between neighbours (m); 0 = 5.</summary>
        [JsonPropertyName("Spacing")]
        public float Spacing;

        /// <summary>The members' asked speed (m/s); 0 = 1.5. Their stance caps it.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>How close the lead must get to the objective (m); 0 = 5.</summary>
        [JsonPropertyName("ArrivalRadius")]
        public float ArrivalRadius;

        /// <summary>A follower this far from its slot halts the lead until it closes up (m); 0 = 10.</summary>
        [JsonPropertyName("MaxLagMetres")]
        public float MaxLagMetres;
    }
}
