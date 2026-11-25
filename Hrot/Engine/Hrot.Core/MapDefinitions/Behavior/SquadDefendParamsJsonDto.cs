using Fdp.Toolkit.Replication;
using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2275</c> D-1 (DA-39) — the authored contract of a squad commander's <c>SquadDefend</c>: the squad moves to the position and
    /// mans posts within the search radius (windows of every storey that see its engagement area — the squad's own sector — then cover);
    /// it runs until the next order. A zero field keeps its default. 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8c.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct SquadDefendParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.SquadDefend;

        /// <summary>The position to defend (sim metres).</summary>
        [JsonPropertyName("Position")]
        public PointLink Position;

        /// <summary>Posts are looked for within this distance of the position (m); 0 = 30.</summary>
        [JsonPropertyName("SearchRadius")]
        public float SearchRadius;

        /// <summary>The members' asked speed (m/s); 0 = 3.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>Accepted — unused at this echelon (agg-behaviors R-8300: the Defend order's neutral DTO).</summary>
        [JsonPropertyName("Left")]
        public Fdp.Core.Bearing Left;

        /// <summary>Accepted — unused at this echelon (agg-behaviors R-8300: the Defend order's neutral DTO).</summary>
        [JsonPropertyName("Right")]
        public Fdp.Core.Bearing Right;

        /// <summary>Accepted — unused at this echelon (agg-behaviors R-8300: the Defend order's neutral DTO).</summary>
        [JsonPropertyName("FallBackTo")]
        public PointLink FallBackTo;

        /// <summary>Accepted — unused at this echelon (agg-behaviors R-8300: the Defend order's neutral DTO).</summary>
        [JsonPropertyName("Threat")]
        public PointLink Threat;

        /// <summary>⭐ <c>CE-2275</c> D-2f (DA-5, DA-46) — AUTHORED fighting positions: preferred over the ones the terrain finds (the first
        /// <see cref="SquadDefendPostJsonDto.MaxPosts"/> are used). Each goes to the nearest man; the terrain is asked only for the men left.</summary>
        [JsonPropertyName("Posts")]
        public SquadDefendPostJsonDto[]? Posts;

        /// <summary>⭐ <c>CE-2306</c> (DA-96, §8e) — an AMBUSH: the charge that initiates it. With it, the squad's own hold-fire (an operator's)
        /// lifts when that charge is gone (set off, or destroyed) or a round lands at a man — not when the enemy is first seen in the sector.</summary>
        [JsonPropertyName("InitiatedBy")]
        public Fdp.Toolkit.Replication.EntityLink InitiatedBy;

        /// <summary>⭐ <c>CE-7007</c> (City Battle §3a — active defence) — SHOOT AND MOVE: a posted man in contact changes to another post of
        /// the squad's set after this many seconds at one (each man 1 – 1.6 × it, seeded by who he is); 0 = off (he stays until his post is
        /// untenable or flanked, as before).</summary>
        [JsonPropertyName("ShootAndMoveSeconds")]
        public float ShootAndMoveSeconds;

        /// <summary>⭐ <c>CE-8327</c> — a RE-SENT order to the point the unit already holds (a flank turn re-plans the posts): the squad takes posts
        /// from where it stands instead of marching to the centre. false = as ever.</summary>
        [JsonPropertyName("InPlace")]
        public bool InPlace;
    }

    /// <summary>⭐ <c>CE-2275</c> D-2f (DA-46) — one authored fighting position of a <c>SquadDefend</c> order.</summary>
    public struct SquadDefendPostJsonDto
    {
        /// <summary>⭐ <c>X-2</c> X2-F (R-8359, R-8365) — 13 (= <c>PlannedPosts.Capacity</c>, a squad's men): a split squad's re-issued Defend carries a planned
        /// post for EVERY man (DP3-D ②). ⛔ SUPERSEDED: 8. 🔒 User, <c>2026-11-17</c>: "Breaking replay is no issue." — the recorded blackboard block grows
        /// (<c>SquadDefendParams.Authored</c>); an OLD recording replayed mid-<c>SquadDefend</c> may mis-read it, ACCEPTED (R-8365).</summary>
        public const int MaxPosts = 13;

        /// <summary>Where he stands (sim metres; the storey's floor height in Z).</summary>
        [JsonPropertyName("point")]
        public PointLink Point;

        /// <summary>A point he faces — his arc (±45°) points there instead of at the squad's sector; zero = toward the sector.</summary>
        [JsonPropertyName("watchTowards")]
        public PointLink WatchTowards;

        /// <summary>A window post: he fights it the window way (prone under the sill, up at it — D-2a).</summary>
        [JsonPropertyName("window")]
        public bool Window;
    }
}
