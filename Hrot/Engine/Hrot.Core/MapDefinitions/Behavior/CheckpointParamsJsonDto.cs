using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-4622</c> K-4 (Checkpoint CP-1, CP-1a, CP-2; 📄 docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md §2, §5b) — the authored contract of the
    /// <c>Checkpoint</c> squad play: the site is a ROUTE map graphic, <see cref="Approach"/>, drawn the way traffic comes IN — its LAST point is the STOP
    /// line, its last leg the inbound direction. A zero field keeps its default.
    /// </summary>
    [ParamsDto]
    public struct CheckpointParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.Checkpoint;

        /// <summary>The approach route (a map graphic): its last point is the stop line.</summary>
        [JsonPropertyName("Approach")]
        public EntityLink Approach;

        /// <summary>The search bay (world m); zero = beside the lane just past the stop line.</summary>
        [JsonPropertyName("SearchBay")]
        public Vector3 SearchBay;

        /// <summary>The stand-off (m) — how far the protected men stand behind the stop line; 0 = derived from <see cref="ExpectedCharge"/>.</summary>
        [JsonPropertyName("StandOff")]
        public float StandOff;

        /// <summary>The charge the checkpoint is built against (a munition TKB type); 0 = the explosive charge (a car bomb's).</summary>
        [JsonPropertyName("ExpectedCharge")]
        public long ExpectedCharge;

        /// <summary>The warning line, in seconds out (s); 0 = 15.</summary>
        [JsonPropertyName("WarningSeconds")]
        public float WarningSeconds;

        /// <summary>The walk to the posts (m/s); 0 = the default.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;
    }
}
