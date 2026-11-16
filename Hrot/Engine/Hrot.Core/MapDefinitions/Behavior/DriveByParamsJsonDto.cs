using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-4621</c> M-5 (IM-1, MV-12; 📄 docs/designs/vehicle-tactics/DESIGN_Motorcycle.md §2 / §5) — the authored contract of <c>DriveBy</c>,
    /// run by the BIKE: ride up to <see cref="Target"/>, pass it at <see cref="PassSpeed"/> while the pillion fires, then escape to
    /// <see cref="Escape"/> at speed. A zero field keeps its default.
    /// </summary>
    [ParamsDto]
    public struct DriveByParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.DriveBy;

        /// <summary>Whom the pillion fires at.</summary>
        [JsonPropertyName("Target")]
        public EntityLink Target;

        /// <summary>The pre-chosen escape point.</summary>
        [JsonPropertyName("Escape")]
        public PointLink Escape;

        /// <summary>The pass alongside (m/s); 0 = 4 (the 2–5 m/s of the reported drive-bys).</summary>
        [JsonPropertyName("PassSpeed")]
        public float PassSpeed;

        /// <summary>The ride up and the escape (m/s); 0 = 14.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>The pass starts this far from the target (m); 0 = 40.</summary>
        [JsonPropertyName("PassMetres")]
        public float PassMetres;
    }
}
