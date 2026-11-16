using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Toolkit.Replication;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-B1 (Minefields MF-5 as amended by MR-5, MR-6, MR-16; 📄 docs/designs/minefields/DESIGN_Minefields.md) — the contract of
    /// <c>MineStrike</c>, the reaction of a unit (or a vehicle) one of whose men or which itself set off a mine: halt, the casualty cared for where he
    /// lies, wait for the engineers. A zero field keeps its default.
    /// </summary>
    [ParamsDto]
    public struct MineStrikeParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.MineStrike;

        /// <summary>The struck field (none = unknown).</summary>
        [JsonPropertyName("Field")] public EntityLink Field;

        /// <summary>Where the mine went off (world m).</summary>
        [JsonPropertyName("At")]
        public Vector3 At;

        /// <summary>How long the unit holds before it may go on (s); 0 = <c>MineStrikeBehavior.DefaultHoldSeconds</c>. It goes on sooner once its
        /// side has MARKED the field.</summary>
        [JsonPropertyName("Hold")]
        public SimDuration Hold;
    }
}
