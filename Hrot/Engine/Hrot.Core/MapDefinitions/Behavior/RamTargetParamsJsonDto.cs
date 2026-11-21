using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-4622</c> K-2 (Checkpoint RM-2, RM-3; 📄 docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md §2) — the authored contract of <c>RamTarget</c>, run by
    /// the VEHICLE: drive INTO <see cref="Target"/> (a body), or into the crowd at <see cref="At"/> when there is no target. A zero field keeps its default.
    /// </summary>
    [ParamsDto]
    public struct RamTargetParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.RamTarget;

        /// <summary>The body to ram; none = the crowd at <see cref="At"/>.</summary>
        [JsonPropertyName("Target")]
        public EntityLink Target;

        /// <summary>The crowd's place (world m), when there is no <see cref="Target"/>.</summary>
        [JsonPropertyName("At")]
        public Vector3 At;

        /// <summary>The ram's speed (m/s); 0 = 16.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;
    }
}
