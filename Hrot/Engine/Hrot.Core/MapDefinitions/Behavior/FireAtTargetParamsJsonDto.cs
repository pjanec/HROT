using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// JSON serialization DTO for the <c>FireAtTarget</c> behavior parameter block.
    /// </summary>
    [BehaviorContract(BehaviorId, BehaviorCategory.AllMilitary)]
    public sealed class FireAtTargetParamsJsonDto
    {
        public const string BehaviorId = "FireAtTarget";

        /// <summary>Network ID of the target entity. Remapped during scenario load.</summary>
        [JsonPropertyName("targetNetworkId")]
        [RemapNetworkId]
        [MapPickableEntity]
        public long TargetNetworkId { get; set; }

        /// <summary>Maximum number of rounds to fire.</summary>
        [JsonPropertyName("maxRounds")]
        public int MaxRounds { get; set; }

        /// <summary>Minimum cooldown between bursts, in seconds.</summary>
        [JsonPropertyName("cooldownSeconds")]
        public float CooldownSeconds { get; set; }
    }
}
