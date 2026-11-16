using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2436</c> AU-4 (A-4) — the authored contract of a vehicle's <c>VehicleSupportByFire</c>: drive to <see cref="Position"/> (a stand-off
    /// point on the base of fire's line), then fight from cover there — hide, expose, fire, relocate after each engagement. A zero field keeps its
    /// default. 📄 docs/designs/group-maneuvers/DESIGN_Assault_Under_Fire.md §5 A-4, §9.4.
    /// </summary>
    [ParamsDto]
    public struct VehicleSupportByFireParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.VehicleSupportByFire;

        /// <summary>The stand-off point the vehicle drives to (world m).</summary>
        [JsonPropertyName("Position")]
        public Vector3 Position;

        /// <summary>Drive speed (m/s); 0 = the vehicle's own.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>How far round the stand-off point the hide query looks (m); 0 = the cycle's default.</summary>
        [JsonPropertyName("SearchRadius")]
        public float SearchRadius;

        /// <summary>⭐ <c>CE-2436</c> AU-4 (A-4, blind fire) — the REPORTED posts' centre (world m): while its own sensors find nothing, the vehicle
        /// fires cannon bursts at it; (0, 0, 0) = none (the cycle alone).</summary>
        [JsonPropertyName("Posts")]
        public Vector3 Posts;
    }
}
