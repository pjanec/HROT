using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2238</c> part 5 (AW-12, B5; docs/DESIGN_Civilians_And_Crowd_Control.md §5l BM-1) — the authored contract of
    /// <c>DetonateCharge</c>: he stands still <see cref="Seconds"/>, then sets off the charge he carries. 0 seconds = at once.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct DetonateChargeParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.DetonateCharge;

        /// <summary>How long he stands still before he sets it off (s); 0 = at once.</summary>
        [JsonPropertyName("Seconds")]
        public float Seconds;
    }
}
