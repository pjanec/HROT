using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ Buildings Stage 6 (<c>CE-1032</c>; 📄 docs/DESIGN_Building_Interiors.md §3k) — the authored contract of the curated TEST
    /// behaviour <c>HoldStance</c>: take <see cref="Stance"/> and hold it, doing nothing else — the passive targets of
    /// <c>bt-grenade-posture</c> and <c>bt-mortar-roof</c>.
    /// </summary>
    [BehaviorContract(BehaviorId, BehaviorCategory.AllMilitary | BehaviorCategory.Civilian)]
    public struct HoldStanceParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.HoldStance;

        /// <summary>Standing, Crouched or Prone.</summary>
        [JsonPropertyName("stance")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public StanceId Stance { get; set; }
    }
}
