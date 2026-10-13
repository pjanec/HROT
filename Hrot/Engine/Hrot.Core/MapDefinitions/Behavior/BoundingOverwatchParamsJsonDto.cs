using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-3160</c> / <c>CE-507</c> D2 (a) (R-264) — the authored contract of a squad commander's <c>BoundingOverwatch</c>: the
    /// squad moves to <see cref="Objective"/> in bounds, one element moving while the other covers. A zero field keeps its default.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Squad_Wiring.md §6.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct BoundingOverwatchParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.BoundingOverwatch;

        /// <summary>Where the squad goes (sim metres, Z up).</summary>
        [JsonPropertyName("Objective")]
        public Vector3 Objective;

        /// <summary>How far one bound carries the moving element (m); 0 = 30.</summary>
        [JsonPropertyName("BoundMetres")]
        public float BoundMetres;

        /// <summary>The members' asked speed (m/s); 0 = 3. Their stance caps it.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>How close both elements must get to the objective (m); 0 = 10.</summary>
        [JsonPropertyName("ArrivalRadius")]
        public float ArrivalRadius;

        /// <summary>A bound is complete after this long even if a mover never arrives (s); 0 = 60.</summary>
        [JsonPropertyName("BoundTimeoutSeconds")]
        public float BoundTimeoutSeconds;
    }
}
