using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// JSON serialization DTO for the <c>PlatoonHillAttack</c> commander behavior.
    ///
    /// <para>Authored in the scenario editor. Parsed by
    /// <c>HillAttackCommanderNodes.ParsePlatoonHillAttackParams</c> at ingress time;
    /// never referenced from BTree hot-path nodes.</para>
    /// </summary>
    [BehaviorContract(BehaviorIds.PlatoonHillAttack_BT, BehaviorId, BehaviorCategory.Commander)]
    public sealed class PlatoonHillAttackParamsJsonDto
    {
        public const string BehaviorId = "PlatoonHillAttack";

        /// <summary>
        /// Nested geo/ENU point used for firing-line and baseline coordinates.
        /// Either <c>lat</c>/<c>lon</c> (geodetic, converted via IGeographicTransform)
        /// or <c>x</c>/<c>y</c> (ENU Cartesian fallback).
        /// </summary>
        public sealed class GeoPoint
        {
            /// <summary>Latitude in degrees (geodetic path).</summary>
            [JsonPropertyName("lat")]
            public double Lat { get; set; }

            /// <summary>Longitude in degrees (geodetic path).</summary>
            [JsonPropertyName("lon")]
            public double Lon { get; set; }

            /// <summary>X coordinate in ENU metres (Cartesian fallback).</summary>
            [JsonPropertyName("x")]
            public float X { get; set; }

            /// <summary>Y coordinate in ENU metres (Cartesian fallback).</summary>
            [JsonPropertyName("y")]
            public float Y { get; set; }
        }

        /// <summary>Start of the firing-line segment.</summary>
        [JsonPropertyName("firingLineStart")]
        public GeoPoint? FiringLineStart { get; set; }

        /// <summary>End of the firing-line segment.</summary>
        [JsonPropertyName("firingLineEnd")]
        public GeoPoint? FiringLineEnd { get; set; }

        /// <summary>Start of the baseline retreat segment.</summary>
        [JsonPropertyName("baselineStart")]
        public GeoPoint? BaselineStart { get; set; }

        /// <summary>End of the baseline retreat segment.</summary>
        [JsonPropertyName("baselineEnd")]
        public GeoPoint? BaselineEnd { get; set; }

        /// <summary>Spacing (metres) between adjacent firing-line slots. Defaults to 30 m.</summary>
        [JsonPropertyName("tankSpacing")]
        public float TankSpacing { get; set; }

        /// <summary>
        /// Network-stable ID of the target area polygon entity.
        /// Remapped by the Orchestrator when transitioning from a staging scenario.
        /// Resolved to a local ECS entity via <c>NetworkEntityMap</c> at parse time.
        /// </summary>
        [JsonPropertyName("targetAreaNetworkId")]
        [RemapNetworkId]
        [MapPickableEntity("tactical_graphics")]
        public long TargetAreaNetworkId { get; set; }
    }
}
