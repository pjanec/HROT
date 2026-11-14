using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2159</c> X-b (R-2014; 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.7.6) — the authored contract of a squad
    /// commander's <c>SquadDangerCrossing</c>: the area is the commander's danger-area sensor's next one; these shape the crossing. A
    /// zero field keeps its default.
    /// </summary>
    [ParamsDto]
    public struct SquadDangerCrossingParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.SquadDangerCrossing;

        /// <summary>Lanes across the crossing line, the next pair on the next one; 0 = 4.</summary>
        [JsonPropertyName("Lanes")]
        public int Lanes;

        /// <summary>The distance between lanes (m); 0 = 4.</summary>
        [JsonPropertyName("LaneSpacing")]
        public float LaneSpacing;

        /// <summary>The near-side spots stand this far short of the area's near handle (m); 0 = 3.</summary>
        [JsonPropertyName("BackMetres")]
        public float BackMetres;

        /// <summary>The far-side spots stand this far past its far handle (m); 0 = 4.</summary>
        [JsonPropertyName("BeyondMetres")]
        public float BeyondMetres;

        /// <summary>The walking speed to the spots (m/s); 0 = 2.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>The crossers' speed (m/s); 0 = 4. Their stance caps it.</summary>
        [JsonPropertyName("CrossSpeed")]
        public float CrossSpeed;

        /// <summary>A phase whose gate does not hold in this long moves on anyway (s); 0 = 40.</summary>
        [JsonPropertyName("PhaseTimeoutSeconds")]
        public float PhaseTimeoutSeconds;
    }
}
