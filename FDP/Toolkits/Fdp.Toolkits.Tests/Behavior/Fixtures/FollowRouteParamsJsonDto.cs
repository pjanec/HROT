using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;

namespace Fdp.Toolkit.Behavior.Tests.Fixtures
{
    // ⭐ CE-447 (2026-09-30): a TEST FIXTURE, moved out of Fdp.Toolkits production. The one authored contract is
    //   Hrot.Core's [BehaviorContract] class of the same name; FDP tests cannot reference Hrot.Core, so they
    //   carry this attribute-bearing stand-in for the FDP-level mechanisms (remapper, presentation attributes).
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
        [MapPickableEntity("road_graphs")]   // narrows the picker only
        public Fdp.Toolkit.Replication.EntityRef RouteEntityId { get; set; }
    }
}
