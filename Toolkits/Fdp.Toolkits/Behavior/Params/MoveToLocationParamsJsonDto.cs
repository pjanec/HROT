using System.Text.Json.Serialization;

namespace Fdp.Toolkit.Behavior.Params
{
    /// <summary>
    /// JSON serialization DTO for the <c>MoveToLocation</c> behavior parameter block.
    /// JSON keys match what <c>MissionPanel.BuildMoveToLocationParams</c> produces.
    ///
    /// <para>This DTO has no <see cref="Attributes.RemapNetworkIdAttribute"/>-tagged
    /// members; it is used for UI rendering only (Phase 5) and does not participate
    /// in scenario network-ID remapping.</para>
    /// </summary>
    public class MoveToLocationParamsJsonDto
    {
        /// <summary>Target latitude in degrees.</summary>
        [JsonPropertyName("targetLat")]
        public double TargetLat { get; set; }

        /// <summary>Target longitude in degrees.</summary>
        [JsonPropertyName("targetLon")]
        public double TargetLon { get; set; }

        /// <summary>Travel speed in meters per second.</summary>
        [JsonPropertyName("speed")]
        public double Speed { get; set; }

        /// <summary>Radius in meters within which arrival is declared.</summary>
        [JsonPropertyName("arrivalRadius")]
        public double ArrivalRadius { get; set; }
    }
}
