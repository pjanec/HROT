using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Replication;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2284</c> (DA-17, UM-1…UM-7) — the authored contract of a platoon's <c>UrbanMovement</c>: its squads move on foot along
    /// <see cref="Legs"/> (or the waypoints of <see cref="Route"/>), the first live squad leading with its crossing drill, the next following
    /// <see cref="Spacing"/> behind it on the route, the rest the reserve. A zero field keeps its default.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §11b.
    /// </summary>
    [ParamsDto]
    public struct UrbanMovementParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.UrbanMovement;
        public const int MaxLegs = 15;

        /// <summary>The legs' ends, in order (sim metres; ≤ <see cref="MaxLegs"/>). Win over <see cref="Route"/> when both are set.</summary>
        [JsonPropertyName("Legs")]
        public Vector3[]? Legs;

        /// <summary>A route entity whose waypoints are the legs (used when <see cref="Legs"/> is empty).</summary>
        [JsonPropertyName("Route")]
        [MapPickableEntity("road_graphs")]
        public EntityRef Route;

        /// <summary>The squads' marching speed (m/s); 0 = 1.5.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>How far behind the lead each next squad keeps on the route (m); 0 = 30.</summary>
        [JsonPropertyName("Spacing")]
        public float Spacing;

        /// <summary>The lead is at a leg's end within this (m); 0 = 8.</summary>
        [JsonPropertyName("ArrivalRadius")]
        public float ArrivalRadius;
    }
}
