using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2294</c> CC-D — the authored contract of a platoon's <c>PlatoonHold</c> reaction: every squad holds while the platoon's
    /// picture holds an enemy element at least <see cref="MinSize"/>, within <see cref="WithinMetres"/>, observed within
    /// <see cref="FreshSeconds"/>. A zero field keeps its default. 📄 docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md §6, §8a.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct PlatoonHoldParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.PlatoonHold;

        /// <summary>The smallest element that keeps the hold: 1 Individual · 2 Team · 3 Squad · 4 Platoon; 0 = 2 (Team).</summary>
        [JsonPropertyName("MinSize")]
        public int MinSize;

        /// <summary>Within this many metres of the platoon (m); 0 = 400.</summary>
        [JsonPropertyName("WithinMetres")]
        public float WithinMetres;

        /// <summary>Observed within this many seconds (s); 0 = 30.</summary>
        [JsonPropertyName("FreshSeconds")]
        public float FreshSeconds;
    }
}
