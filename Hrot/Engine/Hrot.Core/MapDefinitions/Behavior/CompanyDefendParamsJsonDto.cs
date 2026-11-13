using System.Text.Json.Serialization;
using Fdp.Toolkit.Replication;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-7024</c> (City Battle §6, §11 C-1c) — the authored contract of a company's <c>CompanyDefend</c>: hold a line through
    /// <see cref="Position"/> facing <see cref="Threat"/> — one block (<c>PlatoonDefend</c>) per line platoon spread over
    /// <see cref="Frontage"/>, the last platoon (of three or more) the reserve behind; when half the blocks fail the company falls back
    /// <see cref="Depth"/> metres to the next line. A zero field keeps its default.
    /// </summary>
    [ParamsDto]
    public struct CompanyDefendParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.CompanyDefend;

        /// <summary>The line's middle (sim metres).</summary>
        [JsonPropertyName("Position")]
        public PointLink Position;

        /// <summary>Where the enemy comes from (sim metres); zero = north of <see cref="Position"/>.</summary>
        [JsonPropertyName("Threat")]
        public PointLink Threat;

        /// <summary>The line's width (m); 0 = 300.</summary>
        [JsonPropertyName("Frontage")]
        public float Frontage;

        /// <summary>How far behind the line the company falls back, and the reserve stands at half of it (m); 0 = 250.</summary>
        [JsonPropertyName("Depth")]
        public float Depth;

        /// <summary>The platoons' asked speed (m/s); 0 = their own.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;
    }
}
