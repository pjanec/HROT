using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2239</c> part 2 (CV-11, FL-5; docs/DESIGN_Civilians_And_Crowd_Control.md §5j) — the authored contract of <c>HoldTheLine</c>: a
    /// policeman holds the post where the behaviour started, engages the nearest Hostile within <see cref="Reach"/> and strikes with the baton
    /// within arm's reach. A zero keeps its default.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct HoldTheLineParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.HoldTheLine;

        /// <summary>How far he engages a Hostile from (m); 0 = 40.</summary>
        [JsonPropertyName("Reach")]
        public float Reach;

        /// <summary>How far he lets himself be drawn from his post before he walks back (m); 0 = 3.</summary>
        [JsonPropertyName("Hold")]
        public float Hold;
    }
}
