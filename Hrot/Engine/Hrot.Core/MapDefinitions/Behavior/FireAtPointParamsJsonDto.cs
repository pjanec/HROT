using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ Buildings Stage 6 (<c>CE-1032</c>, R-225 W-8; 📄 docs/DESIGN_Building_Interiors.md §3k) — the authored contract of the curated
    /// <c>FireAtPoint</c> behaviour: fire mount <see cref="Mount"/> at the ground point (<see cref="X"/>, <see cref="Y"/>, <see cref="Z"/>)
    /// — a thrown grenade, a mortar. A STRUCT of unmanaged fields, so a host can also bind it by bytes.
    /// </summary>
    [BehaviorContract(BehaviorId, BehaviorCategory.AllMilitary)]
    public struct FireAtPointParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.FireAtPoint;

        /// <summary>The aim point (local metres, east).</summary>
        [JsonPropertyName("x")]
        public float X { get; set; }

        /// <summary>The aim point (local metres, north).</summary>
        [JsonPropertyName("y")]
        public float Y { get; set; }

        /// <summary>The aim point (local metres, up) — a roof, a floor; 0 = the ground.</summary>
        [JsonPropertyName("z")]
        public float Z { get; set; }

        /// <summary>The mount to fire (its index in the type's weapon suite).</summary>
        [JsonPropertyName("mount")]
        public int Mount { get; set; }

        /// <summary>Rounds to fire; 0 = until the ammunition runs out.</summary>
        [JsonPropertyName("rounds")]
        public int Rounds { get; set; }

        /// <summary>Seconds between rounds.</summary>
        [JsonPropertyName("cooldownSeconds")]
        public float CooldownSeconds { get; set; }
    }
}
