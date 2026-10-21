using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Replication;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2347</c> (R-2069) — the authored contract of a platoon's <c>PlatoonConvoy</c>: its squads ride the vehicles they sit in along
    /// <see cref="Route"/>; on contact the vehicles halt and the squads fight; after <see cref="ClearSeconds"/> of quiet the squads secure
    /// around their vehicles, remount with their wounded and the convoy carries on. A zero field keeps its default.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §11a.
    /// </summary>
    [ParamsDto]
    public struct PlatoonConvoyParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.PlatoonConvoy;

        /// <summary>The route the vehicles drive (a route entity's network id).</summary>
        [JsonPropertyName("Route")]
        [MapPickableEntity("road_graphs")]
        public EntityRef Route;

        /// <summary>The vehicles' speed on the route, and the squads' on foot (m/s); 0 = each one's own default.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>How long nothing must be seen before the squads secure and remount (s); 0 = 30.</summary>
        [JsonPropertyName("ClearSeconds")]
        public float ClearSeconds;
    }
}
