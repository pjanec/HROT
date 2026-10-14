using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2123</c> S3 — the authored contract of a squad commander's <c>SquadMarch</c>: the lead (the first live member) walks
    /// to <see cref="Objective"/>; the others keep a slot of <see cref="Shape"/> on it, and walk in file on the lead's own trail where a
    /// slot does not work. A zero field keeps its default. 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §6.1.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
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

        /// <summary>⭐ <c>CE-2159</c> X-a — watch the route for danger areas (a road, an open strip): the march owns the commander's
        /// danger-area sensor to <see cref="Objective"/>, and the squad SOP's "danger area ahead" row crosses each one as a drill
        /// (Squad Tactics §7.7.6). Off by default.</summary>
        [JsonPropertyName("DangerAreas")]
        public bool DangerAreas;

        /// <summary>⭐ <c>CE-2161</c> (CE-3081, docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.7b A5) — with <see cref="DangerAreas"/>, OPEN
        /// GROUND is a danger area too (an off-road run of ≥ 20 m with no cover in the corridor): the squad's crossing choice (D3) bounds
        /// over it by overwatch. Off by default — on most routes every field would become a drill.</summary>
        [JsonPropertyName("OpenGround")]
        public bool OpenGround;
    }
}
