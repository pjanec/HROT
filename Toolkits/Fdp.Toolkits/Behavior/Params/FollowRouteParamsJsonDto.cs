using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;

namespace Fdp.Toolkit.Behavior.Params
{
    /// <summary>
    /// JSON serialization DTO for the <c>FollowRoute</c> behavior parameter block.
    /// JSON keys match what <c>MissionPanel.BuildFollowRouteParams</c> produces.
    /// </summary>
    public class FollowRouteParamsJsonDto
    {
        /// <summary>
        /// Network ID of the route entity to follow.
        /// Widened from <c>int</c> to <c>long</c> for uniform ID remapping.
        /// </summary>
        [JsonPropertyName("routeEntityId")]
        [RemapNetworkId]
        public long RouteEntityId { get; set; }
    }
}
