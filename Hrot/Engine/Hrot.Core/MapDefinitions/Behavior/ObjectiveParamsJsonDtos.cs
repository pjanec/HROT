using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.ReplayBrowser.Search;
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
        /// <summary>⭐ S6 G-1 — with <see cref="Capture"/>: &gt; 0 = the point is taken BY SIDE — the side with more men inside (every man of the side, not the
        /// subject's roster) than the other, and at least <see cref="MinMen"/>, for this many seconds becomes the Owner; the moment the other side outnumbers
        /// the owner the point is Neutral. 0 = the S1 rule (instant, the subject's roster).</summary>
        [JsonPropertyName("captureSeconds")] public float CaptureSeconds;
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
        /// <summary>⭐ CE-4155 (SR-82) — whose side is judged for a RETHROWN round's harm: <c>last</c> (default; <c>DamageAssessedEvent.Source</c>), <c>original</c>
        /// (the first thrower, <c>OriginalSource</c>, else the source), <c>both</c> (either side counts).</summary>
        [JsonPropertyName("attribute"), JsonConverter(typeof(JsonStringEnumConverter))] public HarmAttribute Attribute;
    }

    /// <summary>⭐ CE-4155 (SR-82) — which source of a harm a <c>NoHarm</c> judges. <c>Last</c> = 0 so an absent param keeps the old behaviour.</summary>
    public enum HarmAttribute : byte { Last = 0, Original = 1, Both = 2 }

    /// <summary>⭐ S6 G-2 — one death rule of a <see cref="CounterParamsJsonDto"/>: each man of <see cref="Side"/> newly OUT of the fight adds <see cref="Per"/> to the value.</summary>
    public struct CounterDeathsJsonDto
    {
        [JsonPropertyName("side"), JsonConverter(typeof(JsonStringEnumConverter))] public ForceId Side;
        /// <summary>Added per man down (tickets: -1).</summary>
        [JsonPropertyName("per")] public float Per;
    }

    /// <summary>
    /// ⭐ S6 G-2 (📄 docs/DESIGN_Scenario_Rules.md §00.12) — <c>Counter</c>, a MEASURE (role Measure): <c>Value</c> starts at <see cref="Start"/> (also its
    /// <c>Target</c>, so the score is value / start); <c>AddToCounter</c> moves it; <see cref="Deaths"/> (at most two sides) moves it by <c>per</c> for every man of the side
    /// newly out of the fight (counted once; a revived man refunds nothing). Clamped to [<see cref="Floor"/>, <see cref="Ceiling"/>].
    /// </summary>
    [ParamsDto]
    public struct CounterParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.Counter;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        [JsonPropertyName("start")] public float Start;
        /// <summary>At most two entries (Friend, Hostile); a Neutral side is ignored.</summary>
        [JsonPropertyName("deaths")] public CounterDeathsJsonDto[]? Deaths;
        /// <summary>The value never goes below this; null = 0 (a lead counter sets a negative one).</summary>
        [JsonPropertyName("floor")] public float? Floor;
        /// <summary>The value never goes above this; null = no cap.</summary>
        [JsonPropertyName("ceiling")] public float? Ceiling;
    }

    // ⭐ rules S4 E-2 (📄 docs/DESIGN_Scenario_Rules.md §00.11, §10.3) — the MEASURES of what the subject's men DID. Every one is a Measure (Value against Target, Score by
    //   ObjectiveRules.Score); `target` null = the kind's own default. ⛔ not the Counter (a pool the scenario moves).

    /// <summary><c>HitRatio</c>: hits ÷ shots by the subject's men (default target 0.30); <see cref="Attribute"/> says which source of a rethrown round's hit counts (CE-4155).</summary>
    [ParamsDto]
    public struct HitRatioParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.HitRatio;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        [JsonPropertyName("attribute"), JsonConverter(typeof(JsonStringEnumConverter))] public HarmAttribute Attribute;
        [JsonPropertyName("target")] public float? Target;
    }

    /// <summary>What a <c>Count</c> counts. <c>Assists</c> (rules S4 ⑥): a hostile the subject's men hit and somebody else finished, within <c>assistSeconds</c> of the subject's last hit — once per victim.</summary>
    public enum CountWhat : byte { Shots = 0, Hits = 1, Kills = 2, Fratricide = 3, CiviliansHarmed = 4, Reports = 5, Assists = 6 }

    /// <summary><c>Count</c>: how many <see cref="What"/> by the subject's men. fratricide / civiliansHarmed are lower-is-better with target 0; the others higher-is-better with target
    /// <see cref="Target"/> (default 1).</summary>
    [ParamsDto]
    public struct CountParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.Count;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        [JsonPropertyName("what"), JsonConverter(typeof(JsonStringEnumConverter))] public CountWhat What;
        [JsonPropertyName("attribute"), JsonConverter(typeof(JsonStringEnumConverter))] public HarmAttribute Attribute;
        [JsonPropertyName("target")] public float? Target;
        /// <summary>⭐ rules S4 ⑥ — <c>assists</c> only: the window (s) before the kill inside which the subject's last hit still earns the assist; 0 = 10.</summary>
        [JsonPropertyName("assistSeconds")] public float AssistSeconds;
    }

    /// <summary><c>ReportLatency</c>: the mean of <c>SentAt − ObservedAt</c> of the subject's reports so far (lower is better; default target 60 s).</summary>
    [ParamsDto]
    public struct ReportLatencyParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.ReportLatency;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        [JsonPropertyName("target")] public float? Target;
    }

    /// <summary><c>TimeTo</c>: the scenario time <see cref="Objective"/> became Achieved (lower is better); never achieved ⇒ Value = now, score 0. <see cref="Target"/> in seconds, default 0.</summary>
    [ParamsDto]
    public struct TimeToParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.TimeTo;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        [JsonPropertyName("objective")] public EntityLink Objective;
        [JsonPropertyName("target")] public float? Target;
    }

    /// <summary>How a <c>Sample</c> folds its readings.</summary>
    public enum SampleAggregate : byte { Mean = 0, Max = 1, TimeAbove = 2 }

    /// <summary><c>Sample</c> (SR-20): a registered <c>[UtilityInput("name")]</c> read on the subject every tick it is Active — the <see cref="Aggregate"/> mean / max / seconds above
    /// <see cref="Threshold"/> is the Value. <see cref="Target"/> default 1; <see cref="LowerIsBetter"/> for an input like stress.</summary>
    [ParamsDto]
    public struct SampleParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.Sample;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        [JsonPropertyName("input")] public string Input;
        [JsonPropertyName("aggregate"), JsonConverter(typeof(JsonStringEnumConverter))] public SampleAggregate Aggregate;
        [JsonPropertyName("threshold")] public float Threshold;
        [JsonPropertyName("target")] public float? Target;
        [JsonPropertyName("lowerIsBetter")] public bool LowerIsBetter;
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

    /// <summary>⭐ rules S4 E-1 (SR-72 / SR-81) — <c>MemberEvaluation</c>: a composite over the men of its <c>common.subject</c> (a commander). For each member it inserts ONE macro
    /// from <see cref="Library"/> (a folder under <c>macros/</c>): <c>overrides[@RefKey]</c> → <c>&lt;library&gt;/&lt;RefKey&gt;</c> → <c>&lt;library&gt;/&lt;TacticalDesignation&gt;</c> →
    /// <c>&lt;library&gt;/&lt;entity class&gt;</c> → <c>&lt;library&gt;/Default</c>, the first the node's macro library HAS. <see cref="Overrides"/> maps <c>"@Key"</c> / a role / a class
    /// name to a macro path (a flat map).</summary>
    [ParamsDto]
    public struct MemberEvaluationParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.MemberEvaluation;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        /// <summary>The macro folder, e.g. <c>macros/evaluation/infantry</c> (the leading <c>macros/</c> is optional).</summary>
        [JsonPropertyName("library")] public string Library;
        [JsonPropertyName("overrides")] public Dictionary<string, string>? Overrides;
    }

    /// <summary>⭐ rules S4 ① (§00.11) — <c>SelectionMean</c>: the weighted mean (the ONE <c>WeightedMeanBehavior.Aggregate</c>) of a SELECTION of the statuses under the
    /// <see cref="Scope"/> (a <c>MemberEvaluation</c>), not of its parent-linked children: <c>by: "role"</c> = the member roots whose subject's role key is <see cref="Key"/>;
    /// <c>by: "standard"</c> = every standard titled <see cref="Key"/> in the members' subtrees (a nested evaluation's men excluded). Pass mark: <c>common.pass</c>, else 0.6.</summary>
    [ParamsDto]
    public struct SelectionMeanParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.SelectionMean;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        /// <summary><c>role</c> or <c>standard</c>.</summary>
        [JsonPropertyName("by")] public string By;
        /// <summary>The role key (a <c>TacticalDesignation</c> name, else the TKB class name) or the standard's title.</summary>
        [JsonPropertyName("key")] public string Key;
        /// <summary>The <c>MemberEvaluation</c> whose members are selected from.</summary>
        [JsonPropertyName("scope")] public EntityLink Scope;
    }

    /// <summary><c>AllMustPass</c>: a composite achieved when every child is.</summary>
    [ParamsDto]
    public struct AllMustPassParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.AllMustPass;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
    }
    /// <summary>
    /// ⭐ rules S2-c (📄 docs/DESIGN_Scenario_Rules.md §10.11, §00.9) — <c>Trigger</c>: when <see cref="When"/> RISES (false → true) wait out
    /// <see cref="HoldFor"/>, then <see cref="Delay"/>, then run the <see cref="Do"/> list in order; once, or <see cref="Repeat"/> (a new edge after
    /// <see cref="Cooldown"/>) up to <see cref="MaxFires"/>. <c>common.scored</c> defaults to FALSE for a trigger (T8); <c>common.startActive</c> false = Disarmed
    /// until a <c>SetObjectiveActive</c> arms it.
    /// <para>⭐ <c>when</c> and <c>do</c> are kept as the caller's JSON (in <c>ActionArgs</c>, items 0 and 1) and refused at ASSIGNMENT when they do not
    /// compile / validate (CE-4163).</para>
    /// </summary>
    [ParamsDto]
    public struct TriggerParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.Trigger;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        /// <summary>The condition — a polymorphic search predicate (<c>{"$type":"OnEntity",...}</c>), evaluated as a LEVEL each tick.</summary>
        [JsonPropertyName("when")] public SearchPredicateDto? When;
        /// <summary>The condition must hold this long (sim s) before it counts; 0 = no debounce.</summary>
        [JsonPropertyName("holdFor")] public float HoldFor;
        /// <summary>Fire this long (sim s) after the edge (or the hold), even if the condition drops meanwhile; 0 = at once.</summary>
        [JsonPropertyName("delay")] public float Delay;
        /// <summary>A repeating trigger re-arms no sooner than this (sim s) after it fired.</summary>
        [JsonPropertyName("cooldown")] public float Cooldown;
        /// <summary>Re-arm after firing (needs the condition to go false again); default once.</summary>
        [JsonPropertyName("repeat")] public bool Repeat;
        /// <summary>A repeating trigger stops after this many fires; 0 = unlimited.</summary>
        [JsonPropertyName("maxFires")] public int MaxFires;
        /// <summary>CE-4166 — a condition already TRUE when the trigger arms counts as an edge (default: it does not).</summary>
        [JsonPropertyName("fireIfTrueWhenArmed")] public bool FireIfTrueWhenArmed;
        /// <summary>⭐ S6 G-4 — with <see cref="Repeat"/>: when the cooldown ends and the condition is STILL true, fire again (the periodic rule: "every 10 s while held");
        /// without it a repeat needs the condition to go false and rise again (T2). <see cref="MaxFires"/> still caps it.</summary>
        [JsonPropertyName("whileTrue")] public bool WhileTrue;
        /// <summary>The actions: <c>[{"act":"Message","args":{...},"bind":{...}?}, ...]</c>, run in order (SR-93). ⭐ Typed (SR-94): a step is a <c>BindableNode</c>, so the one param binder reaches its <c>bind</c>.</summary>
        [JsonPropertyName("do")] public System.Collections.Generic.List<Fdp.Toolkit.ScenarioRules.Actions.ActionStep>? Do;
    }
}
