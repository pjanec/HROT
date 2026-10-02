using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;

namespace Fdp.Toolkit.Behavior.Tests.Fixtures
{
    // ⭐ CE-447 (2026-09-30): a TEST FIXTURE, moved out of Fdp.Toolkits production. The one authored contract is
    //   Hrot.Core's [BehaviorContract] class of the same name; FDP tests cannot reference Hrot.Core, so they
    //   carry this attribute-bearing stand-in for the FDP-level mechanisms (remapper, presentation attributes).
    /// <summary>
    /// JSON serialization DTO for the <c>FireAtTarget</c> behavior parameter block.
    /// JSON keys match what <c>MissionPanel.BuildFireAtTargetParams</c> produces.
    /// </summary>
    public class FireAtTargetParamsJsonDto
    {
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
