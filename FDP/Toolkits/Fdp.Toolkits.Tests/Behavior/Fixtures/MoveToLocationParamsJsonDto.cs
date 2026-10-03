using Fdp.Toolkit.Behavior.Params;
using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;

namespace Fdp.Toolkit.Behavior.Tests.Fixtures
{
    // ⭐ CE-447 (2026-09-30): a TEST FIXTURE, moved out of Fdp.Toolkits production. The one authored contract is
    //   Hrot.Core's [BehaviorContract] class of the same name; FDP tests cannot reference Hrot.Core, so they
    //   carry this attribute-bearing stand-in for the FDP-level mechanisms (remapper, presentation attributes).
    /// <summary>
    /// JSON serialization DTO for the <c>MoveToLocation</c> behavior parameter block.
    /// JSON keys match what <c>MissionPanel.BuildMoveToLocationParams</c> produces.
    ///
    /// <para>This DTO has no <c>EntityRef</c> members; it is used for UI rendering only (Phase 5) and does not
    /// participate in scenario network-ID remapping.</para>
    /// </summary>
    public class MoveToLocationParamsJsonDto
    {
        /// <summary>Target latitude in degrees (flat JSON wire format).</summary>
        [JsonPropertyName("targetLat")]
        public double TargetLat { get; set; }

        /// <summary>Target longitude in degrees (flat JSON wire format).</summary>
        [JsonPropertyName("targetLon")]
        public double TargetLon { get; set; }

        /// <summary>Travel speed in meters per second.</summary>
        [JsonPropertyName("speed")]
        public double Speed { get; set; }

        /// <summary>Radius in meters within which arrival is declared.</summary>
        [JsonPropertyName("arrivalRadius")]
        public double ArrivalRadius { get; set; }

        /// <summary>
        /// Composite facade exposing the target position as a single pickable value.
        /// The UI compiler targets this property to generate a single "Pick" button for
        /// the world-location pick flow, resolving the two-scalar impedance mismatch.
        /// Excluded from JSON serialization; <see cref="TargetLat"/> and
        /// <see cref="TargetLon"/> carry the wire representation.
        /// </summary>
        [JsonIgnore]
        [MapPickableWorldLocation]
        public PickableGeoPoint PickableLocation
        {
            get => new PickableGeoPoint(TargetLat, TargetLon);
            set
            {
                TargetLat = value.Latitude;
                TargetLon = value.Longitude;
            }
        }
    }
}
