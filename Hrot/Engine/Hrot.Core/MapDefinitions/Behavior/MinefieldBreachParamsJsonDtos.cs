using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Toolkit.Replication;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-B3 (Minefields MF-9, MR-11, MR-15) — the contract of <c>ClearLane</c>, run by the mine clearer (TKB 116): drive to
    /// <see cref="Start"/>, switch the plough / flail on, drive the lane to <see cref="End"/> at the tool's pace, switch it off. ⛔ It never writes the
    /// field: the engine's serial swath clear clears the mines under an active tool (terrain MS-T4). A zero field keeps its default.
    /// </summary>
    [ParamsDto]
    public struct ClearLaneParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.ClearLane;
        /// <summary>The field: a GUID (the scenario's), <c>"@Key"</c> or a bare network id.</summary>
        [JsonPropertyName("Field")] public EntityLink Field;
        [JsonPropertyName("Start")] public Vector3 Start;
        [JsonPropertyName("End")] public Vector3 End;
        /// <summary>The swath (m); 0 = 4.5.</summary>
        [JsonPropertyName("Width")] public float Width;
        /// <summary>The tool's pace in the lane (m/s); 0 = 0.5 (a flail).</summary>
        [JsonPropertyName("Pace")] public float Pace;
        /// <summary>The drive to the lane's start (m/s); 0 = 5.</summary>
        [JsonPropertyName("Speed")] public float Speed;
        /// <summary>The tool (terrain's <c>MineClearingTool</c>): 1 plough, 2 flail; 0 = flail.</summary>
        [JsonPropertyName("Tool")] public byte Tool;
    }

    /// <summary>⭐ <c>CE-4624</c> MS-B3 (MF-11) — the contract of <c>CrossLane</c>, a squad's crossing of a cleared, marked lane in SINGLE FILE: assemble on
    /// the lane's axis short of <see cref="Start"/>, then each man after the one ahead is <see cref="Spacing"/> on, straight down the axis past
    /// <see cref="End"/>.</summary>
    [ParamsDto]
    public struct CrossLaneParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.CrossLane;
        [JsonPropertyName("Start")] public Vector3 Start;
        [JsonPropertyName("End")] public Vector3 End;
        /// <summary>The gap between men (m); 0 = 5.</summary>
        [JsonPropertyName("Spacing")] public float Spacing;
        /// <summary>The walk (m/s); 0 = 1.5.</summary>
        [JsonPropertyName("Speed")] public float Speed;
    }

    /// <summary>⭐ <c>CE-4624</c> MS-B3 (MF-11) — the contract of <c>Breach</c>, a commander's play through a minefield: the support units fire on the
    /// FAR side, smoke is asked there, the clearer (the roster's vehicle) cuts the lane <see cref="Start"/> → <see cref="End"/>, then the assault unit
    /// (the roster's last foot unit) crosses it in single file.</summary>
    [ParamsDto]
    public struct BreachParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.Breach;
        /// <summary>The field: a GUID (the scenario's), <c>"@Key"</c> or a bare network id.</summary>
        [JsonPropertyName("Field")] public EntityLink Field;
        [JsonPropertyName("Start")] public Vector3 Start;
        [JsonPropertyName("End")] public Vector3 End;
        /// <summary>The swath (m); 0 = 4.5.</summary>
        [JsonPropertyName("Width")] public float Width;
    }
}
