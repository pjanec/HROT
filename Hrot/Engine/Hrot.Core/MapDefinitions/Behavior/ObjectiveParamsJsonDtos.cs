using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ScenarioRules;

namespace Hrot.Map.Definitions.Behavior
{
    // ⭐ rules S1 (📄 docs/DESIGN_Scenario_Rules.md §10.2) — the authored contracts of the objective KINDS. Every one begins with the same
    //   nested "common" block (title, subject, parent, required, scored, weight, points, deadlineSeconds, startActive, visibleSides).
    //   Zero is the default everywhere (0 = the kind's own default), like every params DTO here.

    /// <summary><c>ReachArea</c>: at least <see cref="MinMen"/> of the subject's men inside <see cref="Area"/>.</summary>
    [ParamsDto]
    public struct ReachAreaParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.ReachArea;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        [JsonPropertyName("area")] public AreaLink Area;
        /// <summary>0 = 1.</summary>
        [JsonPropertyName("minMen")] public int MinMen;
    }

    /// <summary><c>HoldArea</c>: held (≥ MinMen of the subject's men inside, ≤ MaxEnemy enemies) for HoldSeconds; <see cref="Capture"/> never ends.</summary>
    [ParamsDto]
    public struct HoldAreaParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.HoldArea;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        [JsonPropertyName("area")] public AreaLink Area;
        /// <summary>0 = 4.</summary>
        [JsonPropertyName("minMen")] public int MinMen;
        [JsonPropertyName("maxEnemy")] public int MaxEnemy;
        /// <summary>0 = 600.</summary>
        [JsonPropertyName("holdSeconds")] public float HoldSeconds;
        /// <summary>Repeatable: never ends, the Owner flips.</summary>
        [JsonPropertyName("capture")] public bool Capture;
    }

    /// <summary><c>ClearArea</c>: no more than MaxEnemy enemies inside for ForSeconds.</summary>
    [ParamsDto]
    public struct ClearAreaObjectiveParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.ClearAreaObjective;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        [JsonPropertyName("area")] public AreaLink Area;
        /// <summary>Neutral (0) = any side hostile to the subject.</summary>
        [JsonPropertyName("enemySide"), JsonConverter(typeof(JsonStringEnumConverter))] public ForceId EnemySide;
        [JsonPropertyName("maxEnemy")] public int MaxEnemy;
        /// <summary>0 = 30.</summary>
        [JsonPropertyName("forSeconds")] public float ForSeconds;
    }

    /// <summary><c>DestroyTargets</c>: <see cref="Count"/> of <see cref="Targets"/> (at most 8) destroyed.</summary>
    [ParamsDto]
    public struct DestroyTargetsParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.DestroyTargets;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        [JsonPropertyName("targets")] public EntityLink[]? Targets;
        /// <summary>0 = all of them.</summary>
        [JsonPropertyName("count")] public int Count;
    }

    /// <summary><c>Protect</c>: at least <see cref="MinSurvivors"/> of <see cref="Targets"/> (at most 8) stay alive; judged at the end.</summary>
    [ParamsDto]
    public struct ProtectParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.Protect;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        [JsonPropertyName("targets")] public EntityLink[]? Targets;
        /// <summary>0 = all of them.</summary>
        [JsonPropertyName("minSurvivors")] public int MinSurvivors;
    }

    /// <summary><c>NoHarm</c>: no more than MaxCount distinct <see cref="Protected"/> victims harmed by <see cref="BySide"/>; judged at the end.</summary>
    [ParamsDto]
    public struct NoHarmParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.NoHarm;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        /// <summary>The affiliation that must not be harmed (default Neutral).</summary>
        [JsonPropertyName("protected"), JsonConverter(typeof(JsonStringEnumConverter))] public ForceId Protected;
        /// <summary>Harm by this side; Neutral (0) = the subject's side.</summary>
        [JsonPropertyName("bySide"), JsonConverter(typeof(JsonStringEnumConverter))] public ForceId BySide;
        /// <summary>Harm by anybody (BySide ignored).</summary>
        [JsonPropertyName("anySide")] public bool AnySide;
        [JsonPropertyName("maxCount")] public int MaxCount;
    }

    /// <summary><c>WeightedMean</c>: a composite scoring its children (<c>ObjectiveLinks.Parent</c> = it) by weight.</summary>
    [ParamsDto]
    public struct WeightedMeanParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.WeightedMean;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        /// <summary>0 = 0.6.</summary>
        [JsonPropertyName("passScore")] public float PassScore;
    }

    /// <summary><c>AllMustPass</c>: a composite achieved when every child is.</summary>
    [ParamsDto]
    public struct AllMustPassParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.AllMustPass;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
    }
}
