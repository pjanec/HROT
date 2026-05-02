using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Behavior.Params;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// JSON serialization DTO for the <c>MoveToLocation</c> behavior parameter block.
    /// </summary>
    [BehaviorContract(BehaviorIds.MoveTo_BT, BehaviorId, BehaviorCategory.AllMilitary | BehaviorCategory.Civilian)]
    public sealed class MoveToLocationParamsJsonDto
    {
        public const string BehaviorId = "MoveToLocation";

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
