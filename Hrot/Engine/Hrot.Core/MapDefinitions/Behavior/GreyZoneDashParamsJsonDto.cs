using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-4621</c> M-4 (MC-3…MC-7; 📄 docs/designs/vehicle-tactics/DESIGN_Motorcycle.md §5 / §9) — the authored contract of <c>GreyZoneDash</c>,
    /// run by a COMMANDER whose roster is bikes, under an order of kind <c>Dash</c>: every bike rides <see cref="Legs"/> at full speed (its own
    /// <c>DashRide</c>, the next released once the one ahead is <see cref="Spacing"/> off), breaks across a diving drone, never stops, and at the
    /// last leg's end — the edge — halts and puts its riders out. A zero field keeps its default.
    /// </summary>
    [ParamsDto]
    public struct GreyZoneDashParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.GreyZoneDash;
        public const int MaxLegs = 6;

        /// <summary>The legs' ends, in order (≤ <see cref="MaxLegs"/>); the last is the dismount point at the edge.</summary>
        [JsonPropertyName("Legs")]
        public PointLink[]? Legs;

        /// <summary>The ride (m/s); 0 = 25.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>The gap between bikes (m) — one drone, one bike; 0 = 80.</summary>
        [JsonPropertyName("Spacing")]
        public float Spacing;
    }

    /// <summary>
    /// ⭐ <c>CE-4621</c> M-4 (MC-3, MC-4, MC-6) — the authored contract of <c>DashRide</c>, a bike's own part of a dash (order kind <c>Dash</c>):
    /// <see cref="Legs"/> at <see cref="Speed"/>, the lookout's break across a diving drone, and at the last leg's end the riders out.
    /// </summary>
    [ParamsDto]
    public struct DashRideParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.DashRide;

        [JsonPropertyName("Legs")]
        public PointLink[]? Legs;

        /// <summary>The ride (m/s); 0 = 25.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;
    }
}
