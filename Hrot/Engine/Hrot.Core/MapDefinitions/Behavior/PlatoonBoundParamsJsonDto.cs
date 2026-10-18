using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2294</c> CC-D — the authored contract of a platoon's <c>PlatoonBound</c>: the support squad moves to
    /// <see cref="SupportPoint"/>; once it reports SET the assault squad moves to <see cref="Objective"/>; the task is done when the assault
    /// squad reports SET. A squad is named by its place in the platoon's roster, 1-based (0 keeps the default). A zero field keeps its
    /// default. 📄 docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md §1, §6, §8a.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct PlatoonBoundParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.PlatoonBound;

        /// <summary>Where the support squad goes (sim metres, Z up).</summary>
        [JsonPropertyName("SupportPoint")]
        public Vector3 SupportPoint;

        /// <summary>Where the assault squad goes once the support squad is set (sim metres, Z up).</summary>
        [JsonPropertyName("Objective")]
        public Vector3 Objective;

        /// <summary>The support squad's place in the roster, 1-based; 0 = 2 (Bravo).</summary>
        [JsonPropertyName("SupportSquad")]
        public int SupportSquad;

        /// <summary>The assault squad's place in the roster, 1-based; 0 = 1 (Alpha).</summary>
        [JsonPropertyName("AssaultSquad")]
        public int AssaultSquad;

        /// <summary>The squads' asked speed (m/s); 0 = each squad's own default.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>How near counts as there (m); 0 = each squad's own default.</summary>
        [JsonPropertyName("ArrivalRadius")]
        public float ArrivalRadius;

        /// <summary>The extra report asked of each squad with its order (CC-2, CC-8): a SITREP this often (s); 0 = none.</summary>
        [JsonPropertyName("SitrepEverySeconds")]
        public float SitrepEverySeconds;
    }
}
