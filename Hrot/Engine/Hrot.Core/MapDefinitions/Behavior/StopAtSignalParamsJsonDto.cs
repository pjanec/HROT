using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-4622</c> follow-up (R-2133's ⏭; 📄 docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md CP-2) — the contract of <c>StopAtSignal</c>:
    /// the checkpoint's stop line, where a signalled vehicle stops. Sent by the checkpoint at the signal (a reaction).
    /// </summary>
    [ParamsDto]
    public struct StopAtSignalParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.StopAtSignal;

        /// <summary>The stop line (world m).</summary>
        [JsonPropertyName("X")] public float X;
        [JsonPropertyName("Y")] public float Y;
    }
}
