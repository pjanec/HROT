using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Behavior.Params;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// JSON serialization DTO for the <c>PlatoonHillAttack</c> commander behavior.
    ///
    /// <para>Authored in the scenario editor. Parsed by
    /// <c>HillAttackCommanderNodes.ParsePlatoonHillAttackParams</c> at ingress time;
    /// never referenced from BTree hot-path nodes.</para>
    /// </summary>
    [BehaviorContract(BehaviorId, BehaviorCategory.Commander)]
    public sealed class PlatoonHillAttackParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.PlatoonHillAttack;

        /// <summary>Start of the firing-line segment.</summary>
        [JsonPropertyName("firingLineStart")]
        public PickableGeoPoint FiringLineStart { get; set; }

        /// <summary>End of the firing-line segment.</summary>
        [JsonPropertyName("firingLineEnd")]
        public PickableGeoPoint FiringLineEnd { get; set; }

        /// <summary>Start of the baseline retreat segment.</summary>
        [JsonPropertyName("baselineStart")]
        public PickableGeoPoint BaselineStart { get; set; }

        /// <summary>End of the baseline retreat segment.</summary>
        [JsonPropertyName("baselineEnd")]
        public PickableGeoPoint BaselineEnd { get; set; }

        /// <summary>Spacing (metres) between adjacent firing-line slots. Defaults to 30 m.</summary>
        [JsonPropertyName("tankSpacing")]
        public float TankSpacing { get; set; } = 30f;

        /// <summary>
        /// Network-stable ID of the target area polygon entity.
        /// Remapped by the Orchestrator when transitioning from a staging scenario.
        /// Resolved to a local ECS entity via <c>NetworkEntityMap</c> at parse time.
        /// </summary>
        [JsonPropertyName("targetAreaNetworkId")]
        [MapPickableEntity("tactical_graphics")]   // ⭐ narrows the picker; the TYPE makes it pickable and remapped
        public EntityRef TargetAreaNetworkId { get; set; }
    }
}
