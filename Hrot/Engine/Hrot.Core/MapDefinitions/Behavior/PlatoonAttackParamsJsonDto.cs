using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>⭐ <c>CE-2277</c> (AT-1, R-2035) — which of the attack's presets.</summary>
    public enum PlatoonAttackPreset : byte
    {
        /// <summary>Support, approach, assault: open positions are assaulted through, buildings cleared.</summary>
        AttackPosition = 0,
        /// <summary>No base of fire (PL-1): approach and clear.</summary>
        ClearBuilding = 1,
    }

    /// <summary>
    /// ⭐ <c>CE-2277</c> DA-F (AT-1, AT-2; 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8f) — the authored contract of a
    /// platoon's <c>PlatoonAttack</c>: attack the AREA round <see cref="Position"/> — every enemy reported in it, building by building. A zero
    /// field keeps its default.
    /// </summary>
    [ParamsDto]
    public struct PlatoonAttackParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.PlatoonAttack;

        /// <summary>The area's centre (sim metres).</summary>
        [JsonPropertyName("Position")]
        public Vector3 Position;

        /// <summary>The area's radius (m); 0 = 60.</summary>
        [JsonPropertyName("SearchRadius")]
        public float SearchRadius;

        /// <summary>By NAME (<c>"AttackPosition"</c> · <c>"ClearBuilding"</c>) — ⚠ a number does not parse, and the order is dropped.</summary>
        [JsonPropertyName("Preset")]
        public PlatoonAttackPreset Preset;

        /// <summary>The squads' asked speed (m/s); 0 = their own.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;
    }
}
