using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2436</c> AU-5 (A-6) — the authored contract of a drone team's <c>DroneTeam</c>: its recon drone circles <see cref="Watch"/>, its
    /// operator flies the armed drones at <see cref="Strike"/> (unset (0, 0, 0) = no strike: the team only watches). A new order (the strike
    /// shifted) re-tasks them. 📄 docs/designs/group-maneuvers/DESIGN_Assault_Under_Fire.md §5 A-6, §9.6.
    /// </summary>
    [ParamsDto]
    public struct DroneTeamParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.DroneTeam;

        /// <summary>The area the recon drone circles (world m) — the objective.</summary>
        [JsonPropertyName("Watch")]
        public Vector3 Watch;

        /// <summary>Where the armed drones strike (world m) — the reported posts' centre; (0, 0, 0) = none.</summary>
        [JsonPropertyName("Strike")]
        public Vector3 Strike;
    }
}
