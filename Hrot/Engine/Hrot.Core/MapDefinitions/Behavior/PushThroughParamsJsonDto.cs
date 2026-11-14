using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2307</c> (CH-1, CH-2; 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §5b) — the authored contract of
    /// <c>PushThrough</c>: a carrier hit while it drives keeps going, <see cref="Metres"/> on along its heading, out of the kill zone. A zero
    /// field keeps its default.
    /// </summary>
    [ParamsDto]
    public struct PushThroughParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.PushThrough;

        /// <summary>How far on it drives (m); 0 = 150.</summary>
        [JsonPropertyName("Metres")]
        public float Metres;

        /// <summary>Its asked speed (m/s); 0 = 15.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>It gives up after this long (s); 0 = 30.</summary>
        [JsonPropertyName("TimeoutSeconds")]
        public float TimeoutSeconds;
    }
}
