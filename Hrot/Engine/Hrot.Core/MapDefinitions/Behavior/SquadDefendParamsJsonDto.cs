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
        public Vector3 Position;

        /// <summary>Posts are looked for within this distance of the position (m); 0 = 30.</summary>
        [JsonPropertyName("SearchRadius")]
        public float SearchRadius;

        /// <summary>The members' asked speed (m/s); 0 = 3.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>⭐ <c>CE-2275</c> D-2f (DA-5, DA-46) — AUTHORED fighting positions: preferred over the ones the terrain finds (the first
        /// <see cref="SquadDefendPostJsonDto.MaxPosts"/> are used). Each goes to the nearest man; the terrain is asked only for the men left.</summary>
        [JsonPropertyName("Posts")]
        public SquadDefendPostJsonDto[]? Posts;
    }

    /// <summary>⭐ <c>CE-2275</c> D-2f (DA-46) — one authored fighting position of a <c>SquadDefend</c> order.</summary>
    public struct SquadDefendPostJsonDto
    {
        public const int MaxPosts = 8;

        /// <summary>Where he stands (sim metres; the storey's floor height in Z).</summary>
        [JsonPropertyName("point")]
        public Vector3 Point;

        /// <summary>A point he faces — his arc (±45°) points there instead of at the squad's sector; zero = toward the sector.</summary>
        [JsonPropertyName("watchTowards")]
        public Vector3 WatchTowards;

        /// <summary>A window post: he fights it the window way (prone under the sill, up at it — D-2a).</summary>
        [JsonPropertyName("window")]
        public bool Window;
    }
}
