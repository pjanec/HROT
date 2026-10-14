using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2158</c> F-b (R-2013; 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.6.1) — the authored contract of a squad
    /// commander's <c>FireAndMovement</c>: in contact, the squad closes on <see cref="Objective"/> by halves, one suppressing while the
    /// other rushes to cover. A zero field keeps its default.
    /// </summary>
    [ParamsDto]
    public struct FireAndMovementParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.FireAndMovement;

        /// <summary>Where the squad closes to (sim metres, Z up) — usually the enemy's position.</summary>
        [JsonPropertyName("Objective")]
        public Vector3 Objective;

        /// <summary>The longest one rush (m); 0 = 15.</summary>
        [JsonPropertyName("RushMetres")]
        public float RushMetres;

        /// <summary>The movers' asked speed (m/s); 0 = 4. Their stance caps it.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>How close both halves must get to the objective for the drill to end (m); 0 = 20.</summary>
        [JsonPropertyName("ArrivalRadius")]
        public float ArrivalRadius;

        /// <summary>A rush counts as set after this long even if a mover never reaches his point (s); 0 = 12.</summary>
        [JsonPropertyName("RushTimeoutSeconds")]
        public float RushTimeoutSeconds;

        /// <summary>⭐ <c>CE-2158</c> F-d — rush by buddy pairs inside the moving half (one man up while his buddy fires); false = the
        /// whole half at once.</summary>
        [JsonPropertyName("Pairs")]
        public bool Pairs;
    }
}
