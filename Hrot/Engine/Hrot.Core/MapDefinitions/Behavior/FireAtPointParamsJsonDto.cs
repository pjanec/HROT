using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ Buildings Stage 6 (<c>CE-1032</c>, R-225 W-8; 📄 docs/DESIGN_Building_Interiors.md §3k) — the authored contract of the curated
    /// <c>FireAtPoint</c> behaviour: fire mount <see cref="Mount"/> at the ground point (<see cref="X"/>, <see cref="Y"/>, <see cref="Z"/>)
    /// — a thrown grenade, a mortar. A STRUCT of unmanaged fields, so a host can also bind it by bytes.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct FireAtPointParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.FireAtPoint;

        /// <summary>The aim point (local metres, east).</summary>
        [JsonPropertyName("x")]
        public float X;

        /// <summary>The aim point (local metres, north).</summary>
        [JsonPropertyName("y")]
        public float Y;

        /// <summary>The aim point (local metres, up) — a roof, a floor; 0 = the ground.</summary>
        [JsonPropertyName("z")]
        public float Z;

        /// <summary>The mount to fire (its index in the type's weapon suite).</summary>
        [JsonPropertyName("mount")]
        public int Mount;

        /// <summary>Rounds to fire; 0 = until the ammunition runs out.</summary>
        [JsonPropertyName("rounds")]
        public int Rounds;

        /// <summary>Seconds between rounds.</summary>
        [JsonPropertyName("cooldownSeconds")]
        public float CooldownSeconds;
        /// <summary>⭐ Indirect fire IF-2 (FM-4) — sim time (s) before which nothing leaves: a battery's time on target. 0 = now.</summary>
        [JsonPropertyName("fireAt")]
        public double FireAt;
        /// <summary>⭐ Indirect fire IF-2 (IF-C) — the arc a fire solution prefers: 0 = Auto (low when it clears), 1 = Low, 2 = High.</summary>
        [JsonPropertyName("arc")]
        public int Arc;
    }
}
