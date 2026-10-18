using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-3160</c> (L4, R-266) — the authored contract of the library's <c>CombatPosture</c>: where to advance and how fast.
    /// Every other value is the behaviour's own default (📄 docs/DESIGN_Decision_Layer.md §3.3h). ⚠ The shape is the one every
    /// scenario already writes — <c>{"advance":{"Objective":[x,y,z],"Speed":3,"ArrivalRadius":5,"CooldownSeconds":1}}</c> — so the
    /// retired JSON tree's payloads load unchanged. A zero field keeps the default.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct CombatPostureParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.CombatPosture;

        /// <summary>The advance-and-attack leg.</summary>
        [JsonPropertyName("advance")]
        public CombatPostureAdvanceJsonDto Advance;

        /// <summary>⭐ AQ89 D (R-269) — the order is an ASSAULT: at a hidden enemy the unit closes to throw under its own fire when
        /// its OBEDIENCE allows (an ordinary soldier's does), instead of its own bravery deciding. Default false.</summary>
        [JsonPropertyName("assault")]
        public bool Assault;

        /// <summary>⭐ <c>CE-3170</c> (<c>docs/DESIGN_Entity_Control.md</c> C-5) — an ATTACK order's target: the unit fights THIS enemy
        /// (a nearer threat does not take over) and the posture succeeds when it is down. With no objective the advance closes on
        /// it. An <see cref="EntityRef"/>: picked on the map, remapped at scenario load; JSON is the bare network id. Default none.</summary>
        [JsonPropertyName("target")]
        public EntityRef Target;

        /// <summary>⭐ <c>CE-2297</c> LD-3e — a point he WATCHES toward (sim metres, Z up) while no threat and no sector of his own point his
        /// eyes; scanned ±30°. Zero = none. 📄 docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md §3.3a.</summary>
        [JsonPropertyName("watchTowards")]
        public Vector3 WatchTowards;
    }

    /// <summary>⭐ <c>CE-3160</c> — the advance of <see cref="CombatPostureParamsJsonDto"/>.</summary>
    public struct CombatPostureAdvanceJsonDto
    {
        /// <summary>Where to advance to (sim metres, Z up).</summary>
        [JsonPropertyName("Objective")]
        public Vector3 Objective;

        /// <summary>The asked speed (m/s); 0 = 3. The body's stance caps it (AQ88).</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>How close counts as arrived (m); 0 = 5.</summary>
        [JsonPropertyName("ArrivalRadius")]
        public float ArrivalRadius;

        /// <summary>Seconds between shots while advancing; 0 = 1.</summary>
        [JsonPropertyName("CooldownSeconds")]
        public float CooldownSeconds;
    }
}
