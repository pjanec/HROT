using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2241</c> ① (AW-5, AW-21; docs/DESIGN_Civilians_And_Crowd_Control.md §5h.5) — the authored contract of <c>Surrender</c>: he stops, lays
    /// every weapon down and raises his hands; after <see cref="Seconds"/> he takes them up again and the behaviour finishes, so the next
    /// mission task resumes (the feint). 0 = he stays surrendered.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct SurrenderParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.Surrender;

        /// <summary>How long he stays surrendered (s); 0 = for good.</summary>
        [JsonPropertyName("Seconds")]
        public float Seconds;
    }
}
