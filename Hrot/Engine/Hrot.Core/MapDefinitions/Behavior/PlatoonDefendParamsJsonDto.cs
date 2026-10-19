using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2276</c> E-1 (DA-51…DA-54) — the authored contract of a platoon's <c>PlatoonDefend</c>: each squad defends its point
    /// (<c>SquadDefend</c>), the line squads each watch a slice of the engagement area, and the line holds its fire until an enemy is
    /// reported inside the area. A zero field keeps its default. 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8d.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct PlatoonDefendParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.PlatoonDefend;

        /// <summary>The position defended (sim metres) — the engagement area's origin, and every squad's point unless authored.</summary>
        [JsonPropertyName("Position")]
        public Vector3 Position;

        /// <summary>Each squad's posts are looked for within this distance of its point (m); 0 = each squad's own default.</summary>
        [JsonPropertyName("SearchRadius")]
        public float SearchRadius;

        /// <summary>The squads' asked speed (m/s); 0 = each squad's own default.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>The engagement area as an arc from <see cref="Position"/>: its left and right compass bearings (°, 0 = north, clockwise)
        /// and its range band (m; <see cref="MaxRange"/> 0 = no limit). Left = Right = 0 ⇒ the platoon's own sector is used.</summary>
        [JsonPropertyName("Left")]
        public float Left;

        [JsonPropertyName("Right")]
        public float Right;

        [JsonPropertyName("MinRange")]
        public float MinRange;

        [JsonPropertyName("MaxRange")]
        public float MaxRange;

        /// <summary>⭐ DA-51 — each squad's role and point; none ⇒ every squad on the line at <see cref="Position"/>, and with three or
        /// more the last is the reserve, behind.</summary>
        [JsonPropertyName("Squads")]
        public PlatoonDefendSquadJsonDto[]? Squads;
    }

    /// <summary>⭐ <c>CE-2276</c> E-1 (DA-51) — one squad's place in a <c>PlatoonDefend</c> order.</summary>
    public struct PlatoonDefendSquadJsonDto
    {
        /// <summary>The squad's place in the platoon's roster, 1-based.</summary>
        [JsonPropertyName("Squad")]
        public int Squad;

        /// <summary>The reserve: no sector, held back, returns fire only.</summary>
        [JsonPropertyName("Reserve")]
        public bool Reserve;

        /// <summary>Where it defends (sim metres); zero = the order's position.</summary>
        [JsonPropertyName("Point")]
        public Vector3 Point;

        /// <summary>Its posts' search radius (m); 0 = the order's.</summary>
        [JsonPropertyName("SearchRadius")]
        public float SearchRadius;

        /// <summary>⭐ <c>CE-2276</c> E-2 (DA-59) — the observation post: forward at <see cref="Point"/>, no sector, returns fire; an enemy within
        /// 300 m of it brings it back to <see cref="Back"/>, where it becomes a reserve.</summary>
        [JsonPropertyName("Op")]
        public bool Op;

        /// <summary>⭐ DA-59 — where the OP comes back to (sim metres); zero = the order's position.</summary>
        [JsonPropertyName("Back")]
        public Vector3 Back;
    }
}
