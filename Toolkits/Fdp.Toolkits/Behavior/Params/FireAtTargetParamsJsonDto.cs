using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;

namespace Fdp.Toolkit.Behavior.Params
{
    /// <summary>
    /// JSON serialization DTO for the <c>FireAtTarget</c> behavior parameter block.
    /// JSON keys match what <c>MissionPanel.BuildFireAtTargetParams</c> produces.
    /// </summary>
    public class FireAtTargetParamsJsonDto
    {
        /// <summary>Network ID of the target entity. Remapped during scenario load.</summary>
        [JsonPropertyName("targetNetworkId")]
        [RemapNetworkId]
        public long TargetNetworkId { get; set; }

        /// <summary>Maximum number of rounds to fire.</summary>
        [JsonPropertyName("maxRounds")]
        public int MaxRounds { get; set; }

        /// <summary>Minimum cooldown between bursts, in seconds.</summary>
        [JsonPropertyName("cooldownSeconds")]
        public float CooldownSeconds { get; set; }
    }
}
