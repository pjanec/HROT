using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// JSON serialization DTO for the <c>FireAtTarget</c> behavior parameter block.
    /// </summary>
    /// <remarks>⭐ <c>CE-2023</c> ③ (<c>DESIGN_Unified_Behaviour_Run.md</c> "S8n"): a STRUCT, so a host can bind it — a
    /// blueprint's Behaviour Task passes its bytes and the typed resolver's from-bytes arm converts them. ⛔ It was a class,
    /// which the from-bytes arm cannot take. Same properties and JSON keys.</remarks>
    [BehaviorContract(BehaviorId, BehaviorCategory.AllMilitary)]
    public struct FireAtTargetParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.FireAtTarget;

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
