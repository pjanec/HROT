using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// JSON serialization DTO for the <c>FollowRoute</c> behavior parameter block.
    /// </summary>
    [BehaviorContract(BehaviorId, BehaviorCategory.AllMilitary)]
    public sealed class FollowRouteParamsJsonDto
    {
        public const string BehaviorId = "FollowRoute";

        /// <summary>
        /// Network ID of the route entity to follow.
        /// Widened from <c>int</c> to <c>long</c> for uniform ID remapping.
        /// </summary>
        [JsonPropertyName("routeEntityId")]
        [RemapNetworkId]
        [MapPickableEntity("road_graphs")]
        public long RouteEntityId { get; set; }
    }
}
