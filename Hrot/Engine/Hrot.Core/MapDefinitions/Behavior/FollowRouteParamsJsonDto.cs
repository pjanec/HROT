using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Replication;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// JSON serialization DTO for the <c>FollowRoute</c> behavior parameter block.
    /// <para>⚠ <c>CE-2131</c> — this is the AUTHORED contract (what a scenario, mission or agent writes). Today the route entity is
    /// converted to a trajectory id on the SENDING side (<c>MissionControlBehaviorParamsHelper</c>), and the curated resolver
    /// parses that converted <c>{trajectoryId, Speed, Loop}</c>; moving the conversion into the behaviour is <c>CE-2145</c>.</para>
    /// </summary>
    public sealed class FollowRouteParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.FollowRoute;

        /// <summary>
        /// Network ID of the route entity to follow.
        /// Widened from <c>int</c> to <c>long</c> for uniform ID remapping.
        /// </summary>
        [JsonPropertyName("routeEntityId")]
        [MapPickableEntity("road_graphs")]   // ⭐ narrows the picker; the TYPE makes it pickable and remapped
        public EntityRef RouteEntityId { get; set; }

        /// <summary>⭐ <c>CE-2131</c> (CE-2128 ①) — the asked speed (m/s); 0 = the trajectory's own. The mission path already sends
        /// it (<c>MissionControlBehaviorParamsHelper</c> reads <c>"Speed"</c>); the published contract left it out.</summary>
        [JsonPropertyName("Speed")]
        public float Speed { get; set; }

        /// <summary>⭐ <c>CE-2131</c> — follow the route again from its start at the end (the helper reads <c>"Loop"</c>).</summary>
        [JsonPropertyName("Loop")]
        public bool Loop { get; set; }
    }
}
