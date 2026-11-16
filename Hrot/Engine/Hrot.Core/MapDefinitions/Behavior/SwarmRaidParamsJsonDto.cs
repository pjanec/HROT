using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-4621</c> M-6 (IM-5, MV-12; 📄 docs/designs/vehicle-tactics/DESIGN_Motorcycle.md §2 / §5) — the authored contract of <c>SwarmRaid</c>,
    /// run by a COMMANDER whose roster is bikes: each bike rides to its own approach point around <see cref="Objective"/> (N directions), all
    /// converge on it (the pillions fire on the approach — their own weapon brains, bounded by the firing seat), hold and fight for
    /// <see cref="Fight"/>, then DISPERSE: each rides out on its own bearing, then to <see cref="Rally"/>. A zero field keeps its default.
    /// </summary>
    [ParamsDto]
    public struct SwarmRaidParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.SwarmRaid;

        [JsonPropertyName("Objective")]
        public PointLink Objective;

        [JsonPropertyName("Rally")]
        public PointLink Rally;

        /// <summary>The approach ring's radius (m); 0 = 300.</summary>
        [JsonPropertyName("ApproachMetres")]
        public float ApproachMetres;

        /// <summary>The fight at the objective; 0 = 60 s.</summary>
        [JsonPropertyName("Fight")]
        public SimDuration Fight;

        /// <summary>The ride (m/s); 0 = 14.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;
    }
}
