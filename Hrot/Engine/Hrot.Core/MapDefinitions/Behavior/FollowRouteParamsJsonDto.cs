using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Replication;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// JSON serialization DTO for the <c>FollowRoute</c> behavior parameter block.
    /// <para>⭐ <c>CE-2131</c> / <c>CE-2145</c> — this is the AUTHORED contract (what a scenario, mission, order or agent writes),
    /// and it is also what the behaviour parses: its typed resolver (<c>CgfNodes.ResolveFollowRoute</c>) turns the route entity
    /// into its compiled trajectory on the RECEIVING side (R-273).</para>
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
        public EntityRef RouteEntityId;

        /// <summary>⭐ <c>CE-2131</c> (CE-2128 ①) — the asked speed (m/s); 0 = the trajectory's own. The mission path always sent
        /// it; the published contract left it out until CE-2131.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>⭐ <c>CE-2131</c> — follow the route again from its start at the end (the helper reads <c>"Loop"</c>).</summary>
        [JsonPropertyName("Loop")]
        public bool Loop;
    }
}
