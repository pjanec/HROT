using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Replication;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2152</c> C-c (R-c; 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.5) — the authored contract of the commander's
    /// library <c>CasualtyCare</c>: the squad covers, the medic brings a man who is down to cover. Every field may be left out.
    /// </summary>
    [ParamsDto]
    public struct CasualtyCareParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.CasualtyCare;

        /// <summary>The man to care for; none ⇒ the first member who is down and not yet cared for.</summary>
        [JsonPropertyName("casualty")]
        public EntityLink Casualty;

        /// <summary>Walking speed (m/s); 0 ⇒ 3 m/s.</summary>
        [JsonPropertyName("speed")]
        public float Speed;

        /// <summary>How far around the casualty to look for cover (m); 0 ⇒ 25 m.</summary>
        [JsonPropertyName("coverSearchRadius")]
        public float CoverSearchRadius;

        /// <summary>⭐ <c>CE-7009</c> (lane default R-7009) — a man down is carried this far straight back, away from the threat (the collection
        /// point; the showcase's 150 m), instead of to the nearest cover; 0 = off (cover).</summary>
        [JsonPropertyName("evacuateMetres")]
        public float EvacuateMetres;
    }
}
