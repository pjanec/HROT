using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2241</c> ② (CV-14, B7; docs/DESIGN_Civilians_And_Crowd_Control.md §5m PV-4) — the authored contract of <c>BatonCharge</c>:
    /// a policeman goes at the point and strikes the protesters within <see cref="Reach"/> of it for <see cref="Seconds"/>.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct BatonChargeParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.BatonCharge;

        [JsonPropertyName("x")]
        public float X;
        [JsonPropertyName("y")]
        public float Y;
        [JsonPropertyName("z")]
        public float Z;

        /// <summary>How far from the point a protester may stand and still be struck (m); 0 = 8.</summary>
        [JsonPropertyName("Reach")]
        public float Reach;

        /// <summary>How long the charge lasts (s); 0 = 10.</summary>
        [JsonPropertyName("Seconds")]
        public float Seconds;
    }
}
